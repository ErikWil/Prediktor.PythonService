using Prediktor.PythonService.Configuration;
using Python.Runtime;

namespace Prediktor.PythonService.Services;

public class PythonExecutorService
{
    private readonly PythonSettings _settings;
    private readonly ILogger<PythonExecutorService> _logger;
    private bool _initialized;

    public PythonExecutorService(PythonSettings settings, ILogger<PythonExecutorService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public void Initialize()
    {
        if (_initialized)
            return;

        if (string.IsNullOrWhiteSpace(_settings.PythonDllPath))
        {
            _logger.LogError("PythonSettings.PythonDllPath is not configured. Python execution is disabled.");
            return;
        }

        _logger.LogInformation("Initializing Python engine with DLL: {PythonDllPath}", _settings.PythonDllPath);

        Runtime.PythonDLL = _settings.PythonDllPath;
        PythonEngine.Initialize();
        _initialized = true;

        _logger.LogInformation("Python engine initialized. Version: {Version}", PythonEngine.Version);
    }

    public void ExecuteScript(string scriptPath)
    {
        if (!_initialized)
        {
            _logger.LogWarning("Python engine is not initialized. Skipping script: {Script}", scriptPath);
            return;
        }

        if (!File.Exists(scriptPath))
        {
            _logger.LogError("Python script not found: {Script}", scriptPath);
            return;
        }

        _logger.LogInformation("Executing Python script: {Script}", scriptPath);

        using (Py.GIL())
        {
            try
            {
                var code = File.ReadAllText(scriptPath);
                PythonEngine.RunSimpleString(code);
                _logger.LogInformation("Python script completed: {Script}", scriptPath);
            }
            catch (PythonException ex)
            {
                _logger.LogError(ex, "Python error while executing script: {Script}", scriptPath);
            }
        }
    }

    public void ExecuteAllScripts()
    {
        if (_settings.Scripts == null || _settings.Scripts.Count == 0)
        {
            _logger.LogInformation("No Python scripts configured.");
            return;
        }

        foreach (var script in _settings.Scripts)
        {
            ExecuteScript(script);
        }
    }

    public void Shutdown()
    {
        if (_initialized)
        {
            _logger.LogInformation("Shutting down Python engine.");
            PythonEngine.Shutdown();
            _initialized = false;
        }
    }
}
