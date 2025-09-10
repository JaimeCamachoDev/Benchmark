using System.IO;
using UnityEngine;
using UnityEngine.XR;

// Registro simple de métricas para Quest y otros visores XR.
// Guarda CPU/GPU time y nivel de batería en CSV y muestra valores en pantalla.
public class QuestPerfLogger : MonoBehaviour
{
    [Tooltip("Tiempo entre muestras en segundos")]
    public float logInterval = 1f;

    string _filePath;
    float _nextLogTime;
    float _lastCpu, _lastGpu, _lastBatt;

    void Start()
    {
        _filePath = Path.Combine(Application.persistentDataPath, "QuestPerf.csv");
        File.WriteAllText(_filePath, "Time,AppCPU_ms,AppGPU_ms,Battery\n");
        _nextLogTime = Time.time + logInterval;
    }

    void Update()
    {
        if (Time.time >= _nextLogTime)
        {
            _nextLogTime = Time.time + logInterval;

            XRStats.TryGetAppCPUTimeLastFrame(out float cpuMs);
            XRStats.TryGetAppGPUTimeLastFrame(out float gpuMs);
            float batt = SystemInfo.batteryLevel;

            File.AppendAllText(_filePath, string.Format("{0:F2},{1:F3},{2:F3},{3:F2}\n", Time.time, cpuMs, gpuMs, batt));

            _lastCpu = cpuMs;
            _lastGpu = gpuMs;
            _lastBatt = batt;
        }
    }

    void OnGUI()
    {
        GUILayout.BeginVertical("box");
        GUILayout.Label($"CPU: {_lastCpu:F2} ms");
        GUILayout.Label($"GPU: {_lastGpu:F2} ms");
        GUILayout.Label($"Batería: {_lastBatt:P0}");
        GUILayout.Label($"Log: {_filePath}");
        GUILayout.EndVertical();
    }
}
