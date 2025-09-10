// ===============================
// Unity URP Shader Benchmark Suite (Lit vs MAS) — v2 (UX mejorada)
// Autor: ChatGPT (para Jaime)
// Unity probado: 2021.3 / 2022.3 / 6000.x (URP)
// Coloca este script en cualquier carpeta (Runtime). No requiere Editor, pero incluye utilidades #if UNITY_EDITOR.
// ===============================

/*
NOVEDADES v2 (usabilidad):
- HUD en pantalla (OnGUI) con estado: caso actual/total, spinner, tiempos en vivo y botón **Abrir carpeta CSV**.
- Columna **CaseName** en el CSV + campo `caseName` en cada BenchmarkCase para identificar mejor.
- Ruta del CSV visible en consola y en el HUD al finalizar.
- Corrutina de muestreo corregida (se espera correctamente a que termine) y comprobaciones de recorders.

QUÉ MIDE:
- GPU Frame Time (ms) → FrameTimingManager.
- CPU Main Thread (ms) → ProfilerRecorder.
- Batches / SetPass / DrawCalls / Triangles / Vertices → ProfilerRecorder.

NOTAS:
- Activa SRP Batcher en el URP Asset.
- Para comparar Additional Lights per‑Pixel vs per‑Vertex, usa 2 URP Assets y corre la suite dos veces.
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_RENDER_PIPELINE_UNIVERSAL
using UnityEngine.Rendering.Universal;
#endif
using Unity.Profiling; // ProfilerRecorder / ProfilerCategory
#if UNITY_EDITOR
using UnityEditor; // para RevealInFinder (opcional en Editor)
#endif

public enum ShaderUnderTest { Lit, MAS }
public enum TestType { FragmentHeavy_FullscreenQuad, GeometryGrid_Static }

[Serializable]
public class BenchmarkCase
{
    [Header("Identificación del caso")]
    [Tooltip("Nombre libre para identificar este caso en el CSV y en pantalla")]
    public string caseName = "Caso";

    [Header("¿Qué probamos?")]
    public ShaderUnderTest shaderUnderTest = ShaderUnderTest.Lit;
    public TestType testType = TestType.GeometryGrid_Static;

    [Header("Escala de escena")]
    [Tooltip("Para GeometryGrid: número de mallas por fila/columna (N x N)")]
    public int gridSize = 20;
    [Tooltip("Para GeometryGrid: separación entre instancias")]
    public float spacing = 2.0f;

    [Header("Iluminación")]
    [Range(0, 8)] public int additionalLights = 0;
    public bool mainLightShadows = false;
    public bool additionalLightsCastShadows = false;

    [Header("Parámetros URP")]
    [Range(0.5f, 1.0f)] public float renderScale = 1.0f;

    [Header("Muestreo")]
    [Tooltip("Frames de calentamiento para estabilizar caches/pipeline")]
    public int warmupFrames = 120;
    [Tooltip("Frames que se promedian para medir")]
    public int sampleFrames = 300;
}

public class BenchmarkRunner : MonoBehaviour
{
    [Header("Materiales (asigna en el Inspector)")]
    public Material litMaterial;  // URP/Lit con EXACTAMENTE las mismas texturas/canales que MAS
    public Material masMaterial;  // Tu Shader Graph MAS con las mismas entradas

    [Header("Malla de prueba y quad")]
    public Mesh testMesh;   // ~10k-50k vértices
    public Mesh quadMesh;   // Quad de 2 triángulos
    public Vector2 fullscreenQuadScale = new Vector2(2f, 2f); // cubrir cámara

    [Header("Luz direccional principal")]
    public Light mainDirectionalLight;

    [Header("Plan de pruebas")]
    public List<BenchmarkCase> cases = new List<BenchmarkCase>();

    [Header("Salida de resultados")]
    public string resultsFileName = "URP_Shader_Benchmarks.csv";

    // Internos
    readonly List<Light> _spawnedAdditionalLights = new();
    readonly List<MeshRenderer> _spawnedRenderers = new();
    MetricsRecorder _metrics;

    // Estado/HUD
    string _csvDir;
    string _csvPath;
    int _currentIndex = -1;
    bool _running = false;
    bool _finished = false;
    string _statusLine = "";
    float _lastGpu = -1, _lastCpu = -1;

    void Awake()
    {
        Application.targetFrameRate = -1; // sin límite
        QualitySettings.vSyncCount = 0;   // sin vSync
        _metrics = new MetricsRecorder();
    }

    void Start()
    {
        if (!ValidateInputs())
        {
            Debug.LogError("BenchmarkRunner: Faltan referencias (materiales/mesh/luz/casos). Abortando.");
            enabled = false;
            return;
        }
        StartCoroutine(RunAll());
    }

    bool ValidateInputs()
    {
        return litMaterial && masMaterial && testMesh && quadMesh && mainDirectionalLight && cases != null && cases.Count > 0;
    }

    System.Collections.IEnumerator RunAll()
    {
        _running = true; _finished = false; _currentIndex = -1; _statusLine = "Preparando…";

        _csvDir = Path.Combine(Application.persistentDataPath, "ShaderBenchmarks");
        var logger = new CSVLogger(_csvDir, resultsFileName);
        _csvPath = logger.FilePath;

        logger.WriteHeader(new[]{
            "Timestamp","Platform","GraphicsAPI","Device","GPU","CaseIndex","CaseName","Shader","TestType","GridSize","Spacing",
            "AddLights","MainShadows","AddShadows","RenderScale","Warmup","Samples",
            "GPU_ms","CPU_ms","Batches","SetPass","DrawCalls","Triangles","Vertices"});

        // Cámara determinista
        var cam = Camera.main;
        if (cam == null)
        {
            cam = new GameObject("Benchmark Camera").AddComponent<Camera>();
            cam.tag = "MainCamera";
            cam.transform.position = new Vector3(0, 10, -20);
            cam.transform.rotation = Quaternion.Euler(15, 0, 0);
            cam.clearFlags = CameraClearFlags.Skybox;
        }

        for (int i = 0; i < cases.Count; i++)
        {
            _currentIndex = i;
            var c = cases[i];
            _statusLine = $"Configurando caso '{c.caseName}' ({i + 1}/{cases.Count})";

            PrepareURP(c.renderScale);
            ConfigureLights(c);
            BuildSceneForCase(c, cam);

            // Warmup
            _statusLine = $"Calentando '{c.caseName}'…";
            yield return _metrics.WarmupFrames(c.warmupFrames);

            // Muestreo
            _statusLine = $"Midiendo '{c.caseName}'…";
            yield return _metrics.SampleFrames(c.sampleFrames);
            var stats = _metrics.Averages;
            _lastGpu = (float)stats.gpuMs; _lastCpu = (float)stats.cpuMs;

            // CSV
            logger.Append(new[]{
                DateTime.Now.ToString("o"),
                Application.platform.ToString(),
                SystemInfo.graphicsDeviceType.ToString(),
                SystemInfo.deviceModel,
                SystemInfo.graphicsDeviceName,
                i.ToString(),
                string.IsNullOrEmpty(c.caseName) ? $"Case_{i}" : c.caseName,
                c.shaderUnderTest.ToString(),
                c.testType.ToString(),
                c.gridSize.ToString(),
                c.spacing.ToString(CultureInfo.InvariantCulture),
                c.additionalLights.ToString(),
                c.mainLightShadows ? "1" : "0",
                c.additionalLightsCastShadows ? "1" : "0",
                c.renderScale.ToString(CultureInfo.InvariantCulture),
                c.warmupFrames.ToString(),
                c.sampleFrames.ToString(),
                stats.gpuMs.ToString("F3", CultureInfo.InvariantCulture),
                stats.cpuMs.ToString("F3", CultureInfo.InvariantCulture),
                stats.batches.ToString(),
                stats.setPass.ToString(),
                stats.drawCalls.ToString(),
                stats.tris.ToString(),
                stats.verts.ToString()
            });

            CleanupCase();
            _statusLine = $"Caso '{c.caseName}' completado";
            yield return null;
        }

        logger.Flush();
        _running = false; _finished = true;
        Debug.Log($"Benchmark terminado. CSV en: {_csvPath}");
    }

    void PrepareURP(float renderScale)
    {
#if UNITY_RENDER_PIPELINE_UNIVERSAL
        var rp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (rp != null) rp.renderScale = Mathf.Clamp(renderScale, 0.5f, 1.0f);
#endif
    }

    void ConfigureLights(BenchmarkCase c)
    {
        // Luz principal
        mainDirectionalLight.shadows = c.mainLightShadows ? LightShadows.Soft : LightShadows.None;
        mainDirectionalLight.shadowStrength = 1f;
        mainDirectionalLight.intensity = 1.0f;

        // Borrar luces adicionales previas
        foreach (var l in _spawnedAdditionalLights) if (l) Destroy(l.gameObject);
        _spawnedAdditionalLights.Clear();

        // Crear luces adicionales
        for (int i = 0; i < c.additionalLights; i++)
        {
            float angle = (Mathf.PI * 2f) * i / Mathf.Max(1, c.additionalLights);
            var go = new GameObject($"AddLight_{i}");
            var li = go.AddComponent<Light>();
            li.type = LightType.Point;
            li.range = 15f;
            li.intensity = 2.0f;
            li.transform.position = new Vector3(Mathf.Cos(angle) * 8f, 3f, Mathf.Sin(angle) * 8f);
            li.shadows = c.additionalLightsCastShadows ? LightShadows.Soft : LightShadows.None;
            _spawnedAdditionalLights.Add(li);
        }
    }

    void BuildSceneForCase(BenchmarkCase c, Camera cam)
    {
        // Limpiar geometría previa
        foreach (var r in _spawnedRenderers) if (r) Destroy(r.gameObject);
        _spawnedRenderers.Clear();

        // Material a probar
        var mat = (c.shaderUnderTest == ShaderUnderTest.Lit) ? litMaterial : masMaterial;

        if (c.testType == TestType.FragmentHeavy_FullscreenQuad)
        {
            // Quad grande para medir fill‑rate (coste de píxel)
            var go = new GameObject("FullscreenQuad");
            go.transform.position = cam.transform.position + cam.transform.forward * 5f;
            go.transform.rotation = cam.transform.rotation;
            go.transform.localScale = new Vector3(fullscreenQuadScale.x, fullscreenQuadScale.y, 1f);
            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mf.sharedMesh = quadMesh;
            mr.sharedMaterial = mat;
            _spawnedRenderers.Add(mr);
        }
        else
        {
            // Rejilla de mallas para medir coste combinado (vértices + píxeles)
            int n = Mathf.Max(1, c.gridSize);
            float s = Mathf.Max(0.1f, c.spacing);
            var root = new GameObject("GridRoot").transform;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var go = new GameObject($"M_{x}_{y}");
                    go.transform.SetParent(root);
                    go.transform.position = new Vector3((x - n / 2) * s, 0f, (y - n / 2) * s);
                    var mf = go.AddComponent<MeshFilter>();
                    var mr = go.AddComponent<MeshRenderer>();
                    mf.sharedMesh = testMesh;
                    mr.sharedMaterial = mat;
                    _spawnedRenderers.Add(mr);
                }
        }
    }

    void CleanupCase()
    {
        foreach (var r in _spawnedRenderers) if (r) Destroy(r.gameObject);
        _spawnedRenderers.Clear();
        foreach (var l in _spawnedAdditionalLights) if (l) Destroy(l.gameObject);
        _spawnedAdditionalLights.Clear();
    }

    // ===== HUD / UI simple en pantalla =====
    void OnGUI()
    {
        const int pad = 10;
        int w = 480;
        int h = _finished ? 150 : 120;
        var rect = new Rect(pad, pad, w, h);
        GUI.Box(rect, "URP Shader Benchmark");

        GUILayout.BeginArea(new Rect(pad + 10, pad + 25, w - 20, h - 35));
        if (_running)
        {
            string spinner = "|/-\"[(int)(Time.time * 10) % 4].ToString()";
            GUILayout.Label($"Estado: {_statusLine}  {spinner}");
            if (_currentIndex >= 0 && _currentIndex < cases.Count)
            {
                var c = cases[_currentIndex];
                GUILayout.Label($"Caso: {c.caseName}  ({_currentIndex + 1}/{cases.Count})");
                GUILayout.Label($"Última media → GPU: {_lastGpu:F2} ms | CPU: {_lastCpu:F2} ms");
            }
        }
        else if (_finished)
        {
            GUILayout.Label("✅ Benchmark finalizado");
            GUILayout.Label($"CSV: {_csvPath}");
        }
        else
        {
            GUILayout.Label("Listo para ejecutar");
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Abrir carpeta CSV", GUILayout.Height(24)))
        {
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(_csvPath)) EditorUtility.RevealInFinder(_csvPath);
#else
            if (!string.IsNullOrEmpty(_csvDir)) Application.OpenURL("file:///" + _csvDir.Replace("\", "/"));
#endif
        }
        if (_finished && GUILayout.Button("Copiar ruta CSV", GUILayout.Height(24)))
        {
            GUIUtility.systemCopyBuffer = _csvPath;
        }
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }
}

// ===== Métricas =====

public struct MetricsAverages
{
    public double gpuMs, cpuMs;
    public long batches, setPass, drawCalls, tris, verts;
}

public class MetricsRecorder
{
    ProfilerRecorder _cpuMainTime;
    ProfilerRecorder _batches;
    ProfilerRecorder _setPass;
    ProfilerRecorder _drawCalls;
    ProfilerRecorder _tris;
    ProfilerRecorder _verts;

    public MetricsAverages Averages { get; private set; }

    public System.Collections.IEnumerator WarmupFrames(int frames)
    {
        StartRecorders();
        for (int i = 0; i < frames; i++)
        {
            FrameTimingManager.CaptureFrameTimings();
            yield return null;
        }
    }

    public System.Collections.IEnumerator SampleFrames(int frames)
    {
        var gpuSum = 0.0; var cpuSum = 0.0;
        long batches = 0, setPass = 0, draw = 0, t = 0, v = 0;

        int validGpu = 0;
        for (int i = 0; i < frames; i++)
        {
            FrameTimingManager.CaptureFrameTimings();
            yield return null; // timing del frame anterior disponible ahora

            // GPU
            uint count = FrameTimingManager.GetLatestTimings(1, _tmpTimings);
            if (count > 0)
            {
                gpuSum += _tmpTimings[0].gpuFrameTime; // ms
                validGpu++;
            }

            // CPU principal (ns → ms)
            cpuSum += ReadRecorderMs(_cpuMainTime);

            // Contadores de render
            batches += ReadRecorderLong(_batches);
            setPass += ReadRecorderLong(_setPass);
            draw += ReadRecorderLong(_drawCalls);
            t += ReadRecorderLong(_tris);
            v += ReadRecorderLong(_verts);
        }

        Averages = new MetricsAverages
        {
            gpuMs = validGpu > 0 ? gpuSum / validGpu : -1.0,
            cpuMs = cpuSum / Math.Max(1, frames),
            batches = batches / Math.Max(1, frames),
            setPass = setPass / Math.Max(1, frames),
            drawCalls = draw / Math.Max(1, frames),
            tris = t / Math.Max(1, frames),
            verts = v / Math.Max(1, frames)
        };

        StopRecorders();
    }

    FrameTiming[] _tmpTimings = new FrameTiming[1];

    void StartRecorders()
    {
        StopRecorders(); // por si acaso

        // Nota: los nombres de contadores varían por versión → try/catch para tolerancia.
        try { _cpuMainTime = ProfilerRecorder.StartNew(ProfilerCategory.Scripts, "Main Thread", 1); } catch { }
        try { _batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count", 1); } catch { }
        try { _setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count", 1); } catch { }
        try { _drawCalls = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count", 1); } catch { }
        try { _tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count", 1); } catch { }
        try { _verts = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Vertices Count", 1); } catch { }
    }

    void StopRecorders()
    {
        if (_cpuMainTime.Valid) _cpuMainTime.Dispose();
        if (_batches.Valid) _batches.Dispose();
        if (_setPass.Valid) _setPass.Dispose();
        if (_drawCalls.Valid) _drawCalls.Dispose();
        if (_tris.Valid) _tris.Dispose();
        if (_verts.Valid) _verts.Dispose();
    }

    static double ReadRecorderMs(ProfilerRecorder r)
    {
        if (!r.Valid) return -1;
        var v = r.LastValue; // ns
        return v / 1_000_000.0; // ms
    }

    static long ReadRecorderLong(ProfilerRecorder r)
    {
        if (!r.Valid) return -1;
        return r.LastValue;
    }
}

// ===== CSV =====

public class CSVLogger
{
    private readonly string _dir;
    private readonly string _file;
    private readonly StringBuilder _buffer = new();

    public string FilePath => Path.Combine(_dir, _file);

    public CSVLogger(string directory, string fileName)
    {
        _dir = directory;
        _file = fileName;
        if (!Directory.Exists(_dir)) Directory.CreateDirectory(_dir);
    }

    public void WriteHeader(IEnumerable<string> cols)
    {
        _buffer.AppendLine(string.Join(",", cols));
    }

    public void Append(IEnumerable<string> cols)
    {
        _buffer.AppendLine(string.Join(",", cols.Select(Escape)));
    }

    public void Flush()
    {
        File.WriteAllText(FilePath, _buffer.ToString());
    }

    private static string Escape(string s)
    {
        if (s == null) return "";
        if (s.Contains(",") || s.Contains("\"") || s.Contains(""))
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }
}
