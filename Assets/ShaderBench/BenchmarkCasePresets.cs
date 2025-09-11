// Assets/ShaderBench/Editor/BenchmarkCasePresets.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class BenchmarkCasePresets
{
    [MenuItem("JaimeCamachoDev/ShaderBench/Create Example Cases")]
    public static void CreateExamples()
    {
        string folder = GetSelectedPathOrFallback();

        // 1) Baseline - Cubo Unlit (sin sombras, instancing ON)
        var baseUnlit = ScriptableObject.CreateInstance<BenchmarkCase>();
        baseUnlit.name = "Baseline_Unlit_InstancingON";
        baseUnlit.caseName = "Baseline Unlit (Instancing ON, No Shadows)";
        baseUnlit.gridX = 40; baseUnlit.gridY = 40; baseUnlit.spacing = 1.0f;
        baseUnlit.overrideMaterial = false;
        baseUnlit.enableGPUInstancing = true;
        baseUnlit.shadowCasting = ShadowCastingMode.Off;
        baseUnlit.receiveShadows = false;
        AssetDatabase.CreateAsset(baseUnlit, $"{folder}/Baseline_Unlit_InstancingON.asset");

        // 2) URP/Lit - Instancing ON, Shadows OFF
        var litInstancing = ScriptableObject.CreateInstance<BenchmarkCase>();
        litInstancing.name = "URP_Lit_InstancingON_ShadowsOFF";
        litInstancing.caseName = "URP/Lit (Instancing ON, Shadows OFF)";
        litInstancing.gridX = 50; litInstancing.gridY = 50; litInstancing.spacing = 1.0f;
        litInstancing.overrideMaterial = false; // usa el material del prefab
        litInstancing.enableGPUInstancing = true;
        litInstancing.shadowCasting = ShadowCastingMode.Off;
        litInstancing.receiveShadows = false;
        AssetDatabase.CreateAsset(litInstancing, $"{folder}/URP_Lit_InstancingON_ShadowsOFF.asset");

        // 3) URP/Lit - Instancing OFF, Shadows ON
        var litNoInstancing = ScriptableObject.CreateInstance<BenchmarkCase>();
        litNoInstancing.name = "URP_Lit_InstancingOFF_ShadowsON";
        litNoInstancing.caseName = "URP/Lit (Instancing OFF, Shadows ON)";
        litNoInstancing.gridX = 35; litNoInstancing.gridY = 35; litNoInstancing.spacing = 1.0f;
        litNoInstancing.overrideMaterial = false;
        litNoInstancing.enableGPUInstancing = false;
        litNoInstancing.shadowCasting = ShadowCastingMode.On;
        litNoInstancing.receiveShadows = true;
        AssetDatabase.CreateAsset(litNoInstancing, $"{folder}/URP_Lit_InstancingOFF_ShadowsON.asset");

        AssetDatabase.SaveAssets();
        Selection.activeObject = baseUnlit;
        Debug.Log("[ShaderBench] Example cases created.");
    }

    static string GetSelectedPathOrFallback()
    {
        string path = "Assets";
        foreach (Object obj in Selection.GetFiltered(typeof(Object), SelectionMode.Assets))
        {
            path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
            {
                path = System.IO.Path.GetDirectoryName(path);
                break;
            }
        }
        return path;
    }
}
#endif
