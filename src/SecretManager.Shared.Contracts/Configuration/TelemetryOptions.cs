namespace SecretManager.Shared.Contracts.Configuration;

public sealed class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    public bool EnableConsoleExporter { get; set; }

    public string? OtlpEndpoint { get; set; }
}