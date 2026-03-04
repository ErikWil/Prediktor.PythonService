using Opc.Ua;
using Opc.Ua.Client;
using Prediktor.PythonService.Configuration;

namespace Prediktor.PythonService.Services;

/// <summary>
/// Sets up OPC UA subscriptions for configured trigger tags and invokes the corresponding
/// Python functions when tag values change.
/// </summary>
public class OpcUaTriggerService : IDisposable
{
    private readonly OpcUaSettings _settings;
    private readonly OpcUaConnectionService _connection;
    private readonly PythonExecutorService _pythonExecutor;
    private readonly ILogger<OpcUaTriggerService> _logger;
    private Subscription? _subscription;

    public OpcUaTriggerService(
        OpcUaSettings settings,
        OpcUaConnectionService connection,
        PythonExecutorService pythonExecutor,
        ILogger<OpcUaTriggerService> logger)
    {
        _settings = settings;
        _connection = connection;
        _pythonExecutor = pythonExecutor;
        _logger = logger;
    }

    /// <summary>
    /// Creates an OPC UA subscription and registers a monitored item for every configured trigger.
    /// </summary>
    public void SetupTriggers()
    {
        if (_settings.Triggers == null || _settings.Triggers.Count == 0)
        {
            _logger.LogInformation("No OPC UA triggers configured.");
            return;
        }

        if (!_connection.IsConnected || _connection.Session == null)
        {
            _logger.LogWarning("Cannot set up triggers: OPC UA session is not connected.");
            return;
        }

        // The OPC UA SDK recommends Subscription(TelemetryContext) and async Create/ApplyChanges/Delete.
        // The synchronous overloads used here are deprecated but still functional; they are retained
        // because SetupTriggers is intentionally synchronous (called from the hosted service lifecycle).
#pragma warning disable CS0618
        _subscription = new Subscription(_connection.Session.DefaultSubscription)
        {
            DisplayName = "Prediktor.PythonService.Triggers",
            PublishingInterval = 1000,
            LifetimeCount = 60,
            KeepAliveCount = 10,
            MaxNotificationsPerPublish = 0,
            Priority = 0,
            PublishingEnabled = true
        };

        _connection.Session.AddSubscription(_subscription);
        _subscription.Create();
#pragma warning restore CS0618

        foreach (var (nodeIdStr, scriptFunction) in _settings.Triggers)
        {
            var monitoredItem = new MonitoredItem(_subscription.DefaultItem)
            {
                StartNodeId = NodeId.Parse(nodeIdStr),
                AttributeId = Attributes.Value,
                DisplayName = nodeIdStr,
                SamplingInterval = 1000,
                QueueSize = 1,
                MonitoringMode = MonitoringMode.Reporting
            };

            // Capture loop variables for the closure
            var capturedNodeId = nodeIdStr;
            var capturedFunction = scriptFunction;
            monitoredItem.Notification += (item, _) => OnTriggerFired(capturedNodeId, capturedFunction, item);

            _subscription.AddItem(monitoredItem);
            _logger.LogInformation("Registered trigger: {NodeId} -> {ScriptFunction}", nodeIdStr, scriptFunction);
        }

#pragma warning disable CS0618
        _subscription.ApplyChanges();
#pragma warning restore CS0618
        _logger.LogInformation("Set up {Count} OPC UA trigger(s).", _settings.Triggers.Count);
    }

    private void OnTriggerFired(string nodeIdStr, string scriptFunction, MonitoredItem item)
    {
        foreach (var dv in item.DequeueValues())
        {
            _logger.LogDebug("Trigger fired: {NodeId} -> {ScriptFunction}", nodeIdStr, scriptFunction);
            try
            {
                _pythonExecutor.CallTriggerFunction(
                    scriptFunction,
                    nodeIdStr,
                    dv.Value,
                    dv.StatusCode.Code,
                    dv.SourceTimestamp);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error invoking trigger function '{ScriptFunction}' for {NodeId}",
                    scriptFunction, nodeIdStr);
            }
        }
    }

    public void Dispose()
    {
        if (_subscription != null)
        {
            try
            {
                // Delete is the synchronous counterpart of DeleteAsync; used here
                // because Dispose() cannot be async.
#pragma warning disable CS0618
                _subscription.Delete(true);
#pragma warning restore CS0618
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while deleting OPC UA trigger subscription.");
            }
            _subscription = null;
        }
    }
}
