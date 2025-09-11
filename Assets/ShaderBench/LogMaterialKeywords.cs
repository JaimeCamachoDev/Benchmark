// Assets/ShaderBench/LogMaterialKeywords.cs
using UnityEngine;

public class LogMaterialKeywords : MonoBehaviour
{
    void Start()
    {
        var r = GetComponent<Renderer>();
        if (r && r.sharedMaterial != null)
        {
            var kws = r.sharedMaterial.shaderKeywords;
            Debug.Log($"{name} Keywords: " + (kws != null ? string.Join(", ", kws) : "(none)"));
        }
    }
}

