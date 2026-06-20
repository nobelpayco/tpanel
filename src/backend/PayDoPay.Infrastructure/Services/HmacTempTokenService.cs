using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using PayDoPay.Application.Common.Interfaces;

namespace PayDoPay.Infrastructure.Services;

/// <summary>
/// 2FA arası geçici token: base64url("{userId}|{expiryUnix}") + "." + base64url(HMAC-SHA256).
/// Aynı backend üretip doğruladığı için self-contained ve imzalıdır.
/// </summary>
public class HmacTempTokenService : ITempTokenService
{
    private readonly byte[] _key;
    private readonly IClock _clock;

    public HmacTempTokenService(IConfiguration config, IClock clock)
    {
        _clock = clock;
        var secret = config["Security:TempTokenKey"]
            ?? config["Security:AppKey"]
            ?? "paydopay-default-temp-token-key-change-me";
        _key = Encoding.UTF8.GetBytes(secret);
    }

    public string Create(int userId, int ttlMinutes = 5)
    {
        var expiry = _clock.UnixNow + ttlMinutes * 60;
        var payload = $"{userId}|{expiry}";
        var payloadB64 = Base64Url(Encoding.UTF8.GetBytes(payload));
        var sig = Base64Url(Sign(payloadB64));
        return $"{payloadB64}.{sig}";
    }

    public int? Validate(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var parts = token.Split('.');
        if (parts.Length != 2)
            return null;

        var expectedSig = Base64Url(Sign(parts[0]));
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(parts[1]),
                Encoding.ASCII.GetBytes(expectedSig)))
            return null;

        string payload;
        try { payload = Encoding.UTF8.GetString(FromBase64Url(parts[0])); }
        catch { return null; }

        var seg = payload.Split('|');
        if (seg.Length != 2 || !int.TryParse(seg[0], out var userId) || !long.TryParse(seg[1], out var expiry))
            return null;

        if (_clock.UnixNow > expiry)
            return null;

        return userId;
    }

    private byte[] Sign(string data)
    {
        using var hmac = new HMACSHA256(_key);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string s)
    {
        var padded = s.Replace('-', '+').Replace('_', '/');
        padded += (padded.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(padded);
    }
}
