using System.Security.Cryptography;
using System.Text;
using PayDoPay.Application.Common.Interfaces;

namespace PayDoPay.Infrastructure.Services;

/// <summary>Legacy MD5 (mevcut DB ile uyum). Sabit-zamanlı karşılaştırma kullanır.</summary>
public class Md5PasswordHasher : IPasswordHasher
{
    public string HashMd5(string plain)
    {
        var bytes = MD5.HashData(Encoding.UTF8.GetBytes(plain));
        return Convert.ToHexStringLower(bytes);
    }

    public bool VerifyMd5(string plain, string storedHash)
    {
        var computed = HashMd5(plain);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(computed),
            Encoding.ASCII.GetBytes(storedHash ?? string.Empty));
    }
}
