namespace TPanel.Domain.Entities;

/// <summary>
/// Sanctum opaque token deposu — `personal_access_tokens`.
/// Frontend'e verilen düz metin: "{id}|{plainText}". DB'deki `token` = sha256(plainText).
/// </summary>
public class PersonalAccessToken
{
    public ulong Id { get; set; }
    public string TokenableType { get; set; } = "App\\Models\\User";
    public ulong TokenableId { get; set; }
    public string Name { get; set; } = "auth-token";

    /// <summary>sha256(plainText) — hex.</summary>
    public string Token { get; set; } = string.Empty;

    public string? Abilities { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
