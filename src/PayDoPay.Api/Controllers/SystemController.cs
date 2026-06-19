using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayDoPay.Application.Common.Interfaces;
using PayDoPay.Application.Features.Background;

namespace PayDoPay.Api.Controllers;

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
}

public record RunSnapshotBody([property: System.Text.Json.Serialization.JsonPropertyName("date")] string? Date);
