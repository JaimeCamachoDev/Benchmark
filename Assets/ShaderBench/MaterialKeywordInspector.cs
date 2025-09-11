// Assets/ShaderBench/Editor/MaterialKeywordInspector.cs
#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using System.Text;

public class MaterialKeywordInspector : EditorWindow
{
    private Material mat;

    [MenuItem("JaimeCamachoDev/ShaderBench/Material Keyword Inspector")]
    public static void Open()
    {
        GetWindow<MaterialKeywordInspector>("Keyword Inspector");
    }

    void OnGUI()
    {
        mat = (Material)EditorGUILayout.ObjectField("Material", mat, typeof(Material), false);

        if (mat == null)
        {
            EditorGUILayout.HelpBox("Arrastra un Material aquí.", MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("Shader", mat.shader != null ? mat.shader.name : "(null)");
        EditorGUILayout.Space();

        var kws = mat.shaderKeywords ?? System.Array.Empty<string>();
        EditorGUILayout.LabelField($"Material Keywords ({kws.Length})");
        foreach (var k in kws)
            EditorGUILayout.LabelField("• " + k);

        EditorGUILayout.Space();

        if (GUILayout.Button("Copiar keywords (separadas por coma)"))
        {
            var sb = new StringBuilder();
            for (int i = 0; i < kws.Length; i++)
            {
                sb.Append(kws[i]);
                if (i < kws.Length - 1) sb.Append(", ");
            }
            EditorGUIUtility.systemCopyBuffer = sb.ToString();
            Debug.Log("[ShaderBench] Copiadas keywords al portapapeles.");
        }
    }
}
#endif
