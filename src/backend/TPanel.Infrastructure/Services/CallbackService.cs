using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Dapper;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.PublicApi;

namespace TPanel.Infrastructure.Services;

/// <summary>Merchant callback POST + api_callback_logs kaydı (PHP CallbackService birebir).</summary>
public class CallbackService : ICallbackService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IDbConnectionFactory _dbFactory;
    private readonly IClock _clock;

    public CallbackService(IHttpClientFactory httpFactory, IDbConnectionFactory dbFactory, IClock clock)
    {
        _httpFactory = httpFactory;
        _dbFactory = dbFactory;
        _clock = clock;
    }

    public async Task<CallbackResult> SendAsync(string url, object payload, string type,
        int? investId = null, int? merchantId = null, int? triggeredBy = null, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        int? status = null;
        string? body = null;
        string? error = null;
        var json = JsonSerializer.Serialize(payload);

        if (string.IsNullOrEmpty(url))
        {
            error = "callback URL boş";
        }
        else
        {
            try
            {
                using var client = _httpFactory.CreateClient("callback");
                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                using var resp = await client.PostAsync(url, content, ct);
                status = (int)resp.StatusCode;
                body = Trunc(await resp.Content.ReadAsStringAsync(ct), 4000);
            }
            catch (Exception e)
            {
                error = Trunc(e.Message, 500);
            }
        }

        await LogAsync(investId, merchantId, type, url, json, status, body, (int)sw.ElapsedMilliseconds, error, triggeredBy, ct);
        return new CallbackResult(status is >= 200 and < 300, status, body, error);
    }

    public Task<CallbackResult> SendExpireAsync(InvestRow invest, int? triggeredBy = null, CancellationToken ct = default)
    {
        var payload = new { order_id = invest.OrderId, uID = invest.UId, status = false, message = "Ödeme bulunmadı" };
        return SendAsync(invest.CallbackUrl ?? invest.CallbackFailUrl ?? "", payload, "expire",
            invest.Id, invest.FirmId, triggeredBy, ct);
    }

    public async Task<CallbackResult> SendForInvestAsync(InvestRow invest, bool approved, string detail = "",
        int? triggeredBy = null, bool force = false, string? type = null, CancellationToken ct = default)
    {
        if (!force && invest.CallbackSended == 1)
            return new CallbackResult(false, null, null, "already sent");
        if (string.IsNullOrEmpty(invest.CallbackUrl))
            return new CallbackResult(false, null, null, "callbackUrl empty");

        using var conn = await _dbFactory.CreateOpenConnectionAsync(ct);
        var merchant = await conn.QueryFirstOrDefaultAsync(
            "SELECT apiKey, new_api FROM merchantUser WHERE id = @id", new { id = invest.FirmId });
        if (merchant is null)
            return new CallbackResult(false, null, null, "merchant not found");

        var apiKey = (string)merchant.apiKey;
        var useJson = Convert.ToInt32(merchant.new_api) == 1;

        var hash = Sha256Hex(apiKey + "|" + invest.OrderId + "|" + (approved ? "true" : "false"));

        // Sıralama PHP ile aynı (form-encoded müşteriler alan sırasına duyarlı olabilir)
        var ordered = approved
            ? new (string, object?)[]
            {
                ("code", 200), ("status", true), ("uID", invest.OrderId), ("saleID", invest.UId),
                ("amount", invest.Amount), ("senderName", invest.Name), ("hash", hash),
                ("message", "Ödeme onaylandı - Transaction approved"),
            }
            : new (string, object?)[]
            {
                ("code", 201), ("status", false), ("message", string.IsNullOrEmpty(detail) ? "Ödeme reddedildi" : detail),
                ("detail", detail), ("uID", invest.OrderId), ("saleID", invest.UId),
                ("amount", invest.Amount), ("senderName", invest.Name), ("hash", hash),
            };

        var payloadDict = ordered.ToDictionary(x => x.Item1, x => x.Item2);
        var jsonLog = JsonSerializer.Serialize(payloadDict);

        var sw = Stopwatch.StartNew();
        int? status = null;
        string? body = null;
        string? error = null;

        try
        {
            using var client = _httpFactory.CreateClient("callback");
            HttpContent content = useJson
                ? new StringContent(jsonLog, Encoding.UTF8, "application/json")
                : new FormUrlEncodedContent(ordered.Select(x =>
                    new KeyValuePair<string, string>(x.Item1, FormValue(x.Item2))));
            using var resp = await client.PostAsync(invest.CallbackUrl, content, ct);
            status = (int)resp.StatusCode;
            body = Trunc(await resp.Content.ReadAsStringAsync(ct), 4000);
        }
        catch (Exception e)
        {
            error = Trunc(e.Message, 500);
        }

        var isSuccess = status is >= 200 and < 300;
        await LogAsync(invest.Id, invest.FirmId, type ?? (approved ? "success" : "fail"),
            invest.CallbackUrl, jsonLog, status, body, (int)sw.ElapsedMilliseconds, error, triggeredBy, ct);

        if (isSuccess)
            await conn.ExecuteAsync("UPDATE invest SET callbackSended = 1 WHERE id = @id", new { id = invest.Id });

        return new CallbackResult(isSuccess, status, body, error);
    }

    private async Task LogAsync(int? investId, int? merchantId, string type, string? url, string payload,
        int? status, string? body, int durationMs, string? error, int? triggeredBy, CancellationToken ct)
    {
        try
        {
            using var conn = await _dbFactory.CreateOpenConnectionAsync(ct);
            await conn.ExecuteAsync(
                @"INSERT INTO api_callback_logs
                  (invest_id, merchant_id, direction, type, url, request_payload, response_status, response_body, duration_ms, error, triggered_by, created_at)
                  VALUES (@investId, @merchantId, 'out', @type, @url, @payload, @status, @body, @durationMs, @error, @triggeredBy, @now)",
                new
                {
                    investId, merchantId, type, url = Trunc(url, 500), payload,
                    status, body, durationMs, error, triggeredBy, now = _clock.Now,
                });
        }
        catch { /* log tablosu yoksa sessiz geç */ }
    }

    private static string FormValue(object? v) => v switch
    {
        null => "",
        bool b => b ? "1" : "0",
        double d => d.ToString(System.Globalization.CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => v.ToString() ?? "",
    };

    private static string Sha256Hex(string input)
        => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    private static string? Trunc(string? s, int max) => s is null ? null : (s.Length <= max ? s : s[..max]);
}
