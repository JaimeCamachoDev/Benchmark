// Assets/PerfLab/PerfLabAnalyzer.cs
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// PerfLab: arrastra un prefab al campo "Subject Prefab", dale a Play y lee
/// en pantalla / en el mundo cuántos ms de CPU y GPU cuesta ese elemento en
/// concreto, comparado contra el presupuesto de su departamento.
///
/// Método: mide la escena con el elemento apagado (línea base) y luego con
/// el elemento encendido; la diferencia es el coste real de ese elemento.
/// No requiere grids, materiales ni configuración adicional.
/// </summary>
public class PerfLabAnalyzer : MonoBehaviour
{
    [Header("Elemento a analizar (arrástralo aquí)")]
    [Tooltip("Arrastra un prefab del Proyecto (se instanciará al darle a Play) o un objeto que ya esté en la escena (se usará directamente, sin clonarlo).")]
    public GameObject subjectPrefab;

    [Header("Departamento y presupuesto")]
    public PerfLabDepartment department = PerfLabDepartment.VFX;
    [Tooltip("Hz objetivo del dispositivo (Quest 3 / Pico 4). Define el presupuesto total de frame.")]
    public PerfLabTargetHz targetHz = PerfLabTargetHz.Hz72;
    [Tooltip("Si es mayor que 0 ms, sustituye al 25% automático por un límite fijo para este elemento concreto.")]
    [Min(0f)] public float customBudgetMs = 0f;

    [Header("Medición")]
    [Tooltip("Segundos midiendo la escena SIN el elemento (línea base) antes de activarlo.")]
    [Min(0.1f)] public float baselineSeconds = 1.5f;
    [Tooltip("Ventana de tiempo (segundos) usada para el promedio en vivo una vez el elemento está activo.")]
    [Min(0.1f)] public float rollingWindowSeconds = 1.0f;

    [Header("Visualización")]
    public bool showWorldPanel = true;
    public bool showScreenOverlay = true;
    public Vector3 worldPanelOffset = new Vector3(0f, 0.3f, 0f);

    // =================== Resultados públicos ===================
    public double CostCpuMs { get; private set; }
    public double CostGpuMs { get; private set; }
    public double CostTotalMs => Math.Max(CostCpuMs, CostGpuMs);
    public double BudgetMs { get; private set; }
    public bool IsWithinBudget { get; private set; } = true;
    public string StatusText { get; private set; } = "Preparando...";

    GameObject _subjectInstance;
    readonly FrameTiming[] _ftBuffer = new FrameTiming[1];
    double _baselineCpuAvg;
    double _baselineGpuAvg;
    readonly Queue<SampleEntry> _liveSamples = new();

    struct SampleEntry
    {
        public float Time;
        public double Cpu;
        public double Gpu;
    }

    Transform _worldPanel;
    Text _worldText;
    Camera _cam;

    void Awake()
    {
        _cam = Camera.main;
        BudgetMs = customBudgetMs > 0f ? customBudgetMs : (1000.0 / (int)targetHz) / 4.0;

        if (subjectPrefab != null)
        {
            // Si ya es un objeto de la escena (no un prefab del Proyecto), se usa
            // directamente en su sitio: clonarlo dejaría el original sin tocar y
            // parecería que "nunca se activa" porque estarías mirando el objeto
            // equivocado.
            bool isSceneInstance = subjectPrefab.scene.IsValid();
            _subjectInstance = isSceneInstance
                ? subjectPrefab
                : Instantiate(subjectPrefab, transform.position, transform.rotation, transform);

            _subjectInstance.SetActive(false);
        }

        if (showWorldPanel) BuildWorldPanel();
    }

    void Start()
    {
        StartCoroutine(Run());
    }

    IEnumerator Run()
    {
        StatusText = "Midiendo línea base (elemento apagado)...";
        yield return MeasureWindow(baselineSeconds, (cpu, gpu) =>
        {
            _baselineCpuAvg = cpu;
            _baselineGpuAvg = gpu;
        });

        if (_subjectInstance != null) _subjectInstance.SetActive(true);
        StatusText = "Midiendo coste en vivo...";

        while (true)
        {
            SampleFrame(out double cpu, out double gpu);
            _liveSamples.Enqueue(new SampleEntry { Time = Time.realtimeSinceStartup, Cpu = cpu, Gpu = gpu });
            TrimOldSamples();

            double avgCpu = AverageCpu(_liveSamples);
            double avgGpu = AverageGpu(_liveSamples);

            CostCpuMs = Math.Max(0.0, avgCpu - _baselineCpuAvg);
            CostGpuMs = Math.Max(0.0, avgGpu - _baselineGpuAvg);
            IsWithinBudget = CostTotalMs <= BudgetMs;

            yield return null;
        }
    }

    IEnumerator MeasureWindow(float seconds, Action<double, double> onDone)
    {
        var cpuList = new List<double>();
        var gpuList = new List<double>();
        float start = Time.realtimeSinceStartup;

        while (Time.realtimeSinceStartup - start < seconds)
        {
            // OJO: WaitForEndOfFrame() no se dispara si la vista Game no se está
            // renderizando (p. ej. con la pestaña Scene en foco), lo que dejaba la
            // corrutina colgada aquí para siempre y el objeto nunca se activaba.
            // yield return null avanza un frame sin depender de que se renderice.
            yield return null;
            SampleFrame(out double cpu, out double gpu);
            cpuList.Add(cpu);
            gpuList.Add(gpu);
        }

        onDone(Average(cpuList), Average(gpuList));
    }

    void SampleFrame(out double cpuMs, out double gpuMs)
    {
        FrameTimingManager.CaptureFrameTimings();
        uint count = FrameTimingManager.GetLatestTimings(1, _ftBuffer);
        if (count > 0)
        {
            var ft = _ftBuffer[0];
            cpuMs = ft.cpuFrameTime > 0 ? ft.cpuFrameTime : Time.unscaledDeltaTime * 1000.0;
            gpuMs = ft.gpuFrameTime > 0 ? ft.gpuFrameTime : cpuMs;
        }
        else
        {
            cpuMs = Time.unscaledDeltaTime * 1000.0;
            gpuMs = cpuMs;
        }
    }

    void TrimOldSamples()
    {
        float now = Time.realtimeSinceStartup;
        while (_liveSamples.Count > 0 && now - _liveSamples.Peek().Time > rollingWindowSeconds)
            _liveSamples.Dequeue();
    }

    static double Average(List<double> values)
    {
        if (values.Count == 0) return 0.0;
        double sum = 0.0;
        for (int i = 0; i < values.Count; i++) sum += values[i];
        return sum / values.Count;
    }

    static double AverageCpu(Queue<SampleEntry> samples)
    {
        if (samples.Count == 0) return 0.0;
        double sum = 0.0;
        foreach (var s in samples) sum += s.Cpu;
        return sum / samples.Count;
    }

    static double AverageGpu(Queue<SampleEntry> samples)
    {
        if (samples.Count == 0) return 0.0;
        double sum = 0.0;
        foreach (var s in samples) sum += s.Gpu;
        return sum / samples.Count;
    }

    // =================== Panel flotante en el mundo (visible en VR) ===================
    void BuildWorldPanel()
    {
        var panelGo = new GameObject("PerfLabPanel");
        panelGo.transform.SetParent(transform, false);

        var canvas = panelGo.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        panelGo.AddComponent<CanvasScaler>();
        panelGo.AddComponent<GraphicRaycaster>();

        var rect = panelGo.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(500f, 220f);
        panelGo.transform.localScale = Vector3.one * 0.0015f;

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(panelGo.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        _worldText = textGo.AddComponent<Text>();
        _worldText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (_worldText.font == null) _worldText.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        _worldText.fontSize = 26;
        _worldText.alignment = TextAnchor.MiddleCenter;
        _worldText.color = Color.white;
        _worldText.horizontalOverflow = HorizontalWrapMode.Overflow;

        _worldPanel = panelGo.transform;
    }

    void LateUpdate()
    {
        if (_cam == null) _cam = Camera.main;

        if (_worldPanel != null && _cam != null)
        {
            Vector3 basePos = transform.position;
            if (TryGetSubjectBounds(out Bounds b))
                basePos = new Vector3(b.center.x, b.max.y, b.center.z);

            _worldPanel.position = basePos + worldPanelOffset;
            _worldPanel.rotation = Quaternion.LookRotation(_worldPanel.position - _cam.transform.position);
        }

        if (_worldText != null)
        {
            _worldText.text = BuildDisplayText();
            _worldText.color = IsWithinBudget ? new Color(0.35f, 1f, 0.35f) : new Color(1f, 0.35f, 0.35f);
        }
    }

    bool TryGetSubjectBounds(out Bounds bounds)
    {
        bounds = new Bounds(transform.position, Vector3.zero);
        if (_subjectInstance == null) return false;

        var renderers = _subjectInstance.GetComponentsInChildren<Renderer>(true);
        bool any = false;
        foreach (var r in renderers)
        {
            if (!any) { bounds = r.bounds; any = true; }
            else bounds.Encapsulate(r.bounds);
        }
        return any;
    }

    string BuildDisplayText()
    {
        string name = subjectPrefab != null ? subjectPrefab.name : "(sin elemento)";
        string okText = IsWithinBudget ? "DENTRO DEL PRESUPUESTO" : "FUERA DE PRESUPUESTO";
        return $"PerfLab — {name}\n" +
               $"Depto: {department}  |  Cap: {BudgetMs:F2} ms @ {(int)targetHz} Hz\n" +
               $"CPU: {CostCpuMs:F2} ms   GPU: {CostGpuMs:F2} ms\n" +
               $"Coste: {CostTotalMs:F2} ms  →  {okText}";
    }

    // =================== Overlay de pantalla (editor / desktop) ===================
    void OnGUI()
    {
        if (!showScreenOverlay) return;

        var style = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14, wordWrap = true };
        GUI.Box(new Rect(10, 10, 420, 100), "PerfLab");

        GUILayout.BeginArea(new Rect(20, 32, 400, 90));
        GUILayout.Label(StatusText, style);
        GUILayout.Label($"Depto: <b>{department}</b>  |  Cap: <b>{BudgetMs:F2} ms</b> @ {(int)targetHz} Hz", style);
        GUILayout.Label($"CPU: <b>{CostCpuMs:F2} ms</b>   GPU: <b>{CostGpuMs:F2} ms</b>", style);

        string okColor = IsWithinBudget ? "#55FF55" : "#FF5555";
        string okLabel = IsWithinBudget ? "DENTRO" : "FUERA";
        GUILayout.Label($"Coste total: <b><color={okColor}>{CostTotalMs:F2} ms</color></b>  ({okLabel} de presupuesto)", style);
        GUILayout.EndArea();
    }
}
