using System.Text.Json;

namespace TPanel.Application.Features.Telegram;

/// <summary>Telegram webhook update işleyicisi (TelegramWebhookController karşılığı).</summary>
public interface ITelegramWebhookService
{
    Task ProcessUpdateAsync(JsonElement update, CancellationToken ct = default);
}
