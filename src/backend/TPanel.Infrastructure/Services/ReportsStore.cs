using System.Data;
using Dapper;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.Reports;

namespace TPanel.Infrastructure.Services;

/// <summary>Analitik rapor veri erişimi (Dapper) — 6 rapor controller'ının birebir karşılığı.</summary>
public class ReportsStore : IReportsStore
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public ReportsStore(IDbConnectionFactory factory, IClock clock) { _factory = factory; _clock = clock; }

    private static string Start(string d) => d + " 00:00:00";
    private static string End(string d) => d + " 23:59:59";

    private static async Task<List<int>?> ResolveMerchantIds(IDbConnection c, string? param)
    {
        if (string.IsNullOrEmpty(param)) return null;
        if (param.StartsWith("g_")) return (await c.QueryAsync<int>("SELECT id FROM merchantUser WHERE group_id=@g", new { g = int.Parse(param[2..]) })).ToList();
        return int.TryParse(param, out var id) ? new List<int> { id } : null;
    }

    private static List<string> DateList(string from, string to)
    {
        var list = new List<string>();
        for (var d = DateTime.Parse(from); d <= DateTime.Parse(to); d = d.AddDays(1)) list.Add(d.ToString("yyyy-MM-dd"));
        return list;
    }

    private static double CompVal(object expando, string key)
        => ((IDictionary<string, object>)expando).TryGetValue(key, out var v) ? (double)v : 0.0;

    private static string Dur(double seconds)
    {
        var s = (int)Math.Round(seconds);
        if (s < 60) return s + " sn";
        if (s < 3600) return s / 60 + " dk " + s % 60 + " sn";
        return s / 3600 + " sa " + s % 3600 / 60 + " dk";
    }

    // ===================== MERCHANT REPORTS =====================
    public async Task<object> MerchantFilterOptionsAsync(CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var merchants = (await c.QueryAsync("SELECT id, name, group_id FROM merchantUser ORDER BY name")).ToList();
        var groups = (await c.QueryAsync("SELECT id, name FROM merchant_groups WHERE status=1")).ToDictionary(g => (uint)g.id, g => (string)g.name);
        var options = new List<object>(); var processed = new HashSet<uint>();
        foreach (var m in merchants)
        {
            uint? gid = m.group_id is null ? null : (uint)m.group_id;
            if (gid is not null && groups.ContainsKey(gid.Value)) { if (!processed.Add(gid.Value)) continue; options.Add(new { id = "g_" + gid.Value, name = groups[gid.Value] }); }
            else options.Add(new { id = ((int)m.id).ToString(), name = (string)m.name });
        }
        return options;
    }

    private async Task<Dictionary<int, string>> DisplayMap(IDbConnection c)
    {
        var merchants = (await c.QueryAsync("SELECT id, name, group_id FROM merchantUser WHERE status='1'")).ToList();
        var groups = (await c.QueryAsync("SELECT id, name FROM merchant_groups WHERE status=1")).ToDictionary(g => (uint)g.id, g => (string)g.name);
        var map = new Dictionary<int, string>();
        foreach (var m in merchants) { uint? gid = m.group_id is null ? null : (uint)m.group_id; map[(int)m.id] = gid is not null && groups.ContainsKey(gid.Value) ? groups[gid.Value] : (string)m.name; }
        return map;
    }

    private static object FormatSeries(Dictionary<string, Dictionary<string, double>> grouped, List<string> cats)
        => grouped.Select(kv => new { name = kv.Key, data = cats.Select(d => kv.Value.GetValueOrDefault(d, 0)).ToList() }).ToList();

    public async Task<object> VolumePerformanceAsync(string from, string to, string? merchantParam, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var ids = await ResolveMerchantIds(c, merchantParam);
        var names = await DisplayMap(c);
        var p = new DynamicParameters(); p.Add("f", Start(from)); p.Add("t", End(to)); var idW = ids is null ? "" : " AND firm_id IN @ids"; if (ids is not null) p.Add("ids", ids);

        var daily = (await c.QueryAsync($@"SELECT DATE_FORMAT(finalize_date,'%Y-%m-%d') date, firm_id, type, SUM(amount) total_amount
            FROM invest WHERE status='3' AND finalize_date BETWEEN @f AND @t{idW} GROUP BY date, firm_id, type ORDER BY date", p)).ToList();
        var dates = daily.Select(r => (string)r.date).Distinct().OrderBy(x => x).ToList();
        var depSeries = new Dictionary<string, Dictionary<string, double>>(); var wdSeries = new Dictionary<string, Dictionary<string, double>>();
        foreach (var r in daily)
        {
            var label = names.GetValueOrDefault((int)r.firm_id, "Merchant #" + (int)r.firm_id);
            var target = (string)r.type == "1" ? depSeries : wdSeries;
            if (!target.TryGetValue(label, out var dm)) { dm = new(); target[label] = dm; }
            dm[(string)r.date] = dm.GetValueOrDefault((string)r.date, 0) + Convert.ToDouble(r.total_amount);
        }

        var statusCounts = (await c.QueryAsync($@"SELECT DATE_FORMAT(created_at,'%Y-%m-%d') date, status, COUNT(*) cnt
            FROM invest WHERE status IN ('3','4') AND created_at BETWEEN @f AND @t{idW} GROUP BY date, status ORDER BY date", p)).ToList();
        var rateDates = statusCounts.Select(r => (string)r.date).Distinct().OrderBy(x => x).ToList();
        var appMap = new Dictionary<string, long>(); var rejMap = new Dictionary<string, long>();
        foreach (var r in statusCounts) { var m = (string)r.status == "3" ? appMap : rejMap; m[(string)r.date] = m.GetValueOrDefault((string)r.date, 0) + (long)r.cnt; }
        var appRates = new List<double>(); var rejRates = new List<double>();
        foreach (var d in rateDates) { var a = appMap.GetValueOrDefault(d, 0); var r = rejMap.GetValueOrDefault(d, 0); var tot = a + r; appRates.Add(tot > 0 ? Math.Round((double)a / tot * 100, 2) : 0); rejRates.Add(tot > 0 ? Math.Round((double)r / tot * 100, 2) : 0); }

        var avgProc = (await c.QueryAsync($@"SELECT DATE_FORMAT(finalize_date,'%Y-%m-%d') date, AVG(TIMESTAMPDIFF(SECOND,process_date,finalize_date)) avg_seconds
            FROM invest WHERE status='3' AND process_date IS NOT NULL AND finalize_date IS NOT NULL AND finalize_date BETWEEN @f AND @t{idW} GROUP BY date ORDER BY date", p)).ToList();

        var avgAmt = (await c.QueryAsync($@"SELECT DATE_FORMAT(finalize_date,'%Y-%m-%d') date, type, AVG(amount) avg_amount
            FROM invest WHERE status='3' AND finalize_date BETWEEN @f AND @t{idW} GROUP BY date, type ORDER BY date", p)).ToList();
        var avgDates = avgAmt.Select(r => (string)r.date).Distinct().OrderBy(x => x).ToList();
        var avgDep = new Dictionary<string, double>(); var avgWd = new Dictionary<string, double>();
        foreach (var r in avgAmt) { if ((string)r.type == "1") avgDep[(string)r.date] = Math.Round(Convert.ToDouble(r.avg_amount), 2); else avgWd[(string)r.date] = Math.Round(Convert.ToDouble(r.avg_amount), 2); }

        return new
        {
            daily_volume = new { categories = dates, deposit_series = FormatSeries(depSeries, dates), withdrawal_series = FormatSeries(wdSeries, dates) },
            approval_rates = new { categories = rateDates, series = new object[] { new { name = "Onay Oranı (%)", data = appRates }, new { name = "Red Oranı (%)", data = rejRates } } },
            avg_processing_time = new { categories = avgProc.Select(r => (string)r.date).ToList(), series = new object[] { new { name = "Ort. İşlem Süresi (dk)", data = avgProc.Select(r => Math.Round((r.avg_seconds is null ? 0 : Convert.ToDouble(r.avg_seconds)) / 60, 2)).ToList() } } },
            avg_transaction_amount = new { categories = avgDates, series = new object[] { new { name = "Ort. Yatırım", data = avgDates.Select(d => avgDep.GetValueOrDefault(d, 0)).ToList() }, new { name = "Ort. Çekim", data = avgDates.Select(d => avgWd.GetValueOrDefault(d, 0)).ToList() } } },
        };
    }

    public async Task<object> PlayerAnalysisAsync(string from, string to, string? merchantParam, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var ids = await ResolveMerchantIds(c, merchantParam);
        var p = new DynamicParameters(); p.Add("f", Start(from)); p.Add("t", End(to)); var idW = ids is null ? "" : " AND firm_id IN @ids"; if (ids is not null) p.Add("ids", ids);

        var active = (await c.QueryAsync($@"SELECT DATE_FORMAT(finalize_date,'%Y-%m-%d') date, COUNT(DISTINCT player_id) player_count
            FROM invest WHERE status='3' AND finalize_date BETWEEN @f AND @t{idW} GROUP BY date ORDER BY date", p)).ToList();
        p.Add("fd", from); p.Add("td", to);
        var newPer = (await c.QueryAsync($@"SELECT first_date date, COUNT(*) new_count FROM (
            SELECT player_id, MIN(DATE(finalize_date)) first_date FROM invest WHERE status='3' AND finalize_date BETWEEN @f AND @t{idW} GROUP BY player_id) ft
            WHERE first_date BETWEEN @fd AND @td GROUP BY first_date", p))
            .ToDictionary(r => ((DateTime)r.date).ToString("yyyy-MM-dd"), r => (long)r.new_count);

        var pDates = active.Select(r => (string)r.date).ToList();
        var activeData = active.Select(r => (long)r.player_count).ToList();
        var newData = new List<long>(); var retData = new List<long>();
        foreach (var r in active) { var nc = newPer.GetValueOrDefault((string)r.date, 0); newData.Add(nc); retData.Add(Math.Max(0, (long)r.player_count - nc)); }

        var avgTx = (await c.QueryAsync($@"SELECT DATE_FORMAT(finalize_date,'%Y-%m-%d') date, COUNT(*)/COUNT(DISTINCT player_id) avg_tx
            FROM invest WHERE status='3' AND finalize_date BETWEEN @f AND @t{idW} GROUP BY date ORDER BY date", p)).ToList();
        var top = (await c.QueryAsync($@"SELECT player_id, COUNT(*) tx_count, SUM(amount) total_amount
            FROM invest WHERE status='3' AND finalize_date BETWEEN @f AND @t{idW} GROUP BY player_id ORDER BY tx_count DESC LIMIT 20", p)).ToList();

        return new
        {
            active_player_trend = new { categories = pDates, series = new object[] { new { name = "Aktif Oyuncu", data = activeData } } },
            new_vs_returning = new { categories = pDates, series = new object[] { new { name = "Yeni Oyuncu", data = newData }, new { name = "Dönen Oyuncu", data = retData } } },
            avg_tx_per_player = new { categories = avgTx.Select(r => (string)r.date).ToList(), series = new object[] { new { name = "Ort. İşlem/Oyuncu", data = avgTx.Select(r => Math.Round(Convert.ToDouble(r.avg_tx), 2)).ToList() } } },
            top_players = top.Select(r => new { player_id = (string?)r.player_id, tx_count = (long)r.tx_count, total_amount = Convert.ToDouble(r.total_amount) }).ToList(),
        };
    }

    public async Task<object> AmountAnalysisAsync(string from, string to, string? merchantParam, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var ids = await ResolveMerchantIds(c, merchantParam);
        var p = new DynamicParameters(); p.Add("f", Start(from)); p.Add("t", End(to)); var idW = ids is null ? "" : " AND firm_id IN @ids"; if (ids is not null) p.Add("ids", ids);
        const string caseExpr = "CASE WHEN amount>=0 AND amount<500 THEN '0-500' WHEN amount>=500 AND amount<1000 THEN '500-1K' WHEN amount>=1000 AND amount<2500 THEN '1K-2.5K' WHEN amount>=2500 AND amount<5000 THEN '2.5K-5K' WHEN amount>=5000 AND amount<10000 THEN '5K-10K' WHEN amount>=10000 THEN '10K+' END";
        var labels = new[] { "0-500", "500-1K", "1K-2.5K", "2.5K-5K", "5K-10K", "10K+" };

        var dist = (await c.QueryAsync($@"SELECT type, ({caseExpr}) bucket, COUNT(*) cnt, SUM(amount) total_amount
            FROM invest WHERE status='3' AND finalize_date BETWEEN @f AND @t{idW} GROUP BY type, bucket", p)).ToList();
        var depCnt = labels.ToDictionary(l => l, _ => 0L); var wdCnt = labels.ToDictionary(l => l, _ => 0L);
        var depAmt = labels.ToDictionary(l => l, _ => 0.0); var wdAmt = labels.ToDictionary(l => l, _ => 0.0);
        foreach (var r in dist) { if (r.bucket is null) continue; var b = (string)r.bucket; if ((string)r.type == "1") { depCnt[b] = (long)r.cnt; depAmt[b] = Convert.ToDouble(r.total_amount); } else { wdCnt[b] = (long)r.cnt; wdAmt[b] = Convert.ToDouble(r.total_amount); } }

        var hourly = (await c.QueryAsync($@"SELECT HOUR(created_at) hour, type, COUNT(*) cnt FROM invest WHERE status='3' AND finalize_date BETWEEN @f AND @t{idW} GROUP BY hour, type", p)).ToList();
        var hDep = new long[24]; var hWd = new long[24];
        foreach (var r in hourly) { if ((string)r.type == "1") hDep[(int)r.hour] = (long)r.cnt; else hWd[(int)r.hour] = (long)r.cnt; }

        var minmax = (await c.QueryAsync($@"SELECT DATE_FORMAT(finalize_date,'%Y-%m-%d') date, type, MIN(amount) min_amount, MAX(amount) max_amount FROM invest WHERE status='3' AND finalize_date BETWEEN @f AND @t{idW} GROUP BY date, type ORDER BY date", p)).ToList();
        var mmDates = minmax.Select(r => (string)r.date).Distinct().OrderBy(x => x).ToList();
        var dMin = new Dictionary<string, double>(); var dMax = new Dictionary<string, double>(); var wMin = new Dictionary<string, double>(); var wMax = new Dictionary<string, double>();
        foreach (var r in minmax) { if ((string)r.type == "1") { dMin[(string)r.date] = Convert.ToDouble(r.min_amount); dMax[(string)r.date] = Convert.ToDouble(r.max_amount); } else { wMin[(string)r.date] = Convert.ToDouble(r.min_amount); wMax[(string)r.date] = Convert.ToDouble(r.max_amount); } }

        return new
        {
            amount_distribution = new { categories = labels, series = new object[] { new { name = "Yatırım Adet", data = labels.Select(l => depCnt[l]).ToList() }, new { name = "Çekim Adet", data = labels.Select(l => wdCnt[l]).ToList() } }, amount_series = new object[] { new { name = "Yatırım Tutar", data = labels.Select(l => depAmt[l]).ToList() }, new { name = "Çekim Tutar", data = labels.Select(l => wdAmt[l]).ToList() } } },
            hourly_density = new { categories = Enumerable.Range(0, 24).Select(h => h.ToString("D2") + ":00").ToList(), series = new object[] { new { name = "Yatırım", data = hDep }, new { name = "Çekim", data = hWd } } },
            daily_min_max = new { categories = mmDates, series = new object[] { new { name = "Yatırım Min", data = mmDates.Select(d => dMin.GetValueOrDefault(d, 0)).ToList() }, new { name = "Yatırım Max", data = mmDates.Select(d => dMax.GetValueOrDefault(d, 0)).ToList() }, new { name = "Çekim Min", data = mmDates.Select(d => wMin.GetValueOrDefault(d, 0)).ToList() }, new { name = "Çekim Max", data = mmDates.Select(d => wMax.GetValueOrDefault(d, 0)).ToList() } } },
        };
    }

    public async Task<object> FinancialReportAsync(string from, string to, string? merchantParam, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var ids = await ResolveMerchantIds(c, merchantParam);
        var mW = ids is null ? "" : " AND id IN @ids";
        var merchants = (await c.QueryAsync($"SELECT id, name, commission, withdrawCommission, group_id FROM merchantUser WHERE 1=1{mW}", new { ids })).ToDictionary(m => (int)m.id, m => m);
        var mIds = merchants.Keys.ToList();
        var map = await DisplayMap(c);
        var p = new DynamicParameters(); p.Add("f", Start(from)); p.Add("t", End(to)); var idW = mIds.Count > 0 ? " AND firm_id IN @ids" : ""; if (mIds.Count > 0) p.Add("ids", mIds);

        var daily = (await c.QueryAsync($@"SELECT DATE_FORMAT(finalize_date,'%Y-%m-%d') date, firm_id, type, SUM(amount) total_amount FROM invest WHERE status='3' AND finalize_date BETWEEN @f AND @t{idW} GROUP BY date, firm_id, type ORDER BY date", p)).ToList();
        var dates = daily.Select(r => (string)r.date).Distinct().OrderBy(x => x).ToList();
        var commSeries = new Dictionary<string, Dictionary<string, double>>(); var netSeries = new Dictionary<string, Dictionary<string, double>>();
        foreach (var r in daily)
        {
            int fid = (int)r.firm_id; var m = merchants.GetValueOrDefault(fid);
            var label = map.GetValueOrDefault(fid, m is null ? "Merchant #" + fid : (string)m.name);
            double rate = m is null ? 0 : (double)((string)r.type == "1" ? Convert.ToDouble(m.commission) : Convert.ToDouble(m.withdrawCommission));
            var comm = Convert.ToDouble(r.total_amount) * rate / 100;
            if (!commSeries.TryGetValue(label, out var cm)) { cm = new(); commSeries[label] = cm; }
            cm[(string)r.date] = cm.GetValueOrDefault((string)r.date, 0) + comm;
            var sign = (string)r.type == "1" ? 1 : -1;
            if (!netSeries.TryGetValue(label, out var nm)) { nm = new(); netSeries[label] = nm; }
            nm[(string)r.date] = nm.GetValueOrDefault((string)r.date, 0) + sign * Convert.ToDouble(r.total_amount);
        }
        object Build(Dictionary<string, Dictionary<string, double>> g) => g.Select(kv => new { name = kv.Key, data = dates.Select(d => Math.Round(kv.Value.GetValueOrDefault(d, 0), 2)).ToList() }).ToList();

        var comp = (await c.QueryAsync($@"SELECT firm_id, type, SUM(amount) total_amount, COUNT(*) tx_count FROM invest WHERE status='3' AND finalize_date BETWEEN @f AND @t{idW} GROUP BY firm_id, type", p)).ToList();
        var compData = new Dictionary<string, dynamic>();
        foreach (var r in comp)
        {
            var label = map.GetValueOrDefault((int)r.firm_id, "Merchant #" + (int)r.firm_id);
            if (!compData.ContainsKey(label)) compData[label] = new System.Dynamic.ExpandoObject();
            var e = (IDictionary<string, object>)compData[label];
            e["merchant"] = label;
            e["deposit_amount"] = (e.TryGetValue("deposit_amount", out var da) ? (double)da : 0) + ((string)r.type == "1" ? Convert.ToDouble(r.total_amount) : 0);
            e["deposit_count"] = (e.TryGetValue("deposit_count", out var dc) ? (long)dc : 0L) + ((string)r.type == "1" ? (long)r.tx_count : 0L);
            e["withdrawal_amount"] = (e.TryGetValue("withdrawal_amount", out var wa) ? (double)wa : 0) + ((string)r.type == "2" ? Convert.ToDouble(r.total_amount) : 0);
            e["withdrawal_count"] = (e.TryGetValue("withdrawal_count", out var wc) ? (long)wc : 0L) + ((string)r.type == "2" ? (long)r.tx_count : 0L);
        }
        var compLabels = compData.Keys.ToList();

        return new
        {
            commission_trend = new { categories = dates, series = Build(commSeries) },
            net_case_trend = new { categories = dates, series = Build(netSeries) },
            merchant_comparison = new
            {
                categories = compLabels,
                series = new object[] {
                    new { name = "Yatırım", data = compLabels.Select(l => CompVal(compData[l], "deposit_amount")).ToList() },
                    new { name = "Çekim", data = compLabels.Select(l => CompVal(compData[l], "withdrawal_amount")).ToList() } },
                details = compData.Values.ToList(),
            },
        };
    }

    public async Task<object> RiskReportAsync(string from, string to, string? merchantParam, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var ids = await ResolveMerchantIds(c, merchantParam);
        var p = new DynamicParameters(); p.Add("f", Start(from)); p.Add("t", End(to)); var idW = ids is null ? "" : " AND firm_id IN @ids"; if (ids is not null) p.Add("ids", ids);

        var low = (await c.QueryAsync($@"SELECT DATE_FORMAT(created_at,'%Y-%m-%d') date, type, COUNT(*) total_cnt, SUM(CASE WHEN amount<999 THEN 1 ELSE 0 END) low_cnt FROM invest WHERE status='3' AND created_at BETWEEN @f AND @t{idW} GROUP BY date, type ORDER BY date", p)).ToList();
        var lowDates = low.Select(r => (string)r.date).Distinct().OrderBy(x => x).ToList();
        var dT = new Dictionary<string, long>(); var dL = new Dictionary<string, long>(); var wT = new Dictionary<string, long>(); var wL = new Dictionary<string, long>();
        foreach (var r in low) { if ((string)r.type == "1") { dT[(string)r.date] = (long)r.total_cnt; dL[(string)r.date] = (long)r.low_cnt; } else { wT[(string)r.date] = (long)r.total_cnt; wL[(string)r.date] = (long)r.low_cnt; } }
        var lowDep = lowDates.Select(d => { var t = dT.GetValueOrDefault(d, 0); return t > 0 ? Math.Round((double)dL.GetValueOrDefault(d, 0) / t * 100, 2) : 0; }).ToList();
        var lowWd = lowDates.Select(d => { var t = wT.GetValueOrDefault(d, 0); return t > 0 ? Math.Round((double)wL.GetValueOrDefault(d, 0) / t * 100, 2) : 0; }).ToList();

        var rej = (await c.QueryAsync($@"SELECT DATE_FORMAT(created_at,'%Y-%m-%d') date, type, COUNT(*) cnt FROM invest WHERE status='4' AND created_at BETWEEN @f AND @t{idW} GROUP BY date, type ORDER BY date", p)).ToList();
        var rejDates = rej.Select(r => (string)r.date).Distinct().OrderBy(x => x).ToList();
        var rD = new Dictionary<string, long>(); var rW = new Dictionary<string, long>();
        foreach (var r in rej) { if ((string)r.type == "1") rD[(string)r.date] = (long)r.cnt; else rW[(string)r.date] = (long)r.cnt; }
        var rejDep = rejDates.Select(d => rD.GetValueOrDefault(d, 0)).ToList(); var rejWd = rejDates.Select(d => rW.GetValueOrDefault(d, 0)).ToList();

        string Trend(List<long> data) { if (data.Count < 2) return "stable"; var half = (int)Math.Ceiling(data.Count / 2.0); var f1 = data.Take(half).ToList(); var f2 = data.Skip(half).ToList(); var a1 = f1.Count > 0 ? f1.Average() : 0; var a2 = f2.Count > 0 ? f2.Average() : 0; if (a2 > a1 * 1.1) return "increasing"; if (a2 < a1 * 0.9) return "decreasing"; return "stable"; }

        return new
        {
            low_amount_ratio = new { categories = lowDates, series = new object[] { new { name = "Yatırım Düşük Tutar Oranı (%)", data = lowDep }, new { name = "Çekim Düşük Tutar Oranı (%)", data = lowWd } } },
            rejected_trend = new { categories = rejDates, series = new object[] { new { name = "Reddedilen Yatırım", data = rejDep }, new { name = "Reddedilen Çekim", data = rejWd } }, deposit_trend_direction = Trend(rejDep), withdrawal_trend_direction = Trend(rejWd) },
        };
    }

    // ===================== TEAM REPORTS =====================
    public async Task<object> TeamFilterOptionsAsync(CancellationToken ct = default)
    { using var c = await _factory.CreateOpenConnectionAsync(ct); return await c.QueryAsync("SELECT id, name FROM teams WHERE status<>0 ORDER BY name"); }

    public async Task<object> TeamOverviewAsync(string from, string to, IReadOnlyList<int>? teamIds, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var tW = teamIds is null ? "" : " AND id IN @tids";
        var teams = (await c.QueryAsync($"SELECT id, name FROM teams WHERE status<>0{tW} ORDER BY name", new { tids = teamIds })).ToList();
        var p = new DynamicParameters(); p.Add("f", Start(from)); p.Add("t", End(to)); var aW = teamIds is null ? "" : " AND team_id IN @tids"; if (teamIds is not null) p.Add("tids", teamIds);
        var agg = (await c.QueryAsync($@"SELECT team_id, type, status, COUNT(*) cnt, SUM(amount) total_amount,
            AVG(CASE WHEN process_date IS NOT NULL AND finalize_date IS NOT NULL THEN TIMESTAMPDIFF(SECOND,process_date,finalize_date) END) avg_seconds
            FROM invest WHERE status IN ('3','4') AND created_at BETWEEN @f AND @t{aW} GROUP BY team_id, type, status", p)).ToList();

        var rows = teams.ToDictionary(t => (int)t.id, t => new Dictionary<string, object?>
        {
            ["team_id"] = (int)t.id, ["team_name"] = (string)t.name,
            ["deposit_approved"] = 0L, ["deposit_rejected"] = 0L, ["deposit_volume"] = 0.0, ["deposit_avg_seconds"] = (double?)null,
            ["withdraw_approved"] = 0L, ["withdraw_rejected"] = 0L, ["withdraw_volume"] = 0.0, ["withdraw_avg_seconds"] = (double?)null,
        });
        foreach (var r in agg)
        {
            if (r.team_id is null || !rows.TryGetValue((int)r.team_id, out var row)) continue;
            var key = (string)r.type == "1" ? "deposit" : "withdraw";
            if ((string)r.status == "3") { row[key + "_approved"] = (long)r.cnt; row[key + "_volume"] = Convert.ToDouble(r.total_amount); if (r.avg_seconds is not null) row[key + "_avg_seconds"] = Math.Round(Convert.ToDouble(r.avg_seconds), 1); }
            else row[key + "_rejected"] = (long)r.cnt;
        }
        foreach (var row in rows.Values)
        {
            long dT = (long)row["deposit_approved"]! + (long)row["deposit_rejected"]!; long wT = (long)row["withdraw_approved"]! + (long)row["withdraw_rejected"]!;
            row["deposit_success_rate"] = dT > 0 ? Math.Round((long)row["deposit_approved"]! * 100.0 / dT, 2) : 0;
            row["withdraw_success_rate"] = wT > 0 ? Math.Round((long)row["withdraw_approved"]! * 100.0 / wT, 2) : 0;
            row["net_volume"] = Math.Round((double)row["deposit_volume"]! - (double)row["withdraw_volume"]!, 2);
        }
        return new { rows = rows.Values.ToList() };
    }

    public async Task<object> TeamTrendsAsync(string from, string to, IReadOnlyList<int>? teamIds, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var tW = teamIds is null ? "" : " AND id IN @tids";
        var teams = (await c.QueryAsync($"SELECT id, name FROM teams WHERE status<>0{tW} ORDER BY name", new { tids = teamIds })).ToList();
        var p = new DynamicParameters(); p.Add("f", Start(from)); p.Add("t", End(to)); var aW = teamIds is null ? "" : " AND team_id IN @tids"; if (teamIds is not null) p.Add("tids", teamIds);
        var daily = (await c.QueryAsync($@"SELECT team_id, DATE_FORMAT(created_at,'%Y-%m-%d') date, status, type, COUNT(*) cnt, SUM(amount) total_amount,
            AVG(CASE WHEN process_date IS NOT NULL AND finalize_date IS NOT NULL THEN TIMESTAMPDIFF(SECOND,process_date,finalize_date) END) avg_seconds
            FROM invest WHERE status IN ('3','4') AND created_at BETWEEN @f AND @t{aW} GROUP BY team_id, date, status, type ORDER BY date", p)).ToList();
        var dates = DateList(from, to);

        var success = new List<object>(); var avgTime = new List<object>(); var volume = new List<object>();
        foreach (var t in teams)
        {
            int tid = (int)t.id;
            var day = dates.ToDictionary(d => d, _ => new double[6]); // dApp,dRej,dVol,wApp,wRej,secSum, but track secCnt separately
            var secSum = dates.ToDictionary(d => d, _ => 0.0); var secCnt = dates.ToDictionary(d => d, _ => 0L);
            var dApp = dates.ToDictionary(d => d, _ => 0L); var dRej = dates.ToDictionary(d => d, _ => 0L); var dVol = dates.ToDictionary(d => d, _ => 0.0);
            var wApp = dates.ToDictionary(d => d, _ => 0L); var wRej = dates.ToDictionary(d => d, _ => 0L);
            foreach (var r in daily.Where(x => x.team_id is not null && (int)x.team_id == tid))
            {
                var d = (string)r.date; if (!dApp.ContainsKey(d)) continue;
                var isDep = (string)r.type == "1"; var isApp = (string)r.status == "3";
                if (isApp) { if (isDep) { dApp[d] += (long)r.cnt; dVol[d] += Convert.ToDouble(r.total_amount); } else { wApp[d] += (long)r.cnt; } if (r.avg_seconds is not null) { secSum[d] += Convert.ToDouble(r.avg_seconds) * (long)r.cnt; secCnt[d] += (long)r.cnt; } }
                else { if (isDep) dRej[d] += (long)r.cnt; else wRej[d] += (long)r.cnt; }
            }
            var sData = new List<double?>(); var tData = new List<double?>(); var vData = new List<double>();
            foreach (var d in dates) { var tot = dApp[d] + dRej[d] + wApp[d] + wRej[d]; var app = dApp[d] + wApp[d]; sData.Add(tot > 0 ? Math.Round((double)app / tot * 100, 2) : null); tData.Add(secCnt[d] > 0 ? Math.Round(secSum[d] / secCnt[d] / 60, 2) : null); vData.Add(Math.Round(dVol[d], 2)); }
            success.Add(new { name = (string)t.name, data = sData }); avgTime.Add(new { name = (string)t.name, data = tData }); volume.Add(new { name = (string)t.name, data = vData });
        }
        return new { categories = dates, success_rate = success, avg_time_min = avgTime, deposit_volume = volume };
    }

    public async Task<object> TeamHourlyAsync(string from, string to, IReadOnlyList<int>? teamIds, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var p = new DynamicParameters(); p.Add("f", Start(from)); p.Add("t", End(to)); var aW = teamIds is null ? "" : " AND team_id IN @tids"; if (teamIds is not null) p.Add("tids", teamIds);
        var rows = (await c.QueryAsync($"SELECT team_id, HOUR(finalize_date) hour, COUNT(*) cnt FROM invest WHERE status='3' AND finalize_date IS NOT NULL AND finalize_date BETWEEN @f AND @t{aW} GROUP BY team_id, hour", p)).ToList();
        var tW = teamIds is null ? "" : " AND id IN @tids";
        var teams = (await c.QueryAsync($"SELECT id, name FROM teams WHERE status<>0{tW} ORDER BY name", new { tids = teamIds })).ToList();
        var series = teams.Select(t => { var data = new long[24]; foreach (var r in rows.Where(x => (int)x.team_id == (int)t.id)) data[(int)r.hour] = (long)r.cnt; return (object)new { name = (string)t.name, data }; }).ToList();
        return new { categories = Enumerable.Range(0, 24).Select(h => h.ToString("D2") + ":00").ToList(), series };
    }

    // ===================== OPERATIONS =====================
    public async Task<object> QueueAnalysisAsync(string? merchantParam, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var ids = await ResolveMerchantIds(c, merchantParam);
        string MW(string col) => ids is null ? "" : $" AND {col} IN @ids";
        var statusLabels = new Dictionary<string, string> { ["0"] = "Beklemede", ["1"] = "İşlemde", ["2"] = "İşleniyor" };
        var typeLabels = new Dictionary<string, string> { ["1"] = "Yatırım", ["2"] = "Çekim" };
        var now = _clock.Now;

        var queue = (await c.QueryAsync($"SELECT status, type, COUNT(*) count, COALESCE(SUM(amount),0) total_amount FROM invest WHERE status IN ('0','1','2'){MW("firm_id")} GROUP BY status, type", new { ids }))
            .Select(r => (object)new { status = (string)r.status, status_label = statusLabels.GetValueOrDefault((string)r.status, (string)r.status), type = (string)r.type, type_label = typeLabels.GetValueOrDefault((string)r.type, (string)r.type), count = (long)r.count, total_amount = Convert.ToDouble(r.total_amount) }).ToList();

        var ageBuckets = new (string, int, int?)[] { ("0-5 dk", 0, 300), ("5-10 dk", 300, 600), ("10-30 dk", 600, 1800), ("30 dk-1 sa", 1800, 3600), ("1 sa+", 3600, null) };
        var ageDist = new List<object>();
        foreach (var (label, min, max) in ageBuckets)
        {
            var pp = new DynamicParameters(); pp.Add("ids", ids); pp.Add("minT", now.AddSeconds(-min));
            var clause = max is null ? "" : " AND created_at > @maxT"; if (max is not null) pp.Add("maxT", now.AddSeconds(-max.Value));
            var r = await c.QueryFirstOrDefaultAsync($"SELECT COUNT(*) count, COALESCE(SUM(amount),0) total_amount FROM invest WHERE status IN ('0','1','2') AND created_at <= @minT{clause}{MW("firm_id")}", pp);
            ageDist.Add(new { label, count = (long)r.count, total_amount = Convert.ToDouble(r.total_amount) });
        }

        var avgWait = (await c.QueryAsync($@"SELECT invest.team_id, teams.name team_name, COUNT(*) count, AVG(TIMESTAMPDIFF(SECOND,invest.created_at,NOW())) avg_wait
            FROM invest LEFT JOIN teams ON invest.team_id=teams.id WHERE invest.status IN ('0','1','2'){MW("invest.firm_id")} GROUP BY invest.team_id, teams.name", new { ids }))
            .Select(r => (object)new { team_id = (int?)r.team_id, team_name = (string?)r.team_name ?? "Atanmamış", count = (long)r.count, avg_wait_seconds = Math.Round(r.avg_wait is null ? 0 : Convert.ToDouble(r.avg_wait)), avg_wait_label = Dur(r.avg_wait is null ? 0 : Convert.ToDouble(r.avg_wait)) }).ToList();

        var oldest = (await c.QueryAsync($@"SELECT invest.id, invest.type, invest.status, invest.amount, invest.name, invest.created_at, invest.firm_id, invest.team_id, teams.name team_name, TIMESTAMPDIFF(SECOND,invest.created_at,NOW()) wait_seconds
            FROM invest LEFT JOIN teams ON invest.team_id=teams.id WHERE invest.status IN ('0','1','2'){MW("invest.firm_id")} ORDER BY invest.created_at ASC LIMIT 20", new { ids }))
            .Select(r => (object)new { id = (int)r.id, type = (string)r.type, type_label = typeLabels.GetValueOrDefault((string)r.type, (string)r.type), status = (string)r.status, status_label = statusLabels.GetValueOrDefault((string)r.status, (string)r.status), amount = Convert.ToDouble(r.amount), name = (string?)r.name, firm_id = (int)r.firm_id, team_id = (int?)r.team_id, team_name = (string?)r.team_name ?? "Atanmamış", created_at = r.created_at, wait_seconds = (long)r.wait_seconds, wait_label = Dur((long)r.wait_seconds) }).ToList();

        var todayStart = _clock.Today; var todayEnd = _clock.Today.AddDays(1).AddSeconds(-1);
        var entering = (await c.QueryAsync($"SELECT HOUR(created_at) hour, COUNT(*) count FROM invest WHERE created_at BETWEEN @s AND @e{MW("firm_id")} GROUP BY HOUR(created_at)", new { s = todayStart, e = todayEnd, ids })).ToDictionary(r => (int)r.hour, r => (long)r.count);
        var processed = (await c.QueryAsync($"SELECT HOUR(finalize_date) hour, COUNT(*) count FROM invest WHERE status IN ('3','4') AND finalize_date BETWEEN @s AND @e{MW("firm_id")} GROUP BY HOUR(finalize_date)", new { s = todayStart, e = todayEnd, ids })).ToDictionary(r => (int)r.hour, r => (long)r.count);
        var trend = Enumerable.Range(0, 24).Select(h => new { hour = h, entering = entering.GetValueOrDefault(h, 0), processed = processed.GetValueOrDefault(h, 0) }).ToList();

        return new
        {
            queue_status = queue, age_distribution = ageDist, avg_wait_by_team = avgWait, oldest_pending = oldest, queue_trend = trend,
            charts = new
            {
                trend = new { series = new object[] { new { name = "Gelen", data = trend.Select(t => t.entering).ToList() }, new { name = "İşlenen", data = trend.Select(t => t.processed).ToList() } }, categories = Enumerable.Range(0, 24).Select(h => h.ToString("D2") + ":00").ToList() },
                age_distribution = new { series = new object[] { new { name = "İşlem Sayısı", data = ageDist.Select(a => ((dynamic)a).count).ToList() } }, categories = ageBuckets.Select(b => b.Item1).ToList() },
            },
        };
    }

    public async Task<object> PeakHourAnalysisAsync(string from, string to, string? merchantParam, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var ids = await ResolveMerchantIds(c, merchantParam);
        var p = new DynamicParameters(); p.Add("f", Start(from)); p.Add("t", End(to)); var idW = ids is null ? "" : " AND firm_id IN @ids"; if (ids is not null) p.Add("ids", ids);
        var dayNames = new Dictionary<int, string> { [1] = "Pzt", [2] = "Sal", [3] = "Çar", [4] = "Per", [5] = "Cum", [6] = "Cmt", [7] = "Paz" };
        var mysqlToIso = new Dictionary<int, int> { [2] = 1, [3] = 2, [4] = 3, [5] = 4, [6] = 5, [7] = 6, [1] = 7 };

        var raw = (await c.QueryAsync($"SELECT DAYOFWEEK(created_at) dow, HOUR(created_at) hour, COUNT(*) count FROM invest WHERE status IN ('3','4') AND created_at BETWEEN @f AND @t{idW} GROUP BY dow, hour", p)).ToList();
        var matrix = new Dictionary<int, long[]>(); foreach (var iso in Enumerable.Range(1, 7)) matrix[iso] = new long[24];
        foreach (var r in raw) { if (mysqlToIso.TryGetValue((int)r.dow, out var iso)) matrix[iso][(int)r.hour] = (long)r.count; }

        var heatmap = dayNames.Select(kv => new { name = kv.Value, data = Enumerable.Range(0, 24).Select(h => new { x = h.ToString("D2") + ":00", y = matrix[kv.Key][h] }).ToList() }).ToList();
        var hourlyTotals = Enumerable.Range(0, 24).Select(h => Enumerable.Range(1, 7).Sum(iso => matrix[iso][h])).ToList();
        var dayTotals = dayNames.Select(kv => new { day = kv.Value, total = matrix[kv.Key].Sum() }).ToList();
        int peakHour = 0; long peakCount = 0; for (var h = 0; h < 24; h++) if (hourlyTotals[h] > peakCount) { peakHour = h; peakCount = hourlyTotals[h]; }
        string peakDay = ""; long peakDayCount = 0; foreach (var dt in dayTotals) if (dt.total > peakDayCount) { peakDay = dt.day; peakDayCount = dt.total; }

        var typeHourly = (await c.QueryAsync($"SELECT type, HOUR(created_at) hour, COUNT(*) count FROM invest WHERE status IN ('3','4') AND created_at BETWEEN @f AND @t{idW} GROUP BY type, hour", p)).ToList();
        var depH = new long[24]; var wdH = new long[24];
        foreach (var r in typeHourly) { if ((string)r.type == "1") depH[(int)r.hour] = (long)r.count; else if ((string)r.type == "2") wdH[(int)r.hour] = (long)r.count; }

        return new
        {
            heatmap, peak = new { hour = peakHour.ToString("D2") + ":00", hour_count = peakCount, day = peakDay, day_count = peakDayCount }, day_totals = dayTotals,
            charts = new
            {
                heatmap = new { series = heatmap },
                hourly_totals = new { series = new object[] { new { name = "İşlem Sayısı", data = hourlyTotals } }, categories = Enumerable.Range(0, 24).Select(h => h.ToString("D2") + ":00").ToList() },
                day_totals = new { series = new object[] { new { name = "İşlem Sayısı", data = dayTotals.Select(d => d.total).ToList() } }, categories = dayTotals.Select(d => d.day).ToList() },
                type_comparison = new { series = new object[] { new { name = "Yatırım", data = depH }, new { name = "Çekim", data = wdH } }, categories = Enumerable.Range(0, 24).Select(h => h.ToString("D2") + ":00").ToList() },
            },
        };
    }

    public async Task<object> SlaReportAsync(string from, string to, string? merchantParam, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var ids = await ResolveMerchantIds(c, merchantParam);
        const int sla = 600, outlier = 7200;
        var p = new DynamicParameters(); p.Add("f", Start(from)); p.Add("t", End(to)); var idW = ids is null ? "" : " AND firm_id IN @ids"; if (ids is not null) p.Add("ids", ids);
        string Base(string alias = "") { var pre = alias == "" ? "" : alias + "."; return $"{pre}status='3' AND {pre}finalize_date IS NOT NULL AND {pre}created_at BETWEEN @f AND @t AND TIMESTAMPDIFF(SECOND,{pre}created_at,{pre}finalize_date)<={outlier} AND TIMESTAMPDIFF(SECOND,{pre}created_at,{pre}finalize_date)>=0" + (ids is null ? "" : $" AND {pre}firm_id IN @ids"); }

        var overall = await c.QueryFirstOrDefaultAsync($"SELECT COUNT(*) total, SUM(CASE WHEN TIMESTAMPDIFF(SECOND,created_at,finalize_date)<={sla} THEN 1 ELSE 0 END) within_sla FROM invest WHERE {Base()}", p);
        long oTotal = overall?.total is null ? 0 : (long)overall.total, oWithin = overall?.within_sla is null ? 0 : (long)overall.within_sla;

        var byTeam = (await c.QueryAsync($@"SELECT invest.team_id, teams.name team_name, COUNT(*) total,
            SUM(CASE WHEN TIMESTAMPDIFF(SECOND,invest.created_at,invest.finalize_date)<={sla} THEN 1 ELSE 0 END) within_sla, AVG(TIMESTAMPDIFF(SECOND,invest.created_at,invest.finalize_date)) avg_duration
            FROM invest LEFT JOIN teams ON invest.team_id=teams.id WHERE {Base("invest")} GROUP BY invest.team_id, teams.name ORDER BY total DESC", p))
            .Select(r => { long tt = (long)r.total, ws = (long)r.within_sla; return (object)new { team_id = (int?)r.team_id, team_name = (string?)r.team_name ?? "Atanmamış", total = tt, within_sla = ws, breached = tt - ws, rate = tt > 0 ? Math.Round((double)ws / tt * 100, 2) : 0, avg_duration = Math.Round(Convert.ToDouble(r.avg_duration)), avg_label = Dur(Convert.ToDouble(r.avg_duration)) }; }).ToList();

        var byHourRaw = (await c.QueryAsync($"SELECT HOUR(created_at) hour, COUNT(*) total, SUM(CASE WHEN TIMESTAMPDIFF(SECOND,created_at,finalize_date)<={sla} THEN 1 ELSE 0 END) within_sla FROM invest WHERE {Base()} GROUP BY HOUR(created_at)", p)).ToDictionary(r => (int)r.hour, r => ((long)r.total, (long)r.within_sla));
        var byHour = Enumerable.Range(0, 24).Select(h => { var (tt, ws) = byHourRaw.GetValueOrDefault(h, (0L, 0L)); return new { hour = h, total = tt, within_sla = ws, rate = tt > 0 ? Math.Round((double)ws / tt * 100, 2) : 0 }; }).ToList();

        var daily = (await c.QueryAsync($"SELECT DATE_FORMAT(created_at,'%Y-%m-%d') date, COUNT(*) total, SUM(CASE WHEN TIMESTAMPDIFF(SECOND,created_at,finalize_date)<={sla} THEN 1 ELSE 0 END) within_sla FROM invest WHERE {Base()} GROUP BY date ORDER BY date", p))
            .Select(r => { long tt = (long)r.total, ws = (long)r.within_sla; return new { date = (string)r.date, total = tt, within_sla = ws, rate = tt > 0 ? Math.Round((double)ws / tt * 100, 2) : 0 }; }).ToList();

        var worst = (await c.QueryAsync($@"SELECT invest.agent_id, users.name agent_name, COUNT(*) total,
            SUM(CASE WHEN TIMESTAMPDIFF(SECOND,invest.created_at,invest.finalize_date)>{sla} THEN 1 ELSE 0 END) breach_count, AVG(TIMESTAMPDIFF(SECOND,invest.created_at,invest.finalize_date)) avg_duration
            FROM invest LEFT JOIN users ON invest.agent_id=users.id WHERE {Base("invest")} AND invest.agent_id IS NOT NULL GROUP BY invest.agent_id, users.name HAVING breach_count>0 ORDER BY breach_count DESC LIMIT 20", p))
            .Select(r => { long tt = (long)r.total, bc = (long)r.breach_count; return (object)new { agent_id = (int?)r.agent_id, agent_name = (string?)r.agent_name ?? "Bilinmiyor", total = tt, breach_count = bc, avg_duration = Math.Round(Convert.ToDouble(r.avg_duration)), avg_label = Dur(Convert.ToDouble(r.avg_duration)), breach_rate = tt > 0 ? Math.Round((double)bc / tt * 100, 2) : 0 }; }).ToList();

        var breachRanges = new (string, int, int)[] { ("10-15 dk", 600, 900), ("15-30 dk", 900, 1800), ("30 dk-1 sa", 1800, 3600), ("1 sa+", 3600, outlier) };
        var breachCases = new List<object>();
        foreach (var (label, min, max) in breachRanges)
        {
            p.Add("bmin", min); p.Add("bmax", max);
            var cnt = await c.ExecuteScalarAsync<long>($"SELECT COUNT(*) FROM invest WHERE {Base()} AND TIMESTAMPDIFF(SECOND,created_at,finalize_date)>@bmin AND TIMESTAMPDIFF(SECOND,created_at,finalize_date)<=@bmax", p);
            breachCases.Add(new { label, count = cnt });
        }

        return new
        {
            overall = new { total = oTotal, within_sla = oWithin, breached = oTotal - oWithin, rate = oTotal > 0 ? Math.Round((double)oWithin / oTotal * 100, 2) : 0 },
            by_team = byTeam, by_hour = byHour, daily_trend = daily, worst_agents = worst, breach_details = breachCases,
            charts = new
            {
                hourly_sla = new { series = new object[] { new { name = "SLA %", data = byHour.Select(h => h.rate).ToList() } }, categories = Enumerable.Range(0, 24).Select(h => h.ToString("D2") + ":00").ToList() },
                daily_trend = new { series = new object[] { new { name = "SLA %", data = daily.Select(d => d.rate).ToList() } }, categories = daily.Select(d => d.date).ToList() },
                breach_distribution = new { series = new object[] { breachCases.Select(b => ((dynamic)b).count).ToList() }, labels = breachRanges.Select(b => b.Item1).ToList() },
            },
        };
    }

    // ===================== CONVERSION =====================
    public async Task<object> ConversionAsync(string from, string to, string type, IReadOnlyList<int>? userMerchantIds, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var fromDt = DateTime.Parse(Start(from)); var toDt = DateTime.Parse(End(to));
        var p = new DynamicParameters(); p.Add("f", fromDt); p.Add("t", toDt); p.Add("type", type);
        var scopeW = userMerchantIds is null ? "" : " AND firm_id IN @uids"; if (userMerchantIds is not null) p.Add("uids", userMerchantIds);

        var overall = await c.QueryFirstOrDefaultAsync($"SELECT SUM(status='3') approved, SUM(status='4') rejected FROM invest WHERE type=@type AND status IN ('3','4') AND created_at BETWEEN @f AND @t{scopeW}", p);
        long appr = overall?.approved is null ? 0 : (long)overall.approved, rej = overall?.rejected is null ? 0 : (long)overall.rejected; var total = appr + rej;
        var rate = total > 0 ? Math.Round((double)appr / total * 100, 2) : 0;

        var days = (toDt.Date - fromDt.Date).Days + 1;
        var prevTo = fromDt.AddSeconds(-1); var prevFrom = prevTo.AddDays(-days).AddSeconds(1);
        var pp = new DynamicParameters(); pp.Add("f", prevFrom); pp.Add("t", prevTo); pp.Add("type", type); if (userMerchantIds is not null) pp.Add("uids", userMerchantIds);
        var prev = await c.QueryFirstOrDefaultAsync($"SELECT SUM(status='3') approved, SUM(status='4') rejected FROM invest WHERE type=@type AND status IN ('3','4') AND created_at BETWEEN @f AND @t{scopeW}", pp);
        long pa = prev?.approved is null ? 0 : (long)prev.approved, pr = prev?.rejected is null ? 0 : (long)prev.rejected; var pt = pa + pr;
        var prevRate = pt > 0 ? Math.Round((double)pa / pt * 100, 2) : 0;

        var daily = (await c.QueryAsync($"SELECT DATE_FORMAT(created_at,'%Y-%m-%d') date, SUM(status='3') approved, SUM(status='4') rejected FROM invest WHERE type=@type AND status IN ('3','4') AND created_at BETWEEN @f AND @t{scopeW} GROUP BY date ORDER BY date", p))
            .ToDictionary(r => (string)r.date, r => ((long)r.approved, (long)r.rejected));
        var dates = DateList(from, to);
        var dRate = new List<double?>(); var dApp = new List<long>(); var dRej = new List<long>();
        foreach (var d in dates) { var (a, r) = daily.GetValueOrDefault(d, (0L, 0L)); var t = a + r; dRate.Add(t > 0 ? Math.Round((double)a / t * 100, 2) : null); dApp.Add(a); dRej.Add(r); }

        object byTeam = new List<object>();
        if (userMerchantIds is null)
        {
            byTeam = (await c.QueryAsync($"SELECT teams.id, teams.name, SUM(invest.status='3') approved, SUM(invest.status='4') rejected FROM invest JOIN teams ON invest.team_id=teams.id WHERE invest.type=@type AND invest.status IN ('3','4') AND invest.created_at BETWEEN @f AND @t GROUP BY teams.id, teams.name", p))
                .Select(r => { long a = (long)r.approved, j = (long)r.rejected; var t = a + j; return new { id = (int)r.id, name = (string)r.name, approved = a, rejected = j, total = t, rate = t > 0 ? Math.Round((double)a / t * 100, 2) : 0 }; }).OrderByDescending(x => x.total).ToList();
        }

        var map = await DisplayMap(c);
        var byMerchantRaw = (await c.QueryAsync($"SELECT firm_id, SUM(status='3') approved, SUM(status='4') rejected FROM invest WHERE type=@type AND status IN ('3','4') AND created_at BETWEEN @f AND @t{scopeW} GROUP BY firm_id", p)).ToList();
        var merged = new Dictionary<string, (long a, long r)>();
        foreach (var r in byMerchantRaw) { var name = map.GetValueOrDefault((int)r.firm_id, "Merchant #" + (int)r.firm_id); var cur = merged.GetValueOrDefault(name, (0, 0)); merged[name] = (cur.a + (long)r.approved, cur.r + (long)r.rejected); }
        var byMerchant = merged.Select(kv => { var t = kv.Value.a + kv.Value.r; return new { name = kv.Key, approved = kv.Value.a, rejected = kv.Value.r, total = t, rate = t > 0 ? Math.Round((double)kv.Value.a / t * 100, 2) : 0 }; }).OrderByDescending(x => x.total).ToList();

        return new
        {
            overall = new { approved = appr, rejected = rej, total, rate, prev_rate = prevRate, delta_pp = Math.Round(rate - prevRate, 2) },
            daily = new { categories = dates, rate = dRate, approved = dApp, rejected = dRej },
            by_team = byTeam, by_merchant = byMerchant,
        };
    }

    // ===================== PLAYER RISK =====================
    public async Task<object> SuspiciousPlayersAsync(string from, string to, string? merchantParam, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var ids = await ResolveMerchantIds(c, merchantParam);
        var p = new DynamicParameters(); p.Add("f", Start(from)); p.Add("t", End(to)); var idW = ids is null ? "" : " AND firm_id IN @ids"; if (ids is not null) p.Add("ids", ids);
        var txs = (await c.QueryAsync($"SELECT id, player_id, status, amount, created_at FROM invest WHERE type='1' AND created_at BETWEEN @f AND @t{idW}", p)).ToList();
        var grouped = txs.Where(t => t.player_id is not null).GroupBy(t => (string)t.player_id);

        var flagScores = new Dictionary<string, int> { ["low_amount"] = 15, ["same_amount"] = 20, ["night_activity"] = 15, ["high_rejection"] = 25, ["rapid_fire"] = 25 };
        var flagged = new List<dynamic>();
        foreach (var g in grouped)
        {
            var list = g.ToList(); var flags = new List<string>();
            var totalTx = list.Count; var approved = list.Count(x => (string)x.status == "3"); var rejected = list.Count(x => (string)x.status == "4");
            var totalAmount = list.Sum(x => Convert.ToDouble((object)x.amount)); var avgAmount = totalTx > 0 ? Math.Round(totalAmount / totalTx) : 0;
            var approvedTxs = list.Where(x => (string)x.status == "3").ToList();
            if (approvedTxs.Count >= 5 && approvedTxs.Max(x => Convert.ToDouble((object)x.amount)) < 1000) flags.Add("low_amount");
            if (list.Where(x => Convert.ToDouble((object)x.amount) < 1000).GroupBy(x => Convert.ToDouble((object)x.amount)).Any(grp => grp.Count() >= 3)) flags.Add("same_amount");
            var nightCount = list.Count(x => { var h = ((DateTime)x.created_at).Hour; return h >= 0 && h < 6; });
            if (totalTx > 0 && (double)nightCount / totalTx >= 0.5) flags.Add("night_activity");
            if (rejected >= 3 && approved == 0) flags.Add("high_rejection");
            if (totalTx >= 3) { var ts = list.Select(x => ((DateTimeOffset)(DateTime)x.created_at).ToUnixTimeSeconds()).OrderBy(x => x).ToList(); for (var i = 0; i <= ts.Count - 3; i++) if (ts[i + 2] - ts[i] <= 600) { flags.Add("rapid_fire"); break; } }
            if (flags.Count > 0)
            {
                var score = Math.Min(flags.Sum(f => flagScores.GetValueOrDefault(f, 0)), 100);
                flagged.Add(new { player_id = g.Key, risk_score = score, total_tx = totalTx, approved, rejected, total_amount = totalAmount, avg_amount = avgAmount, flags });
            }
        }
        flagged = flagged.OrderByDescending(f => (int)f.risk_score).Take(200).ToList();
        var playerIds = flagged.Select(f => (string)f.player_id).ToList();
        var blacklisted = playerIds.Count == 0 ? new HashSet<string>() : (await c.QueryAsync<string>("SELECT val FROM blacklist WHERE type=1 AND val IN @ids", new { ids = playerIds })).ToHashSet();
        var byFlag = new Dictionary<string, int>();
        var result = flagged.Select(f => { foreach (var fl in (List<string>)f.flags) byFlag[fl] = byFlag.GetValueOrDefault(fl, 0) + 1; return (object)new { f.player_id, f.risk_score, f.total_tx, f.approved, f.rejected, f.total_amount, f.avg_amount, f.flags, is_blacklisted = blacklisted.Contains((string)f.player_id) }; }).ToList();
        return new { players = result, summary = new { total_risky = result.Count, by_flag = byFlag } };
    }

    public async Task<object> PlayerSegmentationAsync(string from, string to, string? merchantParam, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var ids = await ResolveMerchantIds(c, merchantParam);
        var p = new DynamicParameters(); p.Add("f", Start(from)); p.Add("t", End(to)); var idW = ids is null ? "" : " AND firm_id IN @ids"; if (ids is not null) p.Add("ids", ids);
        var stats = (await c.QueryAsync($@"SELECT player_id, SUM(CASE WHEN status='3' THEN 1 ELSE 0 END) approved_count, SUM(CASE WHEN status='4' THEN 1 ELSE 0 END) rejected_count, COUNT(*) total_count, SUM(CASE WHEN status='3' THEN amount ELSE 0 END) approved_amount
            FROM invest WHERE type='1' AND created_at BETWEEN @f AND @t{idW} GROUP BY player_id", p)).ToList();
        var activeIds = stats.Select(s => (string?)s.player_id).Where(x => x is not null).ToList();

        var segs = new[] { "VIP", "Active", "Normal", "New", "Risky", "Inactive" }.ToDictionary(s => s, s => new { name = s, count = new int[1], total = new double[1], players = new List<dynamic>() });
        foreach (var s in stats)
        {
            int approved = (int)(long)s.approved_count, rejected = (int)(long)s.rejected_count, total = (int)(long)s.total_count; double amount = Convert.ToDouble(s.approved_amount);
            string seg = "New";
            if (total >= 3 && rejected > 0 && (double)rejected / total > 0.5) seg = "Risky";
            else if (amount > 100000 || approved >= 50) seg = "VIP";
            else if ((approved >= 10 && approved <= 49) || (amount >= 10000 && amount <= 100000)) seg = "Active";
            else if (approved >= 3 && approved <= 9) seg = "Normal";
            else if (approved >= 1 && approved <= 2) seg = "New";
            segs[seg].count[0]++; segs[seg].total[0] += amount;
            if (segs[seg].players.Count < 10) segs[seg].players.Add(new { player_id = (string?)s.player_id, approved_count = approved, rejected_count = rejected, total_count = total, approved_amount = amount });
        }

        var inactiveQ = new DynamicParameters(); inactiveQ.Add("f", Start(from)); if (ids is not null) inactiveQ.Add("ids", ids);
        var actW = activeIds.Count > 0 ? " AND player_id NOT IN @aids" : ""; if (activeIds.Count > 0) inactiveQ.Add("aids", activeIds);
        var inactive = (await c.QueryAsync($@"SELECT player_id, SUM(CASE WHEN status='3' THEN amount ELSE 0 END) approved_amount, COUNT(*) total_count FROM invest WHERE type='1' AND created_at<@f{idW}{actW} GROUP BY player_id LIMIT 200", inactiveQ)).ToList();
        foreach (var s in inactive) { double amount = Convert.ToDouble(s.approved_amount); segs["Inactive"].count[0]++; segs["Inactive"].total[0] += amount; if (segs["Inactive"].players.Count < 10) segs["Inactive"].players.Add(new { player_id = (string?)s.player_id, approved_count = 0, rejected_count = 0, total_count = (int)(long)s.total_count, approved_amount = amount }); }

        var allPids = segs.Values.SelectMany(s => s.players.Select(p2 => (string?)p2.player_id)).Where(x => x is not null).Distinct().ToList();
        var bl = allPids.Count == 0 ? new HashSet<string>() : (await c.QueryAsync<string>("SELECT val FROM blacklist WHERE type=1 AND val IN @ids", new { ids = allPids })).ToHashSet();

        var segmentList = segs.Values.Select(s => new
        {
            s.name, count = s.count[0], total_amount = s.total[0],
            players = s.players.OrderByDescending(p2 => (double)p2.approved_amount).Select(p2 => (object)new { p2.player_id, p2.approved_count, p2.rejected_count, p2.total_count, p2.approved_amount, is_blacklisted = bl.Contains((string)p2.player_id) }).ToList(),
        }).ToList();

        return new { segments = segmentList, chart = new { labels = segmentList.Select(s => s.name).ToList(), counts = segmentList.Select(s => s.count).ToList(), amounts = segmentList.Select(s => s.total_amount).ToList() } };
    }

    public async Task<object> MultiNamePlayersAsync(string from, string to, string? merchantParam, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var ids = await ResolveMerchantIds(c, merchantParam);
        var p = new DynamicParameters(); p.Add("f", Start(from)); p.Add("t", End(to)); var idW = ids is null ? "" : " AND firm_id IN @ids"; if (ids is not null) p.Add("ids", ids);
        var results = (await c.QueryAsync($@"SELECT player_id, COUNT(DISTINCT name) name_count, COUNT(*) total_count, SUM(CASE WHEN status='3' THEN amount ELSE 0 END) approved_amount,
            SUM(CASE WHEN status='3' THEN 1 ELSE 0 END) approved_count, SUM(CASE WHEN status='4' THEN 1 ELSE 0 END) rejected_count
            FROM invest WHERE type='1' AND status IN ('3','4') AND player_id IS NOT NULL AND name IS NOT NULL AND name<>'' AND created_at BETWEEN @f AND @t{idW}
            GROUP BY player_id HAVING name_count>=2 ORDER BY name_count DESC, total_count DESC LIMIT 200", p)).ToList();
        var pids = results.Select(r => (string)r.player_id).ToList();
        var namesByPlayer = new Dictionary<string, List<object>>();
        if (pids.Count > 0)
        {
            var np = new DynamicParameters(); np.Add("f", Start(from)); np.Add("t", End(to)); np.Add("pids", pids); if (ids is not null) np.Add("ids", ids);
            var names = await c.QueryAsync($@"SELECT player_id, name, COUNT(*) count, MAX(created_at) last_used FROM invest WHERE type='1' AND player_id IN @pids AND status IN ('3','4') AND created_at BETWEEN @f AND @t AND name IS NOT NULL AND name<>''{idW} GROUP BY player_id, name ORDER BY count DESC", np);
            foreach (var n in names) { var pid = (string)n.player_id; if (!namesByPlayer.ContainsKey(pid)) namesByPlayer[pid] = new(); namesByPlayer[pid].Add(new { name = (string)n.name, count = (long)n.count, last_used = n.last_used }); }
        }
        var bl = pids.Count == 0 ? new HashSet<string>() : (await c.QueryAsync<string>("SELECT val FROM blacklist WHERE type=1 AND val IN @ids", new { ids = pids })).ToHashSet();
        var players = results.Select(r => (object)new { player_id = (string)r.player_id, name_count = (long)r.name_count, total_count = (long)r.total_count, approved_count = (long)r.approved_count, rejected_count = (long)r.rejected_count, approved_amount = Math.Round(Convert.ToDouble((object)r.approved_amount), 2), names = namesByPlayer.GetValueOrDefault((string)r.player_id, new()), is_blacklisted = bl.Contains((string)r.player_id) }).ToList();
        return new { players, total = players.Count };
    }

    // ===================== BANK ACCOUNT =====================
    public async Task<object> BankAccountAnalysisAsync(string from, string to, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var rows = (await c.QueryAsync(@"SELECT ba.id account_id, ba.account_holder, ba.account_iban, ba.team_id, ba.status account_status,
            b.name bank_name, b.logo bank_logo, b.code bank_code, t.name team_name,
            SUM(CASE WHEN i.status=3 THEN 1 ELSE 0 END) approved_count, SUM(CASE WHEN i.status=4 THEN 1 ELSE 0 END) rejected_count, COUNT(*) total_count,
            SUM(CASE WHEN i.status=3 THEN i.amount ELSE 0 END) approved_amount, SUM(CASE WHEN i.status=4 THEN i.amount ELSE 0 END) rejected_amount, MAX(i.created_at) last_transaction
            FROM invest i JOIN bankAccounts ba ON ba.id=i.bank_id LEFT JOIN banks b ON b.id=ba.bank_id LEFT JOIN teams t ON t.id=ba.team_id
            WHERE i.type=1 AND i.bank_id IS NOT NULL AND i.created_at>=@f AND i.created_at<=@t
            GROUP BY ba.id, ba.account_holder, ba.account_iban, ba.team_id, ba.bank_id, ba.status, b.name, b.logo, b.code, t.name
            ORDER BY rejected_count DESC, approved_count ASC", new { f = Start(from), t = End(to) })).ToList();
        var result = rows.Select(r => { long total = (long)r.total_count, rejected = (long)r.rejected_count, approved = (long)r.approved_count; return (object)new { account_id = (int)r.account_id, account_holder = (string?)r.account_holder, account_iban = (string?)r.account_iban, team_id = (int?)r.team_id, team_name = (string?)r.team_name, account_status = (int)r.account_status, bank_name = (string?)r.bank_name, bank_logo = (string?)r.bank_logo, bank_code = (string?)r.bank_code, approved_count = approved, rejected_count = rejected, total_count = total, approved_amount = Convert.ToDouble(r.approved_amount), rejected_amount = Convert.ToDouble(r.rejected_amount), reject_ratio = total > 0 ? Math.Round((double)rejected * 100 / total, 1) : 0, last_transaction = r.last_transaction }; }).ToList();
        return new
        {
            date_from = Start(from), date_to = End(to), total_accounts = result.Count,
            totals = new { approved_count = rows.Sum(r => (long)r.approved_count), rejected_count = rows.Sum(r => (long)r.rejected_count), approved_amount = rows.Sum(r => Convert.ToDouble((object)r.approved_amount)), rejected_amount = rows.Sum(r => Convert.ToDouble((object)r.rejected_amount)) },
            rows = result,
        };
    }
}

internal static class DynParamReportsExt
{
    public static DynamicParameters AddP(this DynamicParameters p, string name, object? val) { p.Add(name, val); return p; }
}
