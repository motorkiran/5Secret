using System.Text.Json;
using System.Text.Json.Nodes;
using SecretManager.Shared.Contracts.Agents;

namespace SecretManager.Agent.Worker.Runtime;

public sealed class AgentSnapshotDocumentBuilder
{
	public JsonElement Build(AgentSnapshotResponse snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);

		var root = new JsonObject();
		foreach (var value in snapshot.Values.OrderBy(x => x.FullPath, StringComparer.OrdinalIgnoreCase))
		{
			Insert(root, value.FullPath.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), ParseValue(value.ValueJson));
		}

		return JsonSerializer.SerializeToElement(root, new JsonSerializerOptions(JsonSerializerDefaults.Web));
	}

	private static JsonNode ParseValue(string valueJson)
	{
		return JsonNode.Parse(valueJson)
			?? JsonValue.Create((string?)null)!;
	}

	private static void Insert(JsonObject root, IReadOnlyList<string> segments, JsonNode value)
	{
		var current = root;
		for (var index = 0; index < segments.Count - 1; index++)
		{
			var segment = segments[index];
			if (current[segment] is not JsonObject next)
			{
				next = new JsonObject();
				current[segment] = next;
			}

			current = next;
		}

		current[segments[^1]] = value.DeepClone();
	}
}