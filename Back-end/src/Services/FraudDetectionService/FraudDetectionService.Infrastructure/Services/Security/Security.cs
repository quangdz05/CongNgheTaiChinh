using System.Security.Cryptography;
using System.Text;
using FraudDetectionService.Application;
namespace FraudDetectionService.Infrastructure;

public sealed class PiiHasher(string salt) : IPiiHasher
{
    public string Hash(string value) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(salt), Encoding.UTF8.GetBytes(value.Trim()))).ToLowerInvariant();
}
public static class Passwords
{
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 150000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }
    public static bool Verify(string password, string encoded)
    {
        try
        {
            var parts = encoded.Split('.');
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(parts[0]), 150000, HashAlgorithmName.SHA256, 32);
            return CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(parts[1]));
        }
        catch (FormatException) { return false; }
    }
}
public sealed class PasswordVerifier : IPasswordVerifier
{
    private static readonly string DummyHash = Passwords.Hash("dummy-login-password");
    public bool Verify(string password, string? encoded) => Passwords.Verify(password, encoded ?? DummyHash);
}
