using UnityEngine;
using System.IO;
using System.Text;
using System.Collections.Generic;

/// <summary>
/// 実験データをCSVファイルに出力するクラス
/// シーンに1つ配置して使用。
/// CSV保存タイミング：あなたが「再生停止」ボタンを押したとき（エディタ）、
/// またはビルド版でアプリを閉じたときに自動で保存されます。
/// </summary>
public class ExperimentLogger : MonoBehaviour
{
    public static ExperimentLogger Instance { get; private set; }

    [Header("実験設定")]
    public string experimentName = "experiment";
    public bool autoStartLogging = true;

    [Header("参照")]
    public BoidSettings boidSettings;

    // 内部データ
    private float experimentStartTime;
    private int totalKills = 0;
    private float firstKillTime = -1f;
    private List<CaptureEvent> captureEvents = new List<CaptureEvent>();
    private List<PeriodicSnapshot> snapshots = new List<PeriodicSnapshot>();
    private float lastSnapshotTime = 0f;
    private float snapshotInterval = 5f; // 5秒ごとにスナップショット

    private bool isLogging = false;
    private string outputPath;

    // 捕食イベントのデータ構造
    [System.Serializable]
    public struct CaptureEvent
    {
        public float time;
        public int killNumber;
        public float captureRate; // kills per minute
        public int boidsInPredatorView; // 捕食時の視界内の魚数
        public int remainingBoids;
    }

    // 定期スナップショットのデータ構造
    [System.Serializable]
    public struct PeriodicSnapshot
    {
        public float time;
        public int totalBoids;
        public int totalKills;
        public float captureRatePerMinute;
        public float avgFlockDensity; // 平均近傍距離
        public float flockPolarization; // 群れの極性（0-1）
    }

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
        }
        else
        {
            Destroy(gameObject);
            return;
        }
    }

    void Start()
    {
        if (autoStartLogging)
        {
            StartExperiment();
        }
    }

    void Update()
    {
        if (!isLogging) return;

        // 定期スナップショット
        float elapsed = Time.time - experimentStartTime;
        if (elapsed - lastSnapshotTime >= snapshotInterval)
        {
            TakeSnapshot();
            lastSnapshotTime = elapsed;
        }
    }

    public void StartExperiment()
    {
        experimentStartTime = Time.time;
        totalKills = 0;
        firstKillTime = -1f;
        captureEvents.Clear();
        snapshots.Clear();
        lastSnapshotTime = 0f;
        isLogging = true;

        // 出力先: Assets/ExperimentData（UnityのProjectウィンドウで見える）
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string folderPath = Path.Combine(Application.dataPath, "ExperimentData");
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }
        outputPath = Path.Combine(folderPath, $"{experimentName}_{timestamp}");

        Debug.Log($"<color=green>Experiment started. Results will be saved to: {Path.GetFullPath(outputPath)}</color>");
    }

    /// <summary>
    /// 捕食者から呼び出される：捕食イベントを記録
    /// </summary>
    public void RecordCapture(int boidsInView)
    {
        if (!isLogging) return;

        float elapsed = Time.time - experimentStartTime;
        totalKills++;

        if (firstKillTime < 0f)
        {
            firstKillTime = elapsed;
        }

        Boid[] allBoids = FindObjectsOfType<Boid>();

        CaptureEvent evt = new CaptureEvent
        {
            time = elapsed,
            killNumber = totalKills,
            captureRate = (elapsed > 0) ? (totalKills / elapsed) * 60f : 0f, // per minute
            boidsInPredatorView = boidsInView,
            remainingBoids = allBoids.Length - 1 // -1 because one is being destroyed
        };
        captureEvents.Add(evt);

        Debug.Log($"<color=yellow>Kill #{totalKills} | Time: {elapsed:F2}s | Rate: {evt.captureRate:F2}/min | InView: {boidsInView}</color>");
    }

    void TakeSnapshot()
    {
        Boid[] allBoids = FindObjectsOfType<Boid>();
        float elapsed = Time.time - experimentStartTime;

        float density = CalculateAverageNeighborDistance(allBoids);
        float polarization = CalculatePolarization(allBoids);

        PeriodicSnapshot snap = new PeriodicSnapshot
        {
            time = elapsed,
            totalBoids = allBoids.Length,
            totalKills = totalKills,
            captureRatePerMinute = (elapsed > 0) ? (totalKills / elapsed) * 60f : 0f,
            avgFlockDensity = density,
            flockPolarization = polarization
        };
        snapshots.Add(snap);
    }

    float CalculateAverageNeighborDistance(Boid[] boids)
    {
        if (boids.Length < 2) return 0f;

        float totalDist = 0f;
        int count = 0;
        float neighborRadius = 5f;

        foreach (Boid b in boids)
        {
            foreach (Boid other in boids)
            {
                if (b == other) continue;
                float dist = Vector3.Distance(b.position, other.position);
                if (dist < neighborRadius)
                {
                    totalDist += dist;
                    count++;
                }
            }
        }

        return count > 0 ? totalDist / count : 0f;
    }

    float CalculatePolarization(Boid[] boids)
    {
        if (boids.Length == 0) return 0f;

        Vector3 avgDirection = Vector3.zero;
        foreach (Boid b in boids)
        {
            avgDirection += b.forward;
        }
        avgDirection /= boids.Length;

        // 極性は平均方向ベクトルの大きさ（0=バラバラ、1=完全に揃っている）
        return avgDirection.magnitude;
    }

    // エディタで再生を止めたときにも呼ばれる
    void OnDestroy()
    {
        if (isLogging)
        {
            SaveAllData();
        }
    }

    // ビルド版でアプリを閉じたときに呼ばれる（エディタの再生停止では呼ばれない）
    void OnApplicationQuit()
    {
        if (isLogging)
        {
            SaveAllData();
        }
    }

    private bool isSaving = false;

    public void SaveAllData()
    {
        if (!isLogging || isSaving) return;
        isSaving = true;

        try
        {
            // 最後のスナップショット（再生停止中はスキップしてもよい）
            try { TakeSnapshot(); } catch (System.Exception e) { Debug.LogWarning("Snapshot skip: " + e.Message); }

            SaveSummary();
            SaveCaptureEvents();
            SaveSnapshots();
            SaveParameters();

            string fullPath = Path.GetFullPath(outputPath);
            Debug.Log($"<color=green>Experiment data saved. Folder: {Path.GetDirectoryName(fullPath)}</color>");
            Debug.Log($"<color=green>Files: *_summary.txt, *_captures.csv, *_snapshots.csv, *_parameters.csv</color>");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Experiment save failed: {e.Message}\n{e.StackTrace}");
        }
        finally
        {
            isLogging = false;
            isSaving = false;
        }
    }

    void SaveSummary()
    {
        float totalTime = Time.time - experimentStartTime;
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== Experiment Summary ===");
        sb.AppendLine($"Experiment Name: {experimentName}");
        sb.AppendLine($"Total Duration: {totalTime:F2} seconds");
        sb.AppendLine($"Total Kills: {totalKills}");
        sb.AppendLine($"First Kill Time: {(firstKillTime >= 0 ? firstKillTime.ToString("F2") + "s" : "N/A")}");
        sb.AppendLine($"Average Capture Rate: {(totalTime > 0 ? (totalKills / totalTime) * 60f : 0f):F2} per minute");
        sb.AppendLine($"Initial Boid Count: {(snapshots.Count > 0 ? snapshots[0].totalBoids + totalKills : 0)}");

        File.WriteAllText(outputPath + "_summary.txt", sb.ToString());
    }

    void SaveCaptureEvents()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("time_sec,kill_number,capture_rate_per_min,boids_in_view,remaining_boids");

        foreach (var evt in captureEvents)
        {
            sb.AppendLine($"{evt.time:F2},{evt.killNumber},{evt.captureRate:F3},{evt.boidsInPredatorView},{evt.remainingBoids}");
        }

        File.WriteAllText(outputPath + "_captures.csv", sb.ToString());
    }

    void SaveSnapshots()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("time_sec,total_boids,total_kills,capture_rate_per_min,avg_neighbor_dist,polarization");

        foreach (var snap in snapshots)
        {
            sb.AppendLine($"{snap.time:F2},{snap.totalBoids},{snap.totalKills},{snap.captureRatePerMinute:F3},{snap.avgFlockDensity:F3},{snap.flockPolarization:F3}");
        }

        File.WriteAllText(outputPath + "_snapshots.csv", sb.ToString());
    }

    void SaveParameters()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("category,parameter,value,description");

        // 実験メタデータ
        sb.AppendLine($"experiment,name,{experimentName},実験名");
        sb.AppendLine($"experiment,timestamp,{System.DateTime.Now:yyyy-MM-dd HH:mm:ss},実験日時");
        sb.AppendLine($"experiment,duration_sec,{Time.time - experimentStartTime:F2},実験時間（秒）");

        // 被捕食者（Boid）パラメータ
        Boid[] boids = FindObjectsOfType<Boid>();
        int initialBoidCount = boids.Length + totalKills;
        sb.AppendLine($"boid,initial_count,{initialBoidCount},初期個体数");
        sb.AppendLine($"boid,final_count,{boids.Length},最終個体数");

        if (boidSettings != null)
        {
            sb.AppendLine($"boid,minSpeed,{boidSettings.minSpeed},最小速度");
            sb.AppendLine($"boid,maxSpeed,{boidSettings.maxSpeed},最大速度");
            sb.AppendLine($"boid,perceptionRadius,{boidSettings.perceptionRadius},知覚半径");
            sb.AppendLine($"boid,avoidanceRadius,{boidSettings.avoidanceRadius},回避半径");
            sb.AppendLine($"boid,alignWeight,{boidSettings.alignWeight},整列重み");
            sb.AppendLine($"boid,cohesionWeight,{boidSettings.cohesionWeight},結合重み");
            sb.AppendLine($"boid,seperateWeight,{boidSettings.seperateWeight},分離重み");
            sb.AppendLine($"boid,maxSteerForce,{boidSettings.maxSteerForce},最大操舵力");
        }

        // 捕食者パラメータ
        ConfusionPredator predator = FindObjectOfType<ConfusionPredator>();
        if (predator != null)
        {
            sb.AppendLine($"predator,speed,{predator.speed},移動速度");
            sb.AppendLine($"predator,viewRadius,{predator.viewRadius},視野半径");
            sb.AppendLine($"predator,viewAngle,{predator.viewAngle},視野角（度）");
            sb.AppendLine($"predator,blindAngle,{predator.blindAngle},死角（度）");
            sb.AppendLine($"predator,captureRadius,{predator.captureRadius},捕食判定距離");
            sb.AppendLine($"predator,baseTurnSpeed,{predator.baseTurnSpeed},基本旋回速度");
            sb.AppendLine($"predator,confusionStrength,{predator.confusionStrength},混乱強度");
            sb.AppendLine($"predator,maxConfusionCount,{predator.maxConfusionCount},最大混乱個体数");
            sb.AppendLine($"predator,maxAngleDeviation,{predator.maxAngleDeviation},最大角度ずれ（度）");
            sb.AppendLine($"predator,targetSwitchCooldown,{predator.targetSwitchCooldown},ターゲット切替クールダウン（秒）");
            sb.AppendLine($"predator,maxChaseDistance,{predator.maxChaseDistance},最大追跡距離");
        }

        // 実験結果サマリー
        sb.AppendLine($"result,total_kills,{totalKills},総捕食数");
        sb.AppendLine($"result,first_kill_time,{(firstKillTime >= 0 ? firstKillTime.ToString("F2") : "N/A")},最初の捕食までの時間（秒）");
        float duration = Time.time - experimentStartTime;
        float captureRate = duration > 0 ? (totalKills / duration) * 60f : 0f;
        sb.AppendLine($"result,capture_rate_per_min,{captureRate:F3},時間当たり捕食数（/分）");

        File.WriteAllText(outputPath + "_parameters.csv", sb.ToString());
    }
}
