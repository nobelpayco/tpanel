using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.Dashboard;

namespace TPanel.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/dashboard")]
public class DashboardController : AdminControllerBase
{
    private readonly IDashboardService _s;
    private readonly ICurrentUser _cu;
    public DashboardController(IDashboardService s, ICurrentUser cu) { _s = s; _cu = cu; }

    private string Q(string k, string def) => string.IsNullOrWhiteSpace(Request.Query[k]) ? def : Request.Query[k].ToString();
    private string Today => DateTime.UtcNow.AddHours(3).ToString("yyyy-MM-dd"); // İstanbul
    private async Task<(Domain.Entities.User? u, IActionResult? e)> AuthAsync(CancellationToken ct)
    { var u = await _cu.GetUserAsync(ct); return u is null ? (null, Unauthorized(new { message = "Unauthenticated." })) : (u, null); }

    [HttpGet("stats")]
    public async Task<IActionResult> Stats(CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return Result(await _s.StatsAsync(u!, Q("date_from", Today), Q("date_to", Today), ct)); }

    [HttpGet("merchant-cases")]
    public async Task<IActionResult> MerchantCases(CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return Result(await _s.MerchantCasesAsync(u!, Q("date_from", Today), Q("date_to", Today), ct)); }

    [HttpGet("yearly-volume")]
    public async Task<IActionResult> YearlyVolume(CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return Result(await _s.YearlyVolumeAsync(u!, Q("date_from", Today), Q("date_to", Today), ct)); }

    [HttpGet("recent-transactions")]
    public async Task<IActionResult> Recent(CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return Result(await _s.RecentTransactionsAsync(u!, Q("date_from", Today), Q("date_to", Today), ct)); }

    [HttpGet("team-performance")]
    public async Task<IActionResult> TeamPerformance(CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return Result(await _s.TeamPerformanceAsync(u!, Q("date_from", Today), Q("date_to", Today), ct)); }

    [HttpGet("team-detail/{teamId:int}")]
    public async Task<IActionResult> TeamDetail(int teamId, CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return Result(await _s.TeamDetailAsync(u!, teamId, Q("date_from", Today), Q("date_to", Today), ct)); }

    [HttpGet("player-stats/{playerId}")]
    public async Task<IActionResult> PlayerStats(string playerId, CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return Result(await _s.PlayerStatsAsync(u!, playerId, ct)); }

    [HttpGet("player-transactions/{playerId}")]
    public async Task<IActionResult> PlayerTx(string playerId, CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; var page = int.TryParse(Request.Query["page"], out var pg) ? pg : 1; return Result(await _s.PlayerTransactionsAsync(u!, playerId, page, ct)); }

    // Kullanıcıya özel dashboard düzeni
    [HttpGet("layout")]
    public async Task<IActionResult> GetLayout(CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return Result(await _s.LayoutAsync(u!, ct)); }

    [HttpPut("layout")]
    public async Task<IActionResult> SaveLayout([FromBody] DashboardLayoutBody body, CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return Result(await _s.SaveLayoutAsync(u!, body.Layout, ct)); }
}

public record DashboardLayoutBody(
    [property: System.Text.Json.Serialization.JsonPropertyName("layout")] string? Layout);

[ApiController]
[Authorize]
[Route("api/exports")]
public class ExportsController : AdminControllerBase
{
    private readonly Application.Features.Export.IExportService _s;
    private readonly Application.Features.Export.IExportStore _store;
    private readonly ICurrentUser _cu;
    public ExportsController(Application.Features.Export.IExportService s, Application.Features.Export.IExportStore store, ICurrentUser cu) { _s = s; _store = store; _cu = cu; }

    private async Task<(Domain.Entities.User? u, IActionResult? e)> AuthAsync(CancellationToken ct)
    { var u = await _cu.GetUserAsync(ct); return u is null ? (null, Unauthorized(new { message = "Unauthenticated." })) : (u, null); }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] Application.Features.Export.ExportCreateBody b, CancellationToken ct)
    { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return Result(await _s.CreateAsync(u!, b, ct)); }

    [HttpGet]
    public async Task<IActionResult> Status(CancellationToken ct)
    { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return Result(await _s.StatusAsync(u!, ct)); }

    [HttpDelete("clear")]
    public async Task<IActionResult> Clear(CancellationToken ct)
    { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return Result(await _s.ClearAsync(u!, ct)); }
}

/// <summary>Public dashboard widget — sabit token ile korunur (Laravel ile aynı).</summary>
[ApiController]
[AllowAnonymous]
public class WidgetController : ControllerBase
{
    private readonly IDashboardService _s;
    public WidgetController(IDashboardService s) => _s = s;

    [HttpGet("api/widget/{token}")]
    public async Task<IActionResult> Widget(string token, CancellationToken ct)
    {
        if (token != "paylira-w12-2026") return Unauthorized(new { error = "Unauthorized" });
        var r = await _s.WidgetAsync(ct);
        return StatusCode(r.HttpStatus, r.Body);
    }
}

/// <summary>Export indirme — auth middleware dışında, token query param ile (Laravel ile aynı).</summary>
[ApiController]
[AllowAnonymous]
public class ExportDownloadController : ControllerBase
{
    private readonly Application.Features.Export.IExportStore _store;
    private readonly ICurrentUser _cu;
    public ExportDownloadController(Application.Features.Export.IExportStore store, ICurrentUser cu) { _store = store; _cu = cu; }

    [HttpGet("api/exports/{id:long}/download")]
    public async Task<IActionResult> Download(long id, CancellationToken ct)
    {
        int? userId = _cu.UserId;
        var token = Request.Query["token"].ToString();
        if (userId is null && !string.IsNullOrEmpty(token))
            userId = await _store.ResolveTokenUserAsync(token, ct);
        if (userId is null) return Unauthorized(new { message = "Unauthenticated." });

        var (found, status, filename, ownerId) = await _store.GetJobAsync(id, ct);
        if (!found || ownerId != userId.Value) return NotFound(new { message = "Export bulunamadı." });
        if (status != "completed") return StatusCode(422, new { message = "Export henüz hazır değil." });
        if (string.IsNullOrEmpty(filename)) return StatusCode(410, new { message = "Dosya süresi dolmuş, lütfen tekrar export alın." });
        var path = _store.ResolveExportPath(filename);
        if (!System.IO.File.Exists(path)) return StatusCode(410, new { message = "Dosya süresi dolmuş, lütfen tekrar export alın." });

        Response.Headers["Content-Disposition"] = $"attachment; filename=\"{filename}\"";
        return PhysicalFile(path, "text/csv; charset=UTF-8");
    }
}
