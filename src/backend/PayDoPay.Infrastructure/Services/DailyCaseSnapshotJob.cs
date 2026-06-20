using System.Data;
using System.Text.Json;
using Dapper;
using PayDoPay.Application.Common.Interfaces;
using PayDoPay.Application.Features.Background;

namespace PayDoPay.Infrastructure.Services;

/// <summary>Gün sonu kasa snapshot — PHP DailyCaseSnapshot komutunun birebir Dapper karşılığı.</summary>
public class DailyCaseSnapshotJob : IDailyCaseSnapshotJob
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    private string _dayStart = "";
    private string _dayEnd = "";

    public DailyCaseSnapshotJob(IDbConnectionFactory factory, IClock clock) { _factory = factory; _clock = clock; }

    public async Task RunAsync(string date, CancellationToken ct = default)
    {
        _dayStart = date + " 00:00:00";
        _dayEnd = DateTime.Parse(date).AddDays(1).ToString("yyyy-MM-dd") + " 00:00:00";
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        await SnapshotMerchants(c, date);
        await SnapshotIntermediaries(c, date);
        await SnapshotTeams(c, date);
        await SnapshotPaylira(c, date);
        await SnapshotPartners(c, date);
        await SnapshotFundStorages(c, date);
    }

    private Task<double> SumDay(IDbConnection c, string table, string col, object id, string sumCol = "amount", string extra = "")
        => Sum(c, $"SELECT COALESCE(SUM({sumCol}),0) FROM {table} WHERE {col}=@id AND created_at>=@s AND created_at<@e {extra}", new { id, s = _dayStart, e = _dayEnd });
    private Task<double> SumDayFinalize(IDbConnection c, string sql, object id)
        => Sum(c, sql, new { id, s = _dayStart, e = _dayEnd });
    private static async Task<double> Sum(IDbConnection c, string sql, object p) => await c.ExecuteScalarAsync<double?>(sql, p) ?? 0;
    private async Task<double> PrevSnap(IDbConnection c, string type, object? entityId, string date, double fallback)
    {
        var sql = entityId is null
            ? "SELECT amount FROM daily_case_snapshots WHERE entity_type=@t AND entity_id IS NULL AND snapshot_date<@d ORDER BY snapshot_date DESC LIMIT 1"
            : "SELECT amount FROM daily_case_snapshots WHERE entity_type=@t AND entity_id=@e AND snapshot_date<@d ORDER BY snapshot_date DESC LIMIT 1";
        return await c.ExecuteScalarAsync<double?>(sql, new { t = type, e = entityId, d = date }) ?? fallback;
    }

    private async Task SnapshotFundStorages(IDbConnection c, string date)
    {
        foreach (var s in await c.QueryAsync("SELECT id, name, type, wallet_address FROM fund_storages"))
        {
            int sid = (int)s.id;
            var prev = await PrevSnap(c, "fund_storage", sid, date, 0);
            double @out = 0;
            @out += await SumDay(c, "merchant_payments", "fund_storage_id", sid);
            @out += await SumDay(c, "intermediary_payments", "fund_storage_id", sid);
            @out += await SumDay(c, "paylira_partner_payments", "fund_storage_id", sid);
            @out += await SumDay(c, "fund_transfers", "from_storage_id", sid);
            @out += await SumDay(c, "paylira_expenses", "fund_storage_id", sid);
            var syncTotal = await SumDay(c, "fund_storage_syncs", "fund_storage_id", sid);
            double @in = 0;
            @in += await SumDay(c, "merchant_payments", "fund_storage_id", sid, "delivery_profit");
            @in += await SumDay(c, "team_payments", "fund_storage_id", sid);
            @in += await SumDay(c, "partner_capitals", "fund_storage_id", sid);
            @in += await SumDay(c, "fund_transfers", "to_storage_id", sid, "received_amount");
            var balance = prev + @in - @out + syncTotal;
            await Upsert(c, date, "fund_storage", sid, (string)s.name, Math.Round(balance, 2),
                new { previous_balance = Math.Round(prev, 2), @in = Math.Round(@in, 2), @out = Math.Round(@out, 2), type = s.type, wallet_address = s.wallet_address });
        }
    }

    private async Task SnapshotMerchants(IDbConnection c, string date)
    {
        var all = (await c.QueryAsync("SELECT id, name, caseNow, commission, withdrawCommission, group_id FROM merchantUser WHERE status='1'")).ToList();
        var groups = (await c.QueryAsync("SELECT id, name FROM merchant_groups WHERE status=1")).ToDictionary(g => (uint)g.id, g => (string)g.name);
        var processed = new HashSet<uint>();
        foreach (var m in all)
        {
            List<dynamic> gms; string name; string et; int eid; double totalCaseNow;
            uint? gid = m.group_id is null ? null : (uint)m.group_id;
            if (gid is not null && groups.ContainsKey(gid.Value)) { if (!processed.Add(gid.Value)) continue; gms = all.Where(x => x.group_id is not null && (uint)x.group_id == gid.Value).ToList(); name = groups[gid.Value]; et = "merchant_group"; eid = (int)gid.Value; totalCaseNow = gms.Sum(x => Convert.ToDouble(x.caseNow)); }
            else { gms = new List<dynamic> { m }; name = (string)m.name; et = "merchant"; eid = (int)m.id; totalCaseNow = Convert.ToDouble(m.caseNow); }

            double deposits = 0, withdrawals = 0, netDep = 0, netWd = 0, payments = 0, payComm = 0, depComm = 0, wdComm = 0;
            foreach (var gm in gms)
            {
                int id = (int)gm.id;
                var dep = await SumDayFinalize(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE firm_id=@id AND type=1 AND status=3 AND finalize_date>=@s AND finalize_date<@e", id);
                var wd = await SumDayFinalize(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE firm_id=@id AND type=2 AND status=3 AND finalize_date>=@s AND finalize_date<@e", id);
                var dc = dep * Convert.ToDouble(gm.commission) / 100; var wc = wd * Convert.ToDouble(gm.withdrawCommission) / 100;
                deposits += dep; withdrawals += wd; depComm += dc; wdComm += wc; netDep += dep - dc; netWd += wd + wc;
                payments += await SumDay(c, "merchant_payments", "merchant_id", id);
                payComm += await SumDay(c, "merchant_payments", "merchant_id", id, "delivery_commission_amount");
            }
            var dailyChange = netDep - netWd - payments;
            var prev = await PrevSnap(c, et, eid, date, totalCaseNow);
            await Upsert(c, date, et, eid, name, Math.Round(prev + dailyChange, 2), new
            {
                previous_balance = Math.Round(prev, 2), merchant_ids = gms.Select(x => (int)x.id).ToList(), deposits = Math.Round(deposits, 2), withdrawals = Math.Round(withdrawals, 2),
                net_deposit = Math.Round(netDep, 2), net_withdraw = Math.Round(netWd, 2), deposit_commission_amount = Math.Round(depComm, 2), withdraw_commission_amount = Math.Round(wdComm, 2),
                payments = Math.Round(payments, 2), payment_commissions = Math.Round(payComm, 2), daily_change = Math.Round(dailyChange, 2),
            });
        }
    }

    private async Task SnapshotIntermediaries(IDbConnection c, string date)
    {
        var today = _clock.Today.ToString("yyyy-MM-dd");
        foreach (var inter in await c.QueryAsync("SELECT id, name, type, balance FROM new_intermediaries WHERE status=1"))
        {
            int id = (int)inter.id; double total = 0; var details = new List<object>();
            foreach (var mr in await c.QueryAsync("SELECT m.id, m.name, nim.commission_rate FROM new_intermediary_merchant nim JOIN merchantUser m ON nim.merchant_id=m.id WHERE nim.intermediary_id=@id AND nim.status=1", new { id }))
            {
                var dep = await SumDayFinalize(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE firm_id=@id AND type=1 AND status=3 AND finalize_date>=@s AND finalize_date<@e", (int)mr.id);
                var comm = dep * Convert.ToDouble(mr.commission_rate) / 100; total += comm;
                if (comm > 0) details.Add(new { name = (string)mr.name, type = "merchant", rate = mr.commission_rate, deposits = Math.Round(dep, 2), commission = Math.Round(comm, 2) });
            }
            foreach (var tr in await c.QueryAsync("SELECT t.id, t.name, nit.commission_rate FROM new_intermediary_team nit JOIN teams t ON nit.team_id=t.id WHERE nit.intermediary_id=@id AND nit.status=1", new { id }))
            {
                var dep = await SumDayFinalize(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE team_id=@id AND type=1 AND status=3 AND finalize_date>=@s AND finalize_date<@e", (int)tr.id);
                var comm = dep * Convert.ToDouble(tr.commission_rate) / 100; total += comm;
                if (comm > 0) details.Add(new { name = (string)tr.name, type = "team", rate = tr.commission_rate, deposits = Math.Round(dep, 2), commission = Math.Round(comm, 2) });
            }
            var payments = await SumDay(c, "intermediary_payments", "intermediary_id", id);
            var prev = await PrevSnap(c, "intermediary", id, date, Convert.ToDouble(inter.balance));
            var bal = prev + total - payments;
            if (date == today) await c.ExecuteAsync("UPDATE new_intermediaries SET balance=@b WHERE id=@id", new { b = Math.Round(bal, 2), id });
            await Upsert(c, date, "intermediary", id, (string)inter.name, Math.Round(bal, 2), new { intermediary_type = inter.type, previous_balance = Math.Round(prev, 2), daily_commission = Math.Round(total, 2), payments = Math.Round(payments, 2), breakdowns = details });
        }
    }

    private async Task SnapshotTeams(IDbConnection c, string date)
    {
        foreach (var team in await c.QueryAsync("SELECT id, name, overturn, commission FROM teams"))
        {
            int id = (int)team.id;
            if (id == 1) { await Upsert(c, date, "team", id, (string)team.name, 0, new { manual_zero = true }); continue; }
            var dep = await SumDayFinalize(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE team_id=@id AND type=1 AND status=3 AND finalize_date>=@s AND finalize_date<@e", id);
            var wd = await SumDayFinalize(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE team_id=@id AND type=2 AND status=3 AND finalize_date>=@s AND finalize_date<@e", id);
            var teamComm = dep * Convert.ToDouble(team.commission) / 100;
            var payments = await SumDay(c, "team_payments", "team_id", id);
            var expenses = await SumDay(c, "paylira_expenses", "team_id", id);
            var partnerPay = await SumDay(c, "paylira_partner_payments", "team_id", id, "amount", "AND payment_type='3'");
            var interPay = await SumDay(c, "intermediary_payments", "team_id", id, "amount", "AND payment_type='3'");
            var transferOut = await SumDay(c, "team_transfers", "from_team_id", id);
            var transferIn = await SumDay(c, "team_transfers", "to_team_id", id);
            var syncs = await SumDay(c, "team_syncs", "team_id", id);
            var accumulated = dep - teamComm - wd - payments - expenses - partnerPay - interPay - transferOut + transferIn - syncs;
            var prev = await PrevSnap(c, "team", id, date, Convert.ToDouble(team.overturn));
            await Upsert(c, date, "team", id, (string)team.name, Math.Round(prev + accumulated, 2), new
            {
                overturn = Math.Round(prev, 2), commission = team.commission, deposits = Math.Round(dep, 2), withdrawals = Math.Round(wd, 2), team_commission = Math.Round(teamComm, 2),
                payments = Math.Round(payments, 2), paylira_expenses = Math.Round(expenses, 2), partner_capital_payments = Math.Round(partnerPay, 2), intermediary_payments = Math.Round(interPay, 2),
                transfer_in = Math.Round(transferIn, 2), transfer_out = Math.Round(transferOut, 2), team_syncs = Math.Round(syncs, 2), accumulated = Math.Round(accumulated, 2),
            });
        }
    }

    private async Task SnapshotPaylira(IDbConnection c, string date)
    {
        var merchants = (await c.QueryAsync("SELECT id, name, commission, withdrawCommission, group_id FROM merchantUser WHERE status='1'")).ToList();
        var groupNames = (await c.QueryAsync("SELECT id, name FROM merchant_groups WHERE status=1")).ToDictionary(g => (uint)g.id, g => (string)g.name);
        var interTeamRates = (await c.QueryAsync(@"SELECT nit.team_id, SUM(nit.commission_rate) total_rate FROM new_intermediary_team nit JOIN new_intermediaries ni ON nit.intermediary_id=ni.id
            WHERE nit.status=1 AND ni.status=1 GROUP BY nit.team_id")).ToDictionary(r => (int)r.team_id, r => Convert.ToDouble(r.total_rate));

        double totalDailyNet = 0; var breakdown = new List<Dictionary<string, object>>();
        foreach (var m in merchants)
        {
            int mid = (int)m.id;
            var dep = await SumDayFinalize(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE firm_id=@id AND type=1 AND status=3 AND finalize_date>=@s AND finalize_date<@e", mid);
            var wd = await SumDayFinalize(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE firm_id=@id AND type=2 AND status=3 AND finalize_date>=@s AND finalize_date<@e", mid);
            var depComm = dep * Convert.ToDouble(m.commission) / 100; var wdComm = wd * Convert.ToDouble(m.withdrawCommission) / 100;
            var delComm = await SumDay(c, "merchant_payments", "merchant_id", mid, "delivery_commission_amount");
            var delProfit = await SumDay(c, "merchant_payments", "merchant_id", mid, "delivery_profit");
            var brut = depComm + wdComm + delComm + delProfit;
            var teamComm = await SumDayFinalize(c, "SELECT COALESCE(SUM(invest.amount*teams.commission/100),0) FROM invest JOIN teams ON invest.team_id=teams.id WHERE invest.firm_id=@id AND invest.type=1 AND invest.status=3 AND invest.finalize_date>=@s AND invest.finalize_date<@e", mid);

            double interComm = 0;
            var teamDeps = (await c.QueryAsync("SELECT team_id, SUM(amount) total FROM invest WHERE firm_id=@id AND type=1 AND status=3 AND finalize_date>=@s AND finalize_date<@e GROUP BY team_id", new { id = mid, s = _dayStart, e = _dayEnd })).ToList();
            foreach (var td in teamDeps) { if (td.team_id is not null && interTeamRates.TryGetValue((int)td.team_id, out var rate)) interComm += Convert.ToDouble(td.total) * rate / 100; }
            var mRates = await c.QueryAsync<decimal>("SELECT nim.commission_rate FROM new_intermediary_merchant nim JOIN new_intermediaries ni ON nim.intermediary_id=ni.id WHERE nim.merchant_id=@id AND nim.status=1 AND ni.status=1", new { id = mid });
            foreach (var rate in mRates) interComm += dep * (double)rate / 100;

            var merchantNet = brut - teamComm - interComm; totalDailyNet += merchantNet;
            if (dep > 0 || wd > 0 || delComm > 0 || delProfit > 0)
            {
                uint? gid = m.group_id is null ? null : (uint)m.group_id;
                var displayName = gid is not null && groupNames.ContainsKey(gid.Value) ? groupNames[gid.Value] : (string)m.name;
                var depProfitNet = depComm - teamComm - interComm;
                var existing = breakdown.FirstOrDefault(b => (string)b["name"] == displayName);
                if (existing is not null) { existing["net"] = (double)existing["net"] + Math.Round(merchantNet, 2); existing["deposit_profit"] = (double)existing["deposit_profit"] + Math.Round(depProfitNet, 2); existing["withdraw_profit"] = (double)existing["withdraw_profit"] + Math.Round(wdComm, 2); existing["delivery_profit"] = (double)existing["delivery_profit"] + Math.Round(delComm + delProfit, 2); }
                else breakdown.Add(new Dictionary<string, object> { ["name"] = displayName, ["net"] = Math.Round(merchantNet, 2), ["deposit_profit"] = Math.Round(depProfitNet, 2), ["withdraw_profit"] = Math.Round(wdComm, 2), ["delivery_profit"] = Math.Round(delComm + delProfit, 2) });
            }
        }

        double systemInterComm = 0;
        var systemInters = (await c.QueryAsync("SELECT commission_rate FROM new_intermediaries WHERE status=1 AND type=3")).ToList();
        if (systemInters.Count > 0)
        {
            var totalSystem = await SumDayFinalize(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE type=1 AND status=3 AND finalize_date>=@s AND finalize_date<@e", 0);
            foreach (var si in systemInters) systemInterComm += totalSystem * Convert.ToDouble(si.commission_rate) / 100;
            totalDailyNet -= systemInterComm;
        }

        var prev = await PrevSnap(c, "paylira", null, date, 0);
        var expenses = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM paylira_expenses WHERE created_at>=@s AND created_at<@e", new { s = _dayStart, e = _dayEnd });
        var partnerPayments = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM paylira_partner_payments WHERE created_at>=@s AND created_at<@e", new { s = _dayStart, e = _dayEnd });
        var partnerCapitals = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM partner_capitals WHERE created_at>=@s AND created_at<@e", new { s = _dayStart, e = _dayEnd });
        var cumulative = prev + totalDailyNet - expenses - partnerPayments + partnerCapitals;
        await Upsert(c, date, "paylira", null, "Paylira", Math.Round(cumulative, 2), new
        {
            daily_net = Math.Round(totalDailyNet, 2), expenses = Math.Round(expenses, 2), partner_payments = Math.Round(partnerPayments, 2), partner_capitals = Math.Round(partnerCapitals, 2),
            previous_balance = Math.Round(prev, 2), system_inter_comm = Math.Round(systemInterComm, 2), merchants = breakdown,
        });
    }

    private async Task SnapshotPartners(IDbConnection c, string date)
    {
        var payliraSnap = await c.QueryFirstOrDefaultAsync("SELECT details FROM daily_case_snapshots WHERE entity_type='paylira' AND entity_id IS NULL AND snapshot_date=@d", new { d = date });
        if (payliraSnap is null) return;
        double dailyNet = 0; try { using var doc = JsonDocument.Parse((string)payliraSnap.details); if (doc.RootElement.TryGetProperty("daily_net", out var v)) dailyNet = v.GetDouble(); } catch { }

        foreach (var partner in await c.QueryAsync("SELECT id, name, share_percent FROM paylira_partners WHERE status=1"))
        {
            int id = (int)partner.id;
            var share = dailyNet * Convert.ToDouble(partner.share_percent) / 100;
            var prev = await PrevSnap(c, "partner", id, date, 0);
            var payments = await SumDay(c, "paylira_partner_payments", "partner_id", id);
            var capitals = await SumDay(c, "partner_capitals", "partner_id", id);
            var expenses = await Sum(c, "SELECT COALESCE(SUM(sh.amount),0) FROM paylira_expense_shares sh JOIN paylira_expenses e ON sh.expense_id=e.id WHERE sh.partner_id=@id AND e.created_at>=@s AND e.created_at<@e", new { id, s = _dayStart, e = _dayEnd });
            var transferOut = await SumDay(c, "partner_transfers", "from_partner_id", id);
            var transferIn = await SumDay(c, "partner_transfers", "to_partner_id", id);
            var bal = prev + share + capitals - payments - expenses - transferOut + transferIn;
            await Upsert(c, date, "partner", id, (string)partner.name, Math.Round(bal, 2), new
            {
                previous_balance = Math.Round(prev, 2), daily_net = Math.Round(dailyNet, 2), share_percent = partner.share_percent, daily_share = Math.Round(share, 2),
                capitals = Math.Round(capitals, 2), expenses = Math.Round(expenses, 2), payments = Math.Round(payments, 2), transfer_in = Math.Round(transferIn, 2), transfer_out = Math.Round(transferOut, 2),
            });
        }
    }

    private async Task Upsert(IDbConnection c, string date, string type, int? entityId, string name, double amount, object details)
    {
        var json = JsonSerializer.Serialize(details);
        var existing = await c.ExecuteScalarAsync<int?>("SELECT id FROM daily_case_snapshots WHERE snapshot_date=@d AND entity_type=@t AND " + (entityId is null ? "entity_id IS NULL" : "entity_id=@e") + " LIMIT 1", new { d = date, t = type, e = entityId });
        if (existing is not null)
            await c.ExecuteAsync("UPDATE daily_case_snapshots SET entity_name=@n, amount=@a, details=@det WHERE id=@id", new { n = name, a = amount, det = json, id = existing });
        else
            await c.ExecuteAsync("INSERT INTO daily_case_snapshots (snapshot_date, entity_type, entity_id, entity_name, amount, details, created_at) VALUES (@d,@t,@e,@n,@a,@det,@at)",
                new { d = date, t = type, e = entityId, n = name, a = amount, det = json, at = _clock.Now });
    }
}
