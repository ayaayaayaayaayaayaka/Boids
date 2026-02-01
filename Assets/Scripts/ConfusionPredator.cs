using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Confusion Effect を再現した捕食者（論文 Fig.2 準拠）
/// - 視野内に「最後に入った魚」を追う（目移り）
/// - ただし切り替え後クールダウン中は切り替えない
/// - 視界内の魚が多いほど追跡精度が下がる（混乱）
/// </summary>
public class ConfusionPredator : MonoBehaviour {
    [Header("基本パラメータ")]
    public float speed = 10f;           // 移動速度（魚の maxSpeed=5 より速く）
    public float viewRadius = 20f;      // 視野半径
    public float viewAngle = 120f;      // 視野角（度）
    public float blindAngle = 40f;      // 後方の死角（度）
    public float boundaryRadius = 50f;  // 活動範囲
    public float captureRadius = 1.2f;  // 捕食判定距離
    public float baseTurnSpeed = 8f;    // 基本旋回速度

    [Header("Confusion Effect（論文 Fig.2 準拠）")]
    [Tooltip("混乱の強さ：0=混乱しない、1=視界内の魚が多いと大きく混乱")]
    [Range(0f, 1f)]
    public float confusionStrength = 0f;
    [Tooltip("この数以上の魚が視界にいると最大混乱")]
    public int maxConfusionCount = 10;
    [Tooltip("混乱時の追跡角度ずれ最大値（度）")]
    public float maxAngleDeviation = 30f;

    [Header("ターゲット切り替え（論文再現のバランス調整）")]
    [Tooltip("ターゲット切り替え後のクールダウン（秒）：この間は新しい魚に切り替えない")]
    public float targetSwitchCooldown = 1.5f;
    [Tooltip("クールダウン中でも、この距離より遠いターゲットは切り替え可能")]
    public float maxChaseDistance = 15f;

    // 内部状態
    private Boid currentTarget;
    private HashSet<Boid> boidsInViewLastFrame = new HashSet<Boid>();
    private int currentBoidsInView = 0;
    private float currentConfusion = 0f;
    private float lastSwitchTime = -999f;
    private int targetSwitchCount = 0;  // デバッグ用

    public float CurrentConfusion => currentConfusion;

    // 計測用
    private float startTime;
    private int killCount = 0;
    private bool firstKillRecorded = false;

    void Start() {
        startTime = Time.time;
    }

    void Update() {
        UpdateTarget();
        Move();
    }

    void LateUpdate() {
        ClampToBoundary();
    }

    void UpdateTarget() {
        Boid[] allBoids = FindObjectsOfType<Boid>();
        HashSet<Boid> currentInView = new HashSet<Boid>();
        List<Boid> newlyEnteredBoids = new List<Boid>();

        // 視野判定
        foreach (Boid b in allBoids) {
            Vector3 toBoid = b.transform.position - transform.position;
            float dist = toBoid.magnitude;
            float angle = Vector3.Angle(transform.forward, toBoid);

            // 視野角内 かつ 死角外
            if (dist < viewRadius && angle < viewAngle / 2f && angle < (180f - blindAngle / 2f)) {
                currentInView.Add(b);
                if (!boidsInViewLastFrame.Contains(b)) {
                    newlyEnteredBoids.Add(b);
                }
            }
        }

        currentBoidsInView = currentInView.Count;

        // === Confusion Effect：視界内の魚の数で混乱度を計算 ===
        float rawConfusion = Mathf.Clamp01((float)currentBoidsInView / maxConfusionCount);
        currentConfusion = rawConfusion * confusionStrength;

        // === ターゲット選択（論文 Fig.2 準拠 + クールダウン）===
        bool currentStillInView = currentTarget != null && currentInView.Contains(currentTarget);
        float distToCurrent = currentTarget != null 
            ? Vector3.Distance(transform.position, currentTarget.transform.position) 
            : float.MaxValue;
        float timeSinceSwitch = Time.time - lastSwitchTime;
        bool cooldownOver = timeSinceSwitch > targetSwitchCooldown;

        // ターゲットが視界から外れた or 死んだ → 強制切り替え
        if (!currentStillInView) {
            SwitchTarget(PickNewTarget(currentInView, newlyEnteredBoids));
        }
        // ターゲットが遠すぎる → 切り替え（追いかけても追いつけない）
        else if (distToCurrent > maxChaseDistance && currentInView.Count > 0) {
            SwitchTarget(PickNewTarget(currentInView, newlyEnteredBoids));
        }
        // クールダウンが終わっていて、新しく視界に入った魚がいる → 論文通り目移り
        else if (cooldownOver && newlyEnteredBoids.Count > 0) {
            // 最後に視界に入った魚をターゲットに（論文 Fig.2）
            SwitchTarget(newlyEnteredBoids[newlyEnteredBoids.Count - 1]);
        }

        boidsInViewLastFrame = currentInView;
    }

    Boid PickNewTarget(HashSet<Boid> inView, List<Boid> newlyEntered) {
        // 新しく入った魚がいればその最後の1匹（論文準拠）
        if (newlyEntered.Count > 0) {
            return newlyEntered[newlyEntered.Count - 1];
        }
        // いなければ最も近い魚
        Boid closest = null;
        float closestDist = float.MaxValue;
        foreach (Boid b in inView) {
            float d = Vector3.Distance(transform.position, b.transform.position);
            if (d < closestDist) { closestDist = d; closest = b; }
        }
        return closest;
    }

    void SwitchTarget(Boid newTarget) {
        if (newTarget != currentTarget) {
            currentTarget = newTarget;
            lastSwitchTime = Time.time;
            targetSwitchCount++;
        }
    }

    void Move() {
        if (currentTarget != null) {
            Vector3 targetDir = (currentTarget.transform.position - transform.position).normalized;

            // === Confusion Effect：混乱度に応じて追跡精度を下げる ===
            if (currentConfusion > 0.05f) {
                // 角度ずれ（Perlin Noiseでなめらかに変動）
                float noiseX = Mathf.PerlinNoise(Time.time * 1.5f, 0f) - 0.5f;
                float noiseY = Mathf.PerlinNoise(0f, Time.time * 1.5f) - 0.5f;
                float deviationH = maxAngleDeviation * currentConfusion * noiseX * 2f;
                float deviationV = maxAngleDeviation * currentConfusion * noiseY;
                Quaternion deviation = Quaternion.Euler(deviationV, deviationH, 0f);
                targetDir = deviation * targetDir;
            }

            // 旋回速度を混乱度に応じて低下
            float effectiveTurnSpeed = baseTurnSpeed * (1f - currentConfusion * 0.5f);
            transform.forward = Vector3.Slerp(transform.forward, targetDir, Time.deltaTime * effectiveTurnSpeed);

            // 移動（混乱時は少し遅くなる）
            float effectiveSpeed = speed * (1f - currentConfusion * 0.15f);
            Vector3 newPos = transform.position + transform.forward * effectiveSpeed * Time.deltaTime;
            transform.position = ClampPositionToBoundary(newPos);

            // 捕食判定
            if (Vector3.Distance(transform.position, currentTarget.transform.position) < captureRadius) {
                CapturePrey(currentTarget);
                currentTarget = null;
            }
        } else {
            // ターゲットなし：前進して探す
            Vector3 newPos = transform.position + transform.forward * speed * Time.deltaTime;
            transform.position = ClampPositionToBoundary(newPos);
        }
    }

    void ClampToBoundary() {
        Vector3 pos = ClampPositionToBoundary(transform.position);
        transform.position = pos;
        float mag = pos.magnitude;
        if (mag > 0.001f && mag >= boundaryRadius * 0.99f) {
            Vector3 inward = -pos.normalized;
            if (Vector3.Dot(transform.forward, inward) < 0.3f) {
                transform.forward = Vector3.Slerp(transform.forward, inward, 0.4f).normalized;
            }
        }
    }

    Vector3 ClampPositionToBoundary(Vector3 pos) {
        float mag = pos.magnitude;
        if (mag <= boundaryRadius) return pos;
        if (mag < 0.001f) return Vector3.forward * boundaryRadius;
        return pos / mag * boundaryRadius;
    }

    void CapturePrey(Boid prey) {
        if (!firstKillRecorded) {
            Debug.Log($"<color=red>First Capture Time: {Time.time - startTime:F2}s</color>");
            firstKillRecorded = true;
        }
        killCount++;

        if (ExperimentLogger.Instance != null) {
            ExperimentLogger.Instance.RecordCapture(currentBoidsInView);
        }

        Destroy(prey.gameObject);
        Debug.Log($"Kill #{killCount} | Time: {Time.time - startTime:F1}s | Confusion: {currentConfusion:P0} | Switches: {targetSwitchCount}");
    }

    // Gizmos：視野とターゲットを可視化
    void OnDrawGizmos() {
        Gizmos.color = new Color(1f, 1f, 0f, 0.15f);
        Gizmos.DrawWireSphere(transform.position, viewRadius);

        // 混乱度で色を変える
        Gizmos.color = Color.Lerp(Color.green, Color.red, currentConfusion);
        if (currentTarget != null) {
            Gizmos.DrawLine(transform.position, currentTarget.transform.position);
            Gizmos.DrawWireSphere(currentTarget.transform.position, 0.5f);
        }

        // 視野角
        Vector3 left = Quaternion.Euler(0, -viewAngle / 2f, 0) * transform.forward * viewRadius;
        Vector3 right = Quaternion.Euler(0, viewAngle / 2f, 0) * transform.forward * viewRadius;
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position, left);
        Gizmos.DrawRay(transform.position, right);
    }

    // UI
    void OnGUI() {
        GUIStyle style = new GUIStyle(GUI.skin.label) { fontSize = 14 };
        GUI.Label(new Rect(10, 10, 350, 25), $"Kills: {killCount}  |  Switches: {targetSwitchCount}", style);
        GUI.Label(new Rect(10, 32, 350, 25), $"Time: {Time.time - startTime:F1}s", style);
        GUI.Label(new Rect(10, 54, 350, 25), $"Confusion: {currentConfusion:P0} ({currentBoidsInView} in view)", style);
        if (killCount > 0) {
            float rate = killCount / (Time.time - startTime) * 60f;
            GUI.Label(new Rect(10, 76, 350, 25), $"Rate: {rate:F2} kills/min", style);
        }
    }
}
