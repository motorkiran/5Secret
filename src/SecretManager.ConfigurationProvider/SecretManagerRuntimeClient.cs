using System.Net.Http.Json;
using SecretManager.Shared.Contracts.Agents;

namespace SecretManager.ConfigurationProvider;

public sealed class SecretManagerRuntimeClient : IDisposable
{
	private readonly HttpClient httpClient;
	private readonly bool ownsHttpClient;
	private readonly string applicationSlug;

	public SecretManagerRuntimeClient(SecretManagerRuntimeOptions options)
	{
		ArgumentNullException.ThrowIfNull(options);
		if (string.IsNullOrWhiteSpace(options.ApplicationSlug))
		{
			throw new InvalidOperationException("ApplicationSlug is required.");
		}

		applicationSlug = options.ApplicationSlug;
		if (options.HttpClient is not null)
		{
			httpClient = options.HttpClient;
			ownsHttpClient = false;
		}
		else
		{
			httpClient = new HttpClient
			{
				BaseAddress = new Uri(options.BaseAddress, UriKind.Absolute)
			};
			ownsHttpClient = true;
		}
	}

	public async Task<RuntimeApplicationSnapshotResponse> GetSnapshotAsync(CancellationToken cancellationToken)
	{
		return await httpClient.GetFromJsonAsync<RuntimeApplicationSnapshotResponse>(
			$"/runtime/v1/applications/{applicationSlug}/snapshot",
			cancellationToken)
			?? throw new InvalidOperationException("The local runtime snapshot response was empty.");
	}

	public async Task<RuntimeApplicationVersionResponse> GetVersionAsync(CancellationToken cancellationToken)
	{
		return await httpClient.GetFromJsonAsync<RuntimeApplicationVersionResponse>(
			$"/runtime/v1/applications/{applicationSlug}/version",
			cancellationToken)
			?? throw new InvalidOperationException("The local runtime version response was empty.");
	}

	public void Dispose()
	{
		if (ownsHttpClient)
		{
			httpClient.Dispose();
		}
	}
}