using System.Net.Http;
using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TPanel.Application.Common.Interfaces;

namespace TPanel.Infrastructure.Services;

/// <summary>
/// Telegram bildirim gönderimi. Bot token yapılandırılmamışsa no-op (false).
/// Tam entegrasyon Faz 6'da; şimdilik temel sendMessage.
/// </summary>
public class TelegramService : ITelegramService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly string? _botToken;
    private readonly ILogger<TelegramService> _logger;

    public TelegramService(IHttpClientFactory httpFactory, IConfiguration config, ILogger<TelegramService> logger)
    {
        _httpFactory = httpFactory;
        _botToken = config["Telegram:BotToken"];
        _logger = logger;
    }

    public async Task<bool> SendAsync(string chatId, string markdownMessage, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_botToken) || string.IsNullOrEmpty(chatId))
            return false;

        try
        {
            using var client = _httpFactory.CreateClient("telegram");
            var url = $"https://api.telegram.org/bot{_botToken}/sendMessage";
            using var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("chat_id", chatId),
                new KeyValuePair<string, string>("text", markdownMessage),
                new KeyValuePair<string, string>("parse_mode", "MarkdownV2"),
            });
            using var resp = await client.PostAsync(url, content, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Telegram send failed");
            return false;
        }
    }

    public async Task<long?> SendReturnIdAsync(string chatId, string text, string parseMode = "HTML", object? replyMarkup = null, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_botToken) || string.IsNullOrEmpty(chatId)) return null;
        try
        {
            using var client = _httpFactory.CreateClient("telegram");
            var body = new Dictionary<string, object> { ["chat_id"] = chatId, ["text"] = text, ["parse_mode"] = parseMode };
            if (replyMarkup is not null) body["reply_markup"] = replyMarkup;
            using var resp = await client.PostAsJsonAsync($"https://api.telegram.org/bot{_botToken}/sendMessage", body, ct);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = System.Text.Json.JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.TryGetProperty("result", out var r) && r.TryGetProperty("message_id", out var mid)) return mid.GetInt64();
            return null;
        }
        catch (Exception e) { _logger.LogWarning(e, "Telegram sendReturnId failed"); return null; }
    }

    public async Task<bool> EditMessageTextWithMarkupAsync(string chatId, long messageId, string text, string parseMode, object? replyMarkup, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_botToken) || string.IsNullOrEmpty(chatId)) return false;
        try
        {
            using var client = _httpFactory.CreateClient("telegram");
            var body = new Dictionary<string, object> { ["chat_id"] = chatId, ["message_id"] = messageId, ["text"] = text, ["parse_mode"] = parseMode };
            if (replyMarkup is not null) body["reply_markup"] = replyMarkup;
            using var resp = await client.PostAsJsonAsync($"https://api.telegram.org/bot{_botToken}/editMessageText", body, ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception e) { _logger.LogWarning(e, "Telegram editMessageText failed"); return false; }
    }
}
