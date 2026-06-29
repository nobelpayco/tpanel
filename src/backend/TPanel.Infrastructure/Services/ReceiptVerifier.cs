using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.Receipts;
using TPanel.Application.Features.Transactions;

namespace TPanel.Infrastructure.Services;

/// <summary>Channel tabanlı dekont doğrulama kuyruğu (Faz 3'teki no-op'un yerine).</summary>
public class ReceiptVerificationQueue : IReceiptVerificationQueue
{
    private readonly Channel<int> _channel = Channel.CreateUnbounded<int>();
    public Task EnqueueAsync(int receiptId, CancellationToken ct = default) { _channel.Writer.TryWrite(receiptId); return Task.CompletedTask; }
    public IAsyncEnumerable<int> DequeueAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

public class ReceiptVerificationBackgroundService : BackgroundService
{
    private readonly ReceiptVerificationQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReceiptVerificationBackgroundService> _logger;

    public ReceiptVerificationBackgroundService(IReceiptVerificationQueue queue, IServiceScopeFactory scopeFactory, ILogger<ReceiptVerificationBackgroundService> logger)
    {
        _queue = (ReceiptVerificationQueue)queue; _scopeFactory = scopeFactory; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var rid in _queue.DequeueAllAsync(stoppingToken))
        {
            try { using var scope = _scopeFactory.CreateScope(); await scope.ServiceProvider.GetRequiredService<IReceiptVerifier>().RunAsync(rid, stoppingToken); }
            catch (Exception e) { _logger.LogError(e, "Receipt verify {Id} failed", rid); }
        }
    }
}

/// <summary>VerifyReceiptJob karşılığı — hash dup + sahte şablon + metadata + Claude skorlama.</summary>
public class ReceiptVerifier : IReceiptVerifier
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClaudeVisionService _claude;
    private readonly IFileMetadataService _metadata;
    private readonly IPerceptualHasher _hasher;
    private readonly IWithdrawReceiptStorage _storage;
    private readonly IClock _clock;
    private readonly ILogger<ReceiptVerifier> _logger;

    private static readonly string[] KnownBanks = { "garanti", "akbank", "is bankasi", "iş bankası", "yapi kredi", "yapı kredi", "ziraat", "finansbank", "qnb", "halkbank", "vakif", "vakıf", "denizbank", "enpara", "odeabank", "şekerbank", "sekerbank", "ing", "hsbc", "fibabanka", "turkiye finans", "türkiye finans", "albaraka", "kuveyt türk", "kuveyt turk", "tom", "papara", "param" };

    public ReceiptVerifier(IDbConnectionFactory factory, IClaudeVisionService claude, IFileMetadataService metadata, IPerceptualHasher hasher, IWithdrawReceiptStorage storage, IClock clock, ILogger<ReceiptVerifier> logger)
    {
        _factory = factory; _claude = claude; _metadata = metadata; _hasher = hasher; _storage = storage; _clock = clock; _logger = logger;
    }

    public async Task RunAsync(int receiptId, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var receipt = await c.QueryFirstOrDefaultAsync("SELECT id, invest_id, file_path, mime_type, file_hash, perceptual_hash FROM invest_receipts WHERE id=@id", new { id = receiptId });
        if (receipt is null) return;
        var invest = await c.QueryFirstOrDefaultAsync("SELECT id, amount, iban, name FROM invest WHERE id=@id", new { id = (int)receipt.invest_id });
        if (invest is null) { await Save(c, receiptId, "rejected", 0, null, "İlgili çekim bulunamadı.", null); return; }

        string? fileHash = receipt.file_hash; string? pHash = receipt.perceptual_hash;

        // 1) Aynı dosya başka invest'te mi?
        if (!string.IsNullOrEmpty(fileHash))
        {
            var dup = await c.QueryFirstOrDefaultAsync("SELECT id, invest_id FROM invest_receipts WHERE file_hash=@h AND id<>@id AND invest_id<>@iid ORDER BY id LIMIT 1",
                new { h = fileHash, id = receiptId, iid = (int)receipt.invest_id });
            if (dup is not null)
            {
                await Save(c, receiptId, "rejected", 0, new { duplicate_of = new { receipt_id = (int)dup.id, invest_id = (int)dup.invest_id } },
                    $"Aynı dosya daha önce başka bir çekime (#{(int)dup.invest_id}) yüklenmiş.", null);
                return;
            }
        }

        // 1B) Bilinen sahte şablon
        foreach (var tpl in await c.QueryAsync("SELECT id, perceptual_hash, file_hash, reason FROM fake_receipt_templates"))
        {
            if (!string.IsNullOrEmpty(fileHash) && !string.IsNullOrEmpty((string?)tpl.file_hash) && fileHash == (string)tpl.file_hash)
            {
                await Save(c, receiptId, "rejected", 0, new { matched_template_id = (int)tpl.id, match_type = "file_hash" },
                    $"🚫 Bilinen sahte şablon (#{(int)tpl.id}) ile bire bir eşleşti." + (tpl.reason is null ? "" : $" (Sebep: {(string)tpl.reason})"), null);
                return;
            }
            if (!string.IsNullOrEmpty(pHash) && !string.IsNullOrEmpty((string?)tpl.perceptual_hash))
            {
                var dist = _hasher.HammingDistance(pHash!, (string)tpl.perceptual_hash);
                if (dist <= 8)
                {
                    await Save(c, receiptId, "rejected", 0, new { matched_template_id = (int)tpl.id, match_type = "perceptual_hash", hamming_distance = dist },
                        $"🚫 Bilinen sahte şablon (#{(int)tpl.id}) ile görsel eşleşti (Hamming: {dist}).", null);
                    return;
                }
            }
        }

        // 2) Dosyayı oku
        var (exists, fullPath) = await _storage.ResolveAsync((string)receipt.file_path, ct);
        if (!exists) { await Save(c, receiptId, "rejected", 0, null, "Dosya bulunamadı.", null); return; }
        var binary = await File.ReadAllBytesAsync(fullPath, ct);
        var mime = (string?)receipt.mime_type ?? "image/jpeg";

        // 2B) Metadata
        var meta = _metadata.Analyze(binary, mime);

        // 3) Claude
        var expAmount = Convert.ToDouble(invest.amount);
        var expIban = (string?)invest.iban ?? "";
        var result = await _claude.AnalyzeReceiptAsync(binary, mime, new ReceiptExpected(expAmount, expIban, (string?)invest.name ?? ""), ct);
        if (result is null) { _logger.LogWarning("Claude null, receipt {Id} pending kalıyor", receiptId); return; }
        var j = result.Json;

        int score = 0; var notes = new List<string>();
        if (Bool(j, "is_receipt")) { score += 10; notes.Add("Banka makbuzu formatı tespit edildi."); }
        else notes.Add("Banka makbuzu formatı tespit edilemedi.");

        var rAmount = Num(j, "amount");
        if (rAmount is not null)
        {
            var diff = expAmount > 0 ? Math.Abs(rAmount.Value - expAmount) / expAmount : 0;
            if (diff <= 0.01) { score += 30; notes.Add($"Tutar tam eşleşiyor ({rAmount.Value:N2} TL)."); }
            else notes.Add($"Tutar uyumsuz (dekont: {rAmount.Value:N2}, beklenen: {expAmount:N2}).");
        }

        var expDigits = Regex.Replace(expIban, "[^0-9]", "");
        var expLast4 = expDigits.Length >= 4 ? expDigits[^4..] : "";
        // Önce yapısal "iban_last4" alanını baz al (iban_full OCR'ı sık yanlış/transpoze okur); yoksa iban_full'e düş.
        string rLast4 = "";
        if (Str(j, "iban_last4") is { } l4f) { var d = Regex.Replace(l4f, "[^0-9]", ""); if (d.Length >= 4) rLast4 = d[^4..]; }
        if (rLast4 == "")
        {
            var ibanFull = Str(j, "iban_full");
            if (!string.IsNullOrEmpty(ibanFull)) { var d = Regex.Replace(ibanFull!, "[^0-9]", ""); if (d.Length >= 4) rLast4 = d[^4..]; }
        }
        if (expLast4 != "" && rLast4 != "")
        {
            if (expLast4 == rLast4) { score += 25; notes.Add($"IBAN son 4 hane eşleşiyor (****{expLast4})."); }
            else if (SameDigitsAnyOrder(expLast4, rLast4)) { score += 25; notes.Add($"IBAN son 4 hane eşleşiyor (****{expLast4}; OCR rakam sırası normalize edildi)."); }
            else notes.Add($"IBAN son 4 hane uyumsuz (dekont: {rLast4}, beklenen: {expLast4}).");
        }
        else if (expLast4 != "") notes.Add("IBAN okunamadı (manuel kontrol önerilir).");

        var expName = NormalizeName((string?)invest.name ?? "");
        var rName = NormalizeName(Str(j, "recipient_name") ?? "");
        if (expName != "" && rName != "")
        {
            var expTokens = expName.Split(' ').Where(t => t.Length >= 2).ToList();
            var rTokens = rName.Split(' ').Where(t => t.Length >= 2).ToList();
            var allMatch = expTokens.Count > 0 && rTokens.Count > 0;
            var sims = new List<double>();
            foreach (var et in expTokens) { double best = rTokens.Count == 0 ? 0 : rTokens.Max(rt => Similarity(et, rt)); sims.Add(best); if (best < 80) allMatch = false; }
            var avg = sims.Count > 0 ? sims.Average() : 0;
            if (allMatch) { score += 25; notes.Add($"Alıcı adı eşleşiyor ({Math.Round(avg)}% benzerlik)."); }
            else notes.Add($"Alıcı adı uyumsuz (dekont: \"{Str(j, "recipient_name")}\", beklenen: \"{(string?)invest.name}\").");
        }

        var bankName = (Str(j, "bank_name") ?? "").ToLowerInvariant();
        if (bankName != "" && KnownBanks.Any(b => bankName.Contains(b))) { score += 10; notes.Add($"Banka tanındı: {Str(j, "bank_name")}."); }

        if (Bool(j, "signs_of_tampering")) { score = Math.Max(0, score - 40); notes.Add("⚠️ Manipülasyon belirtisi: " + (Str(j, "tampering_reasons") ?? "görsel düzenleme izleri")); }
        if (Bool(j, "appears_ai_generated")) { score = Math.Max(0, score - 80); notes.Add("⛔ Sahte dekont (AI üretimi): " + (Str(j, "ai_generation_reasons") ?? "AI üretim belirtileri")); }
        if (meta.Suspicious) { score = Math.Max(0, score - 30); notes.Add("📄 Dosya metadata şüpheli: " + string.Join(" | ", meta.Flags)); }
        if (Str(j, "notes") is { Length: > 0 } aiNote) notes.Add("AI: " + aiNote);

        var status = score >= 80 ? "verified" : score >= 50 ? "suspicious" : "rejected";
        // PHP ile aynı: AI sonucu + raw_text + _usage tek seviyede
        var node = System.Text.Json.Nodes.JsonNode.Parse(j.GetRawText())!.AsObject();
        node["raw_text"] = result.RawText;
        node["_usage"] = new System.Text.Json.Nodes.JsonObject { ["model"] = result.Model, ["input_tokens"] = result.InputTokens, ["output_tokens"] = result.OutputTokens };
        await Save(c, receiptId, status, score, node, string.Join("\n", notes), meta.Flags);
    }

    private async Task Save(System.Data.IDbConnection c, int id, string status, int score, object? data, string notes, IReadOnlyList<string>? flags)
    {
        await c.ExecuteAsync(@"UPDATE invest_receipts SET verification_status=@s, verification_score=@sc, verification_data=@d, verification_notes=@n, metadata_flags=@mf, verified_at=@at WHERE id=@id",
            new { s = status, sc = score, d = data is null ? null : JsonSerializer.Serialize(data), n = notes, mf = flags is { Count: > 0 } ? JsonSerializer.Serialize(flags) : null, at = _clock.Now, id });
    }

    private static bool Bool(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.True;
    private static double? Num(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), out var d) ? d : null);
    private static string? Str(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    // OCR son-4 rakamlarını sık sık sıra-değiştirerek (transpoze) okur; aynı rakam kümesiyse eşleşmiş say.
    private static bool SameDigitsAnyOrder(string a, string b)
    {
        if (a.Length != b.Length) return false;
        var ca = a.ToCharArray(); var cb = b.ToCharArray();
        Array.Sort(ca); Array.Sort(cb);
        return new string(ca) == new string(cb);
    }

    private static string NormalizeName(string name)
    {
        var map = new Dictionary<char, char> { ['ç'] = 'c', ['Ç'] = 'C', ['ğ'] = 'g', ['Ğ'] = 'G', ['ı'] = 'i', ['İ'] = 'I', ['ö'] = 'o', ['Ö'] = 'O', ['ş'] = 's', ['Ş'] = 'S', ['ü'] = 'u', ['Ü'] = 'U' };
        var sb = new StringBuilder();
        foreach (var ch in name) sb.Append(map.GetValueOrDefault(ch, ch));
        var s = Regex.Replace(sb.ToString(), "[^A-Za-z0-9\\s]", "");
        return Regex.Replace(s, "\\s+", " ").Trim().ToLowerInvariant();
    }

    private static double Similarity(string a, string b)
    {
        if (a == b) return 100;
        var max = Math.Max(a.Length, b.Length); if (max == 0) return 100;
        return (1.0 - Levenshtein(a, b) / (double)max) * 100;
    }

    private static int Levenshtein(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var jj = 0; jj <= b.Length; jj++) d[0, jj] = jj;
        for (var i = 1; i <= a.Length; i++)
            for (var jj = 1; jj <= b.Length; jj++)
                d[i, jj] = Math.Min(Math.Min(d[i - 1, jj] + 1, d[i, jj - 1] + 1), d[i - 1, jj - 1] + (a[i - 1] == b[jj - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }
}
