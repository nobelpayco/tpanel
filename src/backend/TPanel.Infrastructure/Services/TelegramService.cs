using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TPanel.Application.Common.Interfaces;

namespace TPanel.Infrastructure.Services;

/// <summary>
/// Telegram bot API. Token önce system_settings (telegram_bot_token), yoksa config (Telegram:BotToken).
/// Token yoksa tüm gönderimler no-op (false/null).
/// </summary>
public class TelegramService : ITelegramService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ISystemSettingService _settings;
    private readonly IDbConnectionFactory _factory;
    private readonly ILogger<TelegramService> _logger;

    public TelegramService(IHttpClientFactory httpFactory, IConfiguration config, ISystemSettingService settings, IDbConnectionFactory factory, ILogger<TelegramService> logger)
    {
        _httpFactory = httpFactory; _config = config; _settings = settings; _factory = factory; _logger = logger;
    }

    /// <summary>Token: önce panel ayarı (system_settings.telegram_bot_token), yoksa .env (Telegram:BotToken).</summary>
    private async Task<string?> TokenAsync(CancellationToken ct)
    {
        var fromSettings = await _settings.GetAsync("telegram_bot_token", ct);
        return !string.IsNullOrEmpty(fromSettings) ? fromSettings : _config["Telegram:BotToken"];
    }

    private HttpClient Client() { var c = _httpFactory.CreateClient("telegram"); c.Timeout = TimeSpan.FromSeconds(30); return c; }

    public async Task<bool> SendAsync(string chatId, string markdownMessage, CancellationToken ct = default)
        => await SendReturnIdAsync(chatId, markdownMessage, "MarkdownV2", null, null, ct) is not null;

    public async Task<bool> SendTextAsync(string chatId, string text, string parseMode = "HTML", long? replyToMessageId = null, CancellationToken ct = default)
        => await SendReturnIdAsync(chatId, text, parseMode, null, replyToMessageId, ct) is not null;

    public async Task<long?> SendReturnIdAsync(string chatId, string text, string parseMode = "HTML", object? replyMarkup = null, long? replyToMessageId = null, CancellationToken ct = default)
    {
        var token = await TokenAsync(ct);
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId)) return null;
        var payload = BuildSendPayload(chatId, text, parseMode, replyMarkup, replyToMessageId);
        try
        {
            using var client = Client();
            using var resp = await client.PostAsJsonAsync($"https://api.telegram.org/bot{token}/sendMessage", payload, ct);
            if (resp.IsSuccessStatusCode) return ReadMessageId(await resp.Content.ReadAsStringAsync(ct));

            // Supergroup migration: chat upgrade edildiyse yeni chat_id ile retry
            var body = await resp.Content.ReadAsStringAsync(ct);
            var migrated = ReadMigrateChatId(body);
            if (migrated is not null)
            {
                await MigrateChatIdAsync(chatId, migrated, ct);
                payload["chat_id"] = migrated;
                using var retry = await client.PostAsJsonAsync($"https://api.telegram.org/bot{token}/sendMessage", payload, ct);
                if (retry.IsSuccessStatusCode) return ReadMessageId(await retry.Content.ReadAsStringAsync(ct));
            }
            _logger.LogWarning("Telegram send failed: {Body}", body.Length > 300 ? body[..300] : body);
            return null;
        }
        catch (Exception e) { _logger.LogWarning(e, "Telegram send exception"); return null; }
    }

    public async Task<bool> EditMessageTextWithMarkupAsync(string chatId, long messageId, string text, string parseMode, object? replyMarkup, CancellationToken ct = default)
    {
        var token = await TokenAsync(ct);
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId)) return false;
        var body = new Dictionary<string, object> { ["chat_id"] = chatId, ["message_id"] = messageId, ["text"] = text, ["parse_mode"] = parseMode };
        if (replyMarkup is not null) body["reply_markup"] = replyMarkup;
        return await PostOkAsync(token, "editMessageText", body, ct);
    }

    public async Task<bool> EditMessageTextAsync(string chatId, long messageId, string text, string parseMode = "HTML", CancellationToken ct = default)
    {
        var token = await TokenAsync(ct);
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(chatId)) return false;
        return await PostOkAsync(token, "editMessageText", new Dictionary<string, object> { ["chat_id"] = chatId, ["message_id"] = messageId, ["text"] = text, ["parse_mode"] = parseMode }, ct);
    }

    public async Task<bool> AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, bool showAlert = false, CancellationToken ct = default)
    {
        var token = await TokenAsync(ct);
        if (string.IsNullOrEmpty(token)) return false;
        var body = new Dictionary<string, object> { ["callback_query_id"] = callbackQueryId };
        if (text is not null) { body["text"] = text; body["show_alert"] = showAlert; }
        return await PostOkAsync(token, "answerCallbackQuery", body, ct);
    }

    public async Task<TelegramFile?> DownloadFileAsync(string fileId, CancellationToken ct = default)
    {
        var token = await TokenAsync(ct);
        if (string.IsNullOrEmpty(token)) return null;
        try
        {
            using var client = Client();
            using var info = await client.GetAsync($"https://api.telegram.org/bot{token}/getFile?file_id={Uri.EscapeDataString(fileId)}", ct);
            if (!info.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await info.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("result", out var r) || !r.TryGetProperty("file_path", out var fp)) return null;
            var filePath = fp.GetString();
            if (string.IsNullOrEmpty(filePath)) return null;
            var bin = await client.GetByteArrayAsync($"https://api.telegram.org/file/bot{token}/{filePath}", ct);
            var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrEmpty(ext)) ext = "bin";
            return new TelegramFile(bin, bin.LongLength, ext);
        }
        catch (Exception e) { _logger.LogWarning(e, "Telegram downloadFile exception"); return null; }
    }

    public async Task<(bool Ok, string Message)> SetWebhookAsync(string url, string? secret, CancellationToken ct = default)
    {
        var token = await TokenAsync(ct);
        if (string.IsNullOrEmpty(token)) return (false, "Bot token tanımlı değil.");
        try
        {
            var body = new Dictionary<string, object> { ["url"] = url, ["allowed_updates"] = new[] { "message", "edited_message", "callback_query" } };
            if (!string.IsNullOrEmpty(secret)) body["secret_token"] = secret;
            using var client = Client();
            using var resp = await client.PostAsJsonAsync($"https://api.telegram.org/bot{token}/setWebhook", body, ct);
            var respBody = await resp.Content.ReadAsStringAsync(ct);
            return resp.IsSuccessStatusCode ? (true, "Webhook ayarlandı: " + url) : (false, "setWebhook hata: " + (respBody.Length > 300 ? respBody[..300] : respBody));
        }
        catch (Exception e) { return (false, "İstisna: " + e.Message); }
    }

    // ---- yardımcılar ----
    private static Dictionary<string, object> BuildSendPayload(string chatId, string text, string parseMode, object? replyMarkup, long? replyToMessageId)
    {
        var p = new Dictionary<string, object> { ["chat_id"] = chatId, ["text"] = text, ["parse_mode"] = parseMode };
        if (replyToMessageId is not null) p["reply_parameters"] = new { message_id = replyToMessageId.Value, allow_sending_without_reply = true };
        if (replyMarkup is not null) p["reply_markup"] = replyMarkup;
        return p;
    }

    private async Task<bool> PostOkAsync(string token, string method, object body, CancellationToken ct)
    {
        try
        {
            using var client = Client();
            using var resp = await client.PostAsJsonAsync($"https://api.telegram.org/bot{token}/{method}", body, ct);
            if (!resp.IsSuccessStatusCode) _logger.LogWarning("Telegram {Method} failed: {S}", method, resp.StatusCode);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception e) { _logger.LogWarning(e, "Telegram {Method} exception", method); return false; }
    }

    private static long? ReadMessageId(string json)
    {
        try { using var d = JsonDocument.Parse(json); return d.RootElement.TryGetProperty("result", out var r) && r.TryGetProperty("message_id", out var m) ? m.GetInt64() : null; }
        catch { return null; }
    }

    private static string? ReadMigrateChatId(string json)
    {
        try { using var d = JsonDocument.Parse(json); return d.RootElement.TryGetProperty("parameters", out var p) && p.TryGetProperty("migrate_to_chat_id", out var m) ? m.GetInt64().ToString() : null; }
        catch { return null; }
    }

    private async Task MigrateChatIdAsync(string oldChatId, string newChatId, CancellationToken ct)
    {
        try
        {
            using var c = await _factory.CreateOpenConnectionAsync(ct);
            foreach (var col in new[] { "telegram_chat_id", "telegram_withdraw_chat_id", "telegram_reconciliation_chat_id" })
                await c.ExecuteAsync($"UPDATE teams SET {col}=@nw WHERE {col}=@old", new { nw = newChatId, old = oldChatId });
            _logger.LogInformation("Telegram chat migrated {Old} -> {New}", oldChatId, newChatId);
        }
        catch (Exception e) { _logger.LogWarning(e, "migrateChatId failed"); }
    }
}
