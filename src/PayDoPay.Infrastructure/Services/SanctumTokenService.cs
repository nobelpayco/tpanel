using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PayDoPay.Application.Common.Interfaces;
using PayDoPay.Domain.Entities;

namespace PayDoPay.Infrastructure.Services;

public class SanctumTokenOptions
{
    /// <summary>Token toplam ömrü (dk). Sanctum expiration. 0/negatif = süresiz.</summary>
    public int ExpirationMinutes { get; set; } = 480;

    /// <summary>Hareketsizlik zaman aşımı (dk). EnforceIdleTimeout. 0 = kapalı.</summary>
    public int IdleMinutes { get; set; } = 30;
}

/// <summary>Sanctum uyumlu opaque token servisi (personal_access_tokens).</summary>
public class SanctumTokenService : ITokenService
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    private readonly IApplicationDbContext _db;
    private readonly IClock _clock;
    private readonly SanctumTokenOptions _options;

    public SanctumTokenService(IApplicationDbContext db, IClock clock, SanctumTokenOptions options)
    {
        _db = db;
        _clock = clock;
        _options = options;
    }

    public async Task<string> CreateTokenAsync(User user, string name = "auth-token", CancellationToken ct = default)
    {
        var random = GenerateRandom(40);
        var now = _clock.Now;

        var entity = new PersonalAccessToken
        {
            TokenableType = "App\\Models\\User",
            TokenableId = (ulong)user.Id,
            Name = name,
            Token = Sha256Hex(random),
            Abilities = "[\"*\"]",
            CreatedAt = now,
            UpdatedAt = now,
            ExpiresAt = _options.ExpirationMinutes > 0 ? now.AddMinutes(_options.ExpirationMinutes) : null,
        };

        _db.PersonalAccessTokens.Add(entity);
        await _db.SaveChangesAsync(ct);

        return $"{entity.Id}|{random}";
    }

    public async Task<TokenValidationResult> ValidateAsync(string plainTextToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(plainTextToken))
            return TokenValidationResult.Invalid;

        PersonalAccessToken? token;
        var pipeIndex = plainTextToken.IndexOf('|');
        if (pipeIndex > 0 && ulong.TryParse(plainTextToken[..pipeIndex], out var tokenId))
        {
            var random = plainTextToken[(pipeIndex + 1)..];
            var hash = Sha256Hex(random);
            token = await _db.PersonalAccessTokens.FirstOrDefaultAsync(t => t.Id == tokenId, ct);
            if (token is null || !FixedEquals(token.Token, hash))
                return TokenValidationResult.Invalid;
        }
        else
        {
            var hash = Sha256Hex(plainTextToken);
            token = await _db.PersonalAccessTokens.FirstOrDefaultAsync(t => t.Token == hash, ct);
            if (token is null)
                return TokenValidationResult.Invalid;
        }

        var now = _clock.Now;

        // Süre dolumu (Sanctum expiration)
        if (token.ExpiresAt is not null && token.ExpiresAt.Value < now)
            return TokenValidationResult.Expired;

        // Idle timeout (EnforceIdleTimeout) — token silinir
        if (_options.IdleMinutes > 0 && token.LastUsedAt is not null
            && token.LastUsedAt.Value < now.AddMinutes(-_options.IdleMinutes))
        {
            _db.PersonalAccessTokens.Remove(token);
            await _db.SaveChangesAsync(ct);
            return TokenValidationResult.IdleTimedOut;
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == (int)token.TokenableId, ct);
        if (user is null || user.IsBlocked)
            return TokenValidationResult.Invalid;

        // last_used_at güncelle
        token.LastUsedAt = now;
        await _db.SaveChangesAsync(ct);

        return new TokenValidationResult(TokenValidationStatus.Valid, user, token.Id);
    }

    public async Task DeleteAsync(ulong tokenId, CancellationToken ct = default)
    {
        await _db.PersonalAccessTokens
            .Where(t => t.Id == tokenId)
            .ExecuteDeleteAsync(ct);
    }

    public async Task DeleteAllForUserAsync(int userId, CancellationToken ct = default)
    {
        await _db.PersonalAccessTokens
            .Where(t => t.TokenableId == (ulong)userId)
            .ExecuteDeleteAsync(ct);
    }

    private static string GenerateRandom(int length)
    {
        var chars = new char[length];
        var bytes = RandomNumberGenerator.GetBytes(length);
        for (var i = 0; i < length; i++)
            chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        return new string(chars);
    }

    private static string Sha256Hex(string input)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    private static bool FixedEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(a ?? string.Empty),
            Encoding.ASCII.GetBytes(b ?? string.Empty));
}
