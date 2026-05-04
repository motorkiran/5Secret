using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SecretManager.Infrastructure.Persistence;

public sealed class SecretManagerDesignTimeDbContextFactory : IDesignTimeDbContextFactory<SecretManagerDbContext>
{
    public SecretManagerDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SECRETMANAGER_DESIGNTIME_POSTGRES")
            ?? "Host=localhost;Port=5432;Database=secretmanager;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<SecretManagerDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new SecretManagerDbContext(optionsBuilder.Options);
    }
}