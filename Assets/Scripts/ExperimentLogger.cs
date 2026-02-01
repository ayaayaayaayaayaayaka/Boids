using UnityEngine;
using System.IO;
using System.Text;
using System.Collections.Generic;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// 実験データをCSVファイルに出力するクラス
/// シーンに1つ配置。Inspectorで条件を選び、再生停止で summary / captures / snapshots が保存される。
/// </summary>
public class ExperimentLogger : MonoBehaviour
{
    public static ExperimentLogger Instance { get; private set; }

    /// <summary>比較実験の条件（Inspectorで選択）</summary>
    public enum ExperimentCondition
    {
        A1, A2, A3,  // 個体数の影響
        B1, B2, B3,  // 結合力の影響
        C1, C2, C3   // 混乱強度の影響
    }

    [Header("実験設定")]
    public string experimentName = "experiment";
    public bool autoStartLogging = true;

    [Header("自動停止設定")]
    [Tooltip("この数だけ捕食されたら自動的に実験を終了する（0=無効）")]
    public int autoStopAtKills = 12;
    [Tooltip("自動停止が有効かどうか")]
    public bool enableAutoStop = true;

    [Header("比較実験の条件（再生前に選択）")]
    [Tooltip("A1=20匹, A2=50匹, A3=100匹 / B1=疎, B2=標準, B3=密 / C1=混乱0, C2=0.5, C3=1.0")]
    public ExperimentCondition experimentCondition = ExperimentCondition.A1;

    [Header("参照")]
    public BoidSettings boidSettings;

    // 内部データ
    private float experimentStartTime;
    private int totalKills = 0;
    private float firstKillTime = -1f;
    private List<CaptureEvent> captureEvents = new List<CaptureEvent>();
    private List<PeriodicSnapshot> snapshots = new List<PeriodicSnapshot>();
    private float lastSnapshotTime = 0f;
    private float snapshotInterval = 5f;

    private bool isLogging = false;
    private string outputPath;
    private int initialBoidCountRecorded = 0;
    private int configuredSpawnTotal = 0;

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

        // 出力先: Assets/ExperimentData（条件名＋タイムスタンプでファイル名）
        string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string folderPath = Path.Combine(Application.dataPath, "ExperimentData");
        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
        }
        string conditionStr = experimentCondition.ToString();
        outputPath = Path.Combine(folderPath, $"{experimentName}_{conditionStr}_{timestamp}");

        // 実験開始直後の個体数とSpawner設定を記録
        initialBoidCountRecorded = FindObjectsOfType<Boid>().Length;
        configuredSpawnTotal = 0;
        Spawner[] spawners = FindObjectsOfType<Spawner>();
        foreach (Spawner s in spawners) configuredSpawnTotal += s.spawnCount;

        Debug.Log($"<color=green>Experiment [{conditionStr}] started. Initial boids: {initialBoidCountRecorded}. Results → {Path.GetFullPath(outputPath)}</color>");
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

        // 自動停止：指定数に達したら実験終了
        if (enableAutoStop && autoStopAtKills > 0 && totalKills >= autoStopAtKills)
        {
            Debug.Log($"<color=cyan>=== 自動停止: {autoStopAtKills}匹捕食達成 ===</color>");
            StopExperiment();
        }
    }

    /// <summary>
    /// 実験を終了し、データを保存して再生を停止する
    /// </summary>
    public void StopExperiment()
    {
        if (!isLogging) return;

        SaveAllData();

#if UNITY_EDITOR
        // エディタの場合は再生を停止
        EditorApplication.isPlaying = false;
#else
        // ビルド版の場合はアプリを終了
        Application.Quit();
#endif
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

            string fullPath = Path.GetFullPath(outputPath);
            Debug.Log($"<color=green>Experiment [{experimentCondition}] saved. Folder: {Path.GetDirectoryName(fullPath)}</color>");
            Debug.Log($"<color=green>Files: *_summary.txt, *_captures.csv, *_snapshots.csv</color>");
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
        float captureRate = totalTime > 0 ? (totalKills / totalTime) * 60f : 0f;

        float cohesionWeight = boidSettings != null ? boidSettings.cohesionWeight : 0f;
        float confusionStrength = 0f;
        ConfusionPredator predator = FindObjectOfType<ConfusionPredator>();
        if (predator != null) confusionStrength = predator.confusionStrength;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("=== Experiment Summary ===");
        sb.AppendLine($"Condition: {experimentCondition}");
        sb.AppendLine($"Initial Boid Count: {initialBoidCountRecorded}");
        sb.AppendLine($"Cohesion Weight: {cohesionWeight}");
        sb.AppendLine($"Confusion Strength: {confusionStrength}");
        sb.AppendLine($"Total Duration: {totalTime:F2} seconds");
        sb.AppendLine($"Total Kills: {totalKills}");
        sb.AppendLine($"First Kill Time: {(firstKillTime >= 0 ? firstKillTime.ToString("F2") + "s" : "N/A")}");
        sb.AppendLine($"Capture Rate: {captureRate:F2} per minute");

        File.WriteAllText(outputPath + "_summary.txt", sb.ToString());
    }

    void SaveCaptureEvents()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("condition,time_sec,kill_number,capture_rate_per_min,boids_in_view,remaining_boids");

        string conditionStr = experimentCondition.ToString();
        foreach (var evt in captureEvents)
        {
            sb.AppendLine($"{conditionStr},{evt.time:F2},{evt.killNumber},{evt.captureRate:F3},{evt.boidsInPredatorView},{evt.remainingBoids}");
        }

        File.WriteAllText(outputPath + "_captures.csv", sb.ToString());
    }

    void SaveSnapshots()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("condition,time_sec,total_boids,total_kills,capture_rate_per_min,avg_neighbor_dist,polarization");

        string conditionStr = experimentCondition.ToString();
        foreach (var snap in snapshots)
        {
            sb.AppendLine($"{conditionStr},{snap.time:F2},{snap.totalBoids},{snap.totalKills},{snap.captureRatePerMinute:F3},{snap.avgFlockDensity:F3},{snap.flockPolarization:F3}");
        }

        File.WriteAllText(outputPath + "_snapshots.csv", sb.ToString());
    }
}
