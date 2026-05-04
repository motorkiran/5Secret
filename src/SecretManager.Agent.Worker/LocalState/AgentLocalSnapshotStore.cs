using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SecretManager.Shared.Contracts.Agents;

namespace SecretManager.Agent.Worker.LocalState;

public sealed class AgentLocalSnapshotStoreOptions
{
	public string FilePath { get; set; } = Path.Combine(AppContext.BaseDirectory, "state", "snapshots.enc.json");
}

public sealed class AgentPersistedRuntimeStateDocument
{
	public DateTimeOffset PersistedAtUtc { get; set; } = DateTimeOffset.UtcNow;

	public List<AgentPersistedApplicationState> Applications { get; set; } = [];
}

public sealed class AgentPersistedApplicationState
{
	public Guid ApplicationId { get; set; }

	public string ApplicationSlug { get; set; } = string.Empty;

	public AgentSnapshotResponse? ActiveSnapshot { get; set; }

	public AgentSnapshotResponse? StagedSnapshot { get; set; }
}

public sealed record AgentLocalSnapshotLoadResult(
	bool Success,
	AgentPersistedRuntimeStateDocument? State,
	string? FailureReason);

public interface IAgentLocalSnapshotStore
{
	Task SaveAsync(
		AgentPersistedRuntimeStateDocument state,
		string enrollmentSecret,
		string localSalt,
		CancellationToken cancellationToken);

	Task<AgentLocalSnapshotLoadResult> LoadAsync(
		string enrollmentSecret,
		string localSalt,
		CancellationToken cancellationToken);
}

public sealed class AgentLocalSnapshotStore(IOptions<AgentLocalSnapshotStoreOptions> options) : IAgentLocalSnapshotStore
{
	private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true
	};

	public async Task SaveAsync(
		AgentPersistedRuntimeStateDocument state,
		string enrollmentSecret,
		string localSalt,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(state);
		ValidateState(state);

		var plaintext = JsonSerializer.SerializeToUtf8Bytes(state, SerializerOptions);
		var key = DeriveKey(enrollmentSecret, localSalt);
		var nonce = RandomNumberGenerator.GetBytes(12);
		var ciphertext = new byte[plaintext.Length];
		var tag = new byte[16];

		using (var aes = new AesGcm(key, 16))
		{
			aes.Encrypt(nonce, plaintext, ciphertext, tag);
		}

		var envelope = new EncryptedAgentSnapshotEnvelope
		{
			Version = 1,
			Nonce = Convert.ToBase64String(nonce),
			Ciphertext = Convert.ToBase64String(ciphertext),
			Tag = Convert.ToBase64String(tag)
		};

		var filePath = options.Value.FilePath;
		var directoryPath = Path.GetDirectoryName(filePath);
		if (!string.IsNullOrWhiteSpace(directoryPath))
		{
			Directory.CreateDirectory(directoryPath);
		}

		var tempPath = $"{filePath}.{Guid.NewGuid():N}.tmp";
		await File.WriteAllTextAsync(
			tempPath,
			JsonSerializer.Serialize(envelope, SerializerOptions),
			Encoding.UTF8,
			cancellationToken);
		File.Move(tempPath, filePath, overwrite: true);
	}

	public async Task<AgentLocalSnapshotLoadResult> LoadAsync(
		string enrollmentSecret,
		string localSalt,
		CancellationToken cancellationToken)
	{
		var filePath = options.Value.FilePath;
		if (!File.Exists(filePath))
		{
			return new AgentLocalSnapshotLoadResult(true, null, null);
		}

		try
		{
			var payload = await File.ReadAllTextAsync(filePath, cancellationToken);
			var envelope = JsonSerializer.Deserialize<EncryptedAgentSnapshotEnvelope>(payload, SerializerOptions);
			if (envelope is null
				|| envelope.Version != 1
				|| string.IsNullOrWhiteSpace(envelope.Nonce)
				|| string.IsNullOrWhiteSpace(envelope.Ciphertext)
				|| string.IsNullOrWhiteSpace(envelope.Tag))
			{
				return new AgentLocalSnapshotLoadResult(false, null, "Snapshot store envelope is invalid.");
			}

			var key = DeriveKey(enrollmentSecret, localSalt);
			var nonce = Convert.FromBase64String(envelope.Nonce);
			var ciphertext = Convert.FromBase64String(envelope.Ciphertext);
			var tag = Convert.FromBase64String(envelope.Tag);
			var plaintext = new byte[ciphertext.Length];

			using (var aes = new AesGcm(key, 16))
			{
				aes.Decrypt(nonce, ciphertext, tag, plaintext);
			}

			var state = JsonSerializer.Deserialize<AgentPersistedRuntimeStateDocument>(plaintext, SerializerOptions);
			if (state is null)
			{
				return new AgentLocalSnapshotLoadResult(false, null, "Snapshot store payload is invalid.");
			}

			ValidateState(state);
			return new AgentLocalSnapshotLoadResult(true, state, null);
		}
		catch (Exception ex) when (ex is CryptographicException or FormatException or JsonException or InvalidOperationException)
		{
			return new AgentLocalSnapshotLoadResult(false, null, ex.Message);
		}
	}

	private static byte[] DeriveKey(string enrollmentSecret, string localSalt)
	{
		if (string.IsNullOrWhiteSpace(enrollmentSecret))
		{
			throw new ArgumentException("Enrollment secret is required.", nameof(enrollmentSecret));
		}

		if (string.IsNullOrWhiteSpace(localSalt))
		{
			throw new ArgumentException("Local salt is required.", nameof(localSalt));
		}

		return Rfc2898DeriveBytes.Pbkdf2(
			Encoding.UTF8.GetBytes(enrollmentSecret),
			Encoding.UTF8.GetBytes(localSalt),
			100_000,
			HashAlgorithmName.SHA256,
			32);
	}

	private static void ValidateState(AgentPersistedRuntimeStateDocument state)
	{
		foreach (var application in state.Applications)
		{
			if (string.IsNullOrWhiteSpace(application.ApplicationSlug))
			{
				throw new InvalidOperationException("ApplicationSlug is required for persisted snapshots.");
			}

			ValidateSnapshot(application.ActiveSnapshot);
			ValidateSnapshot(application.StagedSnapshot);
		}
	}

	private static void ValidateSnapshot(AgentSnapshotResponse? snapshot)
	{
		if (snapshot is not null && !AgentSnapshotIntegrity.Validate(snapshot))
		{
			throw new InvalidOperationException($"Persisted snapshot '{snapshot.SnapshotId}' failed integrity validation.");
		}
	}

	private sealed class EncryptedAgentSnapshotEnvelope
	{
		public int Version { get; set; }

		public string Nonce { get; set; } = string.Empty;

		public string Ciphertext { get; set; } = string.Empty;

		public string Tag { get; set; } = string.Empty;
	}
}