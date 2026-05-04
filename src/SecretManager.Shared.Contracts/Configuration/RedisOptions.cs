namespace SecretManager.Shared.Contracts.Configuration;

public sealed class RedisOptions
{
    public const string SectionName = "Infrastructure:Redis";

    public string Configuration { get; set; } = string.Empty;

    public string InstanceName { get; set; } = string.Empty;
}