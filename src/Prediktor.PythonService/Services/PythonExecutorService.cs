using Prediktor.PythonService.Configuration;
using Python.Runtime;

namespace Prediktor.PythonService.Services;

public class PythonExecutorService
{
    private readonly PythonSettings _settings;
    private readonly ILogger<PythonExecutorService> _logger;
    private bool _initialized;
    private PyModule? _mainScope;

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

        using (Py.GIL())
        {
            _mainScope = Py.CreateScope("__prediktor_main__");
        }

        _logger.LogInformation("Python engine initialized. Version: {Version}", PythonEngine.Version);
    }

    /// <summary>
    /// Injects OPC UA data-access functions into the Python scope so scripts can call them directly:
    /// <c>GetValues(...)</c>, <c>GetAvg(...)</c>, <c>GetAgg(...)</c>, <c>SetValue(...)</c>.
    /// The underlying <see cref="OpcUaDataService"/> object is also available as <c>opc</c>.
    /// </summary>
    public void InjectOpcFunctions(OpcUaDataService dataService)
    {
        if (!_initialized || _mainScope == null)
        {
            _logger.LogWarning("Python engine is not initialized. Cannot inject OPC functions.");
            return;
        }

        using (Py.GIL())
        {
            _mainScope.Set("opc", dataService);
            // Expose individual top-level aliases so scripts can call them without the "opc." prefix
            _mainScope.Set("GetValues", new Func<string[], List<object?[]>>(dataService.GetValues));
            _mainScope.Set("GetAvg", new Func<string, List<object?[]>>(dataService.GetAvg));
            _mainScope.Set("GetAgg", new Func<string, string, List<object?[]>>(dataService.GetAgg));
            _mainScope.Set("SetValue", new Action<string, object, uint, string>(dataService.SetValue));
        }

        _logger.LogInformation("OPC UA functions (GetValues, GetAvg, GetAgg, SetValue) injected into Python scope.");
    }

    public void ExecuteScript(string scriptPath)
    {
        if (!_initialized || _mainScope == null)
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
                _mainScope.Exec(code);
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

    /// <summary>
    /// Calls a Python function defined in the shared script scope when an OPC UA trigger fires.
    /// </summary>
    /// <param name="scriptFunction">"script:function" or "function" identifying the Python function.</param>
    /// <param name="nodeId">NodeId string of the tag that changed.</param>
    /// <param name="value">New tag value.</param>
    /// <param name="quality">OPC UA StatusCode of the new value.</param>
    /// <param name="timestamp">Source timestamp of the new value.</param>
    public void CallTriggerFunction(string scriptFunction, string nodeId, object? value, uint quality, DateTime timestamp)
    {
        if (!_initialized || _mainScope == null)
        {
            _logger.LogWarning("Python engine is not initialized. Cannot call trigger function '{Function}'.", scriptFunction);
            return;
        }

        // "script:function" -> use the part after the colon as the Python function name
        var parts = scriptFunction.Split(':', 2);
        var functionName = parts.Length > 1 ? parts[1] : parts[0];

        using (Py.GIL())
        {
            try
            {
                if (!_mainScope.Contains(functionName))
                {
                    _logger.LogWarning("Python function '{Function}' not found in script scope.", functionName);
                    return;
                }

                var func = _mainScope.Get<PyObject>(functionName);
                func.Invoke(
                    nodeId.ToPython(),
                    value?.ToPython() ?? PyObject.None,
                    quality.ToPython(),
                    timestamp.ToString("O").ToPython());
            }
            catch (PythonException ex)
            {
                _logger.LogError(ex, "Python error calling trigger function '{Function}'.", functionName);
            }
        }
    }

    public void Shutdown()
    {
        if (_initialized)
        {
            _logger.LogInformation("Shutting down Python engine.");
            using (Py.GIL())
            {
                _mainScope?.Dispose();
                _mainScope = null;
            }
            PythonEngine.Shutdown();
            _initialized = false;
        }
    }
}
