using Prediktor.PythonService;
using Prediktor.PythonService.Configuration;
using Prediktor.PythonService.Services;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName = "Prediktor.PythonService";
});

// Configure Serilog from appsettings.json
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();

builder.Logging.ClearProviders();
builder.Services.AddSerilog();

// Bind configuration sections
var pythonSettings = builder.Configuration
    .GetSection(PythonSettings.SectionName)
    .Get<PythonSettings>() ?? new PythonSettings();

var opcUaSettings = builder.Configuration
    .GetSection(OpcUaSettings.SectionName)
    .Get<OpcUaSettings>() ?? new OpcUaSettings();

builder.Services.AddSingleton(pythonSettings);
builder.Services.AddSingleton(opcUaSettings);

// Register services
builder.Services.AddSingleton<PythonExecutorService>();
builder.Services.AddSingleton<OpcUaConnectionService>();
builder.Services.AddSingleton<OpcUaDataService>();
builder.Services.AddSingleton<OpcUaTriggerService>();

builder.Services.AddHostedService<Worker>();

var host = builder.Build();

try
{
    Log.Information("Starting Prediktor.PythonService host.");
    host.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Prediktor.PythonService host terminated unexpectedly.");
}
finally
{
    Log.CloseAndFlush();
}
