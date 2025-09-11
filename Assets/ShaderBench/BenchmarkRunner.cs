// Assets/ShaderBench/BenchmarkRunner.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Networking; // <- para UnityWebRequest
using System.Text;

/// <summary>
/// Ejecuta casos de benchmark y:
///  - Warmup + Medición por repetición
///  - Calcula estadísticas CPU/GPU/FPS
///  - Guarda CSV local
///  - (Opcional) Sube automáticamente a Google Sheets vía Apps Script Web App
///
/// Incluye auto-encuadre de cámara para evitar medir contenido parcial.
/// </summary>
public class BenchmarkRunner : MonoBehaviour
{
    [Header("Casos a ejecutar (arrástralos aquí)")]
    public List<BenchmarkCase> cases = new List<BenchmarkCase>();

    [Header("Parámetros de ejecución")]
    public float warmupSeconds = 3f;
    public float measureSeconds = 10f;
    public int repetitions = 2;
    public int seed = 1234;

    // =================== Cámara / Auto-encuadre ===================
    public enum CameraFitMode { Manual, AutoFitTopDownOrtho, AutoFitIsoPerspective }

    [Header("Cámara / escena")]
    public CameraFitMode cameraFitMode = CameraFitMode.AutoFitIsoPerspective;
    public Vector3 cameraPosition = new Vector3(20f, 35f, -20f);
    public Vector3 cameraEuler = new Vector3(55f, 35f, 0f);
    public Vector2 isoPitchYaw = new Vector2(55f, 35f);
    [Range(0f, 0.3f)] public float frameMargin = 0.08f;
    public float orthoHeightBoost = 1.10f;
    public Light mainDirectionalLight;

    // =================== Subida a Google Sheets ===================
    [Header("Google Sheets Upload")]
    [Tooltip("Activa el envío automático al endpoint de Apps Script (Web App).")]
    public bool uploadToGoogleSheets = true;

    [Tooltip("URL de la Web App (Apps Script): https://script.google.com/macros/s/.../exec")]
    public string googleScriptUrl = "PASTE_YOUR_WEB_APP_URL_HERE";

    [Tooltip("Debe coincidir con el TOKEN del Apps Script.")]
    public string googleScriptToken = "CAMBIA_ESTE_TOKEN";

    [Tooltip("Enviar cada fila al terminar cada repetición. Si no, hace batch al final.")]
    public bool uploadEachRow = true;

    [Tooltip("Tamaño máximo del batch si haces subidas en bloque.")]
    public int uploadBatchSize = 32;

    // =================== Config hoja por ejecución ====

    [Header("Per-run sheet")]
    public bool newSheetPerRun = true;

    [Tooltip("Prefijo para la pestaña de cada ejecución")]
    public string sheetNamePrefix = "Run_";

    [Tooltip("Si lo dejas vacío, se genera automáticamente con timestamp")]
    public string customRunLabel = "";

    [Tooltip("FPS objetivo para análisis en Sheets (finalize)")]
    public int targetFPS = 72;

    // Internos
    private string _currentSheetName;

    // Estado público (overlay)
    public bool IsRunning { get; private set; }
    public bool HasFinished { get; private set; }
    public string CurrentCaseName { get; private set; } = "-";
    public string CurrentPhase { get; private set; } = "-";
    public float CurrentPhaseElapsed { get; private set; }
    public float CurrentPhaseDuration { get; private set; }
    public int CurrentIndex { get; private set; }
    public int TotalCases => cases?.Count ?? 0;
    public int Repetitions => repetitions;
    public int CurrentRepetition { get; private set; }
    public float InstantFPS { get; private set; }
    public double InstantCPUms { get; private set; }
    public double InstantGPUms { get; private set; }
    public string LastCsvPath { get; private set; } = "";

    // Info de subida (overlay)
    public string LastUploadStatus { get; private set; } = "";
    private float _lastUploadStatusTime;

    // Internos
    private System.Random _rng;
    private Camera _cam;
    private Transform _root;
    private readonly List<GameObject> _spawned = new();
    private readonly List<double> _samplesCPU = new();
    private readonly List<double> _samplesGPU = new();
    private readonly FrameTiming[] _ftBuffer = new FrameTiming[1];

    // Cola de filas pendientes para Sheets
    [Serializable]
    private class SheetRow
    {
        public string Timestamp;
        public string CaseName;
        public int Repetition;
        public int GridX, GridY, Count;
        public string Spacing;
        public bool GPUInstancing, Shadows, ReceiveShadows;
        public string EnabledKW, DisabledKW;

        public double CPU_ms_avg, CPU_ms_p95, CPU_ms_min, CPU_ms_max;
        public double GPU_ms_avg, GPU_ms_p95, GPU_ms_min, GPU_ms_max;
        public double FPS_avg, FPS_p95, FPS_min, FPS_max;

        public string UnityVersion, RenderPipeline, GraphicsAPI, DeviceModel, OS;
        public string Bottleneck;
        public string Quest3Rating;
        public string Summary;
    }

    [Serializable]
    private class SheetPayload
    {
        public string token;
        public string sheetName;   // <--- nuevo
        public string action;      // "append" o "finalize"
        public int targetFps;      // usado en finalize
        public List<SheetRow> rows = new();
    }

    private readonly List<SheetRow> _pendingRows = new();
    private string PendingFilePath => Path.Combine(Application.persistentDataPath, "ShaderBench_PendingUploads.json");

    // =================== Unity lifecycle ===================
    void Awake()
    {
        // Cámara
        _cam = Camera.main;
        if (_cam == null)
        {
            var go = new GameObject("ShaderBenchCamera");
            _cam = go.AddComponent<Camera>();
        }
        _cam.transform.position = cameraPosition;
        _cam.transform.rotation = Quaternion.Euler(cameraEuler);
        _cam.nearClipPlane = 0.1f;
        _cam.farClipPlane = 2000f;

        // Luz
        if (mainDirectionalLight == null)
        {
            var lightObj = new GameObject("ShaderBenchDirectionalLight");
            mainDirectionalLight = lightObj.AddComponent<Light>();
            mainDirectionalLight.type = LightType.Directional;
            mainDirectionalLight.intensity = 1.0f;
            mainDirectionalLight.shadows = LightShadows.Soft;
            mainDirectionalLight.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
        }

        // Intenta cargar/reenviar pendientes de sesiones anteriores
        TryLoadPendingFromDisk();
        if (uploadToGoogleSheets && _pendingRows.Count > 0 && !string.IsNullOrEmpty(googleScriptUrl))
        {
            StartCoroutine(UploadPendingRowsCoroutine());
        }
    }

    void Update()
    {
        // Lanzar / cancelar con teclado
        if (!IsRunning && !HasFinished && Input.GetKeyDown(KeyCode.Space))
            StartCoroutine(RunAll());

        if (IsRunning && Input.GetKeyDown(KeyCode.Escape))
        {
            StopAllCoroutines();
            Cleanup();
            IsRunning = false;
            CurrentPhase = "Cancelado";
        }

        // FPS instantáneo
        InstantFPS = 1f / Mathf.Max(Time.unscaledDeltaTime, 1e-6f);
    }

    // =================== Núcleo de ejecución ===================
    IEnumerator RunAll()
    {
        if (cases == null || cases.Count == 0)
        {
            Debug.LogWarning("[ShaderBench] No hay casos asignados.");
            yield break;
        }

        IsRunning = true;
        HasFinished = false;
        _rng = new System.Random(seed);

        // --- Timestamp para CSV y nombre de hoja por ejecución ---
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string csvPath = Path.Combine(Application.persistentDataPath, $"ShaderBench_{timestamp}.csv");
        PrepareCSV(csvPath);

        // Si newSheetPerRun = true, crea nombre único; si no, usa "Results"
        _currentSheetName = newSheetPerRun
            ? (string.IsNullOrEmpty(customRunLabel) ? $"{sheetNamePrefix}{timestamp}" : $"{sheetNamePrefix}{customRunLabel}")
            : "Results";

        for (int i = 0; i < cases.Count; i++)
        {
            CurrentIndex = i;
            var c = cases[i];
            CurrentCaseName = string.IsNullOrEmpty(c.caseName) ? $"Case_{i + 1}" : c.caseName;

            for (int rep = 1; rep <= repetitions; rep++)
            {
                CurrentRepetition = rep;

                // Construye y encuadra
                BuildCase(c);
                AutoFrameCameraForCase(c);

                // Warmup
                yield return Phase("Warmup", Mathf.Max(0f, warmupSeconds), collectSamples: false);

                // Measuring
                _samplesCPU.Clear();
                _samplesGPU.Clear();
                yield return Phase("Measuring", Mathf.Max(0.01f, measureSeconds), collectSamples: true);

                // Resultados
                var res = ComputeStats(_samplesCPU, _samplesGPU);

                // CSV local
                AppendCSV(csvPath, c, rep, res);

                // Subida a Google Sheets (fila)
                if (uploadToGoogleSheets)
                {
                    var row = BuildSheetRow(c, rep, res);
                    EnqueueRow(row);

                    if (uploadEachRow && _pendingRows.Count >= 1)
                        yield return UploadPendingRowsCoroutine(); // sube inmediatamente
                    else if (_pendingRows.Count >= uploadBatchSize)
                        yield return UploadPendingRowsCoroutine(); // batch parcial
                }

                ClearSpawned();
                yield return null; // margen 1 frame
            }
        }

        LastCsvPath = csvPath;

        // Flush de pendientes (por si quedaron)
        if (uploadToGoogleSheets && _pendingRows.Count > 0)
            yield return UploadPendingRowsCoroutine();

        // --- Dispara el análisis/Resumen en la hoja (crea *_Summary) ---
        if (uploadToGoogleSheets && !string.IsNullOrEmpty(googleScriptUrl))
            yield return FinalizeRunCoroutine();

        IsRunning = false;
        HasFinished = true;
        CurrentPhase = "Terminado";
        Cleanup();

        Debug.Log($"[ShaderBench] Resultados guardados en: {csvPath}");
    }

    void PrepareCSV(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(path);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

            using var w = new StreamWriter(path, false);
            w.WriteLine(string.Join(",",
                "Timestamp", "CaseName", "Repetition", "GridX", "GridY", "Count", "Spacing",
                "GPUInstancing", "Shadows", "ReceiveShadows", "EnabledKW", "DisabledKW",
                "CPU_ms_avg", "CPU_ms_p95", "CPU_ms_min", "CPU_ms_max",
                "GPU_ms_avg", "GPU_ms_p95", "GPU_ms_min", "GPU_ms_max",
                "FPS_avg", "FPS_p95", "FPS_min", "FPS_max",
                "UnityVersion", "RenderPipeline", "GraphicsAPI", "DeviceModel", "OS",
                "Bottleneck", "Quest3Rating", "Summary"));
        }
        catch (Exception e)
        {
            Debug.LogError($"[ShaderBench] No se pudo preparar el CSV: {e}");
        }
    }

    void AppendCSV(string path, BenchmarkCase c, int repetition, Stats s)
    {
        try
        {
            AnalyzeStats(s, out string bottleneck, out string rating, out string summary);
            using var w = new StreamWriter(path, true);
            string rp = DetectRenderPipeline();
            string gapi = SystemInfo.graphicsDeviceType.ToString();
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            w.WriteLine(string.Join(",",
                Escape(timestamp), Escape(CurrentCaseName), repetition,
                c.gridX, c.gridY, c.gridX * c.gridY,
                c.spacing.ToString(CultureInfo.InvariantCulture),
                c.enableGPUInstancing,
                c.shadowCasting != ShadowCastingMode.Off,
                c.receiveShadows,
                Escape(c.enabledKeywords),
                Escape(c.disabledKeywords),

                Math.Round(s.cpuAvg, 2).ToString("F2", CultureInfo.InvariantCulture),
                Math.Round(s.cpuP95, 2).ToString("F2", CultureInfo.InvariantCulture),
                Math.Round(s.cpuMin, 2).ToString("F2", CultureInfo.InvariantCulture),
                Math.Round(s.cpuMax, 2).ToString("F2", CultureInfo.InvariantCulture),

                Math.Round(s.gpuAvg, 2).ToString("F2", CultureInfo.InvariantCulture),
                Math.Round(s.gpuP95, 2).ToString("F2", CultureInfo.InvariantCulture),
                Math.Round(s.gpuMin, 2).ToString("F2", CultureInfo.InvariantCulture),
                Math.Round(s.gpuMax, 2).ToString("F2", CultureInfo.InvariantCulture),

                Math.Round(s.fpsAvg, 1).ToString("F1", CultureInfo.InvariantCulture),
                Math.Round(s.fpsP95, 1).ToString("F1", CultureInfo.InvariantCulture),
                Math.Round(s.fpsMin, 1).ToString("F1", CultureInfo.InvariantCulture),
                Math.Round(s.fpsMax, 1).ToString("F1", CultureInfo.InvariantCulture),

                Escape(Application.unityVersion),
                Escape(rp),
                Escape(gapi),
                Escape(SystemInfo.deviceModel),
                Escape(SystemInfo.operatingSystem),
                Escape(bottleneck),
                Escape(rating),
                Escape(summary)));
        }
        catch (Exception e)
        {
            Debug.LogError($"[ShaderBench] Error al escribir CSV: {e}");
        }
    }

    string Escape(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        if (s.Contains(",") || s.Contains("\"")) return $"\"{s.Replace("\"", "\"\"")}\"";
        return s;
    }

    string DetectRenderPipeline()
    {
#if UNITY_2019_1_OR_NEWER
        if (GraphicsSettings.currentRenderPipeline != null)
            return GraphicsSettings.currentRenderPipeline.GetType().Name;
#endif
        return "Built-in (BIRP)";
    }

    IEnumerator Phase(string phaseName, float seconds, bool collectSamples)
    {
        CurrentPhase = phaseName;
        CurrentPhaseDuration = Mathf.Max(0.01f, seconds);
        CurrentPhaseElapsed = 0f;

        GC.Collect();
        GC.WaitForPendingFinalizers();

        float start = Time.realtimeSinceStartup;
        while (Time.realtimeSinceStartup - start < seconds)
        {
            // Espera al final del frame
            yield return new WaitForEndOfFrame();

            // Captura timings del último frame
            FrameTimingManager.CaptureFrameTimings();
            uint count = FrameTimingManager.GetLatestTimings(1, _ftBuffer);
            if (count > 0)
            {
                var ft = _ftBuffer[0];
                InstantCPUms = ft.cpuFrameTime;
                InstantGPUms = ft.gpuFrameTime;

                // salvaguardas
                if (InstantCPUms <= 0) InstantCPUms = Time.unscaledDeltaTime * 1000.0;
                if (InstantGPUms <= 0) InstantGPUms = InstantCPUms;
            }
            else
            {
                InstantCPUms = Time.unscaledDeltaTime * 1000.0;
                InstantGPUms = InstantCPUms;
            }

            // >>>>>>>>>>>>>>>>>>  ¡ESTO FALTABA!  <<<<<<<<<<<<<<<<<<
            if (collectSamples)
            {
                _samplesCPU.Add(InstantCPUms);
                _samplesGPU.Add(InstantGPUms);
            }
            // >>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>>

            CurrentPhaseElapsed = Time.realtimeSinceStartup - start;
        }
    }


    void BuildCase(BenchmarkCase c)
    {
        ClearSpawned();

        if (_root == null)
        {
            var rootGo = new GameObject("ShaderBenchRoot");
            _root = rootGo.transform;
        }

        if (c.prefab == null)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            c.prefab = cube;
        }

        float offX = (c.gridX - 1) * c.spacing * 0.5f;
        float offY = (c.gridY - 1) * c.spacing * 0.5f;

        for (int y = 0; y < c.gridY; y++)
            for (int x = 0; x < c.gridX; x++)
            {
                var go = Instantiate(c.prefab, _root);
                go.transform.position = new Vector3(x * c.spacing - offX, 0f, y * c.spacing - offY);

                var renderers = go.GetComponentsInChildren<Renderer>(true);
                foreach (var r in renderers)
                    c.ApplyToRenderer(r);

                _spawned.Add(go);
            }
    }

    void ClearSpawned()
    {
        foreach (var go in _spawned) if (go) Destroy(go);
        _spawned.Clear();
    }

    void Cleanup() => ClearSpawned();

    // =================== Estadística ===================
    struct Stats
    {
        public double cpuAvg, cpuP95, cpuMin, cpuMax;
        public double gpuAvg, gpuP95, gpuMin, gpuMax;
        public double fpsAvg, fpsP95, fpsMin, fpsMax;
    }

    Stats ComputeStats(List<double> cpuMs, List<double> gpuMs)
    {
        int nCPU = cpuMs?.Count ?? 0;
        int nGPU = gpuMs?.Count ?? 0;
        int m = Math.Min(nCPU, nGPU);
        if (m == 0) return default;

        var fps = new List<double>(m);
        for (int i = 0; i < m; i++)
        {
            double boundMs = Math.Max(cpuMs[i], gpuMs[i]);
            fps.Add(1000.0 / Math.Max(boundMs, 0.0001));
        }

        return new Stats
        {
            cpuAvg = Avg(cpuMs),
            cpuP95 = Percentile(cpuMs, 95),
            cpuMin = Min(cpuMs),
            cpuMax = Max(cpuMs),

            gpuAvg = Avg(gpuMs),
            gpuP95 = Percentile(gpuMs, 95),
            gpuMin = Min(gpuMs),
            gpuMax = Max(gpuMs),

            fpsAvg = Avg(fps),
            fpsP95 = Percentile(fps, 95),
            fpsMin = Min(fps),
            fpsMax = Max(fps),
        };
    }

    double Avg(List<double> a) { if (a == null || a.Count == 0) return 0; double s = 0; for (int i = 0; i < a.Count; i++) s += a[i]; return s / a.Count; }
    double Min(List<double> a) { if (a == null || a.Count == 0) return 0; double m = double.MaxValue; for (int i = 0; i < a.Count; i++) if (a[i] < m) m = a[i]; return m == double.MaxValue ? 0 : m; }
    double Max(List<double> a) { if (a == null || a.Count == 0) return 0; double m = double.MinValue; for (int i = 0; i < a.Count; i++) if (a[i] > m) m = a[i]; return m == double.MinValue ? 0 : m; }
    double Percentile(List<double> a, double p)
    {
        if (a == null || a.Count == 0) return 0;
        var tmp = new List<double>(a); tmp.Sort();
        double rank = (p / 100.0) * (tmp.Count - 1);
        int lo = (int)Math.Floor(rank), hi = (int)Math.Ceiling(rank);
        double t = rank - lo;
        double loVal = tmp[Mathf.Clamp(lo, 0, tmp.Count - 1)];
        double hiVal = tmp[Mathf.Clamp(hi, 0, tmp.Count - 1)];
        return loVal + (hiVal - loVal) * t;
    }

    // =================== Auto-Framing Cámara ===================
    void AutoFrameCameraForCase(BenchmarkCase c)
    {
        if (_cam == null) return;
        if (!TryComputeRootBounds(out Bounds b)) { ApplyManualCamera(); return; }

        switch (cameraFitMode)
        {
            case CameraFitMode.Manual: ApplyManualCamera(); break;
            case CameraFitMode.AutoFitTopDownOrtho: ApplyTopDownOrtho(b); break;
            case CameraFitMode.AutoFitIsoPerspective: ApplyIsoPerspectiveFit(b); break;
        }
    }

    void ApplyManualCamera()
    {
        _cam.orthographic = false;
        _cam.transform.position = cameraPosition;
        _cam.transform.rotation = Quaternion.Euler(cameraEuler);
        _cam.nearClipPlane = 0.1f; _cam.farClipPlane = 2000f;
    }

    void ApplyTopDownOrtho(Bounds b)
    {
        _cam.orthographic = true;
        float height = Mathf.Max(b.extents.y * 4f, 10f) * orthoHeightBoost;
        _cam.transform.position = new Vector3(b.center.x, b.center.y + height, b.center.z);
        _cam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        float halfW = Mathf.Max(b.extents.x, b.extents.z);
        _cam.orthographicSize = halfW * (1f + frameMargin);
        _cam.nearClipPlane = 0.1f;
        _cam.farClipPlane = height + b.extents.y * 4f + 200f;
    }

    void ApplyIsoPerspectiveFit(Bounds b)
    {
        _cam.orthographic = false;
        var euler = new Vector3(Mathf.Clamp(isoPitchYaw.x, 1f, 89f), isoPitchYaw.y, 0f);
        _cam.transform.rotation = Quaternion.Euler(euler);

        Vector3 fwd = _cam.transform.forward;
        Vector3 target = b.center;
        float low = 0.1f, high = 10000f, chosen = high;
        Vector3[] corners = GetBoundsCorners(b);

        for (int i = 0; i < 24; i++)
        {
            float mid = 0.5f * (low + high);
            Vector3 pos = target - fwd * mid;
            _cam.transform.position = pos;

            if (BoundsWithinViewportWithMargin(corners, _cam, frameMargin)) { chosen = mid; high = mid; }
            else { low = mid; }
        }

        _cam.transform.position = target - fwd * chosen;
        float rad = b.extents.magnitude;
        _cam.nearClipPlane = Mathf.Max(0.05f, chosen - rad - 50f);
        _cam.farClipPlane = chosen + rad + 200f;
        if (_cam.farClipPlane <= _cam.nearClipPlane + 1f) _cam.farClipPlane = _cam.nearClipPlane + 1f;
    }

    static bool BoundsWithinViewportWithMargin(Vector3[] corners, Camera cam, float margin)
    {
        float min = margin, max = 1f - margin;
        for (int i = 0; i < corners.Length; i++)
        {
            Vector3 vp = cam.WorldToViewportPoint(corners[i]);
            if (vp.z <= 0f) return false;
            if (vp.x < min || vp.x > max) return false;
            if (vp.y < min || vp.y > max) return false;
        }
        return true;
    }

    static Vector3[] GetBoundsCorners(Bounds b)
    {
        var c = new Vector3[8];
        Vector3 min = b.min, max = b.max;
        c[0] = new Vector3(min.x, min.y, min.z);
        c[1] = new Vector3(max.x, min.y, min.z);
        c[2] = new Vector3(min.x, min.y, max.z);
        c[3] = new Vector3(max.x, min.y, max.z);
        c[4] = new Vector3(min.x, max.y, min.z);
        c[5] = new Vector3(max.x, max.y, min.z);
        c[6] = new Vector3(min.x, max.y, max.z);
        c[7] = new Vector3(max.x, max.y, max.z);
        return c;
    }

    bool TryComputeRootBounds(out Bounds b)
    {
        b = new Bounds(Vector3.zero, Vector3.zero);
        if (_root == null) return false;
        var renderers = _root.GetComponentsInChildren<Renderer>(true);
        bool any = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null || !r.enabled) continue;
            if (!any) { b = r.bounds; any = true; }
            else b.Encapsulate(r.bounds);
        }
        return any;
    }

    // =================== Google Sheets: helpers ===================
    private SheetRow BuildSheetRow(BenchmarkCase c, int rep, Stats s)
    {
        AnalyzeStats(s, out string bottleneck, out string rating, out string summary);

        return new SheetRow
        {
            Timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            CaseName = CurrentCaseName,
            Repetition = rep,
            GridX = c.gridX,
            GridY = c.gridY,
            Count = c.gridX * c.gridY,
            Spacing = c.spacing.ToString(CultureInfo.InvariantCulture),
            GPUInstancing = c.enableGPUInstancing,
            Shadows = c.shadowCasting != ShadowCastingMode.Off,
            ReceiveShadows = c.receiveShadows,
            EnabledKW = c.enabledKeywords,
            DisabledKW = c.disabledKeywords,

            CPU_ms_avg = Math.Round(s.cpuAvg, 2),
            CPU_ms_p95 = Math.Round(s.cpuP95, 2),
            CPU_ms_min = Math.Round(s.cpuMin, 2),
            CPU_ms_max = Math.Round(s.cpuMax, 2),
            GPU_ms_avg = Math.Round(s.gpuAvg, 2),
            GPU_ms_p95 = Math.Round(s.gpuP95, 2),
            GPU_ms_min = Math.Round(s.gpuMin, 2),
            GPU_ms_max = Math.Round(s.gpuMax, 2),
            FPS_avg = Math.Round(s.fpsAvg, 1),
            FPS_p95 = Math.Round(s.fpsP95, 1),
            FPS_min = Math.Round(s.fpsMin, 1),
            FPS_max = Math.Round(s.fpsMax, 1),

            UnityVersion = Application.unityVersion,
            RenderPipeline = DetectRenderPipeline(),
            GraphicsAPI = SystemInfo.graphicsDeviceType.ToString(),
            DeviceModel = SystemInfo.deviceModel,
            OS = SystemInfo.operatingSystem,
            Bottleneck = bottleneck,
            Quest3Rating = rating,
            Summary = summary
        };
    }

    void AnalyzeStats(Stats s, out string bottleneck, out string rating, out string summary)
    {
        double frameBudget = 1000.0 / Mathf.Max(1, targetFPS);
        double cpu = s.cpuP95;
        double gpu = s.gpuP95;
        bottleneck = cpu > gpu ? "CPU" : gpu > cpu ? "GPU" : "Balanced";
        double worst = Math.Max(cpu, gpu);
        if (worst <= frameBudget) rating = "Good";
        else if (worst <= frameBudget * 1.5) rating = "Medium";
        else rating = "Bad";
        summary = $"{bottleneck} bottleneck - p95 {worst:F2}ms vs {frameBudget:F2}ms";
    }
    void EnqueueRow(SheetRow r)
    {
        _pendingRows.Add(r);
        TrySavePendingToDisk(); // persistencia por si se cierra/crashea
    }

    IEnumerator UploadPendingRowsCoroutine()
    {
        if (!uploadToGoogleSheets || string.IsNullOrEmpty(googleScriptUrl) || _pendingRows.Count == 0)
            yield break;

        // Tamaño del chunk a enviar
        int take = Mathf.Min(uploadBatchSize > 0 ? uploadBatchSize : _pendingRows.Count, _pendingRows.Count);

        // --- Payload con sheetName + action="append" ---
        var payload = new SheetPayload
        {
            token = googleScriptToken,
            sheetName = _currentSheetName,
            action = "append",
            rows = new List<SheetRow>(_pendingRows.GetRange(0, take))
        };

        string json = JsonUtility.ToJson(payload);

        using (var req = new UnityWebRequest(googleScriptUrl, UnityWebRequest.kHttpVerbPOST))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool ok = req.result == UnityWebRequest.Result.Success;
#else
        bool ok = !req.isHttpError && !req.isNetworkError;
#endif
            if (ok && req.responseCode >= 200 && req.responseCode < 300)
            {
                // Éxito → retira del buffer y guarda
                _pendingRows.RemoveRange(0, take);
                TrySavePendingToDisk();

                LastUploadStatus = $"Sheets: OK ({take} filas)";
                _lastUploadStatusTime = Time.realtimeSinceStartup;

                // Si quedan por subir, envía más
                if (_pendingRows.Count > 0)
                    yield return UploadPendingRowsCoroutine();
            }
            else
            {
                LastUploadStatus = $"Sheets ERROR {req.responseCode}: {req.error}";
                _lastUploadStatusTime = Time.realtimeSinceStartup;
                Debug.LogWarning($"[ShaderBench] Upload failed: {req.responseCode} - {req.error}\n{req.downloadHandler?.text}");
            }
        }
    }


    void TrySavePendingToDisk()
    {
        try
        {
            var wrapper = new SheetPayload { token = googleScriptToken, rows = new List<SheetRow>(_pendingRows) };
            string json = JsonUtility.ToJson(wrapper);
            File.WriteAllText(PendingFilePath, json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ShaderBench] No se pudo guardar pendientes: {e}");
        }
    }

    void TryLoadPendingFromDisk()
    {
        try
        {
            if (!File.Exists(PendingFilePath)) return;
            string json = File.ReadAllText(PendingFilePath);
            var wrapper = JsonUtility.FromJson<SheetPayload>(json);
            _pendingRows.Clear();
            if (wrapper != null && wrapper.rows != null) _pendingRows.AddRange(wrapper.rows);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ShaderBench] No se pudo cargar pendientes: {e}");
        }
    }

    void OnGUI()
    {
        const int pad = 10, line = 20;
        int w = 560;

        Rect r = new Rect(pad, pad, w, 230);
        GUI.Box(r, "ShaderBench");

        int y = pad + 25;
        Label(pad + 10, ref y, $"Estado: {(IsRunning ? "Ejecutando" : HasFinished ? "Terminado" : "Listo")}");
        Label(pad + 10, ref y, $"Caso: {CurrentCaseName}  ({CurrentIndex + 1}/{Mathf.Max(1, TotalCases)})");
        Label(pad + 10, ref y, $"Repetición: {Mathf.Max(1, CurrentRepetition)}/{Mathf.Max(1, Repetitions)}");
        Label(pad + 10, ref y, $"Fase: {CurrentPhase}  ({CurrentPhaseElapsed:F1}/{Mathf.Max(0.01f, CurrentPhaseDuration):F1}s)");
        Label(pad + 10, ref y, $"FPS inst.: {InstantFPS:F1} | CPU: {InstantCPUms:F2} ms | GPU: {InstantGPUms:F2} ms");
        Label(pad + 10, ref y, $"Cam: {cameraFitMode}  |  Margen: {frameMargin * 100f:F0}%");

        float t = Mathf.InverseLerp(0f, Mathf.Max(0.01f, CurrentPhaseDuration), CurrentPhaseElapsed);
        Rect bar = new Rect(pad + 10, y + 4, w - 20, 14);
        GUI.Box(bar, GUIContent.none);
        var fill = new Rect(bar.x + 2, bar.y + 2, (bar.width - 4) * Mathf.Clamp01(t), bar.height - 4);
        GUI.DrawTexture(fill, Texture2D.whiteTexture);
        y += line + 6;

        if (!string.IsNullOrEmpty(LastCsvPath))
            Label(pad + 10, ref y, $"CSV: {LastCsvPath}");

        if (!string.IsNullOrEmpty(LastUploadStatus) && Time.realtimeSinceStartup - _lastUploadStatusTime < 10f)
            Label(pad + 10, ref y, LastUploadStatus);

        Label(pad + 10, ref y, "Teclas: [ESPACIO] iniciar  |  [ESC] cancelar");
    }

    void Label(int x, ref int y, string text)
    {
        GUI.Label(new Rect(x, y, 4000, 20), text);
        y += 20;
    }
    IEnumerator FinalizeRunCoroutine()
    {
        var payload = new SheetPayload
        {
            token = googleScriptToken,
            sheetName = _currentSheetName,
            action = "finalize",
            targetFps = targetFPS
        };
        string json = JsonUtility.ToJson(payload);

        using (var req = new UnityWebRequest(googleScriptUrl, UnityWebRequest.kHttpVerbPOST))
        {
            byte[] bodyRaw = Encoding.UTF8.GetBytes(json);
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            yield return req.SendWebRequest();

#if UNITY_2020_2_OR_NEWER
            bool ok = req.result == UnityWebRequest.Result.Success;
#else
        bool ok = !req.isHttpError && !req.isNetworkError;
#endif
            if (ok && req.responseCode >= 200 && req.responseCode < 300)
            {
                LastUploadStatus = $"Sheets finalize OK: {_currentSheetName}";
                _lastUploadStatusTime = Time.realtimeSinceStartup;
            }
            else
            {
                LastUploadStatus = $"Sheets finalize ERROR {req.responseCode}: {req.error}";
                _lastUploadStatusTime = Time.realtimeSinceStartup;
                Debug.LogWarning($"[ShaderBench] Finalize failed: {req.responseCode} - {req.error}\n{req.downloadHandler?.text}");
            }
        }
    }
}