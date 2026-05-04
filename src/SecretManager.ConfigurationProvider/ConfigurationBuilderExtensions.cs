using Microsoft.Extensions.Configuration;

namespace SecretManager.ConfigurationProvider;

public static class ConfigurationBuilderExtensions
{
	public static IConfigurationBuilder AddSecretManagerRuntime(
		this IConfigurationBuilder builder,
		Action<SecretManagerRuntimeOptions> configure,
		out SecretManagerRuntimeMetadataAccessor metadataAccessor)
	{
		var options = new SecretManagerRuntimeOptions();
		configure(options);
		metadataAccessor = new SecretManagerRuntimeMetadataAccessor();
		builder.Add(new SecretManagerRuntimeConfigurationSource(options, metadataAccessor));
		return builder;
	}
}