using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PayDoPay.Application.Features.Cases;

namespace PayDoPay.Infrastructure.Services;

/// <summary>TRON zincir sorguları (TronGrid bakiye + TronScan tx lookup).</summary>
public class TronService : ITronService
{
    private const string UsdtContract = "TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t";
    private readonly IHttpClientFactory _http;
    private readonly ILogger<TronService> _logger;

    public TronService(IHttpClientFactory http, ILogger<TronService> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<double?> GetUsdtBalanceAsync(string walletAddress, CancellationToken ct = default)
    {
        try
        {
            using var client = _http.CreateClient("tron");
            client.Timeout = TimeSpan.FromSeconds(5);
            using var resp = await client.GetAsync($"https://api.trongrid.io/v1/accounts/{walletAddress}", ct);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("data", out var data) || data.GetArrayLength() == 0) return 0;
            if (!data[0].TryGetProperty("trc20", out var trc20)) return 0;
            foreach (var token in trc20.EnumerateArray())
                if (token.TryGetProperty(UsdtContract, out var val) && long.TryParse(val.GetString(), out var raw))
                    return Math.Round(raw / 1_000_000.0, 2);
            return 0;
        }
        catch (Exception e) { _logger.LogWarning(e, "Tron balance failed"); return null; }
    }

    public async Task<(double?, string?, string?)> TxLookupAsync(string txLink, CancellationToken ct = default)
    {
        var match = System.Text.RegularExpressions.Regex.Match(txLink, "([a-f0-9]{64})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success) return (null, null, "Geçersiz işlem linki.");
        var hash = match.Groups[1].Value;
        try
        {
            using var client = _http.CreateClient("tron");
            client.Timeout = TimeSpan.FromSeconds(10);
            using var resp = await client.GetAsync($"https://apilist.tronscanapi.com/api/transaction-info?hash={hash}", ct);
            if (!resp.IsSuccessStatusCode) return (null, null, "İşlem bilgisi alınamadı.");
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            var contractType = root.TryGetProperty("contractType", out var ctp) && ctp.ValueKind == JsonValueKind.Number ? ctp.GetInt32() : (int?)null;

            if (contractType == 31)
            {
                double? quantity = null;
                if (root.TryGetProperty("trigger_info", out var ti) && ti.TryGetProperty("parameter", out var par)
                    && par.TryGetProperty("_value", out var v) && long.TryParse(v.GetString(), out var raw))
                    quantity = raw / 1_000_000.0;
                else if (root.TryGetProperty("contractData", out var cd) && cd.TryGetProperty("amount", out var am) && am.TryGetInt64(out var a))
                    quantity = a / 1_000_000.0;
                return (quantity, "TRC20", null);
            }
            if (contractType == 1)
            {
                double amount = 0;
                if (root.TryGetProperty("contractData", out var cd) && cd.TryGetProperty("amount", out var am) && am.TryGetInt64(out var a))
                    amount = a / 1_000_000.0;
                return (amount, "TRX", null);
            }
            return (null, null, "Desteklenmeyen işlem türü.");
        }
        catch (Exception e) { _logger.LogWarning(e, "Tron tx lookup failed"); return (null, null, "İşlem bilgisi alınamadı."); }
    }
}
