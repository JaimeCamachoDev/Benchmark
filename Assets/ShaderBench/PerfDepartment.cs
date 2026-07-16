// Assets/ShaderBench/PerfDepartment.cs

/// <summary>
/// Las 4 áreas de responsabilidad de rendimiento del proyecto (25% del
/// presupuesto de frame cada una). Se usa para etiquetar cada BenchmarkCase
/// y poder filtrar/ponderar resultados en el CSV y en Google Sheets.
/// </summary>
public enum PerfDepartment
{
    Programming,
    Animation,
    Modeling,
    VFX
}
