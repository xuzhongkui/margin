using System.Security.Cryptography;
using System.Text;

namespace WebApi.Services.Security;

public interface IPasswordHasher
{
    string Hash(string password, string salt);
    bool Verify(string password, string salt, string passwordHash);
    string GenerateSalt();
}

public sealed class PasswordHasher : IPasswordHasher
{
    public string Hash(string password, string salt)
    {
        var bytes = Encoding.UTF8.GetBytes(password + salt);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    public bool Verify(string password, string salt, string passwordHash)
    {
        var computed = Hash(password, salt);
        return string.Equals(computed, passwordHash, StringComparison.OrdinalIgnoreCase);
    }

    public string GenerateSalt()
    {
        var saltBytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToHexString(saltBytes);
    }
}
