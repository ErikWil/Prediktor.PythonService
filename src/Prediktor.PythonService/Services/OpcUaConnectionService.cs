using System.Security.Cryptography.X509Certificates;
using System.Text;
using Opc.Ua;
using Opc.Ua.Client;
using Prediktor.PythonService.Configuration;
using Prediktor.UA.Client;

namespace Prediktor.PythonService.Services;

public class OpcUaConnectionService : IAsyncDisposable
{
    private readonly OpcUaSettings _settings;
    private readonly ILogger<OpcUaConnectionService> _logger;
    private ISession? _session;

    public OpcUaConnectionService(OpcUaSettings settings, ILogger<OpcUaConnectionService> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    public bool IsConnected => _session != null && _session.Connected;

    public ISession? Session => _session;

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.EndpointUrl))
        {
            _logger.LogError("OpcUaSettings.EndpointUrl is not configured. OPC UA connection is disabled.");
            return;
        }

        _logger.LogInformation("Connecting to OPC UA server at {EndpointUrl}", _settings.EndpointUrl);

        if (_settings.AutoAcceptUntrustedCertificates)
        {
            _logger.LogWarning("AutoAcceptUntrustedCertificates is enabled. Disable this in production environments.");
        }

        try
        {
            var appConfig = BuildApplicationConfiguration();
            await appConfig.ValidateAsync(ApplicationType.Client);

            var factory = new SessionFactory(cert => ValidateCertificate(cert));

            if (!string.IsNullOrEmpty(_settings.Username))
            {
                var passwordBytes = Encoding.UTF8.GetBytes(_settings.Password ?? string.Empty);
                var userIdentity = new UserIdentity(_settings.Username, passwordBytes);
                _session = await factory.CreateSessionAsync(
                    _settings.EndpointUrl,
                    _settings.SessionName,
                    userIdentity,
                    _settings.UseSecurity,
                    false,
                    appConfig);
            }
            else
            {
                _session = await factory.CreateAnonymouslyAsync(
                    _settings.EndpointUrl,
                    _settings.SessionName,
                    _settings.UseSecurity,
                    false,
                    appConfig);
            }

            _logger.LogInformation("Connected to OPC UA server at {EndpointUrl}", _settings.EndpointUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to OPC UA server at {EndpointUrl}", _settings.EndpointUrl);
        }
    }

    public async Task DisconnectAsync()
    {
        if (_session != null)
        {
            _logger.LogInformation("Disconnecting from OPC UA server.");
            try
            {
                await _session.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while closing OPC UA session.");
            }
            _session.Dispose();
            _session = null;
        }
    }

    private bool ValidateCertificate(X509Certificate2 certificate)
    {
        if (_settings.AutoAcceptUntrustedCertificates)
            return true;

        // When AutoAcceptUntrustedCertificates is false, only accept certificates that are valid
        // and trusted according to the system certificate store.
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        bool isValid = chain.Build(certificate);
        if (!isValid)
            _logger.LogWarning("OPC UA server certificate validation failed for: {Subject}", certificate.Subject);
        return isValid;
    }

    private ApplicationConfiguration BuildApplicationConfiguration()
    {
        return new ApplicationConfiguration
        {
            ApplicationName = "Prediktor.PythonService",
            ApplicationType = ApplicationType.Client,
            ApplicationUri = "urn:Prediktor:PythonService",
            SecurityConfiguration = new SecurityConfiguration
            {
                ApplicationCertificate = new CertificateIdentifier(),
                AutoAcceptUntrustedCertificates = _settings.AutoAcceptUntrustedCertificates,
                AddAppCertToTrustedStore = true
            },
            ClientConfiguration = new ClientConfiguration
            {
                DefaultSessionTimeout = 60000
            },
            TransportQuotas = new TransportQuotas
            {
                OperationTimeout = 15000
            }
        };
    }

    public async ValueTask DisposeAsync()
    {
        await DisconnectAsync().ConfigureAwait(false);
    }
}
