using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.Receipts;

namespace TPanel.Infrastructure.Services;

/// <summary>Anthropic Claude Vision — banka dekontu OCR/fraud analizi (PHP ClaudeVisionService karşılığı).</summary>
public class ClaudeVisionService : IClaudeVisionService
{
    private readonly IHttpClientFactory _http;
    private readonly ISystemSettingService _settings;
    private readonly IClock _clock;
    private readonly IConfiguration _config;
    private readonly ILogger<ClaudeVisionService> _logger;

    public ClaudeVisionService(IHttpClientFactory http, ISystemSettingService settings, IClock clock, IConfiguration config, ILogger<ClaudeVisionService> logger)
    {
        _http = http; _settings = settings; _clock = clock; _config = config; _logger = logger;
    }

    private async Task<string?> ApiKey(CancellationToken ct)
        => await _settings.GetAsync("anthropic_api_key", ct) is { Length: > 0 } k ? k : _config["Anthropic:ApiKey"];

    public string Model() => "claude-haiku-4-5";
    private async Task<string> ModelAsync(CancellationToken ct)
        => await _settings.GetAsync("anthropic_vision_model", ct) is { Length: > 0 } m ? m : (_config["Anthropic:Model"] ?? "claude-haiku-4-5");

    public double EstimateCost(int inputTokens, int outputTokens, string? model = null)
    {
        model ??= "claude-haiku-4-5";
        var (pin, pout) = model switch
        {
            "claude-sonnet-4-6" => (3.0, 15.0),
            "claude-opus-4-7" or "claude-opus-4-8" => (15.0, 75.0),
            _ => (1.0, 5.0),
        };
        return inputTokens / 1_000_000.0 * pin + outputTokens / 1_000_000.0 * pout;
    }

    public async Task<(bool Ok, string Message)> PingAsync(CancellationToken ct = default)
    {
        var key = await ApiKey(ct);
        if (string.IsNullOrEmpty(key)) return (false, "API key tanımlı değil.");
        try
        {
            var model = await ModelAsync(ct);
            using var client = _http.CreateClient("anthropic");
            client.Timeout = TimeSpan.FromSeconds(15);
            var payload = JsonSerializer.Serialize(new { model, max_tokens = 5, messages = new[] { new { role = "user", content = "ping" } } });
            using var req = BuildRequest(key, payload);
            using var resp = await client.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode) return (true, "Bağlantı başarılı. Model: " + model);
            return (false, "Hata: " + (await resp.Content.ReadAsStringAsync(ct))[..Math.Min(200, (int)resp.Content.Headers.ContentLength.GetValueOrDefault(200))]);
        }
        catch (Exception e) { return (false, "İstisna: " + e.Message); }
    }

    public async Task<ClaudeReceiptResult?> AnalyzeReceiptAsync(byte[] binary, string mimeType, ReceiptExpected expected, CancellationToken ct = default)
    {
        var key = await ApiKey(ct);
        if (string.IsNullOrEmpty(key)) { _logger.LogWarning("ClaudeVision: API key yok"); return null; }
        var model = await ModelAsync(ct);
        var base64 = Convert.ToBase64String(binary);
        var today = _clock.Now.ToString("yyyy-MM-dd");
        var expDigits = Regex.Replace(expected.Iban ?? "", "[^0-9]", "");
        var expLast4 = expDigits.Length >= 4 ? expDigits[^4..] : "";

        var systemPrompt = BuildSystemPrompt(today);
        var userText = $"Bu görüntüyü banka dekontu olarak analiz et. Beklenen değerler:\n- Tutar: {expected.Amount:0.00} TL\n- Alıcı IBAN son 4 hane: {expLast4}\n- Alıcı adı: {expected.RecipientName}\n\nBunlarla karşılaştırarak yukarıdaki JSON şemasına uygun cevap ver.";

        var payload = JsonSerializer.Serialize(new
        {
            model,
            max_tokens = 800,
            messages = new[] { new { role = "user", content = new object[] {
                new { type = "image", source = new { type = "base64", media_type = mimeType, data = base64 } },
                new { type = "text", text = userText } } } },
            system = systemPrompt,
        });

        try
        {
            using var client = _http.CreateClient("anthropic");
            client.Timeout = TimeSpan.FromSeconds(45);
            using var req = BuildRequest(key, payload);
            using var resp = await client.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) { _logger.LogWarning("ClaudeVision API error {S}", resp.StatusCode); return null; }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            var root = doc.RootElement;
            var text = root.TryGetProperty("content", out var cont) && cont.GetArrayLength() > 0 && cont[0].TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            var raw = text;
            var match = Regex.Match(text, @"\{[\s\S]+\}");
            if (match.Success) text = match.Value;
            JsonElement parsed;
            try { parsed = JsonDocument.Parse(text).RootElement.Clone(); }
            catch { _logger.LogWarning("ClaudeVision JSON parse failed"); return null; }
            int inTok = 0, outTok = 0;
            if (root.TryGetProperty("usage", out var u)) { if (u.TryGetProperty("input_tokens", out var i)) inTok = i.GetInt32(); if (u.TryGetProperty("output_tokens", out var o)) outTok = o.GetInt32(); }
            return new ClaudeReceiptResult(parsed, inTok, outTok, model, raw);
        }
        catch (Exception e) { _logger.LogWarning(e, "ClaudeVision exception"); return null; }
    }

    private static HttpRequestMessage BuildRequest(string key, string json)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        req.Headers.Add("x-api-key", key);
        req.Headers.Add("anthropic-version", "2023-06-01");
        return req;
    }

    private static string BuildSystemPrompt(string today) =>
        $"Bugünün tarihi: {today}. Dekonttaki işlem tarihi bugün veya öncesi ise NORMAL kabul edilir; 'gelecek tarih' diye işaretleme.\n\n" +
        "Sen bir banka dekontu/makbuzu analiz uzmanısın. Sana gönderilen görseli inceleyip aşağıdaki JSON formatında cevap ver. Sadece JSON döndür, başka metin yok:\n\n" +
        "{\n  \"is_receipt\": boolean,\n  \"amount\": number|null,\n" +
        "  \"iban_full\": string|null (SADECE ALICI/KARŞI TARAF/HEDEF IBAN'ın tam metni — GÖNDEREN değil; TR ile başlar, 26 karakter),\n" +
        "  \"iban_last4\": string|null (ALICI IBAN'ın son 4 rakamı),\n  \"recipient_name\": string|null,\n  \"sender_name\": string|null,\n" +
        "  \"bank_name\": string|null,\n  \"transaction_date\": string|null,\n  \"transaction_id\": string|null,\n  \"confidence\": number,\n" +
        "  \"signs_of_tampering\": boolean (Photoshop/GIMP/PDF editör ile alan değiştirme izi),\n  \"tampering_reasons\": string|null,\n" +
        "  \"appears_ai_generated\": boolean (ChatGPT/DALL-E/Midjourney ile sıfırdan üretim),\n  \"ai_generation_reasons\": string|null,\n  \"notes\": string\n}\n\n" +
        "KURALLAR: IBAN'ı hesap numarasıyla karıştırma (TR ile başlar, 26 karakter). Dekontta genelde 2 IBAN olur; etiketlere bak (Alıcı/Karşı Taraf=alıcı, Gönderen/Kaynak=gönderen), sadece ALICI IBAN'ını ver, ayıramazsan null bırak. " +
        "Net okuyamadığın değeri TAHMİN ETME, null bırak. confidence<60 ise kritik alanları null ver. signs_of_tampering sadece görsel manipülasyon için (veri uyuşmazlığı değil). " +
        "appears_ai_generated: yazıyla yazılan tutar semantik uyumsuzluğu (anlamsız harf dizisi/tutar uyumsuzluğu) tek başına yeterli; diğer sinyaller için en az 2 gerekli. '%XX' kuruş gösterimi ve bitişik yazım gerçek banka formatıdır, sahte sanma. " +
        "Orijinal alınıp içeriği değiştirilmişse signs_of_tampering=true; sıfırdan üretilmişse appears_ai_generated=true.";
}
