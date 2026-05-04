namespace SecretManager.ConfigurationProvider;

public sealed class SecretManagerRuntimeOptions
{
	public string BaseAddress { get; set; } = "http://localhost:5159";

	public string ApplicationSlug { get; set; } = string.Empty;

	public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

	public HttpClient? HttpClient { get; set; }
}