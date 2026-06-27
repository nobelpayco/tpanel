using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.Extensions.Logging;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.Telegram;
using TPanel.Application.Features.Transactions;

namespace TPanel.Infrastructure.Services;

/// <summary>TelegramWebhookController'ın port'u — risk butonları, chat kaydı, #fin dekont, "kasa" raporu.</summary>
public class TelegramWebhookService : ITelegramWebhookService
{
    private static readonly Regex FinTag = new(@"(?:^|\s)#f[iİı]n\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex WithdrawId = new(@"\bW\d+\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex KasaCmd = new(@"@\w+\s+kasa\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex IbanCmd = new(@"#iban\b[:\s]*([A-Za-z0-9 ]{15,40})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex HesapCmd = new(@"#hesap\b[:\s]*([A-Za-z0-9 ]{15,40})", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ListeCmd = new(@"^/liste(@\w+)?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Callback = new(@"^(risk_block|risk_dismiss):(\d+)$", RegexOptions.Compiled);
    private static readonly string[] AllowedMime = { "image/jpeg", "image/png", "image/webp", "application/pdf" };

    private readonly IDbConnectionFactory _factory;
    private readonly ITelegramService _telegram;
    private readonly IWithdrawReceiptStorage _storage;
    private readonly IReceiptVerificationQueue _verifyQueue;
    private readonly IClock _clock;
    private readonly ILogger<TelegramWebhookService> _logger;

    public TelegramWebhookService(IDbConnectionFactory factory, ITelegramService telegram, IWithdrawReceiptStorage storage, IReceiptVerificationQueue verifyQueue, IClock clock, ILogger<TelegramWebhookService> logger)
    {
        _factory = factory; _telegram = telegram; _storage = storage; _verifyQueue = verifyQueue; _clock = clock; _logger = logger;
    }

    public async Task ProcessUpdateAsync(JsonElement update, CancellationToken ct = default)
    {
        if (update.TryGetProperty("callback_query", out var cb) && cb.ValueKind == JsonValueKind.Object)
        {
            await HandleCallbackQuery(cb, ct);
            return;
        }

        var msg = Obj(update, "message") ?? Obj(update, "edited_message");
        if (msg is null) return;
        var m = msg.Value;

        var chat = Obj(m, "chat");
        var chatId = chat is null ? (long?)null : Lng(chat.Value, "id");
        var text = (Str(m, "text") ?? Str(m, "caption") ?? "").Trim();
        var isBot = Obj(m, "from") is { } from && from.TryGetProperty("is_bot", out var b) && b.ValueKind == JsonValueKind.True;
        var messageId = Lng(m, "message_id");

        // Bot'un dahil olduğu grupları/kanalları kaydet (Chat ID Bul özelliği)
        if (chatId is not null && chat is not null)
        {
            var chatType = Str(chat.Value, "type");
            if (chatType is "group" or "supergroup" or "channel")
            {
                using var c = await _factory.CreateOpenConnectionAsync(ct);
                await c.ExecuteAsync(@"INSERT INTO telegram_chats (chat_id, title, type, username, last_seen_at)
                    VALUES (@id,@title,@type,@username,@seen)
                    ON DUPLICATE KEY UPDATE title=@title, type=@type, username=@username, last_seen_at=@seen",
                    new { id = chatId.Value, title = Str(chat.Value, "title"), type = chatType, username = Str(chat.Value, "username"), seen = _clock.Now });
            }
        }

        if (chatId is null || isBot || text == "") return;

        if (FinTag.IsMatch(text)) { await HandleFinTag(m, chatId.Value.ToString(), text, messageId, ct); return; }

        // /liste — yalnızca başlığında "DESTEK" geçen kanallarda; aktif banka hesaplarını listele (salt-okuma)
        if (ListeCmd.IsMatch(text))
        {
            var chatTitle = chat is null ? null : Str(chat.Value, "title");
            if (chatTitle is not null && chatTitle.Contains("DESTEK", StringComparison.OrdinalIgnoreCase))
                await HandleListeCmd(chatId.Value.ToString(), messageId, ct);
            return;
        }

        // #hesap <IBAN> — eklenmiş banka hesaplarında (aktif+pasif) arar, varsa hesap bilgisini döner (salt-okuma)
        var hesapMatch = HesapCmd.Match(text);
        if (hesapMatch.Success) { await HandleHesapCmd(chatId.Value.ToString(), hesapMatch.Groups[1].Value, messageId, ct); return; }

        // #iban <IBAN> — her grupta çalışır, aktif+pasif tüm hesaplarda sorgular (salt-okuma)
        var ibanMatch = IbanCmd.Match(text);
        if (ibanMatch.Success) { await HandleIbanCmd(chatId.Value.ToString(), ibanMatch.Groups[1].Value, messageId, ct); return; }

        if (!KasaCmd.IsMatch(text)) return;

        // "kasa" — yalnızca mutabakat chat'i + ilgili bayraklar açıksa
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var team = await conn.QueryFirstOrDefaultAsync(@"SELECT id, name, overturn, commission FROM teams
            WHERE telegram_reconciliation_chat_id=@cid AND telegram_enabled=1 AND telegram_cash_report_enabled=1 LIMIT 1",
            new { cid = chatId.Value.ToString() });
        if (team is null) return;
        try { await _telegram.SendTextAsync(chatId.Value.ToString(), await BuildCashReport(conn, team), "HTML", ct: ct); }
        catch (Exception e) { _logger.LogWarning(e, "cash report failed"); }
    }

    private async Task HandleCallbackQuery(JsonElement cb, CancellationToken ct)
    {
        var callbackId = Str(cb, "id") ?? "";
        var data = Str(cb, "data") ?? "";
        var fromUser = Obj(cb, "from");
        var clicker = "admin";
        if (fromUser is not null)
        {
            var f = fromUser.Value;
            var name = ((Str(f, "first_name") ?? "") + " " + (Str(f, "last_name") ?? "")).Trim();
            if (name == "" && Str(f, "username") is { } un) name = "@" + un;
            if (name != "") clicker = name;
        }

        var match = Callback.Match(data);
        if (!match.Success) { await _telegram.AnswerCallbackQueryAsync(callbackId, ct: ct); return; }
        var action = match.Groups[1].Value;
        var notifId = int.Parse(match.Groups[2].Value);

        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var notif = await c.QueryFirstOrDefaultAsync("SELECT id, player_id, chat_id, message_id, decision FROM player_risk_notifications WHERE id=@id", new { id = notifId });
        if (notif is null) { await _telegram.AnswerCallbackQueryAsync(callbackId, "Bildirim bulunamadı.", true, ct); return; }
        if ((string)notif.decision != "pending") { await _telegram.AnswerCallbackQueryAsync(callbackId, "Bu uyarı zaten kapatıldı.", true, ct); return; }

        var blocked = action == "risk_block";
        var newDecision = blocked ? "blocked" : "dismissed";
        var stamp = _clock.Now.ToString("dd.MM.yyyy HH:mm");
        var playerId = (string)notif.player_id;

        if (blocked)
        {
            var exists = await c.ExecuteScalarAsync<int>("SELECT EXISTS(SELECT 1 FROM blacklist WHERE type=1 AND val=@v)", new { v = playerId });
            if (exists == 0)
                await c.ExecuteAsync("INSERT INTO blacklist (type, val, `desc`) VALUES (1, @v, @d)",
                    new { v = playerId, d = $"Şüpheli oyuncu aktivitesi — risk:check · {stamp} · {clicker}" });
        }

        await c.ExecuteAsync("UPDATE player_risk_notifications SET decision=@dec, decided_at=@at, decided_by=@by WHERE id=@id",
            new { dec = newDecision, at = _clock.Now, by = clicker, id = notifId });

        var originalText = Obj(cb, "message") is { } om ? (Str(om, "text") ?? "") : "";
        var label = blocked ? "🚫 Engellendi" : "⏭️ Vazgeçildi";
        var newText = Esc(originalText) + $"\n\n✅ <b>{label}</b> · {Esc(clicker)} · {stamp}";

        if (notif.chat_id is not null && notif.message_id is not null)
        {
            var chatIdStr = notif.chat_id.ToString();
            var midVal = Convert.ToInt64(notif.message_id);
            await _telegram.EditMessageTextAsync(chatIdStr, midVal, newText, "HTML", ct);
            var icon = blocked ? "🚫" : "⏭️";
            var confirmLabel = blocked ? "Engellendi" : "Vazgeçildi";
            var confirm = $"{icon} <b>{confirmLabel}</b>\n👤 Player ID: <code>{Esc(playerId)}</code>\n👮 İşlem: {Esc(clicker)}\n🕐 {stamp}";
            await _telegram.SendTextAsync(chatIdStr, confirm, "HTML", midVal, ct);
        }

        await _telegram.AnswerCallbackQueryAsync(callbackId, blocked ? "🚫 Engellendi" : "⏭️ Vazgeçildi", ct: ct);
    }

    private async Task HandleFinTag(JsonElement msg, string chatId, string text, long? messageId, CancellationToken ct)
    {
        var widMatch = WithdrawId.Match(text);
        if (!widMatch.Success) { await _telegram.SendTextAsync(chatId, "❓ Çekim ID bulunamadı. Format: <code>#fin W1234567</code>", "HTML", messageId, ct); return; }
        var orderId = widMatch.Value.ToUpperInvariant();

        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var invest = await c.QueryFirstOrDefaultAsync("SELECT id, order_id, team_id, status FROM invest WHERE order_id=@o AND type='2' LIMIT 1", new { o = orderId });
        if (invest is null) { await _telegram.SendTextAsync(chatId, $"❌ Çekim bulunamadı: <code>{Esc(orderId)}</code>", "HTML", messageId, ct); return; }

        var teamChat = await c.ExecuteScalarAsync<string?>("SELECT telegram_withdraw_chat_id FROM teams WHERE id=@id", new { id = (int)invest.team_id });
        if (teamChat is null || teamChat != chatId) { _logger.LogWarning("finTag chat mismatch order={Order}", orderId); return; }

        // Medya tespit (photo veya document)
        string? fileId = null, mimeType = null, originalName = null;
        if (msg.TryGetProperty("photo", out var photo) && photo.ValueKind == JsonValueKind.Array && photo.GetArrayLength() > 0)
        {
            var best = photo[photo.GetArrayLength() - 1]; // en yüksek çözünürlük
            fileId = Str(best, "file_id"); mimeType = "image/jpeg"; originalName = $"telegram-{_clock.Now:yyyyMMddHHmmss}.jpg";
        }
        else if (msg.TryGetProperty("document", out var docu) && docu.ValueKind == JsonValueKind.Object)
        {
            fileId = Str(docu, "file_id"); mimeType = Str(docu, "mime_type"); originalName = Str(docu, "file_name") ?? $"telegram-{_clock.Now:yyyyMMddHHmmss}";
        }
        if (fileId is null) { await _telegram.SendTextAsync(chatId, "📎 Lütfen mesaja foto veya PDF ekleyin.", "HTML", messageId, ct); return; }
        if (mimeType is null || !AllowedMime.Contains(mimeType)) { await _telegram.SendTextAsync(chatId, $"❌ Sadece PDF/JPG/PNG/WEBP kabul edilir. (Aldığım: <code>{Esc(mimeType ?? "bilinmiyor")}</code>)", "HTML", messageId, ct); return; }

        var download = await _telegram.DownloadFileAsync(fileId, ct);
        if (download is null) { await _telegram.SendTextAsync(chatId, "⚠️ Dosya indirilemedi, lütfen tekrar deneyin.", "HTML", messageId, ct); return; }
        if (download.Size > 10 * 1024 * 1024) { await _telegram.SendTextAsync(chatId, "❌ Dosya 10 MB'ı aşamaz.", "HTML", messageId, ct); return; }

        var ext = mimeType switch { "image/jpeg" => "jpg", "image/png" => "png", "image/webp" => "webp", "application/pdf" => "pdf", _ => download.Ext };
        var name = Guid.NewGuid().ToString() + "." + ext;
        var path = await _storage.StoreAsync((int)invest.id, name, download.Binary, ct);
        var fileHash = Convert.ToHexString(SHA256.HashData(download.Binary)).ToLowerInvariant();

        var receiptId = await c.ExecuteScalarAsync<int>(@"INSERT INTO invest_receipts (invest_id, file_path, original_name, mime_type, file_size, file_hash, uploaded_by, uploaded_at)
            VALUES (@iid,@path,@orig,@mime,@size,@hash,NULL,@at); SELECT LAST_INSERT_ID();",
            new { iid = (int)invest.id, path, orig = originalName, mime = mimeType, size = download.Size, hash = fileHash, at = _clock.Now });

        await _verifyQueue.EnqueueAsync(receiptId, ct);

        var sender = Obj(msg, "from") is { } fu ? (Str(fu, "username") ?? Str(fu, "first_name") ?? "unknown") : "unknown";
        await c.ExecuteAsync(@"INSERT INTO investLog (investID, userID, ip, status, createdAt, detail) VALUES (@iid, NULL, '', @st, @at, @detail)",
            new { iid = (int)invest.id, st = invest.status, at = _clock.Now, detail = $"Dekont yüklendi (Telegram: @{sender} #fin)" });

        await _telegram.SendTextAsync(chatId, $"✅ Dekont yüklendi — <b>{Esc(orderId)}</b>", "HTML", messageId, ct);
    }

    private async Task HandleIbanCmd(string chatId, string ibanRaw, long? messageId, CancellationToken ct)
    {
        var iban = Regex.Replace(ibanRaw ?? "", @"\s", "").ToUpperInvariant();
        if (iban.Length < 15) { await _telegram.SendTextAsync(chatId, "❓ Geçerli bir IBAN girin. Format: <code>#iban TR....</code>", "HTML", messageId, ct); return; }

        using var c = await _factory.CreateOpenConnectionAsync(ct);
        // Aktif + pasif tüm kayıtlarda ara; aynı IBAN birden fazla kayıttaysa aktif olanı baz al (status DESC)
        var acc = await c.QueryFirstOrDefaultAsync(
            "SELECT id, status FROM bankAccounts WHERE REPLACE(UPPER(account_iban),' ','')=@iban ORDER BY status DESC LIMIT 1",
            new { iban });

        if (acc is null) { await _telegram.SendTextAsync(chatId, "❌ Böyle bir iban bulunmamaktadır.", "HTML", messageId, ct); return; }

        if ((int)acc.status == 1) { await _telegram.SendTextAsync(chatId, "✅ Bu iban aktif.", "HTML", messageId, ct); return; }

        // Pasif → en son işlem aldığı (onaylı yatırım) tarih
        var lastTx = await c.ExecuteScalarAsync<DateTime?>(
            "SELECT MAX(finalize_date) FROM invest WHERE bank_id=@id AND type='1' AND status='3'", new { id = (int)acc.id });
        var msg = lastTx is null
            ? "⛔ Bu iban pasif, henüz hiç işlem almamıştır."
            : $"⛔ Bu iban pasif. En son <b>{lastTx:dd.MM.yyyy}</b> tarihinde işlem almıştır.";
        await _telegram.SendTextAsync(chatId, msg, "HTML", messageId, ct);
    }

    // #hesap <IBAN> — eklenmiş banka hesaplarında (aktif/pasif fark etmez) arar, varsa hesabı bildirir.
    private async Task HandleHesapCmd(string chatId, string ibanRaw, long? messageId, CancellationToken ct)
    {
        var iban = Regex.Replace(ibanRaw ?? "", @"\s", "").ToUpperInvariant();
        if (iban.Length < 15) { await _telegram.SendTextAsync(chatId, "❓ Geçerli bir IBAN girin. Format: <code>#hesap TR....</code>", "HTML", messageId, ct); return; }

        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var rows = (await c.QueryAsync(@"
            SELECT ba.account_holder, ba.account_iban, ba.status, b.name AS bank_name, t.name AS team_name
            FROM bankAccounts ba
            LEFT JOIN banks b ON ba.bank_id = b.id
            LEFT JOIN teams t ON ba.team_id = t.id
            WHERE REPLACE(UPPER(ba.account_iban),' ','')=@iban
            ORDER BY ba.status DESC", new { iban })).ToList();

        if (rows.Count == 0) { await _telegram.SendTextAsync(chatId, "❌ Bu IBAN sistemde kayıtlı <b>değil</b>.", "HTML", messageId, ct); return; }

        var sb = new System.Text.StringBuilder();
        sb.Append(rows.Count == 1 ? "✅ Bu hesap sistemde <b>kayıtlı</b>:\n" : $"✅ Bu IBAN <b>{rows.Count}</b> kayıtta bulundu:\n");
        foreach (var r in rows)
        {
            var durum = (int)r.status == 1 ? "🟢 Aktif" : "🔴 Pasif";
            sb.Append($"\n👤 <b>{Esc((string?)r.account_holder ?? "-")}</b>\n");
            sb.Append($"🏦 {Esc((string?)r.bank_name ?? "-")}\n");
            sb.Append($"👥 Takım: {Esc((string?)r.team_name ?? "-")}\n");
            sb.Append($"📊 {durum}\n");
        }
        await _telegram.SendTextAsync(chatId, sb.ToString().TrimEnd(), "HTML", messageId, ct);
    }

    // /liste — TÜM aktif banka hesaplarını (status=1) düz liste olarak verir (takım gruplaması yok).
    private async Task HandleListeCmd(string chatId, long? messageId, CancellationToken ct)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var rows = (await c.QueryAsync(@"
            SELECT ba.account_holder, ba.account_iban, b.name AS bank_name
            FROM bankAccounts ba
            LEFT JOIN banks b ON ba.bank_id = b.id
            WHERE ba.status = 1
            ORDER BY b.name, ba.account_holder")).ToList();

        if (rows.Count == 0) { await _telegram.SendTextAsync(chatId, "ℹ️ Aktif banka hesabı bulunmuyor.", "HTML", messageId, ct); return; }

        var sb = new System.Text.StringBuilder();
        sb.Append($"🏦 <b>Aktif Banka Hesapları</b> ({rows.Count})\n\n");
        foreach (var r in rows)
            sb.Append($"• {Esc((string?)r.account_holder ?? "-")} — {Esc((string?)r.bank_name ?? "-")}\n<code>{Esc((string?)r.account_iban ?? "-")}</code>\n");

        var msg = sb.ToString().TrimEnd();
        if (msg.Length > 4000) msg = msg.Substring(0, 3900) + "\n…(liste uzun, kısaltıldı)";
        await _telegram.SendTextAsync(chatId, msg, "HTML", messageId, ct);
    }

    private async Task<string> BuildCashReport(IDbConnection c, dynamic team)
    {
        var today = _clock.Today.ToString("yyyy-MM-dd");
        var dayStart = _clock.Today.ToString("yyyy-MM-dd") + " 00:00:00";
        var tomorrow = _clock.Today.AddDays(1).ToString("yyyy-MM-dd") + " 00:00:00";
        int tid = (int)team.id;
        var pf = new { tid, s = dayStart, e = tomorrow };

        async Task<double> Sum(string sql) => await c.ExecuteScalarAsync<double?>(sql, pf) ?? 0;
        var deposits = await Sum("SELECT COALESCE(SUM(amount),0) FROM invest WHERE team_id=@tid AND type='1' AND status='3' AND finalize_date>=@s AND finalize_date<@e");
        var withdrawals = await Sum("SELECT COALESCE(SUM(amount),0) FROM invest WHERE team_id=@tid AND type='2' AND status='3' AND finalize_date>=@s AND finalize_date<@e");
        var commission = deposits * Convert.ToDouble(team.commission) / 100;
        var payments = await Sum("SELECT COALESCE(SUM(amount),0) FROM team_payments WHERE team_id=@tid AND created_at>=@s AND created_at<@e");
        var expenses = await Sum("SELECT COALESCE(SUM(amount),0) FROM paylira_expenses WHERE team_id=@tid AND created_at>=@s AND created_at<@e");
        var partnerPay = await Sum("SELECT COALESCE(SUM(amount),0) FROM paylira_partner_payments WHERE team_id=@tid AND payment_type='3' AND created_at>=@s AND created_at<@e");
        var interPay = await Sum("SELECT COALESCE(SUM(amount),0) FROM intermediary_payments WHERE team_id=@tid AND payment_type='3' AND created_at>=@s AND created_at<@e");
        var transferIn = await Sum("SELECT COALESCE(SUM(amount),0) FROM team_transfers WHERE to_team_id=@tid AND created_at>=@s AND created_at<@e");
        var transferOut = await Sum("SELECT COALESCE(SUM(amount),0) FROM team_transfers WHERE from_team_id=@tid AND created_at>=@s AND created_at<@e");
        var syncs = await Sum("SELECT COALESCE(SUM(amount),0) FROM team_syncs WHERE team_id=@tid AND created_at>=@s AND created_at<@e");

        var prev = await c.ExecuteScalarAsync<double?>(@"SELECT amount FROM daily_case_snapshots WHERE entity_type='team' AND entity_id=@tid AND snapshot_date<@d ORDER BY snapshot_date DESC LIMIT 1",
            new { tid, d = today });
        var previousBalance = prev ?? Convert.ToDouble(team.overturn);

        var reinforcement = -expenses - partnerPay - interPay - transferOut + transferIn - syncs;
        var endOfDay = previousBalance + deposits - withdrawals - commission - payments + reinforcement;

        string F(double v) => v.ToString("#,##0.00", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
        return $"<b>{Esc((string)team.name)} - {_clock.Now:dd.MM.yyyy}</b>\n\n"
            + $"<b>Devir:</b> {F(previousBalance)} TL\n<b>Yatırım:</b> {F(deposits)} TL\n<b>Çekim:</b> {F(withdrawals)} TL\n"
            + $"<b>Komisyon:</b> {F(commission)} TL\n<b>Manuel Ödeme:</b> {F(payments)} TL\n<b>Toplam Takviye:</b> {F(reinforcement)} TL\n\n"
            + $"<b>Gün Sonu:</b> {F(endOfDay)} TL";
    }

    private static string Esc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");
    private static string? Str(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static long? Lng(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt64() : null;
    private static JsonElement? Obj(JsonElement e, string p) => e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Object ? v : null;
}
