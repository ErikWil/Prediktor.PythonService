namespace Prediktor.PythonService.Configuration;

public class PythonSettings
{
    public const string SectionName = "PythonSettings";

    /// <summary>
    /// Full path to the Python DLL (e.g. C:\Python311\python311.dll).
    /// </summary>
    public string PythonDllPath { get; set; } = string.Empty;

    /// <summary>
    /// Full paths to the Python script files to execute.
    /// </summary>
    public List<string> Scripts { get; set; } = new();
}
