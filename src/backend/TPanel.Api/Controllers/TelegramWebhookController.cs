using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TPanel.Application.Features.Telegram;

namespace TPanel.Api.Controllers;

/// <summary>Telegram webhook — public uç, secret header ile korunur (Telegram çağırır).</summary>
[ApiController]
[AllowAnonymous]
[Route("api/telegram")]
public class TelegramWebhookController : ControllerBase
{
    private readonly ITelegramWebhookService _svc;
    private readonly IConfiguration _config;
    private readonly ILogger<TelegramWebhookController> _logger;

    public TelegramWebhookController(ITelegramWebhookService svc, IConfiguration config, ILogger<TelegramWebhookController> logger)
    {
        _svc = svc; _config = config; _logger = logger;
    }

    [HttpPost("webhook")]
    public async Task<IActionResult> Handle([FromBody] JsonElement update, CancellationToken ct)
    {
        // Secret token kontrolü (Telegram her istekte X-Telegram-Bot-Api-Secret-Token gönderir)
        var expected = _config["Telegram:WebhookSecret"];
        if (!string.IsNullOrEmpty(expected))
        {
            var received = Request.Headers["X-Telegram-Bot-Api-Secret-Token"].ToString();
            if (!string.Equals(received, expected, StringComparison.Ordinal))
                return StatusCode(403, new { ok = false });
        }

        try { await _svc.ProcessUpdateAsync(update, ct); }
        catch (Exception e) { _logger.LogWarning(e, "Telegram webhook işleme hatası"); }

        // Telegram'ın retry fırtınasına girmemesi için her durumda 200/ok
        return Ok(new { ok = true });
    }
}
