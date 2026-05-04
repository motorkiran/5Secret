using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using SecretManager.Agent.Worker.Registration;

namespace SecretManager.Infrastructure.Tests.Agent;

public sealed class AgentRegistrationStateStoreTests : IDisposable
{
	private readonly string tempDirectory = Path.Combine(Path.GetTempPath(), $"secretmanager-agent-registration-{Guid.NewGuid():N}");

	[Fact]
	public async Task SaveAsync_PersistsProtectedRegistration_AndLoadAsync_RestoresIt()
	{
		var filePath = Path.Combine(tempDirectory, "registration.protected.json");
		var store = CreateStore(filePath);
		var state = new AgentRegistrationState
		{
			AgentId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
			ManagedNodeId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
			EnvironmentId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
			AgentCredential = "agent-credential-secret",
			EnrollmentSecret = "enrollment-secret-material",
			LocalSalt = "local-runtime-salt",
			EnrolledAtUtc = DateTimeOffset.Parse("2026-05-03T21:30:00Z")
		};

		await store.SaveAsync(state, CancellationToken.None);

		var payload = await File.ReadAllTextAsync(filePath);
		Assert.DoesNotContain(state.AgentCredential, payload, StringComparison.Ordinal);
		Assert.DoesNotContain(state.EnrollmentSecret, payload, StringComparison.Ordinal);

		var restored = await CreateStore(filePath).LoadAsync(CancellationToken.None);

		Assert.NotNull(restored);
		Assert.Equal(state.AgentId, restored!.AgentId);
		Assert.Equal(state.ManagedNodeId, restored.ManagedNodeId);
		Assert.Equal(state.AgentCredential, restored.AgentCredential);
		Assert.Equal(state.EnrollmentSecret, restored.EnrollmentSecret);
		Assert.Equal(state.LocalSalt, restored.LocalSalt);
	}

	[Fact]
	public async Task LoadAsync_ReturnsNull_WhenRegistrationFileIsMissing()
	{
		var store = CreateStore(Path.Combine(tempDirectory, "missing.protected.json"));

		var restored = await store.LoadAsync(CancellationToken.None);

		Assert.Null(restored);
	}

	public void Dispose()
	{
		if (Directory.Exists(tempDirectory))
		{
			Directory.Delete(tempDirectory, recursive: true);
		}
	}

	private AgentRegistrationStateStore CreateStore(string filePath)
	{
		Directory.CreateDirectory(tempDirectory);
		var dataProtectionProvider = DataProtectionProvider.Create(new DirectoryInfo(Path.Combine(tempDirectory, "keys")));
		return new AgentRegistrationStateStore(
			dataProtectionProvider,
			Options.Create(new AgentRegistrationStateStoreOptions
			{
				FilePath = filePath
			}));
	}
}