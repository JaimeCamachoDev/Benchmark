// Assets/PerfLab/Editor/PerfLabMenu.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class PerfLabMenu
{
    [MenuItem("JaimeCamachoDev/PerfLab/Create Analyzer In Scene")]
    public static void CreateAnalyzerInScene()
    {
        var go = new GameObject("PerfLab Analyzer");
        go.AddComponent<PerfLabAnalyzer>();
        Selection.activeGameObject = go;
        Debug.Log("[PerfLab] Analizador creado. Arrastra tu prefab al campo 'Subject Prefab' y dale a Play.");
    }
}
#endif
