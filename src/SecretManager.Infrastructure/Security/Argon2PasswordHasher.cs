using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Konscious.Security.Cryptography;

namespace SecretManager.Infrastructure.Security;

public sealed class Argon2PasswordHasher
{
    private const int Iterations = 4;
    private const int MemorySizeKb = 65536;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    public string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            Iterations = Iterations,
            MemorySize = MemorySizeKb,
            DegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, 4)
        };

        var hash = argon2.GetBytes(HashSize);

        return string.Join(
            '$',
            "argon2id",
            Iterations,
            MemorySizeKb,
            argon2.DegreeOfParallelism,
            Convert.ToBase64String(salt),
            Convert.ToBase64String(hash));
    }

    public bool Verify(string password, string encodedHash)
    {
        var parts = encodedHash.Split('$');
        if (parts.Length != 6 || !string.Equals(parts[0], "argon2id", StringComparison.Ordinal))
        {
            return false;
        }

        if (!int.TryParse(parts[1], out var iterations) ||
            !int.TryParse(parts[2], out var memorySizeKb) ||
            !int.TryParse(parts[3], out var degreeOfParallelism))
        {
            return false;
        }

        var salt = Convert.FromBase64String(parts[4]);
        var expectedHash = Convert.FromBase64String(parts[5]);

        var argon2 = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            Iterations = iterations,
            MemorySize = memorySizeKb,
            DegreeOfParallelism = degreeOfParallelism
        };

        var actualHash = argon2.GetBytes(expectedHash.Length);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }
}

public interface IDraftValueProtector
{
    string Protect(string plaintext);

    string Unprotect(string protectedPayload);
}

public sealed class DraftValueProtector(IDataProtectionProvider dataProtectionProvider) : IDraftValueProtector
{
    private const string ProtectedPrefix = "smdraft:v1:";
    private readonly IDataProtector protector = dataProtectionProvider.CreateProtector("SecretManager.DraftValue.v1");

    public string Protect(string plaintext)
    {
        if (plaintext.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
        {
            return plaintext;
        }

        return ProtectedPrefix + protector.Protect(plaintext);
    }

    public string Unprotect(string protectedPayload)
    {
        if (!protectedPayload.StartsWith(ProtectedPrefix, StringComparison.Ordinal))
        {
            return protectedPayload;
        }

        return protector.Unprotect(protectedPayload[ProtectedPrefix.Length..]);
    }
}