using System.Data;
using System.Text.Json;
using Dapper;
using PayDoPay.Application.Features.Cases;

namespace PayDoPay.Infrastructure.Services;

public partial class CaseStore
{
    // Paylira bugünkü net (PHP getPayliraCurrentNet birebir)
    private async Task<double> GetPayliraCurrentNet(IDbConnection c, string today, double previousSnap)
    {
        var merchants = await c.QueryAsync("SELECT id, commission, withdrawCommission FROM merchantUser WHERE status='1'");
        double depComm = 0, wdComm = 0, delComm = 0, delProfit = 0, teamComm = 0;
        foreach (var m in merchants)
        {
            int mid = (int)m.id;
            var dep = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE firm_id=@id AND type=1 AND status=3 AND DATE(finalize_date)=@d", new { id = mid, d = today });
            var wd = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE firm_id=@id AND type=2 AND status=3 AND DATE(finalize_date)=@d", new { id = mid, d = today });
            depComm += dep * Convert.ToDouble(m.commission) / 100;
            wdComm += wd * Convert.ToDouble(m.withdrawCommission) / 100;
            delComm += await SumDate(c, "merchant_payments", "merchant_id", mid, today, "delivery_commission_amount");
            delProfit += await SumDate(c, "merchant_payments", "merchant_id", mid, today, "delivery_profit");
            teamComm += await Sum(c, "SELECT COALESCE(SUM(invest.amount*teams.commission/100),0) FROM invest JOIN teams ON invest.team_id=teams.id WHERE invest.firm_id=@id AND invest.type=1 AND invest.status=3 AND DATE(invest.finalize_date)=@d", new { id = mid, d = today });
        }

        double interComm = 0;
        var inters = await c.QueryAsync("SELECT id, type, commission_rate FROM new_intermediaries WHERE status=1");
        double? totalSystemDeposits = null;
        foreach (var i in inters)
        {
            if ((int)i.type == 3)
            {
                totalSystemDeposits ??= await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE type=1 AND status=3 AND DATE(finalize_date)=@d", new { d = today });
                interComm += totalSystemDeposits.Value * Convert.ToDouble(i.commission_rate) / 100;
                continue;
            }
            foreach (var tr in await c.QueryAsync("SELECT team_id, commission_rate FROM new_intermediary_team WHERE intermediary_id=@i AND status=1", new { i = (int)i.id }))
            {
                var d = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE team_id=@t AND type=1 AND status=3 AND DATE(finalize_date)=@d", new { t = (int)tr.team_id, d = today });
                interComm += d * Convert.ToDouble(tr.commission_rate) / 100;
            }
            foreach (var mr in await c.QueryAsync("SELECT merchant_id, commission_rate FROM new_intermediary_merchant WHERE intermediary_id=@i AND status=1", new { i = (int)i.id }))
            {
                var d = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE firm_id=@m AND type=1 AND status=3 AND DATE(finalize_date)=@d", new { m = (int)mr.merchant_id, d = today });
                interComm += d * Convert.ToDouble(mr.commission_rate) / 100;
            }
        }
        var todayNet = (depComm + wdComm + delComm + delProfit) - teamComm - interComm;
        return previousSnap + todayNet;
    }

    private async Task<double> CalcDailyCommission(IDbConnection c, int intermediaryId, string date)
    {
        double total = 0;
        foreach (var tr in await c.QueryAsync("SELECT team_id, commission_rate FROM new_intermediary_team WHERE intermediary_id=@i AND status=1", new { i = intermediaryId }))
        {
            var dep = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE team_id=@t AND type=1 AND status=3 AND DATE(finalize_date)=@d", new { t = (int)tr.team_id, d = date });
            total += dep * Convert.ToDouble(tr.commission_rate) / 100;
        }
        foreach (var mr in await c.QueryAsync("SELECT merchant_id, commission_rate FROM new_intermediary_merchant WHERE intermediary_id=@i AND status=1", new { i = intermediaryId }))
        {
            var dep = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE firm_id=@m AND type=1 AND status=3 AND DATE(finalize_date)=@d", new { m = (int)mr.merchant_id, d = date });
            total += dep * Convert.ToDouble(mr.commission_rate) / 100;
        }
        return total;
    }

    private async Task<double> LastSnap(IDbConnection c, string entityType, object? entityId, string today, double fallback = 0)
    {
        var sql = entityId is null
            ? "SELECT amount FROM daily_case_snapshots WHERE entity_type=@et AND entity_id IS NULL AND snapshot_date<@t ORDER BY snapshot_date DESC LIMIT 1"
            : "SELECT amount FROM daily_case_snapshots WHERE entity_type=@et AND entity_id=@eid AND snapshot_date<@t ORDER BY snapshot_date DESC LIMIT 1";
        return await c.ExecuteScalarAsync<double?>(sql, new { et = entityType, eid = entityId, t = today }) ?? fallback;
    }

    // ========================= INTERMEDIARY =========================
    public async Task<object> IntermediaryIndexAsync(CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var today = Today;
        var list = new List<object>();
        foreach (var i in await c.QueryAsync("SELECT id, name, type, balance FROM new_intermediaries WHERE status=1"))
        {
            int id = (int)i.id;
            var lastSnap = await LastSnap(c, "intermediary", id, today);
            var daily = await CalcDailyCommission(c, id, today);
            var todayPay = await SumDate(c, "intermediary_payments", "intermediary_id", id, today);
            list.Add(new { id, name = (string)i.name, type = (int)i.type, current_case = Math.Round(lastSnap + daily - todayPay, 2) });
        }
        return list;
    }

    public async Task<object?> IntermediaryShowAsync(int id, string? from, string? to, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var today = Today;
        var inter = await c.QueryFirstOrDefaultAsync("SELECT * FROM new_intermediaries WHERE id=@id", new { id });
        if (inter is null) return null;

        var snapSql = @"SELECT DATE_FORMAT(snapshot_date,'%Y-%m-%d') AS SnapshotDate, amount AS Amount, details AS Details FROM daily_case_snapshots
                        WHERE entity_type='intermediary' AND entity_id=@id AND snapshot_date<@today";
        var p = new DynamicParameters(); p.Add("id", id); p.Add("today", today);
        if (from is not null && to is not null) { snapSql += " AND snapshot_date>=@from AND snapshot_date<=@to ORDER BY snapshot_date DESC"; p.Add("from", from); p.Add("to", to); }
        else snapSql += " ORDER BY snapshot_date DESC LIMIT 30";
        var snaps = (await c.QueryAsync<SnapshotRow>(snapSql, p)).ToList();

        var daily = new List<object>();
        foreach (var s in snaps)
        {
            var det = ParseDetails(s.Details);
            var pay = await SumDate(c, "intermediary_payments", "intermediary_id", id, s.SnapshotDate);
            daily.Add(new { date = s.SnapshotDate, amount = s.Amount, previous_balance = Detail(det, "previous_balance"), daily_commission = Detail(det, "daily_commission"), payments = Math.Round(pay, 2) });
        }

        var lastSnap = await LastSnap(c, "intermediary", id, today);
        var todayComm = await CalcDailyCommission(c, id, today);
        var todayPay = await SumDate(c, "intermediary_payments", "intermediary_id", id, today);
        var currentCase = lastSnap + todayComm - todayPay;
        daily.Insert(0, new { date = today, amount = Math.Round(currentCase, 2), previous_balance = Math.Round(lastSnap, 2), daily_commission = Math.Round(todayComm, 2), payments = Math.Round(todayPay, 2), is_today = true });

        var totalPayments = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM intermediary_payments WHERE intermediary_id=@id", new { id });
        var teams = await c.QueryAsync("SELECT id, name, status FROM teams ORDER BY name");

        return new { intermediary = inter, current_case = Math.Round(currentCase, 2), total_payments = Math.Round(totalPayments, 2), daily_cases = daily, teams };
    }

    public async Task<object> IntermediaryPaymentsAsync(int id, string? date, string? from, string? to, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        string dc = date is not null ? " AND DATE(created_at)=@date" : (from is not null && to is not null ? " AND DATE(created_at) BETWEEN @from AND @to" : "");
        var rows = await c.QueryAsync($@"SELECT ip.*, t.name AS team_name FROM intermediary_payments ip LEFT JOIN teams t ON ip.team_id=t.id
            WHERE ip.intermediary_id=@id{dc} ORDER BY ip.created_at DESC LIMIT 200", new { id, date, from, to });
        var list = rows.Select(p => (object)new
        {
            id = (int)p.id, payment_type = (int)p.payment_type, amount = p.amount, crypto_quantity = p.crypto_quantity,
            crypto_rate = p.crypto_rate, tx_link = (string?)p.tx_link, team_id = (int?)p.team_id, team_name = (string?)p.team_name,
            description = (string?)p.description, created_at = p.created_at,
        }).ToList();
        return new { payments = list, total = Math.Round(rows.Sum(p => (double)(decimal)p.amount), 2) };
    }

    public async Task<WriteResult> AddIntermediaryPaymentAsync(int id, IntermediaryPaymentBody b, ActorInfo actor, CancellationToken ct = default)
    {
        if (b.PaymentType is not (1 or 2 or 3) || b.Amount < 0.01) return WriteResult.Err(422, "Geçersiz ödeme bilgileri.");
        if (b.PaymentType == 2 && b.FundStorageId is null) return WriteResult.Err(422, "Kripto ödemede fon deposu seçimi zorunludur.");
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        if (b.FundStorageId is not null)
        {
            var bal = await c.ExecuteScalarAsync<double?>("SELECT balance FROM fund_storages WHERE id=@id", new { id = b.FundStorageId });
            if (bal is null) return WriteResult.Err(404, "Fon deposu bulunamadı.");
            if (bal < b.Amount) return WriteResult.Err(422, "Depoda yeterli bakiye yok.");
        }
        if (await c.ExecuteScalarAsync<int>("SELECT EXISTS(SELECT 1 FROM new_intermediaries WHERE id=@id)", new { id }) != 1)
            return WriteResult.Err(404, "Aracı bulunamadı.");

        await c.ExecuteAsync(@"INSERT INTO intermediary_payments (intermediary_id, payment_type, amount, crypto_quantity, crypto_rate, tx_link, fund_storage_id, team_id, description, created_by, created_at)
            VALUES (@id,@pt,@amt,@cq,@cr,@tx,@fs,@team,@desc,@by,@at)",
            new { id, pt = b.PaymentType, amt = b.Amount, cq = b.PaymentType == 2 ? b.CryptoQuantity : null, cr = b.PaymentType == 2 ? b.CryptoRate : null,
                tx = b.PaymentType == 2 ? b.TxLink : null, fs = b.PaymentType == 2 ? b.FundStorageId : null, team = b.PaymentType == 3 ? b.TeamId : null,
                desc = b.Description, by = actor.UserId, at = PaymentDate(b.PaymentDate) });
        await c.ExecuteAsync("UPDATE new_intermediaries SET balance = balance - @amt WHERE id=@id", new { id, amt = b.Amount });
        if (b.PaymentType == 2 && b.FundStorageId is not null)
            await c.ExecuteAsync("UPDATE fund_storages SET balance = balance - @amt WHERE id=@fs", new { fs = b.FundStorageId, amt = b.Amount });
        if (b.PaymentType == 3 && b.TeamId is not null)
            await c.ExecuteAsync("UPDATE teams SET overturn = overturn - @amt WHERE id=@t", new { t = b.TeamId, amt = b.Amount });
        return WriteResult.Ok;
    }

    public async Task<WriteResult> DeleteIntermediaryPaymentAsync(int id, int paymentId, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var p = await c.QueryFirstOrDefaultAsync("SELECT * FROM intermediary_payments WHERE id=@pid AND intermediary_id=@id", new { pid = paymentId, id });
        if (p is null) return WriteResult.Err(404, "Ödeme bulunamadı.");
        if (((DateTime)p.created_at).Date < _clock.Today) return WriteResult.Err(422, "Sadece bugüne ait ödemeler silinebilir.");
        await c.ExecuteAsync("UPDATE new_intermediaries SET balance = balance + @amt WHERE id=@id", new { id, amt = (decimal)p.amount });
        if (p.fund_storage_id is not null)
            await c.ExecuteAsync("UPDATE fund_storages SET balance = balance + @amt WHERE id=@fs", new { fs = (int)p.fund_storage_id, amt = (decimal)p.amount });
        if ((int)p.payment_type == 3 && p.team_id is not null)
            await c.ExecuteAsync("UPDATE teams SET overturn = overturn + @amt WHERE id=@t", new { t = (int)p.team_id, amt = (decimal)p.amount });
        await c.ExecuteAsync("DELETE FROM intermediary_payments WHERE id=@pid", new { pid = paymentId });
        return WriteResult.Ok;
    }

    // ========================= PARTNER =========================
    public async Task<object> PartnerIndexAsync(CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var today = Today;
        var payliraLast = await LastSnap(c, "paylira", null, today);
        var payliraCurrent = await GetPayliraCurrentNet(c, today, payliraLast);
        var todayNet = payliraCurrent - payliraLast;

        var list = new List<object>();
        foreach (var p in await c.QueryAsync("SELECT id, name, share_percent FROM paylira_partners WHERE status=1 ORDER BY id DESC"))
        {
            int id = (int)p.id;
            var lastSnap = await LastSnap(c, "partner", id, today);
            var share = Convert.ToDouble(p.share_percent);
            var todayShare = todayNet * share / 100;
            var todayPay = await SumDate(c, "paylira_partner_payments", "partner_id", id, today);
            var todayCap = await SumDate(c, "partner_capitals", "partner_id", id, today);
            var todayExp = await Sum(c, @"SELECT COALESCE(SUM(s.amount),0) FROM paylira_expense_shares s JOIN paylira_expenses e ON s.expense_id=e.id
                WHERE s.partner_id=@id AND DATE(e.created_at)=@d", new { id, d = today });
            list.Add(new { id, name = (string)p.name, share_percent = p.share_percent, current_case = Math.Round(lastSnap + todayShare + todayCap - todayPay - todayExp, 2) });
        }
        return list;
    }

    public async Task<object?> PartnerShowAsync(int id, string? from, string? to, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var today = Today;
        var partner = await c.QueryFirstOrDefaultAsync("SELECT * FROM paylira_partners WHERE id=@id", new { id });
        if (partner is null) return null;

        var snapSql = @"SELECT DATE_FORMAT(snapshot_date,'%Y-%m-%d') AS SnapshotDate, amount AS Amount, details AS Details FROM daily_case_snapshots
                        WHERE entity_type='partner' AND entity_id=@id AND snapshot_date<@today";
        var pp = new DynamicParameters(); pp.Add("id", id); pp.Add("today", today);
        if (from is not null && to is not null) { snapSql += " AND snapshot_date>=@from AND snapshot_date<=@to ORDER BY snapshot_date DESC"; pp.Add("from", from); pp.Add("to", to); }
        else snapSql += " ORDER BY snapshot_date DESC LIMIT 30";
        var snaps = (await c.QueryAsync<SnapshotRow>(snapSql, pp)).ToList();

        var daily = new List<object>();
        foreach (var s in snaps)
        {
            var det = ParseDetails(s.Details);
            var pay = await SumDate(c, "paylira_partner_payments", "partner_id", id, s.SnapshotDate);
            var cap = await SumDate(c, "partner_capitals", "partner_id", id, s.SnapshotDate);
            var exp = await Sum(c, @"SELECT COALESCE(SUM(sh.amount),0) FROM paylira_expense_shares sh JOIN paylira_expenses e ON sh.expense_id=e.id
                WHERE sh.partner_id=@id AND DATE(e.created_at)=@d", new { id, d = s.SnapshotDate });
            daily.Add(new { date = s.SnapshotDate, amount = s.Amount, previous_balance = Detail(det, "previous_balance"), daily_share = Detail(det, "daily_share"), capitals = Math.Round(cap, 2), expenses = Math.Round(exp, 2), payments = Math.Round(pay, 2) });
        }

        var lastSnap = await LastSnap(c, "partner", id, today);
        var payliraLast = await LastSnap(c, "paylira", null, today);
        var payliraCurrent = await GetPayliraCurrentNet(c, today, payliraLast);
        var todayNet = payliraCurrent - payliraLast;
        var share = Convert.ToDouble(partner.share_percent);
        var todayShare = todayNet * share / 100;
        var todayPay = await SumDate(c, "paylira_partner_payments", "partner_id", id, today);
        var todayCap = await SumDate(c, "partner_capitals", "partner_id", id, today);
        var todayExp = await Sum(c, @"SELECT COALESCE(SUM(sh.amount),0) FROM paylira_expense_shares sh JOIN paylira_expenses e ON sh.expense_id=e.id
            WHERE sh.partner_id=@id AND DATE(e.created_at)=@d", new { id, d = today });
        var currentCase = lastSnap + todayShare + todayCap - todayPay - todayExp;
        daily.Insert(0, new { date = today, amount = Math.Round(currentCase, 2), previous_balance = Math.Round(lastSnap, 2), daily_share = Math.Round(todayShare, 2), capitals = Math.Round(todayCap, 2), expenses = Math.Round(todayExp, 2), payments = Math.Round(todayPay, 2), is_today = true });

        return new { partner, current_case = Math.Round(currentCase, 2), daily_cases = daily };
    }

    public async Task<object> PartnerPaymentsAsync(int id, string? date, string? from, string? to, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        string dc(string col) => date is not null ? $" AND DATE({col})=@date" : (from is not null && to is not null ? $" AND DATE({col}) BETWEEN @from AND @to" : "");
        var pr = new { id, date, from, to };
        var items = new List<MovementItem>();

        foreach (var p in await c.QueryAsync($@"SELECT pp.*, u.name AS created_by_name FROM paylira_partner_payments pp LEFT JOIN users u ON pp.created_by=u.id
            WHERE pp.partner_id=@id{dc("pp.created_at")} ORDER BY pp.created_at DESC LIMIT 200", pr))
        {
            string? srcName = null;
            if ((int)p.payment_type == 2 && p.fund_storage_id is not null) srcName = await c.ExecuteScalarAsync<string?>("SELECT name FROM fund_storages WHERE id=@i", new { i = (int)p.fund_storage_id });
            else if ((int)p.payment_type == 3 && p.team_id is not null) srcName = await c.ExecuteScalarAsync<string?>("SELECT name FROM teams WHERE id=@i", new { i = (int)p.team_id });
            items.Add(new MovementItem { Id = ((int)p.id).ToString(), Source = "payment", Amount = Convert.ToDouble(p.amount), Description = p.description, CreatedAt = p.created_at, Target = srcName, CreatedBy = p.created_by_name });
        }
        foreach (var e in await c.QueryAsync($@"SELECT sh.id, sh.amount, e.description, e.created_at, t.name AS team_name, u.name AS created_by_name
            FROM paylira_expense_shares sh JOIN paylira_expenses e ON sh.expense_id=e.id LEFT JOIN teams t ON e.team_id=t.id LEFT JOIN users u ON e.created_by=u.id
            WHERE sh.partner_id=@id{dc("e.created_at")} ORDER BY e.created_at DESC LIMIT 200", pr))
            items.Add(new MovementItem { Id = "exp_" + (int)e.id, Source = "expense", Amount = Convert.ToDouble(e.amount), Description = e.description, CreatedAt = e.created_at, Target = e.team_name, CreatedBy = e.created_by_name });
        foreach (var t in await c.QueryAsync($@"SELECT pt.*, par.name AS to_partner_name, u.name AS created_by_name FROM partner_transfers pt
            LEFT JOIN paylira_partners par ON pt.to_partner_id=par.id LEFT JOIN users u ON pt.created_by=u.id
            WHERE pt.from_partner_id=@id{dc("pt.created_at")} ORDER BY pt.created_at DESC LIMIT 200", pr))
            items.Add(new MovementItem { Id = "pto_" + (int)t.id, Source = "partner_transfer_out", Amount = Convert.ToDouble(t.amount), Description = t.description, CreatedAt = t.created_at, Target = t.to_partner_name, CreatedBy = t.created_by_name });
        foreach (var t in await c.QueryAsync($@"SELECT pt.*, par.name AS from_partner_name, u.name AS created_by_name FROM partner_transfers pt
            LEFT JOIN paylira_partners par ON pt.from_partner_id=par.id LEFT JOIN users u ON pt.created_by=u.id
            WHERE pt.to_partner_id=@id{dc("pt.created_at")} ORDER BY pt.created_at DESC LIMIT 200", pr))
            items.Add(new MovementItem { Id = "pti_" + (int)t.id, Source = "partner_transfer_in", Amount = Convert.ToDouble(t.amount), Description = t.description, CreatedAt = t.created_at, Target = t.from_partner_name, CreatedBy = t.created_by_name });
        foreach (var cap in await c.QueryAsync($@"SELECT pc.*, u.name AS created_by_name FROM partner_capitals pc LEFT JOIN users u ON pc.created_by=u.id
            WHERE pc.partner_id=@id{dc("pc.created_at")} ORDER BY pc.created_at DESC LIMIT 200", pr))
            items.Add(new MovementItem { Id = "cap_" + (int)cap.id, Source = "capital", Amount = Convert.ToDouble(cap.amount), Description = cap.description, CreatedAt = cap.created_at, CreatedBy = cap.created_by_name });

        var combined = items.OrderByDescending(x => x.CreatedAt).ToList();
        return new { payments = combined, total = Math.Round(combined.Sum(x => x.Amount), 2) };
    }

    public async Task<WriteResult> AddPartnerPaymentAsync(int id, PartnerPaymentBody b, ActorInfo actor, CancellationToken ct = default)
    {
        if (b.PaymentType is not (1 or 2 or 3) || b.Amount < 0.01) return WriteResult.Err(422, "Geçersiz ödeme bilgileri.");
        if (b.PaymentType == 2 && b.FundStorageId is null) return WriteResult.Err(422, "Kripto ödemede fon deposu seçimi zorunludur.");
        if (b.PaymentType == 3 && b.TeamId is null) return WriteResult.Err(422, "Takım ödemede takım seçimi zorunludur.");
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        if (b.FundStorageId is not null)
        {
            var bal = await c.ExecuteScalarAsync<double?>("SELECT balance FROM fund_storages WHERE id=@id", new { id = b.FundStorageId });
            if (bal is null) return WriteResult.Err(404, "Fon deposu bulunamadı.");
            if (bal < b.Amount) return WriteResult.Err(422, "Depoda yeterli bakiye yok.");
        }
        await c.ExecuteAsync(@"INSERT INTO paylira_partner_payments (partner_id, payment_type, amount, crypto_quantity, crypto_rate, tx_link, fund_storage_id, team_id, is_capital, description, created_by, created_at)
            VALUES (@id,@pt,@amt,@cq,@cr,@tx,@fs,@team,@cap,@desc,@by,@at)",
            new { id, pt = b.PaymentType, amt = b.Amount, cq = b.PaymentType == 2 ? b.CryptoQuantity : null, cr = b.PaymentType == 2 ? b.CryptoRate : null,
                tx = b.PaymentType == 2 ? b.TxLink : null, fs = b.PaymentType == 2 ? b.FundStorageId : null, team = b.PaymentType == 3 ? b.TeamId : null,
                cap = b.IsCapital == true ? 1 : 0, desc = b.Description, by = actor.UserId, at = PaymentDate(b.PaymentDate) });
        if (b.PaymentType == 2 && b.FundStorageId is not null) await c.ExecuteAsync("UPDATE fund_storages SET balance = balance - @amt WHERE id=@fs", new { fs = b.FundStorageId, amt = b.Amount });
        if (b.PaymentType == 3 && b.TeamId is not null) await c.ExecuteAsync("UPDATE teams SET overturn = overturn - @amt WHERE id=@t", new { t = b.TeamId, amt = b.Amount });
        return WriteResult.Ok;
    }

    public async Task<WriteResult> DeletePartnerPaymentAsync(int id, int paymentId, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var p = await c.QueryFirstOrDefaultAsync("SELECT * FROM paylira_partner_payments WHERE id=@pid AND partner_id=@id", new { pid = paymentId, id });
        if (p is null) return WriteResult.Err(404, "Ödeme bulunamadı.");
        if (((DateTime)p.created_at).Date < _clock.Today) return WriteResult.Err(422, "Sadece bugüne ait ödemeler silinebilir.");
        if (p.fund_storage_id is not null) await c.ExecuteAsync("UPDATE fund_storages SET balance = balance + @amt WHERE id=@fs", new { fs = (int)p.fund_storage_id, amt = (decimal)p.amount });
        if (p.team_id is not null) await c.ExecuteAsync("UPDATE teams SET overturn = overturn + @amt WHERE id=@t", new { t = (int)p.team_id, amt = (decimal)p.amount });
        await c.ExecuteAsync("DELETE FROM paylira_partner_payments WHERE id=@pid", new { pid = paymentId });
        return WriteResult.Ok;
    }

    public async Task<object> CapitalsAsync(int id, string? from, string? to, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        string dc = from is not null && to is not null ? " AND DATE(created_at) BETWEEN @from AND @to" : "";
        var rows = await c.QueryAsync($@"SELECT pc.*, (SELECT name FROM fund_storages WHERE id=pc.fund_storage_id) AS fund_storage_name,
            (SELECT name FROM paylira_partners WHERE id=pc.partner_id) AS partner_name
            FROM partner_capitals pc WHERE pc.partner_id=@id{dc} ORDER BY pc.created_at DESC LIMIT 200", new { id, from, to });
        return new { capitals = rows, total = Math.Round(rows.Sum(r => (double)(decimal)r.amount), 2) };
    }

    public async Task<WriteResult> AddCapitalAsync(int id, CapitalBody b, ActorInfo actor, CancellationToken ct = default)
    {
        if (b.PaymentType is not (1 or 2) || b.Amount < 0.01) return WriteResult.Err(422, "Geçersiz bilgiler.");
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        await c.ExecuteAsync(@"INSERT INTO partner_capitals (partner_id, payment_type, amount, crypto_quantity, crypto_rate, tx_link, fund_storage_id, description, created_by, created_at)
            VALUES (@id,@pt,@amt,@cq,@cr,@tx,@fs,@desc,@by,@at)",
            new { id, pt = b.PaymentType, amt = b.Amount, cq = b.PaymentType == 2 ? b.CryptoQuantity : null, cr = b.PaymentType == 2 ? b.CryptoRate : null,
                tx = b.PaymentType == 2 ? b.TxLink : null, fs = b.FundStorageId, desc = b.Description, by = actor.UserId, at = PaymentDate(b.PaymentDate) });
        await c.ExecuteAsync("UPDATE fund_storages SET balance = balance + @amt WHERE id=@fs", new { fs = b.FundStorageId, amt = b.Amount });
        return WriteResult.Ok;
    }

    public async Task<WriteResult> DeleteCapitalAsync(int id, int capitalId, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var cap = await c.QueryFirstOrDefaultAsync("SELECT * FROM partner_capitals WHERE id=@cid AND partner_id=@id", new { cid = capitalId, id });
        if (cap is null) return WriteResult.Err(404, "Sermaye bulunamadı.");
        if (((DateTime)cap.created_at).Date < _clock.Today) return WriteResult.Err(422, "Sadece bugüne ait sermayeler silinebilir.");
        if (cap.fund_storage_id is not null) await c.ExecuteAsync("UPDATE fund_storages SET balance = balance - @amt WHERE id=@fs", new { fs = (int)cap.fund_storage_id, amt = (decimal)cap.amount });
        await c.ExecuteAsync("DELETE FROM partner_capitals WHERE id=@cid", new { cid = capitalId });
        return WriteResult.Ok;
    }

    public async Task<object> ExpensesAsync(string? from, string? to, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        string dc = from is not null && to is not null ? " AND DATE(created_at) BETWEEN @from AND @to" : "";
        var expenses = (await c.QueryAsync($"SELECT * FROM paylira_expenses WHERE 1=1{dc} ORDER BY created_at DESC LIMIT 200", new { from, to })).ToList();
        var list = new List<object>();
        foreach (var e in expenses)
        {
            var shares = await c.QueryAsync(@"SELECT sh.*, par.name AS partner_name FROM paylira_expense_shares sh JOIN paylira_partners par ON sh.partner_id=par.id WHERE expense_id=@id", new { id = (int)e.id });
            list.Add(new { id = (int)e.id, amount = (double)(decimal)e.amount, description = (string?)e.description, created_at = e.created_at, shares });
        }
        return new { expenses = list, total = Math.Round(expenses.Sum(e => (double)(decimal)e.amount), 2) };
    }

    public async Task<WriteResult> AddExpenseAsync(ExpenseBody b, ActorInfo actor, CancellationToken ct = default)
    {
        if (b.Amount < 0.01 || b.Shares is null || b.Shares.Count == 0) return WriteResult.Err(422, "Geçersiz masraf bilgileri.");
        if (b.TeamId is null && b.FundStorageId is null) return WriteResult.Err(422, "Takım ya da fon deposu seçimi zorunludur.");
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var expenseId = await c.ExecuteScalarAsync<long>(@"INSERT INTO paylira_expenses (amount, description, team_id, fund_storage_id, created_by, created_at)
            VALUES (@amt,@desc,@team,@fs,@by,@at); SELECT LAST_INSERT_ID();",
            new { amt = b.Amount, desc = b.Description, team = b.TeamId, fs = b.FundStorageId, by = actor.UserId, at = PaymentDate(b.PaymentDate) });
        if (b.TeamId is not null) await c.ExecuteAsync("UPDATE teams SET overturn = overturn - @amt WHERE id=@t", new { t = b.TeamId, amt = b.Amount });
        if (b.FundStorageId is not null) await c.ExecuteAsync("UPDATE fund_storages SET balance = balance - @amt WHERE id=@fs", new { fs = b.FundStorageId, amt = b.Amount });
        foreach (var s in b.Shares)
            await c.ExecuteAsync("INSERT INTO paylira_expense_shares (expense_id, partner_id, amount) VALUES (@e,@p,@a)", new { e = expenseId, p = s.PartnerId, a = s.Amount });
        return WriteResult.Ok;
    }

    public async Task<WriteResult> DeleteExpenseAsync(int id, ActorInfo actor, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var e = await c.QueryFirstOrDefaultAsync("SELECT * FROM paylira_expenses WHERE id=@id", new { id });
        if (e is null) return WriteResult.Err(404, "Masraf bulunamadı.");
        if (((DateTime)e.created_at).Date < _clock.Today) return WriteResult.Err(422, "Sadece bugüne ait masraflar silinebilir.");
        var shares = await c.QueryAsync("SELECT * FROM paylira_expense_shares WHERE expense_id=@id", new { id });
        var snap = JsonSerializer.Serialize(new { expense = (object)e, shares });
        await c.ExecuteAsync(@"INSERT INTO team_action_log (team_id, entity_type, entity_id, action, amount, description, data_snapshot, performed_by, performer_name, performed_at)
            VALUES (@team,'paylira_expense',@id,'delete',@amt,@desc,@snap,@by,@name,@at)",
            new { team = (int?)e.team_id, id, amt = (decimal)e.amount, desc = (string?)e.description, snap, by = actor.UserId, name = actor.Name, at = NowStr });
        if (e.team_id is not null) await c.ExecuteAsync("UPDATE teams SET overturn = overturn + @amt WHERE id=@t", new { t = (int)e.team_id, amt = (decimal)e.amount });
        if (e.fund_storage_id is not null) await c.ExecuteAsync("UPDATE fund_storages SET balance = balance + @amt WHERE id=@fs", new { fs = (int)e.fund_storage_id, amt = (decimal)e.amount });
        await c.ExecuteAsync("DELETE FROM paylira_expense_shares WHERE expense_id=@id", new { id });
        await c.ExecuteAsync("DELETE FROM paylira_expenses WHERE id=@id", new { id });
        return WriteResult.Ok;
    }

    public async Task<object> AllPartnerPaymentsAsync(string? from, string? to, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        string dc = from is not null && to is not null ? " AND DATE(pp.created_at) BETWEEN @from AND @to" : "";
        var rows = await c.QueryAsync($@"SELECT pp.*, par.name AS partner_name, fs.name AS fund_storage_name, t.name AS team_name, u.name AS created_by_name
            FROM paylira_partner_payments pp LEFT JOIN paylira_partners par ON pp.partner_id=par.id
            LEFT JOIN fund_storages fs ON pp.fund_storage_id=fs.id LEFT JOIN teams t ON pp.team_id=t.id LEFT JOIN users u ON pp.created_by=u.id
            WHERE 1=1{dc} ORDER BY pp.created_at DESC LIMIT 500", new { from, to });
        var list = rows.Select(p => (object)new
        {
            id = (int)p.id, partner_name = (string?)p.partner_name, payment_type = (int)p.payment_type, amount = (double)(decimal)p.amount,
            fund_storage_name = (string?)p.fund_storage_name, team_name = (string?)p.team_name, description = (string?)p.description,
            created_by_name = (string?)p.created_by_name, created_at = p.created_at,
        }).ToList();
        return new { payments = list, total = Math.Round(rows.Sum(p => (double)(decimal)p.amount), 2) };
    }

    public async Task<WriteResult> AddPartnerTransferAsync(int id, PartnerTransferBody b, ActorInfo actor, CancellationToken ct = default)
    {
        if (id == b.ToPartnerId) return WriteResult.Err(422, "Aynı partnere transfer yapılamaz.");
        if (b.Amount < 0.01) return WriteResult.Err(422, "Tutar geçersiz.");
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        if (await c.ExecuteScalarAsync<int>("SELECT EXISTS(SELECT 1 FROM paylira_partners WHERE id=@id)", new { id = b.ToPartnerId }) != 1)
            return WriteResult.Err(404, "Hedef partner bulunamadı.");
        await c.ExecuteAsync(@"INSERT INTO partner_transfers (from_partner_id, to_partner_id, amount, description, created_by, created_at)
            VALUES (@from,@to,@amt,@desc,@by,@at)", new { from = id, to = b.ToPartnerId, amt = b.Amount, desc = b.Description, by = actor.UserId, at = PaymentDate(b.PaymentDate) });
        return WriteResult.Ok;
    }

    public async Task<WriteResult> DeletePartnerTransferAsync(int id, int transferId, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var t = await c.QueryFirstOrDefaultAsync("SELECT * FROM partner_transfers WHERE id=@tid AND from_partner_id=@id", new { tid = transferId, id });
        if (t is null) return WriteResult.Err(404, "Transfer bulunamadı.");
        if (((DateTime)t.created_at).Date < _clock.Today) return WriteResult.Err(422, "Sadece bugüne ait transferler silinebilir.");
        await c.ExecuteAsync("DELETE FROM partner_transfers WHERE id=@tid", new { tid = transferId });
        return WriteResult.Ok;
    }

    // ========================= INITIAL BALANCE =========================
    public async Task<object> InitialBalanceEntitiesAsync(CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var allMerchants = (await c.QueryAsync("SELECT id, name, group_id FROM merchantUser ORDER BY name")).ToList();
        var groups = (await c.QueryAsync("SELECT id, name FROM merchant_groups")).ToDictionary(g => (uint)g.id, g => (string)g.name);
        var merchantItems = new List<object>();
        var processed = new HashSet<uint>();
        foreach (var m in allMerchants)
        {
            uint? gid = m.group_id is null ? null : (uint)m.group_id;
            if (gid is not null && groups.ContainsKey(gid.Value))
            {
                if (!processed.Add(gid.Value)) continue;
                merchantItems.Add(new { type = "merchant_group", id = (int)gid.Value, name = groups[gid.Value], amount = 0 });
            }
            else merchantItems.Add(new { type = "merchant", id = (int)m.id, name = (string)m.name, amount = 0 });
        }
        var teams = (await c.QueryAsync("SELECT id, name FROM teams ORDER BY name")).Select(t => (object)new { type = "team", id = (int)t.id, name = (string)t.name, amount = 0 });
        var inters = (await c.QueryAsync("SELECT id, name FROM new_intermediaries ORDER BY name")).Select(i => (object)new { type = "intermediary", id = (int)i.id, name = (string)i.name, amount = 0 });
        var partners = (await c.QueryAsync("SELECT id, name FROM paylira_partners ORDER BY id")).Select(p => (object)new { type = "partner", id = (int)p.id, name = (string)p.name, amount = 0 });
        var paylira = new[] { new { type = "paylira", id = (int?)null, name = "Paylira Net", amount = 0 } };
        return new { merchants = merchantItems, teams, intermediaries = inters, partners, paylira };
    }

    public async Task SaveInitialBalanceAsync(string date, IReadOnlyList<InitialEntityInput> entities, string userName, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        await c.ExecuteAsync("DELETE FROM daily_case_snapshots WHERE snapshot_date=@d", new { d = date });
        await c.ExecuteAsync("DELETE FROM initial_balances WHERE snapshot_date=@d", new { d = date });
        foreach (var e in entities)
        {
            await c.ExecuteAsync(@"INSERT INTO daily_case_snapshots (snapshot_date, entity_type, entity_id, entity_name, amount, details, created_at)
                VALUES (@d,@et,@eid,@name,@amt,@det,@at)",
                new { d = date, et = e.Type, eid = e.Id, name = e.Name, amt = e.Amount, det = JsonSerializer.Serialize(new { initial_balance = true, set_by = userName }), at = NowStr });
            await c.ExecuteAsync(@"INSERT INTO initial_balances (snapshot_date, entity_type, entity_id, entity_name, amount, set_by, created_at)
                VALUES (@d,@et,@eid,@name,@amt,@by,@at)",
                new { d = date, et = e.Type, eid = e.Id, name = e.Name, amt = e.Amount, by = userName, at = NowStr });
        }
    }

    public async Task<WriteResult> ResetInitialBalanceAsync(string date, string userName, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var initials = (await c.QueryAsync("SELECT * FROM initial_balances WHERE snapshot_date=@d", new { d = date })).ToList();
        if (initials.Count == 0) return WriteResult.Err(404, "Bu tarih için başlangıç bakiyesi bulunamadı.");
        await c.ExecuteAsync("DELETE FROM daily_case_snapshots WHERE snapshot_date>=@d", new { d = date });
        await c.ExecuteAsync("UPDATE new_intermediaries SET balance=0");
        foreach (var init in initials)
            await c.ExecuteAsync(@"INSERT INTO daily_case_snapshots (snapshot_date, entity_type, entity_id, entity_name, amount, details, created_at)
                VALUES (@d,@et,@eid,@name,@amt,@det,@at)",
                new { d = (string)init.snapshot_date.ToString("yyyy-MM-dd"), et = (string)init.entity_type, eid = (int?)init.entity_id, name = (string)init.entity_name, amt = (decimal)init.amount, det = JsonSerializer.Serialize(new { initial_balance = true, reset_by = userName }), at = NowStr });
        return WriteResult.Ok;
    }
}
