
using System.Collections.Generic;
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
    XRDisplaySubsystem _display;

    void Start()
    {
        _filePath = Path.Combine(Application.persistentDataPath, "QuestPerf.csv");
        File.WriteAllText(_filePath, "Time,CPU_ms,GPU_ms,Battery\n");
        _nextLogTime = Time.time + logInterval;
    }

    void Update()
    {
        if (_display == null)
        {
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetInstances(displays);
            if (displays.Count > 0)
                _display = displays[0];
        }

        if (_display != null && Time.time >= _nextLogTime)
        {
            _nextLogTime = Time.time + logInterval;

            XRStats.TryGetStat(_display, "cpuTimeLastFrame", out float cpuMs);
            XRStats.TryGetStat(_display, "gpuTimeLastFrame", out float gpuMs);

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
