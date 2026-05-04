using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SecretManager.Infrastructure.Persistence;
using SecretManager.Infrastructure.Security;

namespace SecretManager.Infrastructure.Tests.Security;

public sealed class DraftValueProtectorTests
{
    [Fact]
    public void DraftValueProtector_CanUnprotectAcrossServiceProviderRestart_WhenKeyRingIsPersisted()
    {
        var keyRingPath = Path.Combine(Path.GetTempPath(), "secretmanager-tests", Guid.NewGuid().ToString("N"), "keys");

        try
        {
            var protectedPayload = ProtectWithNewProvider(keyRingPath, "\"super-secret-value\"");
            var restoredValue = UnprotectWithNewProvider(keyRingPath, protectedPayload);

            Assert.Equal("\"super-secret-value\"", restoredValue);
        }
        finally
        {
            var rootPath = Directory.GetParent(keyRingPath)?.FullName;
            if (!string.IsNullOrWhiteSpace(rootPath) && Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

    private static string ProtectWithNewProvider(string keyRingPath, string plaintext)
    {
        using var services = BuildProvider(keyRingPath);
        var protector = services.GetRequiredService<IDraftValueProtector>();
        return protector.Protect(plaintext);
    }

    private static string UnprotectWithNewProvider(string keyRingPath, string protectedPayload)
    {
        using var services = BuildProvider(keyRingPath);
        var protector = services.GetRequiredService<IDraftValueProtector>();
        return protector.Unprotect(protectedPayload);
    }

    private static ServiceProvider BuildProvider(string keyRingPath)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=localhost;Port=5432;Database=secretmanager_test;Username=secretmanager;Password=secretmanager",
                ["Infrastructure:DataProtection:KeyRingPath"] = keyRingPath
            })
            .Build();

        return new ServiceCollection()
            .AddSecretManagerPersistence(configuration)
            .BuildServiceProvider();
    }
}