// Assets/PerfLab/PerfLabAnalyzer.cs
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// PerfLab: arrastra un elemento al campo "Subject Prefab", dale a Play y lee
/// en pantalla / en el mundo cuántos ms cuesta ese elemento en concreto,
/// comparado contra el presupuesto de su departamento (25% del frame a los
/// Hz elegidos). Si no entra en el cap, el aviso se pone grande y en rojo.
///
/// Método: mide la escena con el elemento apagado (línea base) y luego con
/// el elemento encendido; la diferencia es el coste real de ese elemento.
/// Todo ocurre en Update(), sin corrutinas ni esperas ligadas al renderizado,
/// para que funcione igual mires la pestaña Game o la Scene mientras pruebas.
/// </summary>
public class PerfLabAnalyzer : MonoBehaviour
{
    enum Phase { Baseline, Measuring }

    [Header("Elemento a analizar (arrástralo aquí)")]
    [Tooltip("Arrastra un prefab del Proyecto (se instanciará al darle a Play) o un objeto que ya esté en la escena (se usará directamente, sin clonarlo).")]
    public GameObject subjectPrefab;

    [Header("Departamento y presupuesto")]
    public PerfLabDepartment department = PerfLabDepartment.VFX;
    [Tooltip("Hz objetivo del dispositivo (Quest 3 / Pico 4). El cap es siempre el 25% del presupuesto de frame a esos Hz.")]
    public PerfLabTargetHz targetHz = PerfLabTargetHz.Hz72;

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

    Phase _phase = Phase.Baseline;
    float _phaseStartTime;
    readonly List<double> _baselineCpuSamples = new();
    readonly List<double> _baselineGpuSamples = new();
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
    Image _worldBackground;
    Camera _cam;

    void Awake()
    {
        _cam = Camera.main;
        BudgetMs = (1000.0 / (int)targetHz) / 4.0;

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
            Debug.Log($"[PerfLab] Elemento '{_subjectInstance.name}' listo, midiendo línea base durante {baselineSeconds:F1}s antes de activarlo.");
        }
        else
        {
            Debug.LogWarning("[PerfLab] No hay ningún elemento asignado en 'Subject Prefab'. Arrastra un prefab o un objeto de la escena a ese campo.");
        }

        _phaseStartTime = Time.realtimeSinceStartup;
        if (showWorldPanel) BuildWorldPanel();
    }

    void Update()
    {
        SampleFrame(out double cpu, out double gpu);

        if (_phase == Phase.Baseline)
        {
            _baselineCpuSamples.Add(cpu);
            _baselineGpuSamples.Add(gpu);

            float elapsed = Time.realtimeSinceStartup - _phaseStartTime;
            StatusText = $"Midiendo línea base (elemento apagado)... {elapsed:F1}/{baselineSeconds:F1}s";

            if (elapsed >= baselineSeconds)
            {
                _baselineCpuAvg = Average(_baselineCpuSamples);
                _baselineGpuAvg = Average(_baselineGpuSamples);

                if (_subjectInstance != null)
                {
                    _subjectInstance.SetActive(true);
                    Debug.Log($"[PerfLab] Línea base lista (CPU {_baselineCpuAvg:F2} ms / GPU {_baselineGpuAvg:F2} ms). Elemento activado, midiendo en vivo.");
                }

                _phase = Phase.Measuring;
                StatusText = "Midiendo coste en vivo...";
            }
        }
        else
        {
            _liveSamples.Enqueue(new SampleEntry { Time = Time.realtimeSinceStartup, Cpu = cpu, Gpu = gpu });
            TrimOldSamples();

            double avgCpu = AverageCpu(_liveSamples);
            double avgGpu = AverageGpu(_liveSamples);

            CostCpuMs = Math.Max(0.0, avgCpu - _baselineCpuAvg);
            CostGpuMs = Math.Max(0.0, avgGpu - _baselineGpuAvg);
            IsWithinBudget = CostTotalMs <= BudgetMs;
        }
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

        _worldBackground = panelGo.AddComponent<Image>();
        _worldBackground.color = new Color(0f, 0f, 0f, 0.6f);

        var textGo = new GameObject("Text");
        textGo.transform.SetParent(panelGo.transform, false);
        var textRect = textGo.AddComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 10f);
        textRect.offsetMax = new Vector2(-10f, -10f);

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

        bool measuring = _phase == Phase.Measuring;
        Color statusColor = !measuring
            ? new Color(1f, 0.85f, 0.2f) // amarillo mientras mide la línea base
            : (IsWithinBudget ? new Color(0.25f, 0.9f, 0.25f) : new Color(1f, 0.15f, 0.15f));

        if (_worldText != null)
        {
            _worldText.text = BuildDisplayText();
            _worldText.color = measuring ? statusColor : Color.white;
            _worldText.fontSize = (measuring && !IsWithinBudget) ? 34 : 26;
        }

        if (_worldBackground != null)
        {
            _worldBackground.color = measuring
                ? new Color(statusColor.r, statusColor.g, statusColor.b, IsWithinBudget ? 0.5f : 0.85f)
                : new Color(0f, 0f, 0f, 0.6f);
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

        if (_phase == Phase.Baseline)
            return $"PerfLab — {name}\n{StatusText}";

        string verdict = IsWithinBudget ? "✔ DENTRO DEL PRESUPUESTO" : "✖ NO VÁLIDO — FUERA DE PRESUPUESTO";
        return $"{name}  ({department} @ {(int)targetHz} Hz, cap {BudgetMs:F2} ms)\n" +
               $"CPU {CostCpuMs:F2} ms   GPU {CostGpuMs:F2} ms   Total {CostTotalMs:F2} ms\n" +
               $"{verdict}";
    }

    // =================== Overlay de pantalla (editor / desktop) ===================
    void OnGUI()
    {
        if (!showScreenOverlay) return;

        bool measuring = _phase == Phase.Measuring;
        bool fail = measuring && !IsWithinBudget;

        var boxStyle = new GUIStyle(GUI.skin.box);
        var labelStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = 14, wordWrap = true };
        var verdictStyle = new GUIStyle(GUI.skin.label) { richText = true, fontSize = fail ? 26 : 18, fontStyle = FontStyle.Bold, wordWrap = true };

        float height = fail ? 150 : 120;
        var rect = new Rect(10, 10, 460, height);

        var prevColor = GUI.color;
        if (fail) GUI.color = new Color(1f, 0.35f, 0.35f);
        GUI.Box(rect, "PerfLab");
        GUI.color = prevColor;

        GUILayout.BeginArea(new Rect(20, 32, 440, height - 20));
        GUILayout.Label(StatusText, labelStyle);

        if (measuring)
        {
            GUILayout.Label($"Depto: <b>{department}</b>  |  Cap: <b>{BudgetMs:F2} ms</b> @ {(int)targetHz} Hz", labelStyle);
            GUILayout.Label($"CPU: <b>{CostCpuMs:F2} ms</b>   GPU: <b>{CostGpuMs:F2} ms</b>   Total: <b>{CostTotalMs:F2} ms</b>", labelStyle);

            string verdictColor = IsWithinBudget ? "#55FF55" : "#FF3333";
            string verdictText = IsWithinBudget ? "✔ OK" : "✖ NO VÁLIDO";
            GUILayout.Label($"<color={verdictColor}>{verdictText}</color>", verdictStyle);
        }

        GUILayout.EndArea();
    }
}
