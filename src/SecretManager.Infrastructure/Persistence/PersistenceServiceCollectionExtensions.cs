using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SecretManager.ControlPlane.Application.Authorization;
using SecretManager.ControlPlane.Application.Bootstrap;
using SecretManager.Infrastructure.Auditing;
using SecretManager.Infrastructure.Authorization;
using SecretManager.Infrastructure.Bootstrap;
using SecretManager.Infrastructure.Security;

namespace SecretManager.Infrastructure.Persistence;

public static class PersistenceServiceCollectionExtensions
{
    private const string InitialBootstrapAuthMigrationId = "20260423150940_InitialBootstrapAuth";
    private const string EfProductVersion = "10.0.4";

    public static IServiceCollection AddSecretManagerPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings:Postgres is required.");
        var keyRingPath = configuration["Infrastructure:DataProtection:KeyRingPath"]
            ?? Path.Combine(AppContext.BaseDirectory, "state", "keys");

        Directory.CreateDirectory(keyRingPath);
        services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));
        services.AddDbContext<SecretManagerDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<Argon2PasswordHasher>();
        services.AddSingleton<IDraftValueProtector, DraftValueProtector>();
        services.AddScoped<IAuditEventWriter, AuditEventWriter>();
        services.AddScoped<IPermissionEvaluator, PermissionEvaluator>();
        services.AddScoped<IBootstrapService, BootstrapService>();

        return services;
    }

    public static async Task InitializeSecretManagerPersistenceAsync(this IHost host)
    {
        await using var scope = host.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SecretManagerDbContext>();

        if (dbContext.Database.IsRelational())
        {
            await BaselineLegacySchemaAsync(dbContext);
            await dbContext.Database.MigrateAsync();
        }
        else
        {
            await dbContext.Database.EnsureCreatedAsync();
        }

        await SystemAuthorizationSeeder.SeedAsync(dbContext);
    }

    private static async Task BaselineLegacySchemaAsync(SecretManagerDbContext dbContext)
    {
        var historyRepository = dbContext.GetService<IHistoryRepository>();
        var historyExists = await historyRepository.ExistsAsync();

        if (!await LegacyBootstrapSchemaExistsAsync(dbContext))
        {
            return;
        }

        if (historyExists)
        {
            var appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync();
            if (appliedMigrations.Contains(InitialBootstrapAuthMigrationId, StringComparer.Ordinal))
            {
                return;
            }
        }

        if (!historyExists)
        {
            var createHistoryScript = historyRepository.GetCreateIfNotExistsScript();
            if (!string.IsNullOrWhiteSpace(createHistoryScript))
            {
                await dbContext.Database.ExecuteSqlRawAsync(createHistoryScript);
            }
        }

        var insertHistoryScript = historyRepository.GetInsertScript(
            new HistoryRow(InitialBootstrapAuthMigrationId, EfProductVersion));

        await dbContext.Database.ExecuteSqlRawAsync(insertHistoryScript);
    }

    private static async Task<bool> LegacyBootstrapSchemaExistsAsync(SecretManagerDbContext dbContext)
    {
        var connection = dbContext.Database.GetDbConnection();
        var shouldCloseConnection = connection.State != System.Data.ConnectionState.Open;

        if (shouldCloseConnection)
        {
            await connection.OpenAsync();
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    EXISTS (
                        SELECT 1
                        FROM information_schema.tables
                        WHERE table_schema = 'public' AND table_name = 'installations')
                AND EXISTS (
                        SELECT 1
                        FROM information_schema.tables
                        WHERE table_schema = 'public' AND table_name = 'user_accounts');
                """;

            var result = await command.ExecuteScalarAsync();
            return result is true;
        }
        finally
        {
            if (shouldCloseConnection)
            {
                await connection.CloseAsync();
            }
        }
    }
}