namespace SecretManager.Agent.Worker.Configuration;

public sealed class AgentOptions
{
	public const string SectionName = "Agent";

	public string ControlPlaneBaseUrl { get; set; } = string.Empty;

	public Guid ManagedNodeId { get; set; }

	public string Hostname { get; set; } = Environment.MachineName;

	public string Platform { get; set; } = OperatingSystem.IsWindows() ? "windows" : "linux";

	public string AgentVersion { get; set; } = "0.1.0";

	public string EnrollmentToken { get; set; } = string.Empty;

	public bool EnableBackgroundSync { get; set; } = true;

	public int SyncPollIntervalSeconds { get; set; } = 30;

	public bool EnableChangeNotifications { get; set; } = true;

	public int NotificationReconnectDelaySeconds { get; set; } = 5;
}