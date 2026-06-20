using TPanel.Application.Common;

namespace TPanel.Application.Features.Dashboard;

/// <summary>Dashboard veri erişimi (Dapper). Scope: team_id / firm_id filtresi.</summary>
public interface IDashboardStore
{
    Task<object> WidgetAsync(CancellationToken ct = default);
    Task<object> StatsAsync(QueryScope scope, string from, string to, CancellationToken ct = default);
    Task<object> MerchantCasesAsync(QueryScope scope, int? teamScopeTeamId, string from, string to, CancellationToken ct = default);
    Task<object> YearlyVolumeAsync(QueryScope scope, string from, string to, CancellationToken ct = default);
    Task<object> RecentTransactionsAsync(QueryScope scope, bool isTeamMember, string from, string to, CancellationToken ct = default);
    Task<object> TeamPerformanceAsync(QueryScope scope, string from, string to, CancellationToken ct = default);
    Task<object?> TeamDetailAsync(int teamId, string from, string to, CancellationToken ct = default);
    Task<object> PlayerTransactionsAsync(QueryScope scope, string playerId, int page, CancellationToken ct = default);
    Task<object> PlayerStatsAsync(QueryScope scope, string playerId, CancellationToken ct = default);
}
