using System.Data;
using System.Text;
using Dapper;
using TPanel.Application.Common;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.Dashboard;

namespace TPanel.Infrastructure.Services;

public class MerchantScopeHelper : IManagementHelper
{
    private readonly IDbConnectionFactory _factory;
    public MerchantScopeHelper(IDbConnectionFactory factory) => _factory = factory;
    public async Task<IReadOnlyList<int>> GetMerchantIdsAsync(int? merchantGroupId, int? firmId, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        if (merchantGroupId is not null) return (await c.QueryAsync<int>("SELECT id FROM merchantUser WHERE group_id=@g", new { g = merchantGroupId })).ToList();
        return firmId is not null ? new List<int> { firmId.Value } : new List<int>();
    }
}

/// <summary>Dashboard veri erişimi (Dapper).</summary>
public class DashboardStore : IDashboardStore
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    private readonly IMerchantBankService _banks;

    public DashboardStore(IDbConnectionFactory factory, IClock clock, IMerchantBankService banks)
    {
        _factory = factory; _clock = clock; _banks = banks;
    }

    private static void AddScope(StringBuilder w, DynamicParameters p, QueryScope s, string alias = "")
    {
        var prefix = alias == "" ? "" : alias + ".";
        if (s.Kind == ScopeKind.Team) { w.Append($" AND {prefix}team_id=@scTeam"); p.Add("scTeam", s.TeamId); }
        else if (s.Kind == ScopeKind.Merchant)
        {
            var ids = s.MerchantIds ?? Array.Empty<int>();
            if (ids.Count == 0) { w.Append(" AND 1=0"); }
            else if (ids.Count == 1) { w.Append($" AND {prefix}firm_id=@scFirm"); p.Add("scFirm", ids[0]); }
            else { w.Append($" AND {prefix}firm_id IN @scFirms"); p.Add("scFirms", ids.ToList()); }
        }
    }

    private static async Task<double> Sum(IDbConnection c, string sql, object p) => await c.ExecuteScalarAsync<double?>(sql, p) ?? 0;

    private (int?, int) ComputeTrust(List<string> lastTen)
    {
        if (lastTen.Count == 0) return (null, 0);
        var approved = lastTen.Count(s => s == "3"); var count = lastTen.Count;
        var weight = Math.Min(count / 10.0, 1);
        return ((int)Math.Round((0.75 * (1 - weight) + (double)approved / count * weight) * 100), count);
    }

    public async Task<object> WidgetAsync(CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var today = _clock.Today.ToString("yyyy-MM-dd");
        var dep = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE type=1 AND status=3 AND DATE(finalize_date)=@d", new { d = today });
        var wd = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE type=2 AND status=3 AND DATE(finalize_date)=@d", new { d = today });
        var teams = (await c.QueryAsync("SELECT id, name FROM teams")).ToList();
        var cashes = await _banks.CurrentCashForTeamsAsync(teams.Select(t => (int)t.id).ToList(), ct);

        string Fmt(double v) => v.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));
        var rows = new List<object>
        {
            new { key = "DEPOSIT / WITHDRAW", color = "main" },
            new { key = "Deposit", value = Fmt(dep), color = "success" },
            new { key = "Withdraw", value = Fmt(wd), color = "danger" },
            new { key = "" },
        };
        var show = new[] { "M2", "M18" };
        var teamRows = teams.Where(t => show.Contains((string)t.name)).Select(t => new { name = (string)t.name, cas = cashes.GetValueOrDefault((int)t.id, 0) }).Where(x => x.cas != 0).ToList();
        var filteredTotal = teamRows.Sum(x => x.cas);
        rows.Add(new { key = "TEAMS", value = Fmt(filteredTotal), color = "main" });
        foreach (var t in teamRows) rows.Add(new { key = t.name, value = Fmt(t.cas), color = t.cas >= 0 ? "success" : "danger" });
        rows.Add(new { key = "" });
        rows.Add(new { key = "LAST UPDATE", value = _clock.Now.ToString("HH:mm"), color = "muted" });
        return rows;
    }

    public async Task<object> StatsAsync(QueryScope scope, string from, string to, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var fromStart = from + " 00:00:00"; var toEnd = to + " 23:59:59";

        async Task<double> S(string sql) { var w = new StringBuilder(); var p = new DynamicParameters(); p.Add("fs", fromStart); p.Add("te", toEnd); AddScope(w, p, scope); return await Sum(c, sql.Replace("{scope}", w.ToString()), p); }
        async Task<long> Cnt(string sql) { var w = new StringBuilder(); var p = new DynamicParameters(); AddScope(w, p, scope); return await c.ExecuteScalarAsync<long>(sql.Replace("{scope}", w.ToString()), p); }

        var totalDep = await S("SELECT COALESCE(SUM(amount),0) FROM invest WHERE type='1' AND status='3' AND finalize_date>=@fs AND finalize_date<=@te {scope}");
        var totalWd = await S("SELECT COALESCE(SUM(amount),0) FROM invest WHERE type='2' AND status='3' AND finalize_date>=@fs AND finalize_date<=@te {scope}");
        var pendDep = await Cnt("SELECT COUNT(*) FROM invest WHERE type='1' AND status IN ('1','2') {scope}");
        var pendWd = await Cnt("SELECT COUNT(*) FROM invest WHERE type='2' AND status IN ('0','1','2') {scope}");
        var pendWdAmt = await S("SELECT COALESCE(SUM(amount),0) FROM invest WHERE type='2' AND status IN ('0','1','2') {scope}".Replace("@fs", "@fs").Replace("@te", "@te"));

        var eligible = await _banks.EligibleIbansAsync(ct);
        double availMin = eligible.Count > 0 ? eligible.Min(a => Math.Max(a.MinInvest, a.TeamMin)) : 0;
        double availMax = eligible.Count > 0 ? eligible.Max(a => Math.Min(a.MaxInvest, a.TeamMax)) : 0;

        return new
        {
            total_deposits = totalDep, total_withdrawals = totalWd,
            pending_deposits = pendDep, pending_withdrawals = pendWd, pending_withdrawals_amount = pendWdAmt,
            available_ibans_count = eligible.Count, available_ibans_min = Math.Round(availMin, 2), available_ibans_max = Math.Round(availMax, 2),
        };
    }

    public async Task<object> MerchantCasesAsync(QueryScope scope, int? teamScopeTeamId, string from, string to, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var today = _clock.Today.ToString("yyyy-MM-dd");

        // Team scope → kendi takım kasası
        if (teamScopeTeamId is not null)
        {
            var team = await c.QueryFirstOrDefaultAsync("SELECT * FROM teams WHERE id=@id", new { id = teamScopeTeamId });
            if (team is null) return new { items = Array.Empty<object>(), total_case = 0, type = "team" };
            var dep = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE team_id=@id AND type=1 AND status=3 AND DATE(finalize_date) BETWEEN @f AND @t", new { id = teamScopeTeamId, f = from, t = to });
            var wd = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE team_id=@id AND type=2 AND status=3 AND DATE(finalize_date) BETWEEN @f AND @t", new { id = teamScopeTeamId, f = from, t = to });
            var comm = Convert.ToDouble(team.commission); var overturn = Convert.ToDouble(team.overturn);
            var net = overturn + (dep - dep * comm / 100) - wd;
            var item = new { name = (string)team.name, case_now = Math.Round(overturn, 2), deposits = Math.Round(dep, 2), commission = team.commission, withdrawals = Math.Round(wd, 2), net_case = Math.Round(net, 2) };
            return new { items = new[] { item }, total_case = Math.Round(net, 2), type = "team" };
        }

        var allW = new StringBuilder("WHERE status='1'"); var allP = new DynamicParameters();
        if (scope.Kind == ScopeKind.Merchant) { var ids = scope.MerchantIds ?? Array.Empty<int>(); if (ids.Count == 0) allW.Append(" AND 1=0"); else { allW.Append(" AND id IN @ids"); allP.Add("ids", ids.ToList()); } }
        var allMerchants = (await c.QueryAsync($"SELECT id, name, caseNow, commission, withdrawCommission, group_id FROM merchantUser {allW}", allP)).ToList();
        var groups = (await c.QueryAsync("SELECT id, name FROM merchant_groups WHERE status=1")).ToDictionary(g => (uint)g.id, g => (string)g.name);

        var processed = new HashSet<uint>(); var cases = new List<object>(); double totalCase = 0;
        foreach (var m in allMerchants)
        {
            List<dynamic> gms; string displayName; string et; int eid;
            uint? gid = m.group_id is null ? null : (uint)m.group_id;
            if (gid is not null && groups.ContainsKey(gid.Value)) { if (!processed.Add(gid.Value)) continue; gms = allMerchants.Where(x => x.group_id is not null && (uint)x.group_id == gid.Value).ToList(); displayName = groups[gid.Value]; et = "merchant_group"; eid = (int)gid.Value; }
            else { gms = new List<dynamic> { m }; displayName = (string)m.name; et = "merchant"; eid = (int)m.id; }

            var groupIds = gms.Select(x => (int)x.id).ToList();
            var avgApproval = await c.ExecuteScalarAsync<double?>(
                @"SELECT AVG(TIMESTAMPDIFF(SECOND, created_at, finalize_date)) FROM invest
                  WHERE firm_id IN @ids AND type=1 AND status=3 AND DATE(finalize_date) BETWEEN @f AND @t
                    AND TIMESTAMPDIFF(SECOND, created_at, finalize_date) <= 7200", new { ids = groupIds, f = from, t = to }) ?? 0;

            var snapshot = await c.QueryFirstOrDefaultAsync("SELECT amount, details FROM daily_case_snapshots WHERE entity_type=@et AND entity_id=@eid AND snapshot_date=@d", new { et, eid, d = to });
            object row;
            if (snapshot is not null && string.Compare(to, today, StringComparison.Ordinal) < 0)
            {
                var det = ParseJson((string?)snapshot.details);
                row = new { name = displayName, case_now = Math.Round(D(det, "previous_balance"), 2), deposits = Math.Round(D(det, "deposits"), 2), commission = gms[0].commission, withdrawals = Math.Round(D(det, "withdrawals"), 2), net_case = Math.Round(Convert.ToDouble(snapshot.amount), 2), avg_approval_sec = (int)avgApproval };
            }
            else
            {
                var lastSnap = await c.ExecuteScalarAsync<double?>("SELECT amount FROM daily_case_snapshots WHERE entity_type=@et AND entity_id=@eid AND snapshot_date<@d ORDER BY snapshot_date DESC LIMIT 1", new { et, eid, d = to }) ?? gms.Sum(x => Convert.ToDouble(x.caseNow));
                double deposits = 0, withdrawals = 0, netDep = 0, netWd = 0, pay = 0;
                foreach (var gm in gms)
                {
                    int id = (int)gm.id;
                    var dep = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE firm_id=@id AND type=1 AND status=3 AND DATE(finalize_date) BETWEEN @f AND @t", new { id, f = from, t = to });
                    var wd = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE firm_id=@id AND type=2 AND status=3 AND DATE(finalize_date) BETWEEN @f AND @t", new { id, f = from, t = to });
                    deposits += dep; withdrawals += wd;
                    netDep += dep - dep * Convert.ToDouble(gm.commission) / 100;
                    netWd += wd + wd * Convert.ToDouble(gm.withdrawCommission) / 100;
                    pay += await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM merchant_payments WHERE merchant_id=@id AND DATE(created_at) BETWEEN @f AND @t", new { id, f = from, t = to });
                }
                var cur = lastSnap + netDep - netWd - pay;
                row = new { name = displayName, case_now = Math.Round(lastSnap, 2), deposits = Math.Round(deposits, 2), commission = gms[0].commission, withdrawals = Math.Round(withdrawals, 2), net_case = Math.Round(cur, 2), avg_approval_sec = (int)avgApproval };
            }
            totalCase += (double)((dynamic)row).net_case;
            cases.Add(row);
        }
        return new { items = cases, total_case = Math.Round(totalCase, 2), type = "merchant" };
    }

    public async Task<object> YearlyVolumeAsync(QueryScope scope, string from, string to, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var isSingle = from == to;
        var start = isSingle ? _clock.Today.AddDays(-29) : DateTime.Parse(from);
        var end = isSingle ? DateTime.Parse(to) : DateTime.Parse(to);
        var startStr = start.ToString("yyyy-MM-dd"); var endStr = end.ToString("yyyy-MM-dd");

        async Task<Dictionary<string, double>> Daily(int type)
        {
            var w = new StringBuilder(); var p = new DynamicParameters(); p.Add("type", type); p.Add("s", startStr); p.Add("e", endStr); AddScope(w, p, scope);
            var rows = await c.QueryAsync($@"SELECT DATE_FORMAT(finalize_date,'%Y-%m-%d') AS Day, SUM(amount) AS Total FROM invest
                WHERE type=@type AND status=3 AND DATE(finalize_date)>=@s AND DATE(finalize_date)<=@e {w} GROUP BY Day", p);
            return rows.ToDictionary(r => (string)r.Day, r => Convert.ToDouble((object)r.Total));
        }
        var dep = await Daily(1); var wd = await Daily(2);
        var days = new List<string>(); var depData = new List<double>(); var wdData = new List<double>();
        for (var d = start; d <= end; d = d.AddDays(1))
        {
            var key = d.ToString("yyyy-MM-dd");
            days.Add(d.ToString("dd MMM", System.Globalization.CultureInfo.GetCultureInfo("tr-TR")));
            depData.Add(Math.Round(dep.GetValueOrDefault(key, 0)));
            wdData.Add(Math.Round(wd.GetValueOrDefault(key, 0)));
        }
        return new { days, deposits = depData, withdrawals = wdData };
    }

    public async Task<object> RecentTransactionsAsync(QueryScope scope, bool isTeamMember, string from, string to, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var w = new StringBuilder(); var p = new DynamicParameters(); p.Add("f", from); p.Add("t", to); AddScope(w, p, scope, "invest");
        var rows = (await c.QueryAsync($@"SELECT invest.id, invest.type, invest.status, invest.name, invest.player_id, invest.amount,
            invest.created_at, invest.finalize_date, teams.name AS team_name, banks.name AS bank_name, merchantUser.name AS merchant_name
            FROM invest LEFT JOIN teams ON invest.team_id=teams.id LEFT JOIN bankAccounts ON invest.bank_id=bankAccounts.id
            LEFT JOIN banks ON bankAccounts.bank_id=banks.id LEFT JOIN merchantUser ON invest.firm_id=merchantUser.id
            WHERE DATE(invest.created_at)>=@f AND DATE(invest.created_at)<=@t {w} ORDER BY invest.id DESC LIMIT 20", p)).ToList();

        var result = new List<object>();
        foreach (var tx in rows)
        {
            int? duration = null;
            if (tx.finalize_date is not null && tx.created_at is not null)
            {
                var diff = (int)((DateTime)tx.finalize_date - (DateTime)tx.created_at).TotalSeconds;
                if (diff <= 7200) duration = diff;
            }
            int? trustRate = null; int trustCount = 0;
            var txType = int.Parse((string)tx.type);   // invest.type varchar → güvenli parse
            if (txType == 1 && tx.player_id is not null)
            {
                var statuses = (await c.QueryAsync<string>("SELECT status FROM invest WHERE player_id=@p AND type=1 AND status IN ('3','4') AND id<@id ORDER BY id DESC LIMIT 10", new { p = (string)tx.player_id, id = (int)tx.id })).ToList();
                (trustRate, trustCount) = ComputeTrust(statuses);
            }
            var row = new Dictionary<string, object?>
            {
                ["id"] = (int)tx.id, ["type"] = txType, ["status"] = int.Parse((string)tx.status), ["name"] = (string?)tx.name,
                ["player_id"] = (string?)tx.player_id, ["amount"] = tx.amount, ["team"] = (string?)tx.team_name ?? "-", ["bank"] = (string?)tx.bank_name ?? "-",
                ["trust_rate"] = trustRate, ["trust_count"] = trustCount, ["duration"] = duration,
                ["created_at_raw"] = ((DateTime?)tx.created_at)?.ToString("o"), ["date"] = ((DateTime?)tx.created_at)?.ToString("HH:mm dd.MM.yyyy") ?? "-",
            };
            if (!isTeamMember) row["merchant"] = (string?)tx.merchant_name ?? "-";
            result.Add(row);
        }
        return result;
    }

    public async Task<object> TeamPerformanceAsync(QueryScope scope, string from, string to, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var w = new StringBuilder(); var p = new DynamicParameters(); p.Add("f", from); p.Add("t", to); AddScope(w, p, scope, "invest");
        var rows = (await c.QueryAsync($@"SELECT teams.id, teams.name, teams.maxCase,
            SUM(CASE WHEN invest.status=3 THEN 1 ELSE 0 END) AS approved,
            SUM(CASE WHEN invest.status=4 THEN 1 ELSE 0 END) AS rejected,
            SUM(CASE WHEN invest.status=3 THEN invest.amount ELSE 0 END) AS total_amount,
            SUM(CASE WHEN invest.status=3 AND TIMESTAMPDIFF(SECOND,invest.created_at,invest.finalize_date)<=7200 THEN TIMESTAMPDIFF(SECOND,invest.created_at,invest.finalize_date) ELSE 0 END) AS app_sec,
            SUM(CASE WHEN invest.status=3 AND TIMESTAMPDIFF(SECOND,invest.created_at,invest.finalize_date)<=7200 THEN 1 ELSE 0 END) AS app_cnt,
            SUM(CASE WHEN invest.status=4 AND TIMESTAMPDIFF(SECOND,invest.created_at,invest.finalize_date)<=7200 THEN TIMESTAMPDIFF(SECOND,invest.created_at,invest.finalize_date) ELSE 0 END) AS rej_sec,
            SUM(CASE WHEN invest.status=4 AND TIMESTAMPDIFF(SECOND,invest.created_at,invest.finalize_date)<=7200 THEN 1 ELSE 0 END) AS rej_cnt,
            SUM(CASE WHEN invest.process_date IS NOT NULL AND TIMESTAMPDIFF(SECOND,invest.created_at,invest.process_date)<=7200 THEN TIMESTAMPDIFF(SECOND,invest.created_at,invest.process_date) ELSE 0 END) AS proc_sec,
            SUM(CASE WHEN invest.process_date IS NOT NULL AND TIMESTAMPDIFF(SECOND,invest.created_at,invest.process_date)<=7200 THEN 1 ELSE 0 END) AS proc_cnt
            FROM invest JOIN teams ON invest.team_id=teams.id
            WHERE invest.type=1 AND DATE(invest.finalize_date)>=@f AND DATE(invest.finalize_date)<=@t AND invest.status IN (3,4) {w}
            GROUP BY teams.id, teams.name, teams.maxCase ORDER BY total_amount DESC", p)).ToList();

        var teamIds = rows.Select(r => (int)r.id).ToList();
        var cashes = await _banks.CurrentCashForTeamsAsync(teamIds, ct);
        var lastWd = teamIds.Count == 0 ? new Dictionary<int, object>() :
            (await c.QueryAsync("SELECT team_id AS Id, MAX(finalize_date) AS L FROM invest WHERE team_id IN @ids AND type=2 AND status=3 GROUP BY team_id", new { ids = teamIds }))
            .ToDictionary(r => (int)r.Id, r => (object)r.L);

        return rows.Select(t =>
        {
            long approved = (long)t.approved, rejected = (long)t.rejected; var total = approved + rejected;
            long appCnt = (long)t.app_cnt, rejCnt = (long)t.rej_cnt, procCnt = (long)t.proc_cnt;
            return (object)new
            {
                id = (int)t.id, name = (string)t.name, approved, rejected,
                rate = total > 0 ? Math.Round((double)approved / total * 100, 1) : 0,
                total = t.total_amount,
                avg_approved_sec = appCnt > 0 ? (long)Math.Round((double)(decimal)t.app_sec / appCnt) : 0,
                avg_rejected_sec = rejCnt > 0 ? (long)Math.Round((double)(decimal)t.rej_sec / rejCnt) : 0,
                avg_process_sec = procCnt > 0 ? (long)Math.Round((double)(decimal)t.proc_sec / procCnt) : 0,
                current_case = Math.Round(cashes.GetValueOrDefault((int)t.id, 0), 2),
                max_case = Convert.ToDouble(t.maxCase ?? 0),
                last_withdraw = lastWd.GetValueOrDefault((int)t.id),
            };
        }).ToList();
    }

    public async Task<object?> TeamDetailAsync(int teamId, string from, string to, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var team = await c.QueryFirstOrDefaultAsync("SELECT id, name, commission FROM teams WHERE id=@id", new { id = teamId });
        if (team is null) return null;
        var s = await c.QueryFirstOrDefaultAsync($@"SELECT
            SUM(CASE WHEN status='3' THEN 1 ELSE 0 END) approved, SUM(CASE WHEN status='4' THEN 1 ELSE 0 END) rejected,
            SUM(CASE WHEN status='3' THEN amount ELSE 0 END) approved_amount, SUM(CASE WHEN status='4' THEN amount ELSE 0 END) rejected_amount,
            AVG(CASE WHEN status='3' THEN amount END) avg_amount,
            AVG(CASE WHEN status='3' AND finalize_date IS NOT NULL AND TIMESTAMPDIFF(SECOND,created_at,finalize_date)<=7200 THEN TIMESTAMPDIFF(SECOND,created_at,finalize_date) END) avg_app,
            AVG(CASE WHEN status='4' AND finalize_date IS NOT NULL AND TIMESTAMPDIFF(SECOND,created_at,finalize_date)<=7200 THEN TIMESTAMPDIFF(SECOND,created_at,finalize_date) END) avg_rej,
            AVG(CASE WHEN process_date IS NOT NULL AND TIMESTAMPDIFF(SECOND,created_at,process_date)<=7200 THEN TIMESTAMPDIFF(SECOND,created_at,process_date) END) avg_proc
            FROM invest WHERE team_id=@id AND type='1' AND status IN ('3','4') AND DATE(finalize_date) BETWEEN @f AND @t", new { id = teamId, f = from, t = to });
        long appr = s?.approved is null ? 0 : (long)s.approved, rej = s?.rejected is null ? 0 : (long)s.rejected; var tot = appr + rej;

        var agents = (await c.QueryAsync($@"SELECT users.name,
            SUM(CASE WHEN invest.status='3' THEN 1 ELSE 0 END) approved, SUM(CASE WHEN invest.status='4' THEN 1 ELSE 0 END) rejected,
            SUM(CASE WHEN invest.status='3' THEN invest.amount ELSE 0 END) total_amount,
            AVG(CASE WHEN invest.status='3' AND invest.finalize_date IS NOT NULL AND TIMESTAMPDIFF(SECOND,invest.created_at,invest.finalize_date)<=7200 THEN TIMESTAMPDIFF(SECOND,invest.created_at,invest.finalize_date) END) avg_time
            FROM invest JOIN users ON invest.agent_id=users.id WHERE invest.team_id=@id AND invest.type='1' AND invest.status IN ('3','4') AND DATE(invest.finalize_date) BETWEEN @f AND @t
            GROUP BY users.id, users.name ORDER BY total_amount DESC", new { id = teamId, f = from, t = to }))
            .Select(a => { long ap = (long)a.approved, rj = (long)a.rejected; var tt = ap + rj; return (object)new { name = (string)a.name, approved = ap, rejected = rj, rate = tt > 0 ? Math.Round((double)ap / tt * 100, 1) : 0, total = Math.Round(Convert.ToDouble(a.total_amount), 2), avg_time = a.avg_time is null ? (long?)null : (long)Math.Round(Convert.ToDouble(a.avg_time)) }; }).ToList();

        var hourly = (await c.QueryAsync("SELECT HOUR(created_at) AS H, COUNT(*) AS C FROM invest WHERE team_id=@id AND type='1' AND status='3' AND DATE(finalize_date) BETWEEN @f AND @t GROUP BY H", new { id = teamId, f = from, t = to })).ToDictionary(r => (int)r.H, r => (long)r.C);
        var hLabels = new List<string>(); var hCounts = new List<long>();
        for (var i = 0; i < 24; i++) { hLabels.Add(i.ToString("D2") + ":00"); hCounts.Add(hourly.GetValueOrDefault(i, 0)); }

        var amountDist = await c.QueryAsync(@"SELECT CASE WHEN amount<=500 THEN '0-500' WHEN amount<=1000 THEN '500-1K' WHEN amount<=2500 THEN '1K-2.5K' WHEN amount<=5000 THEN '2.5K-5K' WHEN amount<=10000 THEN '5K-10K' ELSE '10K+' END AS range_label, COUNT(*) AS cnt
            FROM invest WHERE team_id=@id AND type='1' AND status='3' AND DATE(finalize_date) BETWEEN @f AND @t GROUP BY range_label ORDER BY FIELD(range_label,'0-500','500-1K','1K-2.5K','2.5K-5K','5K-10K','10K+')", new { id = teamId, f = from, t = to });

        var recent = (await c.QueryAsync(@"SELECT invest.id, invest.status, invest.amount, invest.name, invest.player_id, invest.created_at, invest.finalize_date, agent.name AS agent_name
            FROM invest LEFT JOIN users agent ON invest.agent_id=agent.id WHERE invest.team_id=@id AND invest.type='1' AND invest.status IN ('3','4') AND DATE(invest.finalize_date) BETWEEN @f AND @t
            ORDER BY invest.id DESC LIMIT 20", new { id = teamId, f = from, t = to }))
            .Select(tx => { int? dur = null; if (tx.finalize_date is not null && tx.created_at is not null) { var df = (int)((DateTime)tx.finalize_date - (DateTime)tx.created_at).TotalSeconds; if (df >= 0 && df <= 7200) dur = df; } return (object)new { id = (int)tx.id, status = int.Parse((string)tx.status), amount = Convert.ToDouble(tx.amount), name = (string?)tx.name, player_id = (string?)tx.player_id, agent_name = (string?)tx.agent_name, duration = dur, date = tx.created_at }; }).ToList();

        return new
        {
            team = new { id = (int)team.id, name = (string)team.name, commission = team.commission },
            summary = new
            {
                approved = appr, rejected = rej, approval_rate = tot > 0 ? Math.Round((double)appr / tot * 100, 1) : 0,
                approved_amount = Math.Round(s?.approved_amount is null ? 0 : Convert.ToDouble(s.approved_amount), 2),
                rejected_amount = Math.Round(s?.rejected_amount is null ? 0 : Convert.ToDouble(s.rejected_amount), 2),
                avg_amount = Math.Round(s?.avg_amount is null ? 0 : Convert.ToDouble(s.avg_amount), 2),
                avg_approve_time = s?.avg_app is null ? (long?)null : (long)Math.Round(Convert.ToDouble(s.avg_app)),
                avg_reject_time = s?.avg_rej is null ? (long?)null : (long)Math.Round(Convert.ToDouble(s.avg_rej)),
                avg_process_time = s?.avg_proc is null ? (long?)null : (long)Math.Round(Convert.ToDouble(s.avg_proc)),
            },
            agents, hourly = new { labels = hLabels, counts = hCounts }, amount_dist = amountDist, recent,
        };
    }

    public async Task<object> PlayerTransactionsAsync(QueryScope scope, string playerId, int page, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        const int perPage = 10;
        var w = new StringBuilder(); var p = new DynamicParameters(); p.Add("pid", playerId); AddScope(w, p, scope);
        var total = await c.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM invest WHERE player_id=@pid AND status IN ('3','4') {w}", p);
        p.Add("off", (page - 1) * perPage); p.Add("lim", perPage);
        var items = (await c.QueryAsync($"SELECT id,type,status,amount,name,created_at,finalize_date FROM invest WHERE player_id=@pid AND status IN ('3','4') {w} ORDER BY id DESC LIMIT @lim OFFSET @off", p))
            .Select(tx => (object)new { id = (int)tx.id, type = int.Parse((string)tx.type), status = int.Parse((string)tx.status), amount = Convert.ToDouble(tx.amount), name = (string?)tx.name, date = tx.created_at, duration = Dur(tx.finalize_date, tx.created_at) }).ToList();
        return new { items, total, page, per_page = perPage, last_page = (int)Math.Ceiling((double)total / perPage) };
    }

    public async Task<object> PlayerStatsAsync(QueryScope scope, string playerId, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var w = new StringBuilder(); var p = new DynamicParameters(); p.Add("pid", playerId); AddScope(w, p, scope);
        var s = await c.QueryFirstOrDefaultAsync($@"SELECT COUNT(*) total,
            SUM(CASE WHEN status='3' THEN 1 ELSE 0 END) approved, SUM(CASE WHEN status='4' THEN 1 ELSE 0 END) rejected,
            SUM(CASE WHEN status='3' THEN amount ELSE 0 END) approved_amount, SUM(CASE WHEN status='4' THEN amount ELSE 0 END) rejected_amount,
            AVG(CASE WHEN status='3' THEN amount END) avg_amount, MIN(CASE WHEN status='3' THEN amount END) min_amount, MAX(CASE WHEN status='3' THEN amount END) max_amount,
            MIN(created_at) first_tx, MAX(created_at) last_tx,
            AVG(CASE WHEN status='3' AND finalize_date IS NOT NULL AND TIMESTAMPDIFF(SECOND,created_at,finalize_date)<=7200 THEN TIMESTAMPDIFF(SECOND,created_at,finalize_date) END) avg_app
            FROM invest WHERE player_id=@pid AND status IN ('3','4') {w}", p);
        long total = s?.total is null ? 0 : (long)s.total, approved = s?.approved is null ? 0 : (long)s.approved;
        var rate = total > 0 ? (int)Math.Round((double)approved / total * 100) : 0;

        p.Add("d", _clock.Today.AddDays(-30).ToString("yyyy-MM-dd"));
        var daily = (await c.QueryAsync($@"SELECT DATE_FORMAT(created_at,'%Y-%m-%d') day, SUM(CASE WHEN status='3' THEN 1 ELSE 0 END) approved, SUM(CASE WHEN status='4' THEN 1 ELSE 0 END) rejected, SUM(CASE WHEN status='3' THEN amount ELSE 0 END) amount
            FROM invest WHERE player_id=@pid AND status IN ('3','4') AND DATE(created_at)>=@d {w} GROUP BY day ORDER BY day", p)).ToList();
        var days = daily.Select(d => DateTime.Parse((string)d.day).ToString("dd MMM", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"))).ToList();

        var ranges = await c.QueryAsync($@"SELECT CASE WHEN amount<=500 THEN '0-500' WHEN amount<=1000 THEN '500-1K' WHEN amount<=2500 THEN '1K-2.5K' WHEN amount<=5000 THEN '2.5K-5K' WHEN amount<=10000 THEN '5K-10K' ELSE '10K+' END range_label, COUNT(*) cnt
            FROM invest WHERE player_id=@pid AND status='3' {w} GROUP BY range_label ORDER BY FIELD(range_label,'0-500','500-1K','1K-2.5K','2.5K-5K','5K-10K','10K+')", p);
        var recent = (await c.QueryAsync($"SELECT id,status,amount,name,created_at,finalize_date FROM invest WHERE player_id=@pid AND type='1' AND status IN ('3','4') {w} ORDER BY id DESC LIMIT 10", p))
            .Select(tx => (object)new { id = (int)tx.id, status = int.Parse((string)tx.status), amount = Convert.ToDouble(tx.amount), name = (string?)tx.name, date = tx.created_at, duration = Dur(tx.finalize_date, tx.created_at) }).ToList();
        var blacklisted = await c.ExecuteScalarAsync<int>("SELECT EXISTS(SELECT 1 FROM blacklist WHERE type=1 AND val=@p)", new { p = playerId }) == 1;

        return new
        {
            player_id = playerId, is_blacklisted = blacklisted,
            summary = new
            {
                total, approved, rejected = s?.rejected is null ? 0 : (long)s.rejected, approval_rate = rate,
                approved_amount = Math.Round(s?.approved_amount is null ? 0 : Convert.ToDouble(s.approved_amount), 2),
                rejected_amount = Math.Round(s?.rejected_amount is null ? 0 : Convert.ToDouble(s.rejected_amount), 2),
                avg_amount = Math.Round(s?.avg_amount is null ? 0 : Convert.ToDouble(s.avg_amount), 2),
                min_amount = Math.Round(s?.min_amount is null ? 0 : Convert.ToDouble(s.min_amount), 2),
                max_amount = Math.Round(s?.max_amount is null ? 0 : Convert.ToDouble(s.max_amount), 2),
                avg_approve_time = s?.avg_app is null ? (long?)null : (long)Math.Round(Convert.ToDouble(s.avg_app)),
                first_tx = s?.first_tx, last_tx = s?.last_tx,
            },
            daily_chart = new { days, approved = daily.Select(d => (long)d.approved).ToList(), rejected = daily.Select(d => (long)d.rejected).ToList(), amounts = daily.Select(d => Math.Round(Convert.ToDouble(d.amount))).ToList() },
            amount_ranges = ranges, recent,
        };
    }

    private static int? Dur(dynamic? fin, dynamic? created)
    {
        if (fin is null || created is null) return null;
        var df = (int)((DateTime)fin - (DateTime)created).TotalSeconds;
        return Math.Max(0, Math.Min(7200, df));
    }
    private static System.Text.Json.JsonElement? ParseJson(string? j) { if (string.IsNullOrEmpty(j)) return null; try { return System.Text.Json.JsonDocument.Parse(j).RootElement.Clone(); } catch { return null; } }
    private static double D(System.Text.Json.JsonElement? e, string k) => e is not null && e.Value.TryGetProperty(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.Number ? v.GetDouble() : 0;
}
