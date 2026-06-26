using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.Background;

namespace TPanel.Api.Controllers;

/// <summary>Super Admin bakım uçları — snapshot yeniden üretimi vb.</summary>
[ApiController]
[Authorize]
[Route("api/system")]
public class SystemController : ControllerBase
{
    private readonly ICurrentUser _cu;
    public SystemController(ICurrentUser cu) => _cu = cu;

    [HttpPost("run-snapshot")]
    public async Task<IActionResult> RunSnapshot([FromBody] RunSnapshotBody body, [FromServices] IDailyCaseSnapshotJob job, CancellationToken ct)
    {
        var u = await _cu.GetUserAsync(ct);
        if (u is null) return Unauthorized(new { message = "Unauthenticated." });
        if (!u.IsSuperAdmin) return StatusCode(403, new { message = "Bu işlem için yetkiniz yok." });
        if (string.IsNullOrWhiteSpace(body?.Date)) return StatusCode(422, new { message = "Tarih zorunludur (yyyy-MM-dd)." });
        await job.RunAsync(body.Date, ct);
        return Ok(new { message = $"{body.Date} snapshot üretildi." });
    }

    /// <summary>Günlük merchant mutabakat raporunu manuel tetikle (Telegram'a gönderir). Test için.</summary>
    [HttpPost("run-recon-report")]
    public async Task<IActionResult> RunReconReport([FromBody] RunSnapshotBody body, [FromServices] TPanel.Application.Features.Background.IMerchantReconReportJob job, CancellationToken ct)
    {
        var u = await _cu.GetUserAsync(ct);
        if (u is null) return Unauthorized(new { message = "Unauthenticated." });
        if (!u.IsSuperAdmin) return StatusCode(403, new { message = "Bu işlem için yetkiniz yok." });
        if (string.IsNullOrWhiteSpace(body?.Date)) return StatusCode(422, new { message = "Tarih zorunludur (yyyy-MM-dd)." });
        await job.RunAsync(body.Date, force: true, ct);
        return Ok(new { message = $"{body.Date} mutabakat raporu Telegram'a gönderildi." });
    }

    /// <summary>Telegram webhook'unu kaydet. body.url verilmezse App:Url + /api/telegram/webhook kullanılır.</summary>
    [HttpPost("telegram/set-webhook")]
    public async Task<IActionResult> SetTelegramWebhook([FromBody] SetWebhookBody? body, [FromServices] ITelegramService telegram, [FromServices] IConfiguration config, CancellationToken ct)
    {
        var u = await _cu.GetUserAsync(ct);
        if (u is null) return Unauthorized(new { message = "Unauthenticated." });
        if (!u.IsSuperAdmin) return StatusCode(403, new { message = "Bu işlem için yetkiniz yok." });

        var url = !string.IsNullOrWhiteSpace(body?.Url)
            ? body!.Url!
            : (config["App:Url"]?.TrimEnd('/') + "/api/telegram/webhook");
        if (string.IsNullOrWhiteSpace(url) || !url.StartsWith("https://"))
            return StatusCode(422, new { message = "Geçerli bir HTTPS URL gerekli (Telegram webhook HTTPS ister)." });

        var (ok, message) = await telegram.SetWebhookAsync(url, config["Telegram:WebhookSecret"], ct);
        return StatusCode(ok ? 200 : 422, new { ok, message });
    }
}

public record RunSnapshotBody([property: System.Text.Json.Serialization.JsonPropertyName("date")] string? Date);
public record SetWebhookBody([property: System.Text.Json.Serialization.JsonPropertyName("url")] string? Url);
