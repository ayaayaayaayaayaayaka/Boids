using UnityEngine;
using System.Collections.Generic;

public class ConfusionPredator : MonoBehaviour {
    [Header("基本パラメータ")]
    public float speed = 12f;        // 小魚(max 5)より十分速く（追いつきやすく）
    public float viewRadius = 25f;   // 視野半径（獲物を早く見つける）
    public float viewAngle = 120f;   // 視野角
    public float blindAngle = 40f;   // 後方の死角
    public float boundaryRadius = 50f;
    public float captureRadius = 1.5f;  // この距離以内で捕食
    public float baseTurnSpeed = 10f;   // 基本旋回速度

    [Header("Confusion Effect（論文Fig.2準拠）")]
    [Tooltip("混乱の強さ：0=混乱しない、1=視界内の魚が多いと大きく混乱")]
    [Range(0f, 1f)]
    public float confusionStrength = 0.7f;
    [Tooltip("この数以上の魚が視界にいると最大混乱")]
    public int maxConfusionCount = 15;
    [Tooltip("混乱時の追跡角度ずれ（度）")]
    public float maxAngleDeviation = 45f;
    [Tooltip("混乱していないときのターゲット固定距離")]
    public float pursuitCommitDist = 6f;

    // 現在の混乱度（0-1）
    private float currentConfusion = 0f;
    public float CurrentConfusion => currentConfusion;

    private Boid currentTarget;
    private HashSet<Boid> boidsInViewLastFrame = new HashSet<Boid>();
    private int currentBoidsInView = 0;

    // 計測用データ
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
        // 他スクリプトや物理の後でも境界内に収める（二重対策）
        ClampToBoundary();
    }

    void UpdateTarget() {
        Boid[] allBoids = FindObjectsOfType<Boid>();
        HashSet<Boid> currentInView = new HashSet<Boid>();
        List<Boid> newlyEnteredBoids = new List<Boid>();

        foreach (Boid b in allBoids) {
            Vector3 dirToBoid = (b.transform.position - transform.position).normalized;
            float angle = Vector3.Angle(transform.forward, dirToBoid);
            float dist = Vector3.Distance(transform.position, b.transform.position);

            // 視野角内 かつ 死角（真後ろ180度付近）ではない判定
            if (dist < viewRadius && angle < viewAngle / 2f && angle < (180f - blindAngle / 2f)) {
                currentInView.Add(b);
                // 新しく視界に入った魚を記録
                if (!boidsInViewLastFrame.Contains(b)) {
                    newlyEnteredBoids.Add(b);
                }
            }
        }

        currentBoidsInView = currentInView.Count;

        // === Confusion Effect（論文Fig.2準拠）===
        // 視界内の魚の数に応じて混乱度を計算
        float rawConfusion = Mathf.Clamp01((float)currentBoidsInView / maxConfusionCount);
        currentConfusion = rawConfusion * confusionStrength;

        // 混乱度に応じてターゲット切り替えの閾値を変える
        // 混乱が高いほど、新しい魚にすぐ目移りする
        float effectiveCommitDist = pursuitCommitDist * (1f - currentConfusion * 0.8f);
        
        float distToCurrent = currentTarget != null ? Vector3.Distance(transform.position, currentTarget.transform.position) : float.MaxValue;
        
        // 新しく視界に入った魚がいれば、混乱度に応じてターゲット切り替え
        if (newlyEnteredBoids.Count > 0) {
            // 混乱度が高いほど、または距離が遠いほど切り替えやすい
            if (distToCurrent > effectiveCommitDist) {
                // 最後に入った魚をターゲットに（論文準拠）
                currentTarget = newlyEnteredBoids[newlyEnteredBoids.Count - 1];
            } else if (currentConfusion > 0.5f && Random.value < currentConfusion * 0.3f) {
                // 高混乱時は確率的にもターゲット切り替え
                currentTarget = newlyEnteredBoids[newlyEnteredBoids.Count - 1];
            }
        }

        // ターゲットが視界から消えた、または死んだ場合のフォールバック
        if (currentTarget == null || !currentInView.Contains(currentTarget)) {
            currentTarget = null;
            if (currentInView.Count > 0) {
                // ランダムに1匹選ぶ（混乱しているので特定の1匹を選べない）
                int idx = Random.Range(0, currentInView.Count);
                var e = currentInView.GetEnumerator();
                for (int i = 0; i <= idx; i++) e.MoveNext();
                currentTarget = e.Current;
            }
        }

        boidsInViewLastFrame = currentInView;
    }

    void Move() {
        if (currentTarget != null) {
            Vector3 targetDir = (currentTarget.transform.position - transform.position).normalized;
            
            // === Confusion Effect: 混乱度に応じて追跡精度を下げる ===
            
            // 1. 追跡方向に角度ずれを加える（混乱していると正確に追えない）
            if (currentConfusion > 0.1f) {
                float deviationAngle = maxAngleDeviation * currentConfusion * (Mathf.PerlinNoise(Time.time * 2f, 0f) - 0.5f) * 2f;
                Quaternion deviation = Quaternion.AngleAxis(deviationAngle, Vector3.up);
                // 上下方向にも少しずれる
                float verticalDeviation = maxAngleDeviation * currentConfusion * 0.5f * (Mathf.PerlinNoise(0f, Time.time * 2f) - 0.5f) * 2f;
                deviation *= Quaternion.AngleAxis(verticalDeviation, Vector3.right);
                targetDir = deviation * targetDir;
            }

            // 2. 旋回速度を混乱度に応じて低下させる（判断が鈍る）
            float effectiveTurnSpeed = baseTurnSpeed * (1f - currentConfusion * 0.6f);
            transform.forward = Vector3.Slerp(transform.forward, targetDir, Time.deltaTime * effectiveTurnSpeed);
            
            // 3. 速度も少し低下（混乱していると迷う）
            float effectiveSpeed = speed * (1f - currentConfusion * 0.2f);
            Vector3 newPos = transform.position + transform.forward * effectiveSpeed * Time.deltaTime;
            transform.position = ClampPositionToBoundary(newPos);

            // 捕食判定
            if (Vector3.Distance(transform.position, currentTarget.transform.position) < captureRadius) {
                CapturePrey(currentTarget);
                currentTarget = null;
            }
        } else {
            Vector3 newPos = transform.position + transform.forward * speed * Time.deltaTime;
            transform.position = ClampPositionToBoundary(newPos);
        }
    }

    void ClampToBoundary() {
        Vector3 pos = ClampPositionToBoundary(transform.position);
        transform.position = pos;
        // 境界上で前方向が外を向いているとずっと同じ位置にクランプされて止まるので、内側を向かせる
        float mag = pos.magnitude;
        if (mag > 0.001f && mag >= boundaryRadius * 0.99f) {
            Vector3 inward = (-pos / mag).normalized;
            if (Vector3.Dot(transform.forward, inward) < 0.5f) {
                transform.forward = Vector3.Slerp(transform.forward, inward, 0.3f).normalized;
            }
        }
    }

    Vector3 ClampPositionToBoundary(Vector3 pos) {
        float mag = pos.magnitude;
        if (mag <= boundaryRadius) return pos;
        if (mag < 0.001f) return Vector3.forward * boundaryRadius; // 中心付近は(1,0,0)方向に
        return pos / mag * boundaryRadius;
    }

    void CapturePrey(Boid prey) {
        if (!firstKillRecorded) {
            Debug.Log($"<color=red>First Capture Time: {Time.time - startTime}s</color>");
            firstKillRecorded = true;
        }
        killCount++;
        
        // ExperimentLoggerに記録（存在する場合）
        if (ExperimentLogger.Instance != null) {
            ExperimentLogger.Instance.RecordCapture(currentBoidsInView);
        }
        
        Destroy(prey.gameObject);
        Debug.Log($"Kills: {killCount} | Time: {Time.time - startTime}s | Confusion: {currentConfusion:F2} | InView: {currentBoidsInView}");
    }

    // デバッグ用：視野とターゲットを可視化
    void OnDrawGizmos() {
        // 視野範囲を描画
        Gizmos.color = new Color(1f, 0f, 0f, 0.1f);
        Gizmos.DrawWireSphere(transform.position, viewRadius);

        // 混乱度に応じて色を変える（緑=冷静、赤=混乱）
        Gizmos.color = Color.Lerp(Color.green, Color.red, currentConfusion);
        
        // ターゲットへの線を描画
        if (currentTarget != null) {
            Gizmos.DrawLine(transform.position, currentTarget.transform.position);
        }

        // 視野角を描画
        Vector3 leftBoundary = Quaternion.Euler(0, -viewAngle / 2f, 0) * transform.forward * viewRadius;
        Vector3 rightBoundary = Quaternion.Euler(0, viewAngle / 2f, 0) * transform.forward * viewRadius;
        Gizmos.color = Color.yellow;
        Gizmos.DrawRay(transform.position, leftBoundary);
        Gizmos.DrawRay(transform.position, rightBoundary);
    }

    // UI表示用
    void OnGUI() {
        GUI.Label(new Rect(10, 10, 300, 25), $"Kills: {killCount}");
        GUI.Label(new Rect(10, 35, 300, 25), $"Time: {Time.time - startTime:F1}s");
        GUI.Label(new Rect(10, 60, 300, 25), $"Confusion: {currentConfusion:P0} ({currentBoidsInView} in view)");
        if (killCount > 0 && Time.time > startTime) {
            float rate = killCount / (Time.time - startTime) * 60f;
            GUI.Label(new Rect(10, 85, 300, 25), $"Rate: {rate:F2}/min");
        }
    }
}