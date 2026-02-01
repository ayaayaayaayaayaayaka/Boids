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

        // 出力パスを設定（プロジェクトルートにExperimentDataフォルダ）
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string folderPath = Path.Combine(Application.dataPath, "..", "ExperimentData");
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }
        outputPath = Path.Combine(folderPath, $"{experimentName}_{timestamp}");

        Debug.Log($"<color=green>Experiment started: {outputPath}</color>");
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

    public void SaveAllData()
    {
        if (!isLogging) return;

        // 最後のスナップショットを取る
        TakeSnapshot();

        // 実験サマリーを保存
        SaveSummary();

        // 捕食イベントCSVを保存
        SaveCaptureEvents();

        // スナップショットCSVを保存
        SaveSnapshots();

        // パラメータを保存
        SaveParameters();

        isLogging = false;
        Debug.Log($"<color=green>Experiment data saved to: {outputPath}</color>");
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
        sb.AppendLine("parameter,value");

        if (boidSettings != null)
        {
            sb.AppendLine($"boid_minSpeed,{boidSettings.minSpeed}");
            sb.AppendLine($"boid_maxSpeed,{boidSettings.maxSpeed}");
            sb.AppendLine($"boid_perceptionRadius,{boidSettings.perceptionRadius}");
            sb.AppendLine($"boid_avoidanceRadius,{boidSettings.avoidanceRadius}");
            sb.AppendLine($"boid_alignWeight,{boidSettings.alignWeight}");
            sb.AppendLine($"boid_cohesionWeight,{boidSettings.cohesionWeight}");
            sb.AppendLine($"boid_seperateWeight,{boidSettings.seperateWeight}");
        }

        // 捕食者パラメータ
        ConfusionPredator predator = FindObjectOfType<ConfusionPredator>();
        if (predator != null)
        {
            sb.AppendLine($"predator_speed,{predator.speed}");
            sb.AppendLine($"predator_viewRadius,{predator.viewRadius}");
            sb.AppendLine($"predator_viewAngle,{predator.viewAngle}");
            sb.AppendLine($"predator_blindAngle,{predator.blindAngle}");
            sb.AppendLine($"predator_confusionStrength,{predator.confusionStrength}");
        }

        // 初期魚数
        Boid[] boids = FindObjectsOfType<Boid>();
        sb.AppendLine($"initial_boid_count,{boids.Length + totalKills}");

        File.WriteAllText(outputPath + "_parameters.csv", sb.ToString());
    }
}
