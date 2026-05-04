using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Options;
using SecretManager.Agent.Worker.Runtime;

namespace SecretManager.Agent.Worker.Projection;

public sealed class AgentProjectionOptions
{
	public List<AgentProjectionTargetOptions> Targets { get; set; } = [];
}

public sealed class AgentProjectionTargetOptions
{
	public string ApplicationSlug { get; set; } = string.Empty;

	public string FilePath { get; set; } = string.Empty;
}

public interface IAgentProjectionService
{
	Task ProjectAsync(AgentApplicationRuntimeState state, CancellationToken cancellationToken);

	void MarkPending(string applicationSlug);

	string GetProjectionState(string applicationSlug);
}

public sealed class AgentProjectionService(
	IOptions<AgentProjectionOptions> options,
	AgentSnapshotDocumentBuilder documentBuilder) : IAgentProjectionService
{
	private readonly ConcurrentDictionary<string, string> projectionStates = new(StringComparer.OrdinalIgnoreCase);

	public async Task ProjectAsync(AgentApplicationRuntimeState state, CancellationToken cancellationToken)
	{
		var target = ResolveTarget(state.ApplicationSlug);
		if (target is null || string.IsNullOrWhiteSpace(target.FilePath) || state.ActiveSnapshot is null)
		{
			projectionStates[state.ApplicationSlug] = target is null ? "not-configured" : "pending";
			return;
		}

		try
		{
			var document = documentBuilder.Build(state.ActiveSnapshot);
			var payload = JsonSerializer.Serialize(document, new JsonSerializerOptions(JsonSerializerDefaults.Web)
			{
				WriteIndented = true
			});

			var directoryPath = Path.GetDirectoryName(target.FilePath);
			if (!string.IsNullOrWhiteSpace(directoryPath))
			{
				Directory.CreateDirectory(directoryPath);
			}

			var tempPath = $"{target.FilePath}.{Guid.NewGuid():N}.tmp";
			await File.WriteAllTextAsync(tempPath, payload, cancellationToken);
			File.Move(tempPath, target.FilePath, overwrite: true);
			projectionStates[state.ApplicationSlug] = "current";
		}
		catch
		{
			projectionStates[state.ApplicationSlug] = "failed";
		}
	}

	public void MarkPending(string applicationSlug)
	{
		projectionStates[applicationSlug] = ResolveTarget(applicationSlug) is null ? "not-configured" : "pending";
	}

	public string GetProjectionState(string applicationSlug)
	{
		return projectionStates.TryGetValue(applicationSlug, out var state)
			? state
			: ResolveTarget(applicationSlug) is null
				? "not-configured"
				: "pending";
	}

	private AgentProjectionTargetOptions? ResolveTarget(string applicationSlug)
	{
		return options.Value.Targets.FirstOrDefault(x => string.Equals(x.ApplicationSlug, applicationSlug, StringComparison.OrdinalIgnoreCase));
	}
}