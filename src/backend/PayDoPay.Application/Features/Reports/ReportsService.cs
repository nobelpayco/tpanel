using PayDoPay.Application.Common;
using PayDoPay.Application.Common.Interfaces;
using PayDoPay.Application.Features.Dashboard;
using PayDoPay.Domain.Entities;

namespace PayDoPay.Application.Features.Reports;

public record ReportQuery(string? DateFrom, string? DateTo, string? MerchantId, string? TeamIds, string? Type);

public interface IReportsService
{
    Task<ApiResult> MerchantFilterOptions(CancellationToken ct);
    Task<ApiResult> VolumePerformance(ReportQuery q, CancellationToken ct);
    Task<ApiResult> PlayerAnalysis(ReportQuery q, CancellationToken ct);
    Task<ApiResult> AmountAnalysis(ReportQuery q, CancellationToken ct);
    Task<ApiResult> Financial(ReportQuery q, CancellationToken ct);
    Task<ApiResult> Risk(ReportQuery q, CancellationToken ct);
    Task<ApiResult> TeamFilterOptions(CancellationToken ct);
    Task<ApiResult> TeamOverview(ReportQuery q, CancellationToken ct);
    Task<ApiResult> TeamTrends(ReportQuery q, CancellationToken ct);
    Task<ApiResult> TeamHourly(ReportQuery q, CancellationToken ct);
    Task<ApiResult> QueueAnalysis(ReportQuery q, CancellationToken ct);
    Task<ApiResult> PeakHours(ReportQuery q, CancellationToken ct);
    Task<ApiResult> Sla(ReportQuery q, CancellationToken ct);
    Task<ApiResult> Conversion(User u, ReportQuery q, CancellationToken ct);
    Task<ApiResult> Suspicious(ReportQuery q, CancellationToken ct);
    Task<ApiResult> Segmentation(ReportQuery q, CancellationToken ct);
    Task<ApiResult> MultiName(ReportQuery q, CancellationToken ct);
    Task<ApiResult> BankAccountAnalysis(ReportQuery q, CancellationToken ct);
}

public class ReportsService : IReportsService
{
    private readonly IReportsStore _store;
    private readonly IClock _clock;
    private readonly IManagementHelper _mh;

    public ReportsService(IReportsStore store, IClock clock, IManagementHelper mh) { _store = store; _clock = clock; _mh = mh; }

    private string MonthStart => new DateTime(_clock.Today.Year, _clock.Today.Month, 1).ToString("yyyy-MM-dd");
    private string Today => _clock.Today.ToString("yyyy-MM-dd");
    private (string from, string to) Range(ReportQuery q) => (string.IsNullOrEmpty(q.DateFrom) ? MonthStart : q.DateFrom!, string.IsNullOrEmpty(q.DateTo) ? Today : q.DateTo!);
    private static List<int>? TeamIds(string? s) => string.IsNullOrEmpty(s) ? null : s.Split(',').Select(x => int.TryParse(x.Trim(), out var v) ? v : 0).Where(v => v > 0).ToList() is { Count: > 0 } l ? l : null;

    public async Task<ApiResult> MerchantFilterOptions(CancellationToken ct) => ApiResult.Ok(await _store.MerchantFilterOptionsAsync(ct));
    public async Task<ApiResult> VolumePerformance(ReportQuery q, CancellationToken ct) { var (f, t) = Range(q); return ApiResult.Ok(await _store.VolumePerformanceAsync(f, t, q.MerchantId, ct)); }
    public async Task<ApiResult> PlayerAnalysis(ReportQuery q, CancellationToken ct) { var (f, t) = Range(q); return ApiResult.Ok(await _store.PlayerAnalysisAsync(f, t, q.MerchantId, ct)); }
    public async Task<ApiResult> AmountAnalysis(ReportQuery q, CancellationToken ct) { var (f, t) = Range(q); return ApiResult.Ok(await _store.AmountAnalysisAsync(f, t, q.MerchantId, ct)); }
    public async Task<ApiResult> Financial(ReportQuery q, CancellationToken ct) { var (f, t) = Range(q); return ApiResult.Ok(await _store.FinancialReportAsync(f, t, q.MerchantId, ct)); }
    public async Task<ApiResult> Risk(ReportQuery q, CancellationToken ct) { var (f, t) = Range(q); return ApiResult.Ok(await _store.RiskReportAsync(f, t, q.MerchantId, ct)); }

    public async Task<ApiResult> TeamFilterOptions(CancellationToken ct) => ApiResult.Ok(await _store.TeamFilterOptionsAsync(ct));
    public async Task<ApiResult> TeamOverview(ReportQuery q, CancellationToken ct) { var (f, t) = Range(q); return ApiResult.Ok(await _store.TeamOverviewAsync(f, t, TeamIds(q.TeamIds), ct)); }
    public async Task<ApiResult> TeamTrends(ReportQuery q, CancellationToken ct) { var (f, t) = Range(q); return ApiResult.Ok(await _store.TeamTrendsAsync(f, t, TeamIds(q.TeamIds), ct)); }
    public async Task<ApiResult> TeamHourly(ReportQuery q, CancellationToken ct) { var (f, t) = Range(q); return ApiResult.Ok(await _store.TeamHourlyAsync(f, t, TeamIds(q.TeamIds), ct)); }

    public async Task<ApiResult> QueueAnalysis(ReportQuery q, CancellationToken ct) => ApiResult.Ok(await _store.QueueAnalysisAsync(q.MerchantId, ct));
    public async Task<ApiResult> PeakHours(ReportQuery q, CancellationToken ct) { var (f, t) = Range(q); return ApiResult.Ok(await _store.PeakHourAnalysisAsync(f, t, q.MerchantId, ct)); }
    public async Task<ApiResult> Sla(ReportQuery q, CancellationToken ct) { var (f, t) = Range(q); return ApiResult.Ok(await _store.SlaReportAsync(f, t, q.MerchantId, ct)); }

    public async Task<ApiResult> Conversion(User u, ReportQuery q, CancellationToken ct)
    {
        var (f, t) = Range(q);
        var type = q.Type == "2" ? "2" : "1";
        List<int>? userMerchantIds = null;
        if (u.HasMerchantScope)
            userMerchantIds = (await _mh.GetMerchantIdsAsync(u.MerchantGroupId.HasValue ? (int)u.MerchantGroupId : null, u.FirmId, ct)).ToList();
        return ApiResult.Ok(await _store.ConversionAsync(f, t, type, userMerchantIds, ct));
    }

    public async Task<ApiResult> Suspicious(ReportQuery q, CancellationToken ct) { var (f, t) = Range(q); return ApiResult.Ok(await _store.SuspiciousPlayersAsync(f, t, q.MerchantId, ct)); }
    public async Task<ApiResult> Segmentation(ReportQuery q, CancellationToken ct) { var (f, t) = Range(q); return ApiResult.Ok(await _store.PlayerSegmentationAsync(f, t, q.MerchantId, ct)); }
    public async Task<ApiResult> MultiName(ReportQuery q, CancellationToken ct) { var f = string.IsNullOrEmpty(q.DateFrom) ? new DateTime(_clock.Today.Year, 1, 1).ToString("yyyy-MM-dd") : q.DateFrom!; var t = string.IsNullOrEmpty(q.DateTo) ? Today : q.DateTo!; return ApiResult.Ok(await _store.MultiNamePlayersAsync(f, t, q.MerchantId, ct)); }

    public async Task<ApiResult> BankAccountAnalysis(ReportQuery q, CancellationToken ct)
    {
        var f = string.IsNullOrEmpty(q.DateFrom) ? _clock.Today.AddDays(-30).ToString("yyyy-MM-dd") : q.DateFrom!;
        var t = string.IsNullOrEmpty(q.DateTo) ? Today : q.DateTo!;
        return ApiResult.Ok(await _store.BankAccountAnalysisAsync(f, t, ct));
    }
}
