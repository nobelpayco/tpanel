using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.Audit;

namespace TPanel.Api.Controllers;

[ApiController]
[Route("api/audit-logs")]
[Authorize]
public class AuditLogsController : ControllerBase
{
    private readonly ICurrentUser _cu;
    private readonly IAuditLogger _audit;

    public AuditLogsController(ICurrentUser cu, IAuditLogger audit) { _cu = cu; _audit = audit; }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var u = await _cu.GetUserAsync(ct);
        if (u is null) return Unauthorized(new { message = "Unauthenticated." });
        if (!u.IsGodMode) return StatusCode(403, new { message = "Bu sayfaya erişim yetkiniz yok." });

        string? S(string k) => Request.Query.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v.ToString() : null;
        int? I(string k) => int.TryParse(S(k), out var n) ? n : null;

        var q = new AuditQuery(
            Search: S("search"), UserId: I("user_id"), Method: S("method"), Action: S("action"),
            From: S("from"), To: S("to"), Page: I("page") ?? 1, PerPage: I("per_page") ?? 50);

        var page = await _audit.QueryAsync(q, ct);
        return Ok(new { items = page.Items, total = page.Total });
    }
}
