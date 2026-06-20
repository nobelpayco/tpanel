using TPanel.Application.Common;
using TPanel.Application.Common.Interfaces;
using TPanel.Domain.Entities;

namespace TPanel.Application.Features.Dashboard;

public interface IDashboardService
{
    Task<ApiResult> WidgetAsync(CancellationToken ct = default);
    Task<ApiResult> StatsAsync(User u, string from, string to, CancellationToken ct = default);
    Task<ApiResult> MerchantCasesAsync(User u, string from, string to, CancellationToken ct = default);
    Task<ApiResult> YearlyVolumeAsync(User u, string from, string to, CancellationToken ct = default);
    Task<ApiResult> RecentTransactionsAsync(User u, string from, string to, CancellationToken ct = default);
    Task<ApiResult> TeamPerformanceAsync(User u, string from, string to, CancellationToken ct = default);
    Task<ApiResult> TeamDetailAsync(User u, int teamId, string from, string to, CancellationToken ct = default);
    Task<ApiResult> PlayerTransactionsAsync(User u, string playerId, int page, CancellationToken ct = default);
    Task<ApiResult> PlayerStatsAsync(User u, string playerId, CancellationToken ct = default);
}

public class DashboardService : IDashboardService
{
    private readonly IDashboardStore _store;
    private readonly IManagementHelper _mh;

    public DashboardService(IDashboardStore store, IManagementHelper mh)
    {
        _store = store;
        _mh = mh;
    }

    private async Task<QueryScope> ScopeAsync(User u, CancellationToken ct)
    {
        if (u.HasTeamScope) return new QueryScope(ScopeKind.Team, u.TeamId);
        if (u.HasMerchantScope)
        {
            var ids = await _mh.GetMerchantIdsAsync(u.MerchantGroupId.HasValue ? (int)u.MerchantGroupId : null, u.FirmId, ct);
            return new QueryScope(ScopeKind.Merchant, MerchantIds: ids);
        }
        return QueryScope.Global;
    }

    public async Task<ApiResult> WidgetAsync(CancellationToken ct = default) => ApiResult.Ok(await _store.WidgetAsync(ct));
    public async Task<ApiResult> StatsAsync(User u, string from, string to, CancellationToken ct = default)
        => ApiResult.Ok(await _store.StatsAsync(await ScopeAsync(u, ct), from, to, ct));
    public async Task<ApiResult> MerchantCasesAsync(User u, string from, string to, CancellationToken ct = default)
        => ApiResult.Ok(await _store.MerchantCasesAsync(await ScopeAsync(u, ct), u.HasTeamScope ? u.TeamId : null, from, to, ct));
    public async Task<ApiResult> YearlyVolumeAsync(User u, string from, string to, CancellationToken ct = default)
        => ApiResult.Ok(await _store.YearlyVolumeAsync(await ScopeAsync(u, ct), from, to, ct));
    public async Task<ApiResult> RecentTransactionsAsync(User u, string from, string to, CancellationToken ct = default)
        => ApiResult.Ok(await _store.RecentTransactionsAsync(await ScopeAsync(u, ct), u.HasTeamScope, from, to, ct));
    public async Task<ApiResult> TeamPerformanceAsync(User u, string from, string to, CancellationToken ct = default)
    {
        if (u.HasMerchantScope) return ApiResult.Msg(403, "Bu içeriği görüntüleme yetkiniz yok.");
        return ApiResult.Ok(await _store.TeamPerformanceAsync(await ScopeAsync(u, ct), from, to, ct));
    }
    public async Task<ApiResult> TeamDetailAsync(User u, int teamId, string from, string to, CancellationToken ct = default)
    {
        if (u.HasMerchantScope) return ApiResult.Msg(403, "Bu içeriği görüntüleme yetkiniz yok.");
        var r = await _store.TeamDetailAsync(teamId, from, to, ct);
        return r is null ? ApiResult.Msg(404, "Takım bulunamadı.") : ApiResult.Ok(r);
    }
    public async Task<ApiResult> PlayerTransactionsAsync(User u, string playerId, int page, CancellationToken ct = default)
        => ApiResult.Ok(await _store.PlayerTransactionsAsync(await ScopeAsync(u, ct), playerId, page, ct));
    public async Task<ApiResult> PlayerStatsAsync(User u, string playerId, CancellationToken ct = default)
        => ApiResult.Ok(await _store.PlayerStatsAsync(await ScopeAsync(u, ct), playerId, ct));
}

/// <summary>Merchant id çözümü (grup → tüm üyeler). Birden çok feature paylaşır.</summary>
public interface IManagementHelper
{
    Task<IReadOnlyList<int>> GetMerchantIdsAsync(int? merchantGroupId, int? firmId, CancellationToken ct = default);
}
