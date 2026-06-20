using System.Security.Cryptography;

namespace TPanel.Application.Features.PublicApi;

/// <summary>Public API yapılandırması (app.url vb.).</summary>
public class PublicApiOptions
{
    public string AppUrl { get; set; } = "http://localhost";
    public string? PayrouteChatId { get; set; }
}

public static class PublicApiHelpers
{
    /// <summary>bin2hex(random_bytes(16)) — 32 hex karakter.</summary>
    public static string GenerateUId() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));

    public static bool IsValidUrl(string? url, int maxLen = 500)
        => !string.IsNullOrWhiteSpace(url) && url.Length <= maxLen
           && Uri.TryCreate(url, UriKind.Absolute, out var u)
           && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

    public static bool IsRequired(string? s, int maxLen) => !string.IsNullOrWhiteSpace(s) && s.Length <= maxLen;
}
