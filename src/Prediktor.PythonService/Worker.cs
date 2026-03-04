using Prediktor.PythonService.Services;

namespace Prediktor.PythonService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly PythonExecutorService _pythonExecutor;
    private readonly OpcUaConnectionService _opcUaConnection;

    public Worker(
        ILogger<Worker> logger,
        PythonExecutorService pythonExecutor,
        OpcUaConnectionService opcUaConnection)
    {
        _logger = logger;
        _pythonExecutor = pythonExecutor;
        _opcUaConnection = opcUaConnection;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Prediktor Python Service starting.");

        _pythonExecutor.Initialize();
        await _opcUaConnection.ConnectAsync(cancellationToken);

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Prediktor Python Service running.");

        _pythonExecutor.ExecuteAllScripts();

        // Keep the service alive until cancellation is requested
        try
        {
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping; do not propagate
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Prediktor Python Service stopping.");

        await _opcUaConnection.DisconnectAsync().ConfigureAwait(false);
        _pythonExecutor.Shutdown();

        await base.StopAsync(cancellationToken);
    }
}
