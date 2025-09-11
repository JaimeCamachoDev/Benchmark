// Assets/ShaderBench/BenchmarkOverlay.cs
using UnityEngine;

/// <summary>
/// Muestra una superposición sencilla con el estado del benchmark y resultados.
/// Se comunica con BenchmarkRunner mediante referencia pública.
/// </summary>
public class BenchmarkOverlay : MonoBehaviour
{
    public BenchmarkRunner runner;

    [Header("Estilo")]
    public int fontSize = 14;
    public float panelWidth = 580f;

    void OnGUI()
    {
        if (runner == null) return;

        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            richText = true,
            wordWrap = true
        };

        GUILayout.BeginArea(new Rect(10, 10, panelWidth, Screen.height - 20), GUI.skin.box);
        GUILayout.Label("<b>ShaderBench</b> – pruebas de rendimiento", style);
        GUILayout.Space(6);

        if (!runner.IsRunning)
        {
            GUILayout.Label("Pulsa <b>ESPACIO</b> para iniciar el benchmark.\n" +
                            "Pulsa <b>Escape</b> para cancelar.\n" +
                            "Consejos de validez: VSync OFF, Build de Player, misma resolución, SRP Batcher coherente.", style);
        }
        else
        {
            GUILayout.Label($"Caso: <b>{runner.CurrentCaseName}</b>  [{runner.CurrentIndex + 1}/{runner.TotalCases}]  | Repetición {runner.CurrentRepetition}/{runner.Repetitions}", style);
            GUILayout.Label($"Estado: <b>{runner.CurrentPhase}</b>  | Tiempo fase: {runner.CurrentPhaseElapsed:F1}s / {runner.CurrentPhaseDuration:F1}s", style);
            GUILayout.Label($"FPS (instante): {runner.InstantFPS:F1}", style);
            GUILayout.Label($"CPU ms (instante): {runner.InstantCPUms:F2}  |  GPU ms (instante): {runner.InstantGPUms:F2}", style);
        }

        if (runner.HasFinished)
        {
            GUILayout.Space(8);
            GUILayout.Label("<color=#7CFC00><b>✔ Benchmark finalizado</b></color>", style);
            GUILayout.Label($"Resultados guardados en:\n<b>{runner.LastCsvPath}</b>", style);
            GUILayout.Label("Ábrelo con tu editor de hojas de cálculo para comparar (filtra por CaseName).", style);
        }

        GUILayout.EndArea();
    }
}
