// Assets/ShaderBench/BenchmarkCase.cs
using System;
using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "ShaderBench/Benchmark Case", fileName = "NewBenchmarkCase")]
public class BenchmarkCase : ScriptableObject
{
    [Header("Identidad")]
    [Tooltip("Nombre legible que verás en overlay y CSV.")]
    public string caseName = "URP/Lit - Instancing ON";

    [Header("Geometría")]
    [Tooltip("Prefab a instanciar. Si está vacío, se usará un Cubo por defecto.")]
    public GameObject prefab;
    [Min(1)] public int gridX = 50;
    [Min(1)] public int gridY = 50;
    [Min(0.01f)] public float spacing = 1.0f;

    [Header("Material")]
    [Tooltip("Si está activo, se sobrescribe el material del prefab.")]
    public bool overrideMaterial = false;
    public Material material;

    [Tooltip("Activa el flag de instancing en el material (si el shader lo soporta).")]
    public bool enableGPUInstancing = true;

    [Header("Sombras")]
    public ShadowCastingMode shadowCasting = ShadowCastingMode.On;
    public bool receiveShadows = true;

    [Header("Shader Keywords (separadas por coma, punto y coma o espacios)")]
    [Tooltip("Keywords a habilitar en el material (ej: _NORMALMAP, _EMISSION).")]
    [TextArea] public string enabledKeywords = "";
    [Tooltip("Keywords a deshabilitar en el material.")]
    [TextArea] public string disabledKeywords = "";

    /// <summary>
    /// Aplica material, keywords y flags de sombras/instancing a un renderer concreto.
    /// El BenchmarkRunner llama a este método para cada instancia creada del grid.
    /// </summary>
    public void ApplyToRenderer(Renderer r)
    {
        if (r == null) return;

        // 1) Material
        if (overrideMaterial && material != null)
        {
            // sharedMaterial para no crear copias por instancia y permitir instancing.
            r.sharedMaterial = material;
        }

        var mat = r.sharedMaterial;
        if (mat != null)
        {
            // 2) Instancing
            mat.enableInstancing = enableGPUInstancing;

            // 3) Keywords
            ApplyKeywords(mat, enabledKeywords, true);
            ApplyKeywords(mat, disabledKeywords, false);
        }

        // 4) Sombras
        r.shadowCastingMode = shadowCasting;
        r.receiveShadows = receiveShadows;
    }

    static void ApplyKeywords(Material m, string list, bool enable)
    {
        if (m == null || string.IsNullOrWhiteSpace(list)) return;

        var parts = list.Split(new[] { ',', ';', '|', '\n', '\t', ' ' },
            StringSplitOptions.RemoveEmptyEntries);

        foreach (var raw in parts)
        {
            var kw = raw.Trim();
            if (kw.Length == 0) continue;
            try
            {
                if (enable) m.EnableKeyword(kw);
                else m.DisableKeyword(kw);
            }
            catch (Exception)
            {
                // Silenciar keywords inválidas sin romper la ejecución.
            }
        }
    }
}
