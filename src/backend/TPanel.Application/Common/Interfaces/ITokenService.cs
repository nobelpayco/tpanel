using TPanel.Domain.Entities;

namespace TPanel.Application.Common.Interfaces;

/// <summary>
/// Sanctum uyumlu opaque token yönetimi (personal_access_tokens).
/// Düz metin biçimi: "{tokenId}|{random40}", DB'de saklanan: sha256(random40).
/// </summary>
public interface ITokenService
{
    /// <summary>Yeni token üretir, DB'ye yazar, frontend'e verilecek düz metni döner.</summary>
    Task<string> CreateTokenAsync(User user, string name = "auth-token", CancellationToken ct = default);

    /// <summary>Düz metin token'ı doğrular.</summary>
    Task<TokenValidationResult> ValidateAsync(string plainTextToken, CancellationToken ct = default);

    /// <summary>Tek bir token'ı siler (logout).</summary>
    Task DeleteAsync(ulong tokenId, CancellationToken ct = default);

    /// <summary>Bir kullanıcının tüm token'larını siler (şifre değişimi).</summary>
    Task DeleteAllForUserAsync(int userId, CancellationToken ct = default);
}

public enum TokenValidationStatus
{
    Valid,
    Invalid,
    IdleTimedOut,
    Expired,
}

public record TokenValidationResult(TokenValidationStatus Status, User? User = null, ulong TokenId = 0)
{
    public static readonly TokenValidationResult Invalid = new(TokenValidationStatus.Invalid);
    public static readonly TokenValidationResult IdleTimedOut = new(TokenValidationStatus.IdleTimedOut);
    public static readonly TokenValidationResult Expired = new(TokenValidationStatus.Expired);
}
