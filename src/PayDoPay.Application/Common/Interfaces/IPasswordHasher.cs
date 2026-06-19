namespace PayDoPay.Application.Common.Interfaces;

/// <summary>Legacy MD5 şifre uyumluluğu (mevcut DB md5(password) saklıyor).</summary>
public interface IPasswordHasher
{
    string HashMd5(string plain);
    bool VerifyMd5(string plain, string storedHash);
}
