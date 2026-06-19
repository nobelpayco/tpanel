using System.Data;
using Dapper;
using PayDoPay.Application.Features.Cases;

namespace PayDoPay.Infrastructure.Services;

public partial class CaseStore
{
    private static async Task<double?> SnapAt(IDbConnection c, string entityType, object? entityId, string date)
    {
        var sql = entityId is null
            ? "SELECT amount FROM daily_case_snapshots WHERE entity_type=@et AND entity_id IS NULL AND snapshot_date=@d LIMIT 1"
            : "SELECT amount FROM daily_case_snapshots WHERE entity_type=@et AND entity_id=@eid AND snapshot_date=@d LIMIT 1";
        return await c.ExecuteScalarAsync<double?>(sql, new { et = entityType, eid = entityId, d = date });
    }

    // ========================= CASE REPORT =========================
    public async Task<object> CaseReportSummaryAsync(string? from, string? to, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var today = Today;
        var dateFrom = from ?? today;
        var dateTo = to ?? today;
        var isPast = string.Compare(dateTo, today, StringComparison.Ordinal) < 0;
        var rangeDate = isPast ? dateTo : today;

        // ---- Merchant kasaları ----
        var allMerchants = (await c.QueryAsync("SELECT id, name, caseNow, commission, withdrawCommission, group_id FROM merchantUser WHERE status='1'")).ToList();
        var groups = (await c.QueryAsync("SELECT id, name FROM merchant_groups WHERE status=1")).ToDictionary(g => (uint)g.id, g => (string)g.name);
        var processed = new HashSet<uint>();
        var merchantCases = new List<dynamic>();

        foreach (var m in allMerchants)
        {
            List<dynamic> gms; string displayName; string et; int eid; double totalCaseNow;
            uint? gid = m.group_id is null ? null : (uint)m.group_id;
            if (gid is not null && groups.ContainsKey(gid.Value))
            {
                if (!processed.Add(gid.Value)) continue;
                gms = allMerchants.Where(x => x.group_id is not null && (uint)x.group_id == gid.Value).ToList();
                displayName = groups[gid.Value]; et = "merchant_group"; eid = (int)gid.Value; totalCaseNow = gms.Sum(x => Convert.ToDouble(x.caseNow));
            }
            else { gms = new List<dynamic> { m }; displayName = (string)m.name; et = "merchant"; eid = (int)m.id; totalCaseNow = Convert.ToDouble(m.caseNow); }

            double caseValue;
            var snap = isPast ? await SnapAt(c, et, eid, dateTo) : null;
            if (snap is not null) caseValue = snap.Value;
            else
            {
                var lastSnap = await LastSnap(c, et, eid, today, totalCaseNow);
                double netDep = 0, netWd = 0, pay = 0;
                foreach (var gm in gms)
                {
                    int gmid = (int)gm.id;
                    var dep = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE firm_id=@id AND type=1 AND status=3 AND DATE(finalize_date)=@d", new { id = gmid, d = rangeDate });
                    var wd = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE firm_id=@id AND type=2 AND status=3 AND DATE(finalize_date)=@d", new { id = gmid, d = rangeDate });
                    netDep += dep - dep * Convert.ToDouble(gm.commission) / 100;
                    netWd += wd + wd * Convert.ToDouble(gm.withdrawCommission) / 100;
                    pay += await SumDate(c, "merchant_payments", "merchant_id", gmid, rangeDate);
                }
                caseValue = lastSnap + netDep - netWd - pay;
            }
            merchantCases.Add(new { id = eid, name = displayName, value = Math.Round(caseValue, 2) });
        }
        var merchantCasesF = merchantCases.Where(x => (double)x.value != 0).ToList();

        // ---- Aracı komisyonları ----
        var interCases = new List<dynamic>();
        double totalInterCommission = 0;
        foreach (var inter in await c.QueryAsync("SELECT id, name, type, balance, commission_rate FROM new_intermediaries WHERE status=1"))
        {
            int id = (int)inter.id;
            double dailyCommission, value;
            var snap = isPast ? await SnapAt(c, "intermediary", id, dateTo) : null;
            if (snap is not null) { value = snap.Value; dailyCommission = 0; }
            else
            {
                var lastSnap = await LastSnap(c, "intermediary", id, today);
                dailyCommission = (int)inter.type == 3
                    ? (await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE type=1 AND status=3 AND DATE(finalize_date)=@d", new { d = rangeDate })) * Convert.ToDouble(inter.commission_rate) / 100
                    : await CalcDailyCommission(c, id, rangeDate);
                var todayPay = await SumDate(c, "intermediary_payments", "intermediary_id", id, rangeDate);
                value = lastSnap + dailyCommission - todayPay;
            }
            totalInterCommission += dailyCommission;
            interCases.Add(new { name = (string)inter.name, type = (int)inter.type, daily_commission = Math.Round(dailyCommission, 2), value = Math.Round(value, 2) });
        }
        var interCasesF = interCases.Where(x => (double)x.value != 0).ToList();

        // ---- Takım kasaları ----
        var teamBalances = new List<dynamic>();
        foreach (var team in await c.QueryAsync("SELECT id, name, overturn, commission FROM teams"))
        {
            int id = (int)team.id;
            double balance;
            var snap = isPast ? await SnapAt(c, "team", id, dateTo) : null;
            if (snap is not null) balance = snap.Value;
            else
            {
                var lastSnap = await LastSnap(c, "team", id, today, Convert.ToDouble(team.overturn));
                var dep = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE team_id=@id AND type=1 AND status=3 AND DATE(finalize_date)=@d", new { id, d = rangeDate });
                var wd = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE team_id=@id AND type=2 AND status=3 AND DATE(finalize_date)=@d", new { id, d = rangeDate });
                var teamComm = dep * Convert.ToDouble(team.commission) / 100;
                var pay = await SumDate(c, "team_payments", "team_id", id, rangeDate);
                var exp = await SumDate(c, "paylira_expenses", "team_id", id, rangeDate);
                var partnerPay = await SumDate(c, "paylira_partner_payments", "team_id", id, rangeDate, "amount", "AND payment_type='3'");
                var interPay = await SumDate(c, "intermediary_payments", "team_id", id, rangeDate, "amount", "AND payment_type='3'");
                var transferOut = await SumDate(c, "team_transfers", "from_team_id", id, rangeDate);
                var transferIn = await SumDate(c, "team_transfers", "to_team_id", id, rangeDate);
                var syncs = await SumDate(c, "team_syncs", "team_id", id, rangeDate);
                balance = lastSnap + dep - teamComm - wd - pay - exp - partnerPay - interPay - transferOut + transferIn - syncs;
            }
            teamBalances.Add(new { name = (string)team.name, value = Math.Round(balance, 2) });
        }
        var teamBalancesF = teamBalances.Where(x => (double)x.value != 0).ToList();

        // ---- Paylira net ----
        double totalDepComm = 0, totalWdComm = 0, totalDelComm = 0, totalTeamComm = 0;
        foreach (var m in allMerchants)
        {
            int mid = (int)m.id;
            var dep = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE firm_id=@id AND type=1 AND status=3 AND DATE(finalize_date)=@d", new { id = mid, d = rangeDate });
            var wd = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE firm_id=@id AND type=2 AND status=3 AND DATE(finalize_date)=@d", new { id = mid, d = rangeDate });
            totalDepComm += dep * Convert.ToDouble(m.commission) / 100;
            totalWdComm += wd * Convert.ToDouble(m.withdrawCommission) / 100;
            totalDelComm += await SumDate(c, "merchant_payments", "merchant_id", mid, rangeDate, "delivery_commission_amount");
            totalTeamComm += await Sum(c, "SELECT COALESCE(SUM(invest.amount*teams.commission/100),0) FROM invest JOIN teams ON invest.team_id=teams.id WHERE invest.firm_id=@id AND invest.type=1 AND invest.status=3 AND DATE(invest.finalize_date)=@d", new { id = mid, d = rangeDate });
        }
        var totalBrut = totalDepComm + totalWdComm + totalDelComm;

        double payliraNet;
        var payliraSnap = isPast ? await SnapAt(c, "paylira", null, dateTo) : null;
        if (payliraSnap is not null) payliraNet = payliraSnap.Value;
        else
        {
            var prev = await LastSnap(c, "paylira", null, today);
            var exp = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM paylira_expenses WHERE DATE(created_at)=@d", new { d = rangeDate });
            var partnerPay = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM paylira_partner_payments WHERE DATE(created_at)=@d", new { d = rangeDate });
            var partnerCap = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM partner_capitals WHERE DATE(created_at)=@d", new { d = rangeDate });
            var todayNet = totalBrut - totalTeamComm - totalInterCommission - exp - partnerPay + partnerCap;
            payliraNet = prev + todayNet;
        }

        // ---- Fon depoları ----
        var storages = (await c.QueryAsync("SELECT id, name, type, balance, wallet_address FROM fund_storages WHERE status=1 ORDER BY type, name")).ToList();
        var fsList = new List<dynamic>();
        foreach (var fs in storages)
        {
            double bal;
            if (isPast) bal = await SnapAt(c, "fund_storage", (int)fs.id, dateTo) ?? 0;
            else bal = Convert.ToDouble(fs.balance);
            fsList.Add(new { id = (int)fs.id, name = (string)fs.name, type = (int)fs.type, balance = Math.Round(bal, 2), wallet_address = (string?)fs.wallet_address, chain_balance = (double?)null });
        }
        var cash = fsList.Where(x => (int)x.type is 1 or 2).ToList();
        var receivable = fsList.Where(x => (int)x.type is 3 or 4).ToList();

        return new
        {
            merchant_cases = merchantCasesF,
            total_merchant_case = Math.Round(merchantCasesF.Sum(x => (double)x.value), 2),
            intermediary_cases = interCasesF,
            total_intermediary = Math.Round(interCasesF.Sum(x => (double)x.value), 2),
            paylira_net = Math.Round(payliraNet, 2),
            team_balances = teamBalancesF,
            total_team_balance = Math.Round(teamBalancesF.Sum(x => (double)x.value), 2),
            fund_storages = cash,
            total_fund_storage = Math.Round(cash.Sum(x => (double)x.balance), 2),
            receivable_payables = receivable,
            total_receivable_payable = Math.Round(receivable.Sum(x => (double)x.balance), 2),
        };
    }

    public async Task<object> CaseReportIndexAsync(string? from, string? to, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var today = Today;
        var dateFrom = from ?? today;
        var dateTo = to ?? today;

        var merchants = (await c.QueryAsync("SELECT id, name, commission, withdrawCommission FROM merchantUser WHERE status='1'")).ToList();
        var report = new List<dynamic>();
        foreach (var m in merchants)
        {
            int mid = (int)m.id;
            var dep = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE firm_id=@id AND type=1 AND status=3 AND DATE(finalize_date) BETWEEN @f AND @t", new { id = mid, f = dateFrom, t = dateTo });
            var wd = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE firm_id=@id AND type=2 AND status=3 AND DATE(finalize_date) BETWEEN @f AND @t", new { id = mid, f = dateFrom, t = dateTo });
            var depComm = dep * Convert.ToDouble(m.commission) / 100;
            var wdComm = wd * Convert.ToDouble(m.withdrawCommission) / 100;
            var brut = depComm + wdComm;
            var teamComm = await Sum(c, "SELECT COALESCE(SUM(invest.amount*teams.commission/100),0) FROM invest JOIN teams ON invest.team_id=teams.id WHERE invest.firm_id=@id AND invest.type=1 AND invest.status=3 AND DATE(invest.finalize_date) BETWEEN @f AND @t", new { id = mid, f = dateFrom, t = dateTo });
            var payliraInter = await CalcInterCommissionByType(c, mid, 1, dep);
            var merchantInter = await CalcInterCommissionByType(c, mid, 2, dep);
            var net = brut - teamComm - payliraInter - merchantInter;
            report.Add(new
            {
                merchant = (string)m.name, total_deposit = Math.Round(dep, 2), total_withdraw = Math.Round(wd, 2),
                paylira_commission_rate = m.commission, withdraw_commission_rate = m.withdrawCommission,
                paylira_brut = Math.Round(brut, 2), team_commission = Math.Round(teamComm, 2),
                paylira_intermediary = Math.Round(payliraInter, 2), merchant_intermediary = Math.Round(merchantInter, 2),
                paylira_net = Math.Round(net, 2),
            });
        }
        var totals = new
        {
            total_deposit = Math.Round(report.Sum(x => (double)x.total_deposit), 2),
            paylira_brut = Math.Round(report.Sum(x => (double)x.paylira_brut), 2),
            team_commission = Math.Round(report.Sum(x => (double)x.team_commission), 2),
            paylira_intermediary = Math.Round(report.Sum(x => (double)x.paylira_intermediary), 2),
            merchant_intermediary = Math.Round(report.Sum(x => (double)x.merchant_intermediary), 2),
            paylira_net = Math.Round(report.Sum(x => (double)x.paylira_net), 2),
        };
        return new { report, totals };
    }

    private static async Task<double> CalcInterCommissionByType(IDbConnection c, int merchantId, int type, double totalDeposit)
    {
        var rate = await c.ExecuteScalarAsync<double?>(
            @"SELECT COALESCE(SUM(nim.commission_rate),0) FROM new_intermediary_merchant nim
              JOIN new_intermediaries ni ON nim.intermediary_id=ni.id
              WHERE nim.merchant_id=@m AND nim.status=1 AND ni.type=@t AND ni.status=1",
            new { m = merchantId, t = type }) ?? 0;
        return totalDeposit * rate / 100;
    }
}
