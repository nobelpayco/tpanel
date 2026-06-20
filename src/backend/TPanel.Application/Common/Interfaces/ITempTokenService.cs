namespace TPanel.Application.Common.Interfaces;

/// <summary>
/// 2FA arası geçici token (Laravel encrypt(userId|expiry) yerine HMAC imzalı self-contained token).
/// Aynı backend üretip doğruladığı için Laravel ile uyumlu olması gerekmez.
/// </summary>
public interface ITempTokenService
{
    string Create(int userId, int ttlMinutes = 5);

    /// <summary>Geçerli ve süresi dolmamışsa userId döner; aksi halde null.</summary>
    int? Validate(string token);
}
