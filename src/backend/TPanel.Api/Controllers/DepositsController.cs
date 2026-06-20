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
