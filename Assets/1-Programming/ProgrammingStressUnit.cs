// Assets/1-Programming/ProgrammingStressUnit.cs
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Unidad de coste de CPU configurable para el departamento de Programación.
/// El equipo arrastra un prefab con este componente en un BenchmarkCase
/// (departamento = Programming) y escala gridX/gridY en BenchmarkRunner para
/// medir cómo cae el framerate al aumentar el número de instancias activas.
/// </summary>
public class ProgrammingStressUnit : MonoBehaviour
{
    public enum WorkloadType { Raycasts, GCAllocations, ListSort, Combined }

    [Header("Tipo de carga a medir")]
    public WorkloadType workload = WorkloadType.Raycasts;

    [Header("Parámetros")]
    [Min(0)] public int raycastsPerFrame = 5;
    [Min(0)] public int allocationsPerFrame = 20;
    [Min(1)] public int listSize = 200;

    private readonly List<int> _scratchList = new();
    private RaycastHit _hit;

    void Update()
    {
        switch (workload)
        {
            case WorkloadType.Raycasts:
                RunRaycasts();
                break;
            case WorkloadType.GCAllocations:
                RunAllocations();
                break;
            case WorkloadType.ListSort:
                RunListSort();
                break;
            case WorkloadType.Combined:
                RunRaycasts();
                RunAllocations();
                RunListSort();
                break;
        }
    }

    void RunRaycasts()
    {
        Vector3 origin = transform.position + Vector3.up;
        for (int i = 0; i < raycastsPerFrame; i++)
        {
            Vector3 dir = Quaternion.Euler(0f, (360f / Mathf.Max(1, raycastsPerFrame)) * i, 0f) * Vector3.forward;
            Physics.Raycast(origin, dir, out _hit, 10f);
        }
    }

    void RunAllocations()
    {
        for (int i = 0; i < allocationsPerFrame; i++)
        {
            string s = "perf_" + i + "_" + Time.frameCount;
            _ = s.Length;
        }
    }

    void RunListSort()
    {
        _scratchList.Clear();
        for (int i = 0; i < listSize; i++)
            _scratchList.Add((i * 7919) % listSize);
        _scratchList.Sort();
    }
}
