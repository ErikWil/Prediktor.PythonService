namespace Prediktor.PythonService.Configuration;

public class OpcUaSettings
{
    public const string SectionName = "OpcUaSettings";

    /// <summary>
    /// OPC UA server endpoint URL (e.g. opc.tcp://localhost:4840).
    /// </summary>
    public string EndpointUrl { get; set; } = string.Empty;

    /// <summary>
    /// Name to use for the OPC UA session.
    /// </summary>
    public string SessionName { get; set; } = "Prediktor.PythonService";

    /// <summary>
    /// Whether to use security when connecting to the OPC UA server.
    /// </summary>
    public bool UseSecurity { get; set; } = false;

    /// <summary>
    /// Username for authenticated connections. Leave null for anonymous access.
    /// </summary>
    public string? Username { get; set; }

    /// <summary>
    /// Password for authenticated connections. Leave null for anonymous access.
    /// </summary>
    public string? Password { get; set; }

    /// <summary>
    /// Whether to automatically accept untrusted server certificates. Should be false in production.
    /// </summary>
    public bool AutoAcceptUntrustedCertificates { get; set; } = true;
}
