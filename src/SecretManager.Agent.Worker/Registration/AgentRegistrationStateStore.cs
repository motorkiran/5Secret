using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace SecretManager.Agent.Worker.Registration;

public sealed class AgentRegistrationStateStoreOptions
{
	public string FilePath { get; set; } = Path.Combine(AppContext.BaseDirectory, "state", "registration.protected.json");
}

public sealed class AgentRegistrationState
{
	public Guid AgentId { get; set; }

	public Guid ManagedNodeId { get; set; }

	public Guid EnvironmentId { get; set; }

	public string AgentCredential { get; set; } = string.Empty;

	public string EnrollmentSecret { get; set; } = string.Empty;

	public string LocalSalt { get; set; } = string.Empty;

	public DateTimeOffset EnrolledAtUtc { get; set; }
}

public interface IAgentRegistrationStateStore
{
	Task<AgentRegistrationState?> LoadAsync(CancellationToken cancellationToken);

	Task SaveAsync(AgentRegistrationState state, CancellationToken cancellationToken);
}

public sealed class AgentRegistrationStateStore(
	IDataProtectionProvider dataProtectionProvider,
	IOptions<AgentRegistrationStateStoreOptions> options) : IAgentRegistrationStateStore
{
	private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true
	};

	private readonly IDataProtector protector = dataProtectionProvider.CreateProtector("SecretManager.Agent.RegistrationState.v1");

	public async Task<AgentRegistrationState?> LoadAsync(CancellationToken cancellationToken)
	{
		var filePath = options.Value.FilePath;
		if (!File.Exists(filePath))
		{
			return null;
		}

		var payload = await File.ReadAllTextAsync(filePath, cancellationToken);
		var envelope = JsonSerializer.Deserialize<ProtectedRegistrationEnvelope>(payload, SerializerOptions);
		if (envelope is null || string.IsNullOrWhiteSpace(envelope.ProtectedPayload))
		{
			return null;
		}

		var unprotectedPayload = protector.Unprotect(envelope.ProtectedPayload);
		return JsonSerializer.Deserialize<AgentRegistrationState>(unprotectedPayload, SerializerOptions);
	}

	public async Task SaveAsync(AgentRegistrationState state, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(state);
		if (state.AgentId == Guid.Empty || state.ManagedNodeId == Guid.Empty || state.EnvironmentId == Guid.Empty)
		{
			throw new InvalidOperationException("Agent registration state requires agent, node, and environment identifiers.");
		}

		if (string.IsNullOrWhiteSpace(state.AgentCredential)
			|| string.IsNullOrWhiteSpace(state.EnrollmentSecret)
			|| string.IsNullOrWhiteSpace(state.LocalSalt))
		{
			throw new InvalidOperationException("Agent registration state requires credential, enrollment secret, and local salt.");
		}

		var filePath = options.Value.FilePath;
		var directoryPath = Path.GetDirectoryName(filePath);
		if (!string.IsNullOrWhiteSpace(directoryPath))
		{
			Directory.CreateDirectory(directoryPath);
		}

		var protectedPayload = protector.Protect(JsonSerializer.Serialize(state, SerializerOptions));
		var envelope = new ProtectedRegistrationEnvelope
		{
			Version = 1,
			ProtectedPayload = protectedPayload,
			Checksum = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(protectedPayload))).ToLowerInvariant()
		};

		var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
		await File.WriteAllTextAsync(tempPath, JsonSerializer.Serialize(envelope, SerializerOptions), cancellationToken);
		File.Move(tempPath, filePath, overwrite: true);
	}

	private sealed class ProtectedRegistrationEnvelope
	{
		public int Version { get; set; }

		public string ProtectedPayload { get; set; } = string.Empty;

		public string Checksum { get; set; } = string.Empty;
	}
}