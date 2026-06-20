using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.PublicApi;

namespace TPanel.Api.Middleware;

/// <summary>
/// Merchant API v1 HMAC (Stripe tarzı) — PHP MerchantApiAuth birebir.
/// Sadece /api/v1/deposit, /withdraw, /transaction yollarına uygulanır (pay/* ve health hariç).
/// signed_payload = METHOD + "\n" + PATH + "\n" + TIMESTAMP + "\n" + sha256(body)
/// </summary>
public class MerchantApiAuthMiddleware
{
    private const int TimestampDriftSeconds = 300;
    private static readonly string[] ProtectedPrefixes =
        { "/api/v1/deposit", "/api/v1/withdraw", "/api/v1/transaction" };

    private readonly RequestDelegate _next;

    public MerchantApiAuthMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;
        if (!ProtectedPrefixes.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            await _next(context);
            return;
        }

        var sw = Stopwatch.StartNew();
        var store = context.RequestServices.GetRequiredService<IMerchantApiStore>();
        var dbFactory = context.RequestServices.GetRequiredService<IDbConnectionFactory>();
        var clock = context.RequestServices.GetRequiredService<IClock>();

        var apiKey = context.Request.Headers["X-Api-Key"].ToString();
        var timestamp = context.Request.Headers["X-Timestamp"].ToString();
        var signature = context.Request.Headers["X-Signature"].ToString();

        async Task<bool> Fail(int status, string message, int? merchantId)
        {
            var bodyJson = System.Text.Json.JsonSerializer.Serialize(new { code = status, status = false, message });
            await LogAsync(dbFactory, clock, context, merchantId, status, "error", message, sw, bodyJson);
            context.Response.StatusCode = status;
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsync(bodyJson);
            return false;
        }

        if (apiKey == "" || timestamp == "" || signature == "")
        {
            await Fail(401, "Missing authentication headers.", null);
            return;
        }

        var merchant = await store.FindMerchantByApiKeyAsync(apiKey, context.RequestAborted);
        if (merchant is null) { await Fail(401, "Invalid apiKey.", null); return; }
        if (merchant.Status != "1") { await Fail(403, "Account is not active.", merchant.Id); return; }
        if (string.IsNullOrEmpty(merchant.ApiSecret)) { await Fail(500, "apiSecret is not configured.", merchant.Id); return; }

        var now = clock.UnixNow;
        if (!long.TryParse(timestamp, out var ts) || Math.Abs(now - ts) > TimestampDriftSeconds)
        {
            await Fail(401, "Timestamp drift exceeded.", merchant.Id);
            return;
        }

        // Gövdeyi oku (controller tekrar okuyabilsin diye buffering)
        context.Request.EnableBuffering();
        string body;
        using (var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            body = await reader.ReadToEndAsync(context.RequestAborted);
            context.Request.Body.Position = 0;
        }

        var bodyHash = Sha256Hex(body);
        var signedPayload = context.Request.Method + "\n" + path + "\n" + timestamp + "\n" + bodyHash;
        var expected = HmacSha256Hex(merchant.ApiSecret!, signedPayload);

        if (!FixedEquals(expected, signature))
        {
            await Fail(401, "İmza hatalı.", merchant.Id);
            return;
        }

        context.Items["_merchant"] = merchant;

        // Yanıtı yakala (loglamak için)
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await _next(context);

        buffer.Position = 0;
        var responseText = await new StreamReader(buffer).ReadToEndAsync(context.RequestAborted);
        buffer.Position = 0;
        await buffer.CopyToAsync(originalBody, context.RequestAborted);
        context.Response.Body = originalBody;

        var code = context.Response.StatusCode;
        var logStatus = code is >= 200 and < 300 ? "success" : "error";
        await LogAsync(dbFactory, clock, context, merchant.Id, code, logStatus, null, sw, responseText, body);
    }

    private static async Task LogAsync(IDbConnectionFactory dbFactory, IClock clock, HttpContext context,
        int? merchantId, int httpCode, string status, string? message, Stopwatch sw, string? responseBody, string? requestBody = null)
    {
        var duration = (int)sw.ElapsedMilliseconds;
        var url = context.Request.Method + " " + (context.Request.Path.Value ?? "");
        var reqData = requestBody ?? await ReadBodySafe(context);

        try
        {
            using var conn = await dbFactory.CreateOpenConnectionAsync(context.RequestAborted);
            await conn.ExecuteAsync(
                @"INSERT INTO api_callback_logs
                  (invest_id, merchant_id, direction, type, url, request_payload, response_status, response_body, duration_ms, error, triggered_by, created_at)
                  VALUES (NULL, @merchantId, 'in', @type, @url, @reqData, @httpCode, @respBody, @duration, @error, NULL, @now)",
                new
                {
                    merchantId,
                    type = status == "success" ? "inbound_api" : "inbound_api_error",
                    url = Trunc(url, 500),
                    reqData = Trunc(reqData, 4000),
                    httpCode,
                    respBody = Trunc(responseBody, 4000),
                    duration,
                    error = message is null ? null : Trunc(message, 500),
                    now = clock.Now,
                });
        }
        catch { /* tablo yoksa sessiz geç */ }

        try
        {
            using var conn = await dbFactory.CreateOpenConnectionAsync(context.RequestAborted);
            await conn.ExecuteAsync(
                @"INSERT INTO apiRequestLog (merchantId, requestIp, requestData, status, message, created_at)
                  VALUES (@merchantId, @ip, @reqData, @status, @message, @now)",
                new
                {
                    merchantId,
                    ip = context.Connection.RemoteIpAddress?.ToString(),
                    reqData = Trunc(reqData, 4000),
                    status,
                    message = Trunc(message ?? (url + " → " + httpCode), 250),
                    now = clock.Now,
                });
        }
        catch { /* eski log tablosu farklı şemada olabilir */ }
    }

    private static async Task<string> ReadBodySafe(HttpContext context)
    {
        try
        {
            if (!context.Request.Body.CanSeek) return "";
            context.Request.Body.Position = 0;
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, leaveOpen: true);
            var s = await reader.ReadToEndAsync();
            context.Request.Body.Position = 0;
            return s;
        }
        catch { return ""; }
    }

    private static string Sha256Hex(string input)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    private static string HmacSha256Hex(string key, string data)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(data)));
    }

    private static bool FixedEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));

    private static string? Trunc(string? s, int max) => s is null ? null : (s.Length <= max ? s : s[..max]);
}
