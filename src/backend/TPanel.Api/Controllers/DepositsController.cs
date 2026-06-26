using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.PublicApi;
using TPanel.Application.Features.Transactions;

namespace TPanel.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/deposits")]
public class DepositsController : AdminControllerBase
{
    private readonly IDepositAdminService _service;
    private readonly ICurrentUser _currentUser;

    public DepositsController(IDepositAdminService service, ICurrentUser currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    [HttpGet("pending")]
    public async Task<IActionResult> Pending(CancellationToken ct)
    {
        var user = await LoadUserAsync(_currentUser, ct);
        if (user is null) return Unauthorized(new { message = "Unauthenticated." });
        return Result(await _service.PendingAsync(user, BuildFilter(), ct));
    }

    [HttpGet("all")]
    public async Task<IActionResult> All(CancellationToken ct)
    {
        var user = await LoadUserAsync(_currentUser, ct);
        if (user is null) return Unauthorized(new { message = "Unauthenticated." });
        return Result(await _service.AllAsync(user, BuildFilter(), ct));
    }

    [HttpGet("filter-meta")]
    public async Task<IActionResult> FilterMeta(CancellationToken ct)
    {
        var user = await LoadUserAsync(_currentUser, ct);
        if (user is null) return Unauthorized(new { message = "Unauthenticated." });
        return Result(await _service.FilterMetaAsync(user, ct));
    }

    [HttpGet("{id:int}/detail")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        var user = await LoadUserAsync(_currentUser, ct);
        if (user is null) return Unauthorized(new { message = "Unauthenticated." });
        return Result(await _service.DetailAsync(user, id, ct));
    }

    [HttpGet("{id:int}/receipt")]
    public async Task<IActionResult> Receipt(int id, [FromServices] ITransactionAdminStore store,
        [FromServices] IReceiptStorage receipts, CancellationToken ct)
    {
        var user = await LoadUserAsync(_currentUser, ct);
        if (user is null) return Unauthorized(new { message = "Unauthenticated." });

        var d = await store.GetInvestAsync(id, ct);
        if (d is null || string.IsNullOrEmpty(d.ReceiptPath)) return NotFound();

        // Scope kontrolü
        if (user.IsTeamMember && d.TeamId != user.TeamId) return Forbid();
        if (user.IsMerchant)
        {
            var allowed = await store.GetMerchantIdsForUserAsync(user.MerchantGroupId.HasValue ? (int)user.MerchantGroupId : null, user.FirmId, ct);
            if (!allowed.Contains(d.FirmId)) return Forbid();
        }

        var (exists, fullPath) = await receipts.ResolveAsync(d.ReceiptPath!, ct);
        if (!exists) return NotFound();

        var mime = MimeFromExtension(Path.GetExtension(fullPath));
        var allowedMimes = new[] { "image/jpeg", "image/png", "image/gif", "image/webp", "application/pdf" };
        if (!allowedMimes.Contains(mime)) return StatusCode(415);

        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers["Content-Disposition"] = $"inline; filename=\"receipt-{d.Id}\"";
        return PhysicalFile(fullPath, mime);
    }

    [HttpPost("approve")]
    public async Task<IActionResult> Approve([FromBody] ApproveDepositBody body, CancellationToken ct)
    {
        var user = await LoadUserAsync(_currentUser, ct);
        if (user is null) return Unauthorized(new { message = "Unauthenticated." });
        return Result(await _service.ApproveAsync(user, body.Id, body.Amount, ClientIp, ct));
    }

    [HttpPost("reject")]
    public async Task<IActionResult> Reject([FromBody] RejectBody body, CancellationToken ct)
    {
        var user = await LoadUserAsync(_currentUser, ct);
        if (user is null) return Unauthorized(new { message = "Unauthenticated." });
        return Result(await _service.RejectAsync(user, body.Id, body.RejectType, ClientIp, ct));
    }

    [HttpPost("{id:int}/resend-callback")]
    public async Task<IActionResult> ResendCallback(int id, CancellationToken ct)
    {
        var user = await LoadUserAsync(_currentUser, ct);
        if (user is null) return Unauthorized(new { message = "Unauthenticated." });
        return Result(await _service.ResendCallbackAsync(user, id, ct));
    }

    // ---- Manuel yatırım ekleme ----
    [HttpGet("manual/meta")]
    public async Task<IActionResult> ManualMeta([FromServices] ITransactionAdminStore store, CancellationToken ct)
    {
        var user = await LoadUserAsync(_currentUser, ct);
        if (user is null) return Unauthorized(new { message = "Unauthenticated." });
        if (!user.IsAdmin) return StatusCode(403, new { message = "Yetkisiz." });

        var merchants = await store.GetActiveMerchantsAsync(ct);
        var teams = await store.GetTeamsForFilterAsync(ct);
        if (user.HasTeamScope) teams = teams.Where(t => t.Id == user.TeamId).ToList();

        return Ok(new
        {
            merchants = merchants.Select(m => new { id = m.Id, name = m.Name }),
            teams = teams.Select(t => new { id = t.Id, name = t.Name, status = t.Status }),
        });
    }

    [HttpGet("manual/team/{teamId:int}")]
    public async Task<IActionResult> ManualTeamMeta(int teamId, [FromServices] ITransactionAdminStore store, CancellationToken ct)
    {
        var user = await LoadUserAsync(_currentUser, ct);
        if (user is null) return Unauthorized(new { message = "Unauthenticated." });
        if (!user.IsAdmin) return StatusCode(403, new { message = "Yetkisiz." });
        if (user.HasTeamScope && teamId != user.TeamId) return StatusCode(403, new { message = "Yetkisiz." });

        var banks = await store.GetTeamBankAccountsAsync(teamId, ct);
        var agents = await store.GetTeamAgentsAsync(teamId, ct);
        return Ok(new
        {
            banks = banks.Select(b => new { id = b.Id, name = b.Name }),
            agents = agents.Select(a => new { id = a.Id, name = a.Name }),
        });
    }

    [HttpPost("manual")]
    public async Task<IActionResult> CreateManual([FromBody] ManualDepositBody body,
        [FromServices] ITransactionAdminStore store, CancellationToken ct)
    {
        var user = await LoadUserAsync(_currentUser, ct);
        if (user is null) return Unauthorized(new { message = "Unauthenticated." });
        if (!user.IsAdmin) return StatusCode(403, new { message = "Yetkisiz." });

        if (body.MerchantId is null or <= 0) return BadRequest(new { message = "Merchant seçilmeli." });
        var teamId = user.HasTeamScope ? user.TeamId : (body.TeamId ?? 0);
        if (teamId <= 0) return BadRequest(new { message = "Takım seçilmeli." });
        if (string.IsNullOrWhiteSpace(body.Name)) return BadRequest(new { message = "Müşteri adı girilmeli." });
        if (body.Amount is null or <= 0) return BadRequest(new { message = "Geçerli bir tutar girilmeli." });

        var id = await store.CreateManualDepositAsync(
            body.MerchantId.Value, teamId, body.BankId, body.AgentId,
            body.Name!.Trim(), body.Amount.Value, user.Id, ClientIp, ct);

        return Ok(new { id, message = "Manuel yatırım eklendi." });
    }

    // ---- Bekleyen yatırımı başka takıma taşı (yalnızca Super/Sub Admin) ----
    [HttpPost("{id:int}/move-team")]
    public async Task<IActionResult> MoveTeam(int id, [FromBody] MoveTeamBody body, [FromServices] ITransactionAdminStore store,
        [FromServices] TPanel.Application.Features.Audit.IAuditContext audit, CancellationToken ct)
    {
        var user = await LoadUserAsync(_currentUser, ct);
        if (user is null) return Unauthorized(new { message = "Unauthenticated." });
        if (!user.IsAdmin) return StatusCode(403, new { message = "Yetkisiz." });
        if (body.TeamId is null or <= 0) return BadRequest(new { message = "Takım seçilmeli." });
        if (body.BankId is null or <= 0) return BadRequest(new { message = "IBAN seçilmeli." });
        var (ok, msg) = await store.MoveDepositTeamAsync(id, body.TeamId.Value, body.BankId.Value, user.Id, ct);
        if (ok) audit.Set($"Yatırım başka takıma taşındı — #{id} → takım #{body.TeamId} (IBAN #{body.BankId})",
            "invest", id.ToString(), null, new { team_id = body.TeamId, bank_id = body.BankId });
        return StatusCode(ok ? 200 : 422, new { message = msg });
    }

    private static string MimeFromExtension(string ext) => ext.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream",
    };
}

public record MoveTeamBody(
    [property: System.Text.Json.Serialization.JsonPropertyName("team_id")] int? TeamId,
    [property: System.Text.Json.Serialization.JsonPropertyName("bank_id")] int? BankId);
