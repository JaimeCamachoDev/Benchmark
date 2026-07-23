// Assets/ShaderBench/Editor/BenchmarkCasePresets.cs
#if UNITY_EDITOR
using System.Collections.Generic;
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

    [MenuItem("JaimeCamachoDev/ShaderBench/Create Programming Example Case")]
    public static void CreateProgrammingExample()
    {
        string folder = GetSelectedPathOrFallback();

        // Prefab "unidad de coste" para el departamento de Programación.
        var unit = GameObject.CreatePrimitive(PrimitiveType.Cube);
        unit.name = "ProgrammingStressUnit";
        unit.AddComponent<ProgrammingStressUnit>();
        var prefab = PrefabUtility.SaveAsPrefabAsset(unit, "Assets/1-Programming/ProgrammingStressUnit.prefab");
        Object.DestroyImmediate(unit);

        var programmingCase = ScriptableObject.CreateInstance<BenchmarkCase>();
        programmingCase.name = "Programming_RaycastStress";
        programmingCase.caseName = "Programming - Raycast Stress (400 units)";
        programmingCase.department = PerfDepartment.Programming;
        programmingCase.prefab = prefab;
        programmingCase.gridX = 20; programmingCase.gridY = 20; programmingCase.spacing = 1.5f;
        programmingCase.overrideMaterial = false;
        programmingCase.enableGPUInstancing = true;
        programmingCase.shadowCasting = ShadowCastingMode.Off;
        programmingCase.receiveShadows = false;
        AssetDatabase.CreateAsset(programmingCase, $"{folder}/Programming_RaycastStress.asset");

        AssetDatabase.SaveAssets();
        Selection.activeObject = programmingCase;
        Debug.Log("[ShaderBench] Programming example case created.");
    }

    [MenuItem("JaimeCamachoDev/ShaderBench/Create Modeling Example Cases")]
    public static void CreateModelingExamples()
    {
        string folder = GetSelectedPathOrFallback();

        // Baseline low-poly (cubo) para comparar contra geometría más pesada.
        var lowPolyObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        lowPolyObj.name = "ModelingBench_LowPoly";
        var lowPolyPrefab = PrefabUtility.SaveAsPrefabAsset(lowPolyObj, "Assets/2-Art/1-3D/ModelingBench_LowPoly.prefab");
        Object.DestroyImmediate(lowPolyObj);

        var lowPolyCase = ScriptableObject.CreateInstance<BenchmarkCase>();
        lowPolyCase.name = "Modeling_LowPoly_Baseline";
        lowPolyCase.caseName = "Modeling - Low Poly Baseline (Cube)";
        lowPolyCase.department = PerfDepartment.Modeling;
        lowPolyCase.prefab = lowPolyPrefab;
        lowPolyCase.gridX = 30; lowPolyCase.gridY = 30; lowPolyCase.spacing = 1.5f;
        lowPolyCase.overrideMaterial = false;
        lowPolyCase.enableGPUInstancing = true;
        lowPolyCase.shadowCasting = ShadowCastingMode.On;
        lowPolyCase.receiveShadows = true;
        AssetDatabase.CreateAsset(lowPolyCase, $"{folder}/Modeling_LowPoly_Baseline.asset");

        // Malla orgánica más pesada ya presente en el proyecto (Ankh) como referencia de "alto poly".
        var highPolyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/2-Art/1-3D/Ankh/Nueva carpeta/Geo.prefab");
        if (highPolyPrefab != null)
        {
            var highPolyCase = ScriptableObject.CreateInstance<BenchmarkCase>();
            highPolyCase.name = "Modeling_HighPoly_Ankh";
            highPolyCase.caseName = "Modeling - High Poly (Ankh)";
            highPolyCase.department = PerfDepartment.Modeling;
            highPolyCase.prefab = highPolyPrefab;
            highPolyCase.gridX = 30; highPolyCase.gridY = 30; highPolyCase.spacing = 1.5f;
            highPolyCase.overrideMaterial = false;
            highPolyCase.enableGPUInstancing = true;
            highPolyCase.shadowCasting = ShadowCastingMode.On;
            highPolyCase.receiveShadows = true;
            AssetDatabase.CreateAsset(highPolyCase, $"{folder}/Modeling_HighPoly_Ankh.asset");
        }
        else
        {
            Debug.LogWarning("[ShaderBench] No se encontró el prefab Ankh de referencia; solo se creó el caso Low Poly.");
        }

        AssetDatabase.SaveAssets();
        Selection.activeObject = lowPolyCase;
        Debug.Log("[ShaderBench] Modeling example case(s) created.");
    }

    [MenuItem("JaimeCamachoDev/ShaderBench/Create EfficientLit vs Lit Comparison")]
    public static void CreateEfficientLitComparison()
    {
        string folder = GetSelectedPathOrFallback();

        // Quad compartido: en modo fillViewport actua como "1 unidad de pantalla
        // shadeada" con el material bajo prueba, para aislar el coste por pixel.
        var quadObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quadObj.name = "EfficientLitBench_Quad";
        var quadPrefab = PrefabUtility.SaveAsPrefabAsset(quadObj, "Assets/2-Art/2-VFX/EfficientLit/EfficientLitBench_Quad.prefab");
        Object.DestroyImmediate(quadObj);

        var litShader = Shader.Find("Universal Render Pipeline/Lit");
        var efficientShader = Shader.Find("Custom/EfficientLit");

        if (litShader == null || efficientShader == null)
        {
            Debug.LogError("[ShaderBench] No se encontró el shader 'Universal Render Pipeline/Lit' o 'Custom/EfficientLit'. ¿Está el proyecto en URP y compila EfficientLit.shader?");
            return;
        }

        var litMat = new Material(litShader) { name = "EfficientLitBench_LitReference" };
        AssetDatabase.CreateAsset(litMat, "Assets/2-Art/2-VFX/EfficientLit/EfficientLitBench_LitReference.mat");

        var efficientMat = new Material(efficientShader) { name = "EfficientLitBench_EfficientLit" };
        AssetDatabase.CreateAsset(efficientMat, "Assets/2-Art/2-VFX/EfficientLit/EfficientLitBench_EfficientLit.mat");

        var densities = new List<int> { 1, 25, 100 };

        var litCase = ScriptableObject.CreateInstance<BenchmarkCase>();
        litCase.name = "EfficientLitBench_Lit";
        litCase.caseName = "MaterialEfficiency - URP Lit (referencia)";
        litCase.department = PerfDepartment.VFX;
        litCase.prefab = quadPrefab;
        litCase.overrideMaterial = true;
        litCase.material = litMat;
        litCase.enableGPUInstancing = true;
        litCase.shadowCasting = ShadowCastingMode.On;
        litCase.receiveShadows = true;
        litCase.fillViewport = true;
        litCase.fillDistance = 5f;
        litCase.densitySweep = new List<int>(densities);
        litCase.packingScheme = "URP Lit estándar (BaseMap + Metallic/Smoothness + Occlusion + Normal)";
        litCase.textureSamplesPerInstance = 4;
        litCase.variantNotes = "Referencia sin texturas asignadas (usa los defaults de Lit); asigna las mismas texturas de contenido real en ambos materiales antes de comparar visualmente.";
        AssetDatabase.CreateAsset(litCase, $"{folder}/EfficientLitBench_Lit.asset");

        var efficientCase = ScriptableObject.CreateInstance<BenchmarkCase>();
        efficientCase.name = "EfficientLitBench_EfficientLit";
        efficientCase.caseName = "MaterialEfficiency - EfficientLit (2 texturas)";
        efficientCase.department = PerfDepartment.VFX;
        efficientCase.prefab = quadPrefab;
        efficientCase.overrideMaterial = true;
        efficientCase.material = efficientMat;
        efficientCase.enableGPUInstancing = true;
        efficientCase.shadowCasting = ShadowCastingMode.On;
        efficientCase.receiveShadows = true;
        efficientCase.fillViewport = true;
        efficientCase.fillDistance = 5f;
        efficientCase.densitySweep = new List<int>(densities);
        efficientCase.packingScheme = "EfficientLit custom (BaseMap RGB+A + DataMap RG normal/B AO/A metallic)";
        efficientCase.textureSamplesPerInstance = 2;
        efficientCase.variantNotes = "DataMap debe importarse como Default/Lineal, no como Normal Map.";
        AssetDatabase.CreateAsset(efficientCase, $"{folder}/EfficientLitBench_EfficientLit.asset");

        AssetDatabase.SaveAssets();
        Selection.activeObject = efficientCase;
        Debug.Log("[ShaderBench] Casos de comparación EfficientLit vs Lit creados. Añádelos a un BenchmarkRunner, activa 'Compute Material Efficiency Summary' y dale a Play.");
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
