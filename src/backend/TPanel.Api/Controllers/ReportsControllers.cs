using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.Reports;

namespace TPanel.Api.Controllers;

[Authorize]
public abstract class ReportControllerBase : AdminControllerBase
{
    protected readonly IReportsService Svc;
    protected readonly ICurrentUser Cu;
    protected ReportControllerBase(IReportsService svc, ICurrentUser cu) { Svc = svc; Cu = cu; }
    protected ReportQuery Query() => new(
        string.IsNullOrWhiteSpace(Request.Query["date_from"]) ? null : Request.Query["date_from"].ToString(),
        string.IsNullOrWhiteSpace(Request.Query["date_to"]) ? null : Request.Query["date_to"].ToString(),
        string.IsNullOrWhiteSpace(Request.Query["merchant_id"]) ? null : Request.Query["merchant_id"].ToString(),
        string.IsNullOrWhiteSpace(Request.Query["team_ids"]) ? null : Request.Query["team_ids"].ToString(),
        string.IsNullOrWhiteSpace(Request.Query["type"]) ? null : Request.Query["type"].ToString());
}

[ApiController, Route("api/merchant-reports")]
public class MerchantReportsController : ReportControllerBase
{
    public MerchantReportsController(IReportsService s, ICurrentUser cu) : base(s, cu) { }
    [HttpGet("filter-options")] public async Task<IActionResult> Options(CancellationToken ct) => Result(await Svc.MerchantFilterOptions(ct));
    [HttpGet("volume-performance")] public async Task<IActionResult> Volume(CancellationToken ct) => Result(await Svc.VolumePerformance(Query(), ct));
    [HttpGet("player-analysis")] public async Task<IActionResult> Players(CancellationToken ct) => Result(await Svc.PlayerAnalysis(Query(), ct));
    [HttpGet("amount-analysis")] public async Task<IActionResult> Amount(CancellationToken ct) => Result(await Svc.AmountAnalysis(Query(), ct));
    [HttpGet("financial")] public async Task<IActionResult> Financial(CancellationToken ct) => Result(await Svc.Financial(Query(), ct));
    [HttpGet("risk")] public async Task<IActionResult> Risk(CancellationToken ct) => Result(await Svc.Risk(Query(), ct));
}

[ApiController, Route("api/team-reports")]
public class TeamReportsController : ReportControllerBase
{
    public TeamReportsController(IReportsService s, ICurrentUser cu) : base(s, cu) { }
    [HttpGet("filter-options")] public async Task<IActionResult> Options(CancellationToken ct) => Result(await Svc.TeamFilterOptions(ct));
    [HttpGet("overview")] public async Task<IActionResult> Overview(CancellationToken ct) => Result(await Svc.TeamOverview(Query(), ct));
    [HttpGet("trends")] public async Task<IActionResult> Trends(CancellationToken ct) => Result(await Svc.TeamTrends(Query(), ct));
    [HttpGet("hourly")] public async Task<IActionResult> Hourly(CancellationToken ct) => Result(await Svc.TeamHourly(Query(), ct));
}

[ApiController, Route("api/operations")]
public class OperationsReportsController : ReportControllerBase
{
    public OperationsReportsController(IReportsService s, ICurrentUser cu) : base(s, cu) { }
    [HttpGet("queue-analysis")] public async Task<IActionResult> Queue(CancellationToken ct) => Result(await Svc.QueueAnalysis(Query(), ct));
    [HttpGet("peak-hours")] public async Task<IActionResult> Peak(CancellationToken ct) => Result(await Svc.PeakHours(Query(), ct));
    [HttpGet("sla")] public async Task<IActionResult> Sla(CancellationToken ct) => Result(await Svc.Sla(Query(), ct));
}

[ApiController]
public class ConversionReportsController : ReportControllerBase
{
    public ConversionReportsController(IReportsService s, ICurrentUser cu) : base(s, cu) { }
    [HttpGet("api/conversion-reports")]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var u = await Cu.GetUserAsync(ct); if (u is null) return Unauthorized(new { message = "Unauthenticated." });
        return Result(await Svc.Conversion(u, Query(), ct));
    }
}

[ApiController, Route("api/player-risk")]
public class PlayerRiskReportsController : ReportControllerBase
{
    public PlayerRiskReportsController(IReportsService s, ICurrentUser cu) : base(s, cu) { }
    [HttpGet("suspicious")] public async Task<IActionResult> Suspicious(CancellationToken ct) => Result(await Svc.Suspicious(Query(), ct));
    [HttpGet("segmentation")] public async Task<IActionResult> Segmentation(CancellationToken ct) => Result(await Svc.Segmentation(Query(), ct));
    [HttpGet("multi-name")] public async Task<IActionResult> MultiName(CancellationToken ct) => Result(await Svc.MultiName(Query(), ct));
}

[ApiController]
public class BankAccountReportsController : ReportControllerBase
{
    public BankAccountReportsController(IReportsService s, ICurrentUser cu) : base(s, cu) { }
    [HttpGet("api/reports/bank-account-analysis")]
    public async Task<IActionResult> Analysis(CancellationToken ct) => Result(await Svc.BankAccountAnalysis(Query(), ct));
}
