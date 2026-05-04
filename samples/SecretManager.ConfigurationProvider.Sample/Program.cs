using System.Text.Json;
using Microsoft.Extensions.Configuration;
using SecretManager.ConfigurationProvider;

namespace SecretManager.ConfigurationProvider.Sample;

public static class Program
{
	public static Task<int> Main(string[] args)
	{
		return SampleApplication.RunAsync(args);
	}
}

public static class SampleApplication
{
	public static async Task<int> RunAsync(
		string[] args,
		HttpClient? httpClient = null,
		TextWriter? output = null,
		CancellationToken cancellationToken = default)
	{
		var settings = SampleArguments.Parse(args);
		var configurationBuilder = new ConfigurationBuilder();
		configurationBuilder.AddSecretManagerRuntime(
			options =>
			{
				options.ApplicationSlug = settings.ApplicationSlug;
				options.BaseAddress = settings.BaseAddress;
				options.HttpClient = httpClient;
				options.RefreshInterval = TimeSpan.Zero;
			},
			out var metadataAccessor);

		var configuration = configurationBuilder.Build();
		var value = configuration[settings.ConfigurationKey]
			?? throw new InvalidOperationException($"Configuration key '{settings.ConfigurationKey}' was not present in the local agent snapshot.");
		var metadata = metadataAccessor.Current
			?? throw new InvalidOperationException("Runtime metadata was not available from the local agent.");

		var writer = output ?? Console.Out;
		var payload = new SampleOutput(
			settings.ApplicationSlug,
			settings.ConfigurationKey,
			value,
			metadata.VersionNumber,
			metadata.SnapshotHash,
			metadata.HealthState);

		var content = JsonSerializer.Serialize(payload);
		if (writer == Console.Out)
		{
			await Console.Out.WriteLineAsync(content);
		}
		else
		{
			await writer.WriteLineAsync(content);
		}
		return 0;
	}
}

internal sealed record SampleArguments(string ApplicationSlug, string BaseAddress, string ConfigurationKey)
{
	public static SampleArguments Parse(string[] args)
	{
		var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			["base-address"] = "http://localhost:5159",
			["config-key"] = "Trading:Core:MaxRetries"
		};

		foreach (var argument in args)
		{
			if (!argument.StartsWith("--", StringComparison.Ordinal))
			{
				continue;
			}

			var separatorIndex = argument.IndexOf('=');
			if (separatorIndex <= 2 || separatorIndex == argument.Length - 1)
			{
				continue;
			}

			var key = argument[2..separatorIndex];
			var value = argument[(separatorIndex + 1)..];
			values[key] = value;
		}

		if (!values.TryGetValue("application-slug", out var applicationSlug) || string.IsNullOrWhiteSpace(applicationSlug))
		{
			throw new InvalidOperationException("Specify --application-slug=<slug>.");
		}

		return new SampleArguments(
			applicationSlug,
			values["base-address"],
			values["config-key"]);
	}
}

public sealed record SampleOutput(
	string ApplicationSlug,
	string ConfigurationKey,
	string Value,
	int VersionNumber,
	string SnapshotHash,
	string HealthState);