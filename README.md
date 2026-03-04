# Prediktor.PythonService

A Windows Service that executes Python scripts using [Python.NET (pythonnet)](https://pythonnet.github.io/) and connects to an OPC UA server using [Prediktor.UA.Client](https://www.nuget.org/packages/Prediktor.UA.Client).

## Features

- Runs as a Windows Service (or a console app for development)
- Executes one or more Python script files at startup
- Connects to an OPC UA server (anonymous or username/password authentication)
- Configurable via `appsettings.json`
- Structured logging via [Serilog](https://serilog.net/) (console and rolling file sinks)

## Requirements

- .NET 8 SDK or Runtime (x64)
- Python installation (version matching the `PythonDllPath` in configuration)
- An accessible OPC UA server (optional; connection errors are logged and the service continues)

## Configuration

Edit `appsettings.json` before deploying the service:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      { "Name": "Console" },
      {
        "Name": "File",
        "Args": {
          "path": "logs/prediktor-pythonservice-.log",
          "rollingInterval": "Day",
          "retainedFileCountLimit": 7
        }
      }
    ]
  },
  "PythonSettings": {
    "PythonDllPath": "C:\\Python311\\python311.dll",
    "Scripts": [
      "C:\\scripts\\main.py"
    ]
  },
  "OpcUaSettings": {
    "EndpointUrl": "opc.tcp://localhost:4840",
    "SessionName": "Prediktor.PythonService",
    "UseSecurity": false,
    "Username": null,
    "Password": null
  }
}
```

### PythonSettings

| Key | Description |
|-----|-------------|
| `PythonDllPath` | Full path to the Python DLL (e.g. `C:\Python311\python311.dll`). Must match your installed Python version. |
| `Scripts` | List of full paths to Python script files to execute when the service starts. |

### OpcUaSettings

| Key | Description |
|-----|-------------|
| `EndpointUrl` | OPC UA server endpoint URL (e.g. `opc.tcp://localhost:4840`). |
| `SessionName` | Name for the OPC UA session. |
| `UseSecurity` | Set to `true` to enable message security. |
| `Username` | Username for authenticated connections. Leave `null` for anonymous access. |
| `Password` | Password for authenticated connections. Leave `null` for anonymous access. |

## Building

```bash
cd src/Prediktor.PythonService
dotnet build
```

## Running as a Console App (Development)

```bash
cd src/Prediktor.PythonService
dotnet run
```

## Installing as a Windows Service

Publish a self-contained executable and install it with `sc.exe`:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -o C:\Services\PythonService

sc.exe create Prediktor.PythonService binPath= "C:\Services\PythonService\Prediktor.PythonService.exe"
sc.exe start Prediktor.PythonService
```

## Project Structure

```
src/
└── Prediktor.PythonService/
    ├── Configuration/
    │   ├── PythonSettings.cs       # Python configuration model
    │   └── OpcUaSettings.cs        # OPC UA configuration model
    ├── Services/
    │   ├── PythonExecutorService.cs # Executes Python scripts via Python.NET
    │   └── OpcUaConnectionService.cs# Manages OPC UA connection
    ├── Worker.cs                    # Background service orchestrator
    ├── Program.cs                   # Host setup, Serilog, DI configuration
    └── appsettings.json             # Service configuration
```
