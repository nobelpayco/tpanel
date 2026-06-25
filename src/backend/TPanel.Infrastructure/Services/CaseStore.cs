using System.Data;
using System.Text.Json;
using Dapper;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.Cases;

namespace TPanel.Infrastructure.Services;

/// <summary>Kasa muhasebesi veri erişimi (Dapper) — Team/Merchant/FundStorage controller SQL'leri birebir.</summary>
public partial class CaseStore : ICaseStore
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public CaseStore(IDbConnectionFactory factory, IClock clock)
    {
        _factory = factory;
        _clock = clock;
    }

    private string Today => _clock.Today.ToString("yyyy-MM-dd");
    private string NowStr => _clock.Now.ToString("yyyy-MM-dd HH:mm:ss");

    private static async Task<double> Sum(IDbConnection c, string sql, object p)
        => await c.ExecuteScalarAsync<double?>(sql, p) ?? 0;

    // Tarih bazlı toplam (DATE(created_at)=@d)
    private static Task<double> SumDate(IDbConnection c, string table, string col, object id, string date, string sumCol = "amount", string extra = "")
        => Sum(c, $"SELECT COALESCE(SUM({sumCol}),0) FROM {table} WHERE {col}=@id AND DATE(created_at)=@d {extra}", new { id, d = date });

    private static double Detail(JsonElement? d, string key)
    {
        if (d is null) return 0;
        if (d.Value.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.Number) return v.GetDouble();
        return 0;
    }

    private static JsonElement? ParseDetails(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonDocument.Parse(json).RootElement.Clone(); } catch { return null; }
    }

    // ========================= TEAM =========================
    public async Task<IReadOnlyList<TeamBasic>> GetTeamsBasicAsync(CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        return (await c.QueryAsync<TeamBasic>("SELECT id, name FROM teams")).ToList();
    }

    public async Task<TeamMeta?> GetTeamMetaAsync(int id, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var r = await c.QueryFirstOrDefaultAsync("SELECT id, name, overturn, commission FROM teams WHERE id=@id", new { id });
        return r is null ? null : new TeamMeta((int)r.id, (string)r.name, Convert.ToDouble(r.overturn), Convert.ToDouble(r.commission));
    }

    public async Task<object> TeamShowAsync(int id, string? from, string? to, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var today = Today;

        var team = await c.QueryFirstOrDefaultAsync("SELECT * FROM teams WHERE id=@id", new { id });
        var commission = Convert.ToDouble(team.commission);
        var overturn = Convert.ToDouble(team.overturn);

        // snapshots
        var snapSql = @"SELECT DATE_FORMAT(snapshot_date,'%Y-%m-%d') AS SnapshotDate, amount AS Amount, details AS Details FROM daily_case_snapshots
                        WHERE entity_type='team' AND entity_id=@id AND snapshot_date < @today";
        var p = new DynamicParameters(); p.Add("id", id); p.Add("today", today);
        if (from is not null && to is not null) { snapSql += " AND snapshot_date >= @from AND snapshot_date <= @to ORDER BY snapshot_date DESC"; p.Add("from", from); p.Add("to", to); }
        else snapSql += " ORDER BY snapshot_date DESC LIMIT 30";
        var snaps = (await c.QueryAsync<SnapshotRow>(snapSql, p)).ToList();

        var daily = new List<object>();
        foreach (var s in snaps)
        {
            var det = ParseDetails(s.Details);
            var d = s.SnapshotDate;
            var payments = await SumDate(c, "team_payments", "team_id", id, d);
            var expenses = await SumDate(c, "paylira_expenses", "team_id", id, d);
            var partnerPay = await SumDate(c, "paylira_partner_payments", "team_id", id, d, "amount", "AND payment_type='3'");
            var interPay = await SumDate(c, "intermediary_payments", "team_id", id, d, "amount", "AND payment_type='3'");
            var transferOut = await SumDate(c, "team_transfers", "from_team_id", id, d);
            var transferIn = await SumDate(c, "team_transfers", "to_team_id", id, d);
            var syncs = await SumDate(c, "team_syncs", "team_id", id, d);
            daily.Add(new
            {
                date = d,
                amount = s.Amount,
                previous_balance = Detail(det, "overturn"),
                deposits = Detail(det, "deposits"),
                withdrawals = Detail(det, "withdrawals"),
                team_commission = Detail(det, "team_commission"),
                payments = Math.Round(payments + expenses + partnerPay + interPay + transferOut + Math.Max(syncs, 0), 2),
                transfers_in = Math.Round(transferIn + Math.Max(-syncs, 0), 2),
            });
        }

        // bugünkü canlı
        var lastSnap = await c.ExecuteScalarAsync<double?>(
            @"SELECT amount FROM daily_case_snapshots WHERE entity_type='team' AND entity_id=@id AND snapshot_date<@today
              ORDER BY snapshot_date DESC LIMIT 1", new { id, today }) ?? overturn;

        var tDep = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE team_id=@id AND type='1' AND status='3' AND DATE(finalize_date)=@d", new { id, d = today });
        var tWd = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE team_id=@id AND type='2' AND status='3' AND DATE(finalize_date)=@d", new { id, d = today });
        var tPay = await SumDate(c, "team_payments", "team_id", id, today);
        var tExp = await SumDate(c, "paylira_expenses", "team_id", id, today);
        var tPartner = await SumDate(c, "paylira_partner_payments", "team_id", id, today, "amount", "AND payment_type='3'");
        var tInter = await SumDate(c, "intermediary_payments", "team_id", id, today, "amount", "AND payment_type='3'");
        var tTransOut = await SumDate(c, "team_transfers", "from_team_id", id, today);
        var tTransIn = await SumDate(c, "team_transfers", "to_team_id", id, today);
        var tSync = await SumDate(c, "team_syncs", "team_id", id, today);

        var teamComm = tDep * commission / 100;
        var currentCase = lastSnap + tDep - teamComm - tWd - tPay - tExp - tPartner - tInter - tTransOut + tTransIn - tSync;

        daily.Insert(0, new
        {
            date = today,
            amount = Math.Round(currentCase, 2),
            previous_balance = Math.Round(lastSnap, 2),
            deposits = Math.Round(tDep, 2),
            withdrawals = Math.Round(tWd, 2),
            team_commission = Math.Round(teamComm, 2),
            payments = Math.Round(tPay + tExp + tPartner + tInter + tTransOut + Math.Max(tSync, 0), 2),
            transfers_in = Math.Round(tTransIn + Math.Max(-tSync, 0), 2),
            is_today = true,
        });

        var fundStorages = await c.QueryAsync("SELECT id, name, type FROM fund_storages WHERE status=1 ORDER BY name");

        return new { team, current_case = Math.Round(currentCase, 2), daily_cases = daily, fund_storages = fundStorages };
    }

    public async Task<object> TeamPaymentsAsync(int id, string? date, string? from, string? to, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        string DateClause(string col) => date is not null ? $" AND DATE({col})=@date"
            : (from is not null && to is not null ? $" AND DATE({col}) BETWEEN @from AND @to" : "");
        var pp = new { id, date, from, to };

        var items = new List<MovementItem>();

        foreach (var r in await c.QueryAsync($@"SELECT id, payment_type, amount, crypto_quantity, crypto_rate, tx_link, fund_storage_id, description, created_at
                FROM team_payments WHERE team_id=@id{DateClause("created_at")} ORDER BY created_at DESC LIMIT 200", pp))
        {
            string? fsName = r.fund_storage_id is null ? null
                : await c.ExecuteScalarAsync<string?>("SELECT name FROM fund_storages WHERE id=@i", new { i = (int)r.fund_storage_id });
            items.Add(new MovementItem { Id = ((int)r.id).ToString(), Source = "team_payment", Amount = Convert.ToDouble(r.amount), Description = r.description, CreatedAt = r.created_at, Target = fsName });
        }
        foreach (var r in await c.QueryAsync($@"SELECT pp.id, pp.amount, pp.description, pp.created_at, par.name AS partner_name
                FROM paylira_partner_payments pp JOIN paylira_partners par ON pp.partner_id=par.id
                WHERE pp.team_id=@id AND pp.payment_type='3'{DateClause("pp.created_at")} ORDER BY pp.created_at DESC LIMIT 200", pp))
            items.Add(new MovementItem { Id = "pp_" + (int)r.id, Source = "partner_payment", Amount = Convert.ToDouble(r.amount), Description = r.description, CreatedAt = r.created_at, Target = r.partner_name });
        foreach (var r in await c.QueryAsync($@"SELECT id, amount, description, created_at FROM paylira_expenses
                WHERE team_id=@id{DateClause("created_at")} ORDER BY created_at DESC LIMIT 200", pp))
            items.Add(new MovementItem { Id = "exp_" + (int)r.id, Source = "expense", Amount = Convert.ToDouble(r.amount), Description = r.description, CreatedAt = r.created_at });
        foreach (var r in await c.QueryAsync($@"SELECT ip.id, ip.amount, ip.description, ip.created_at, ni.name AS intermediary_name
                FROM intermediary_payments ip JOIN new_intermediaries ni ON ip.intermediary_id=ni.id
                WHERE ip.team_id=@id AND ip.payment_type='3'{DateClause("ip.created_at")} ORDER BY ip.created_at DESC LIMIT 200", pp))
            items.Add(new MovementItem { Id = "io_" + (int)r.id, Source = "intermediary_offset", Amount = Convert.ToDouble(r.amount), Description = r.description, CreatedAt = r.created_at, Target = r.intermediary_name });
        foreach (var r in await c.QueryAsync($@"SELECT tt.id, tt.amount, tt.description, tt.created_at, t.name AS to_team_name
                FROM team_transfers tt LEFT JOIN teams t ON tt.to_team_id=t.id
                WHERE tt.from_team_id=@id{DateClause("tt.created_at")} ORDER BY tt.created_at DESC LIMIT 200", pp))
            items.Add(new MovementItem { Id = "tto_" + (int)r.id, Source = "team_transfer_out", Amount = Convert.ToDouble(r.amount), Description = r.description, CreatedAt = r.created_at, Target = r.to_team_name });
        foreach (var r in await c.QueryAsync($@"SELECT tt.id, tt.amount, tt.description, tt.created_at, t.name AS from_team_name
                FROM team_transfers tt LEFT JOIN teams t ON tt.from_team_id=t.id
                WHERE tt.to_team_id=@id{DateClause("tt.created_at")} ORDER BY tt.created_at DESC LIMIT 200", pp))
            items.Add(new MovementItem { Id = "tti_" + (int)r.id, Source = "team_transfer_in", Amount = Convert.ToDouble(r.amount), Description = r.description, CreatedAt = r.created_at, Target = r.from_team_name });
        foreach (var r in await c.QueryAsync($@"SELECT s.id, s.amount, s.description, s.created_at, u.name AS created_by_name
                FROM team_syncs s LEFT JOIN users u ON s.created_by=u.id
                WHERE s.team_id=@id{DateClause("s.created_at")} ORDER BY s.created_at DESC LIMIT 200", pp))
            items.Add(new MovementItem { Id = "sync_" + (int)r.id, Source = "team_sync", Amount = Convert.ToDouble(r.amount), Description = r.description, CreatedAt = r.created_at, CreatedBy = r.created_by_name });

        var combined = items.OrderByDescending(x => x.CreatedAt).ToList();
        return new { payments = combined, total = Math.Round(combined.Sum(x => x.Amount), 2) };
    }

    public async Task<WriteResult> AddTeamPaymentAsync(int id, CasePaymentBody b, ActorInfo actor, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        await c.ExecuteAsync(@"INSERT INTO team_payments (team_id, payment_type, amount, crypto_quantity, crypto_rate, tx_link, fund_storage_id, description, created_by, created_at)
            VALUES (@id,@pt,@amt,@cq,@cr,@tx,@fs,@desc,@by,@at)",
            new { id, pt = b.PaymentType, amt = b.Amount,
                cq = b.PaymentType == 2 ? b.CryptoQuantity : null, cr = b.PaymentType == 2 ? b.CryptoRate : null,
                tx = b.PaymentType == 2 ? b.TxLink : null, fs = b.FundStorageId, desc = b.Description, by = actor.UserId,
                at = PaymentDate(b.PaymentDate) });
        await c.ExecuteAsync("UPDATE teams SET overturn = overturn - @amt WHERE id=@id", new { id, amt = b.Amount });
        if (b.FundStorageId is not null)
            await c.ExecuteAsync("UPDATE fund_storages SET balance = balance + @amt WHERE id=@fs", new { fs = b.FundStorageId, amt = b.Amount });
        return WriteResult.Ok;
    }

    public async Task<WriteResult> DeleteTeamPaymentAsync(int id, int paymentId, ActorInfo actor, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var p = await c.QueryFirstOrDefaultAsync("SELECT * FROM team_payments WHERE id=@pid AND team_id=@id", new { pid = paymentId, id });
        if (p is null) return WriteResult.Err(404, "Ödeme bulunamadı.");
        if (((DateTime)p.created_at).Date < _clock.Today) return WriteResult.Err(422, "Sadece bugüne ait ödemeler silinebilir.");
        await LogAction(c, id, "team_payment", p, actor);
        await c.ExecuteAsync("UPDATE teams SET overturn = overturn + @amt WHERE id=@id", new { id, amt = (decimal)p.amount });
        if (p.fund_storage_id is not null)
            await c.ExecuteAsync("UPDATE fund_storages SET balance = balance - @amt WHERE id=@fs", new { fs = (int)p.fund_storage_id, amt = (decimal)p.amount });
        await c.ExecuteAsync("DELETE FROM team_payments WHERE id=@pid", new { pid = paymentId });
        return WriteResult.Ok;
    }

    public async Task<WriteResult> AddTeamTransferAsync(int id, TeamTransferBody b, ActorInfo actor, CancellationToken ct = default)
    {
        if (b.ToTeamId == id) return WriteResult.Err(422, "Hedef takım kaynak ile aynı olamaz.");
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        if (await c.ExecuteScalarAsync<int>("SELECT EXISTS(SELECT 1 FROM teams WHERE id=@t)", new { t = b.ToTeamId }) != 1)
            return WriteResult.Err(404, "Hedef takım bulunamadı.");
        await c.ExecuteAsync(@"INSERT INTO team_transfers (from_team_id, to_team_id, amount, description, created_by, created_at)
            VALUES (@id,@to,@amt,@desc,@by,@at)", new { id, to = b.ToTeamId, amt = b.Amount, desc = b.Description, by = actor.UserId, at = PaymentDate(b.PaymentDate) });
        await c.ExecuteAsync("UPDATE teams SET overturn = overturn - @amt WHERE id=@id", new { id, amt = b.Amount });
        await c.ExecuteAsync("UPDATE teams SET overturn = overturn + @amt WHERE id=@to", new { to = b.ToTeamId, amt = b.Amount });
        return WriteResult.Ok;
    }

    public async Task<WriteResult> DeleteTeamTransferAsync(int id, int transferId, ActorInfo actor, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var t = await c.QueryFirstOrDefaultAsync("SELECT * FROM team_transfers WHERE id=@tid AND from_team_id=@id", new { tid = transferId, id });
        if (t is null) return WriteResult.Err(404, "Transfer bulunamadı.");
        if (((DateTime)t.created_at).Date < _clock.Today) return WriteResult.Err(422, "Sadece bugüne ait transferler silinebilir.");
        await LogAction(c, (int)t.from_team_id, "team_transfer_out", t, actor);
        await LogAction(c, (int)t.to_team_id, "team_transfer_in", t, actor);
        await c.ExecuteAsync("UPDATE teams SET overturn = overturn + @amt WHERE id=@id", new { id = (int)t.from_team_id, amt = (decimal)t.amount });
        await c.ExecuteAsync("UPDATE teams SET overturn = overturn - @amt WHERE id=@id", new { id = (int)t.to_team_id, amt = (decimal)t.amount });
        await c.ExecuteAsync("DELETE FROM team_transfers WHERE id=@tid", new { tid = transferId });
        return WriteResult.Ok;
    }

    public async Task<WriteResult> AddTeamSyncAsync(int id, TeamSyncBody b, ActorInfo actor, CancellationToken ct = default)
    {
        if (b.Amount == 0) return WriteResult.Err(422, "Tutar sıfır olamaz.");
        if (string.IsNullOrWhiteSpace(b.Description)) return WriteResult.Err(422, "Açıklama zorunludur.");
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        if (await c.ExecuteScalarAsync<int>("SELECT EXISTS(SELECT 1 FROM teams WHERE id=@id)", new { id }) != 1)
            return WriteResult.Err(404, "Takım bulunamadı.");
        await c.ExecuteAsync(@"INSERT INTO team_syncs (team_id, amount, description, created_by, created_at)
            VALUES (@id,@amt,@desc,@by,@at)", new { id, amt = b.Amount, desc = b.Description, by = actor.UserId, at = PaymentDate(b.PaymentDate) });
        await c.ExecuteAsync("UPDATE teams SET overturn = overturn - @amt WHERE id=@id", new { id, amt = b.Amount });
        return WriteResult.Ok;
    }

    public async Task<WriteResult> DeleteTeamSyncAsync(int id, int syncId, ActorInfo actor, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var s = await c.QueryFirstOrDefaultAsync("SELECT * FROM team_syncs WHERE id=@sid AND team_id=@id", new { sid = syncId, id });
        if (s is null) return WriteResult.Err(404, "Senkron bulunamadı.");
        if (((DateTime)s.created_at).Date < _clock.Today) return WriteResult.Err(422, "Sadece bugüne ait senkronlar silinebilir.");
        await LogAction(c, id, "team_sync", s, actor);
        await c.ExecuteAsync("UPDATE teams SET overturn = overturn + @amt WHERE id=@id", new { id, amt = (decimal)s.amount });
        await c.ExecuteAsync("DELETE FROM team_syncs WHERE id=@sid", new { sid = syncId });
        return WriteResult.Ok;
    }

    private async Task LogAction(IDbConnection c, int? teamId, string entityType, dynamic row, ActorInfo actor)
    {
        string snapshot;
        try { snapshot = JsonSerializer.Serialize((object)row); } catch { snapshot = "{}"; }
        decimal? amount = null;
        try { amount = (decimal?)row.amount; } catch { }
        string? desc = null;
        try { desc = (string?)row.description; } catch { }
        await c.ExecuteAsync(@"INSERT INTO team_action_log (team_id, entity_type, entity_id, action, amount, description, data_snapshot, performed_by, performer_name, performed_at)
            VALUES (@team,@et,@eid,'delete',@amt,@desc,@snap,@by,@name,@at)",
            new { team = teamId, et = entityType, eid = (int)row.id, amt = amount, desc = desc?.Length > 1000 ? desc[..1000] : desc, snap = snapshot, by = actor.UserId, name = actor.Name, at = NowStr });
    }

    private string PaymentDate(string? d) => string.IsNullOrEmpty(d) ? NowStr : d + " " + _clock.Now.ToString("HH:mm:ss");

    // ========================= MERCHANT =========================
    public async Task<object> MerchantIndexAsync(CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var today = Today;
        var all = (await c.QueryAsync("SELECT id, name, caseNow, commission, withdrawCommission, group_id FROM merchantUser WHERE status='1'")).ToList();
        var groups = (await c.QueryAsync("SELECT id, name FROM merchant_groups WHERE status=1")).ToDictionary(g => (uint)g.id, g => (string)g.name);

        var ids = all.Select(m => (int)m.id).ToList();
        Dictionary<int, double> dep = new(), wd = new(), pay = new();
        if (ids.Count > 0)
        {
            dep = (await c.QueryAsync("SELECT firm_id AS Id, COALESCE(SUM(amount),0) AS T FROM invest WHERE firm_id IN @ids AND type=1 AND status=3 AND DATE(finalize_date)=@d GROUP BY firm_id", new { ids, d = today })).ToDictionary(r => (int)r.Id, r => Convert.ToDouble((object)r.T));
            wd = (await c.QueryAsync("SELECT firm_id AS Id, COALESCE(SUM(amount),0) AS T FROM invest WHERE firm_id IN @ids AND type=2 AND status=3 AND DATE(finalize_date)=@d GROUP BY firm_id", new { ids, d = today })).ToDictionary(r => (int)r.Id, r => Convert.ToDouble((object)r.T));
            pay = (await c.QueryAsync("SELECT merchant_id AS Id, COALESCE(SUM(amount),0) AS T FROM merchant_payments WHERE merchant_id IN @ids AND DATE(created_at)=@d GROUP BY merchant_id", new { ids, d = today })).ToDictionary(r => (int)r.Id, r => Convert.ToDouble((object)r.T));
        }

        var processed = new HashSet<uint>();
        var list = new List<object>();
        double totalCase = 0;
        foreach (var m in all)
        {
            List<dynamic> groupMerchants; string displayName; string entityType; int entityId; double totalCaseNow;
            uint? gid = m.group_id is null ? null : (uint)m.group_id;
            if (gid is not null && groups.ContainsKey(gid.Value))
            {
                if (!processed.Add(gid.Value)) continue;
                groupMerchants = all.Where(x => x.group_id is not null && (uint)x.group_id == gid.Value).ToList();
                displayName = groups[gid.Value]; entityType = "merchant_group"; entityId = (int)gid.Value;
                totalCaseNow = groupMerchants.Sum(x => Convert.ToDouble(x.caseNow));
            }
            else { groupMerchants = new List<dynamic> { m }; displayName = (string)m.name; entityType = "merchant"; entityId = (int)m.id; totalCaseNow = Convert.ToDouble(m.caseNow); }

            var lastSnap = await c.ExecuteScalarAsync<double?>(
                "SELECT amount FROM daily_case_snapshots WHERE entity_type=@et AND entity_id=@eid AND snapshot_date<@today ORDER BY snapshot_date DESC LIMIT 1",
                new { et = entityType, eid = entityId, today }) ?? totalCaseNow;

            double netDep = 0, netWd = 0, p = 0;
            foreach (var gm in groupMerchants)
            {
                var gmid = (int)gm.id;
                double gdep = dep.GetValueOrDefault(gmid), gwd = wd.GetValueOrDefault(gmid);
                netDep += gdep - gdep * Convert.ToDouble(gm.commission) / 100;
                netWd += gwd + gwd * Convert.ToDouble(gm.withdrawCommission) / 100;
                p += pay.GetValueOrDefault(gmid);
            }
            var value = Math.Round(lastSnap + netDep - netWd - p, 2);
            totalCase += value;
            list.Add(new { id = entityId, name = displayName, group_id = gid, group_name = gid is not null ? displayName : null, value });
        }
        return new { merchants = list, total_case = Math.Round(totalCase, 2) };
    }

    public async Task<object?> MerchantShowAsync(int merchantId, bool isGroup, string? from, string? to, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var today = Today;
        List<dynamic> groupMerchants; string entityType; int entityId; string displayName; double totalCaseNow; dynamic mainMerchant;

        if (isGroup)
        {
            var group = await c.QueryFirstOrDefaultAsync("SELECT id, name FROM merchant_groups WHERE id=@id", new { id = merchantId });
            if (group is null) return null;
            groupMerchants = (await c.QueryAsync("SELECT id, name, caseNow, commission, withdrawCommission, deliveryCommission FROM merchantUser WHERE group_id=@id", new { id = merchantId })).ToList();
            entityType = "merchant_group"; entityId = merchantId; displayName = (string)group.name;
            totalCaseNow = groupMerchants.Sum(x => Convert.ToDouble(x.caseNow));
            mainMerchant = groupMerchants.FirstOrDefault();
            if (mainMerchant is null) return null;
        }
        else
        {
            var merchant = await c.QueryFirstOrDefaultAsync("SELECT id, name, caseNow, commission, withdrawCommission, deliveryCommission FROM merchantUser WHERE id=@id", new { id = merchantId });
            if (merchant is null) return null;
            groupMerchants = new List<dynamic> { merchant }; entityType = "merchant"; entityId = merchantId;
            displayName = (string)merchant.name; totalCaseNow = Convert.ToDouble(merchant.caseNow); mainMerchant = merchant;
        }

        var snapSql = @"SELECT DATE_FORMAT(snapshot_date,'%Y-%m-%d') AS SnapshotDate, amount AS Amount, details AS Details FROM daily_case_snapshots
                        WHERE entity_type=@et AND entity_id=@eid AND snapshot_date<@today";
        var pr = new DynamicParameters(); pr.Add("et", entityType); pr.Add("eid", entityId); pr.Add("today", today);
        if (from is not null && to is not null) { snapSql += " AND snapshot_date>=@from AND snapshot_date<=@to ORDER BY snapshot_date DESC"; pr.Add("from", from); pr.Add("to", to); }
        else snapSql += " ORDER BY snapshot_date DESC LIMIT 30";
        var snaps = (await c.QueryAsync<SnapshotRow>(snapSql, pr)).ToList();

        var daily = new List<object>();
        foreach (var s in snaps)
        {
            var det = ParseDetails(s.Details);
            daily.Add(new
            {
                date = s.SnapshotDate, amount = s.Amount,
                previous_balance = Detail(det, "previous_balance"), deposits = Detail(det, "deposits"),
                withdrawals = Detail(det, "withdrawals"), deposit_commission_amount = Detail(det, "deposit_commission_amount"),
                withdraw_commission_amount = Detail(det, "withdraw_commission_amount"), daily_change = Detail(det, "daily_change"),
                payments = Detail(det, "payments"), payment_commissions = Detail(det, "payment_commissions"),
            });
        }

        var lastSnapshot = await c.ExecuteScalarAsync<double?>(
            "SELECT amount FROM daily_case_snapshots WHERE entity_type=@et AND entity_id=@eid AND snapshot_date<@today ORDER BY snapshot_date DESC LIMIT 1",
            new { et = entityType, eid = entityId, today }) ?? totalCaseNow;

        double tDep = 0, tWd = 0, tPay = 0, netDeposit = 0, netWithdraw = 0, tPayComm = 0, tDepComm = 0, tWdComm = 0;
        foreach (var gm in groupMerchants)
        {
            var gmid = (int)gm.id;
            var dep = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE firm_id=@id AND type=1 AND status=3 AND DATE(finalize_date)=@d", new { id = gmid, d = today });
            var wd = await Sum(c, "SELECT COALESCE(SUM(amount),0) FROM invest WHERE firm_id=@id AND type=2 AND status=3 AND DATE(finalize_date)=@d", new { id = gmid, d = today });
            var pay = await SumDate(c, "merchant_payments", "merchant_id", gmid, today);
            var payComm = await SumDate(c, "merchant_payments", "merchant_id", gmid, today, "delivery_commission_amount");
            var depComm = dep * Convert.ToDouble(gm.commission) / 100;
            var wdComm = wd * Convert.ToDouble(gm.withdrawCommission) / 100;
            tDep += dep; tWd += wd; tPay += pay; tPayComm += payComm; tDepComm += depComm; tWdComm += wdComm;
            netDeposit += dep - depComm; netWithdraw += wd + wdComm;
        }
        var dailyChange = netDeposit - netWithdraw - tPay;
        var currentCase = lastSnapshot + dailyChange;

        daily.Insert(0, new
        {
            date = today, amount = Math.Round(currentCase, 2), previous_balance = Math.Round(lastSnapshot, 2),
            deposits = Math.Round(tDep, 2), withdrawals = Math.Round(tWd, 2),
            deposit_commission_amount = Math.Round(tDepComm, 2), withdraw_commission_amount = Math.Round(tWdComm, 2),
            daily_change = Math.Round(dailyChange, 2), payments = Math.Round(tPay, 2), payment_commissions = Math.Round(tPayComm, 2),
            is_today = true,
        });

        var tabs = new List<object>();
        if (isGroup && groupMerchants.Count > 1)
            foreach (var gm in groupMerchants)
                tabs.Add(new { id = (int)gm.id, name = (string)gm.name, commission = gm.commission, withdrawCommission = gm.withdrawCommission, deliveryCommission = gm.deliveryCommission });

        return new
        {
            merchant = new { id = entityId, name = displayName, commission = mainMerchant.commission, withdrawCommission = mainMerchant.withdrawCommission, deliveryCommission = mainMerchant.deliveryCommission },
            is_group = isGroup, tabs, current_case = Math.Round(currentCase, 2), daily_cases = daily,
        };
    }

    public async Task<object> PayliraDailyNetAsync(string? from, string? to, CancellationToken ct = default)
    {
        // Ağır rapor — Faz 5'te zenginleştirilecek; şimdilik snapshot tabanlı liste döner.
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var today = Today;
        var sql = @"SELECT DATE_FORMAT(snapshot_date,'%Y-%m-%d') AS SnapshotDate, amount AS Amount, details AS Details FROM daily_case_snapshots
                    WHERE entity_type='paylira' AND entity_id IS NULL AND snapshot_date<@today";
        var p = new DynamicParameters(); p.Add("today", today);
        if (from is not null && to is not null) { sql += " AND snapshot_date>=@from AND snapshot_date<=@to ORDER BY snapshot_date DESC"; p.Add("from", from); p.Add("to", to); }
        else sql += " ORDER BY snapshot_date DESC LIMIT 30";
        var snaps = (await c.QueryAsync<SnapshotRow>(sql, p)).ToList();

        var result = new List<object>();
        foreach (var s in snaps)
        {
            var det = ParseDetails(s.Details);
            var expenses = await SumDate(c, "paylira_expenses", "1", "1", s.SnapshotDate); // placeholder replaced below
            result.Add(new
            {
                date = s.SnapshotDate,
                previous_balance = Detail(det, "previous_balance"),
                daily_total = Detail(det, "daily_net"),
                cumulative = s.Amount,
            });
        }
        return result;
    }

    public async Task<object> MerchantPaymentsAsync(int merchantId, bool isGroup, string? date, string? from, string? to, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        string dc = date is not null ? " AND DATE(mp.created_at)=@date" : (from is not null && to is not null ? " AND DATE(mp.created_at) BETWEEN @from AND @to" : "");
        List<int> ids;
        if (isGroup) ids = (await c.QueryAsync<int>("SELECT id FROM merchantUser WHERE group_id=@id", new { id = merchantId })).ToList();
        else ids = new List<int> { merchantId };
        if (ids.Count == 0) return new { payments = Array.Empty<object>(), total = 0.0 };

        var rows = await c.QueryAsync($@"SELECT mp.*, m.name AS merchant_name FROM merchant_payments mp
            LEFT JOIN merchantUser m ON mp.merchant_id=m.id
            WHERE mp.merchant_id IN @ids{dc} ORDER BY mp.created_at DESC LIMIT 200", new { ids, date, from, to });
        var list = rows.Select(p => (object)new
        {
            id = (int)p.id, merchant_id = (int)p.merchant_id, merchant_name = (string?)p.merchant_name,
            payment_type = (int)p.payment_type, amount = p.amount,
            delivery_commission_rate = p.delivery_commission_rate, delivery_commission_amount = p.delivery_commission_amount,
            crypto_quantity = p.crypto_quantity, crypto_rate = p.crypto_rate, tx_link = (string?)p.tx_link,
            description = (string?)p.description, created_at = p.created_at,
        }).ToList();
        var total = rows.Sum(p => (double)(decimal)p.amount);
        return new { payments = list, total = Math.Round(total, 2) };
    }

    public async Task<WriteResult> AddMerchantPaymentAsync(int merchantId, MerchantPaymentBody b, ActorInfo actor, CancellationToken ct = default)
    {
        if (b.Amount == 0) return WriteResult.Err(422, "Tutar sıfır olamaz.");
        using var c = await _factory.CreateOpenConnectionAsync(ct);

        if (b.IsGroup == true)
        {
            var group = await c.QueryFirstOrDefaultAsync("SELECT id FROM merchant_groups WHERE id=@id", new { id = merchantId });
            if (group is null) return WriteResult.Err(404, "Grup bulunamadı.");
            if (b.TargetMerchantId is not null) merchantId = b.TargetMerchantId.Value;
            else
            {
                var first = await c.ExecuteScalarAsync<int?>("SELECT id FROM merchantUser WHERE group_id=@id ORDER BY id LIMIT 1", new { id = (int)group.id });
                if (first is null) return WriteResult.Err(404, "Grupta merchant bulunamadı.");
                merchantId = first.Value;
            }
        }

        if (b.PaymentType == 2 && b.FundStorageId is null) return WriteResult.Err(422, "Kripto ödemede fon deposu seçimi zorunludur.");
        if (b.FundStorageId is not null)
        {
            var storage = await c.QueryFirstOrDefaultAsync("SELECT balance FROM fund_storages WHERE id=@id", new { id = b.FundStorageId });
            if (storage is null) return WriteResult.Err(404, "Fon deposu bulunamadı.");
            if (b.Amount > 0 && Convert.ToDouble(storage.balance) < b.Amount)
                return WriteResult.Err(422, "Depoda yeterli bakiye yok.");
        }

        var merchant = await c.QueryFirstOrDefaultAsync("SELECT deliveryCommission FROM merchantUser WHERE id=@id", new { id = merchantId });
        if (merchant is null) return WriteResult.Err(404, "Merchant bulunamadı.");
        var deliveryRate = Convert.ToDouble(merchant.deliveryCommission);
        // TL ödemede komisyon onay kutusu: işaretsizse komisyon uygulanmaz (tutar tam işlenir).
        // Gönderilmezse (null) varsayılan: uygula (geriye dönük uyumlu). Kripto'yu etkilemez.
        bool applyCommission = b.ApplyCommission ?? true;
        double deliveryAmount = (b.Amount > 0 && applyCommission) ? Math.Round(b.Amount * deliveryRate / 100, 2) : 0;
        double effectiveRate = applyCommission ? deliveryRate : 0;
        double? paidAmount = null, deliveryProfit = null;
        if (b.PaymentType == 2)
        {
            deliveryProfit = b.Amount > 0 ? Math.Round(b.Amount * deliveryRate / 100, 2) : 0;
            paidAmount = Math.Round(b.Amount - deliveryProfit.Value, 2);
            deliveryAmount = 0;
            effectiveRate = deliveryRate;   // kripto: oran değişmez (toggle TL'ye özel)
        }

        await c.ExecuteAsync(@"INSERT INTO merchant_payments (merchant_id, payment_type, amount, paid_amount, delivery_profit, delivery_commission_rate, delivery_commission_amount, crypto_quantity, crypto_rate, tx_link, fund_storage_id, description, created_by, created_at)
            VALUES (@mid,@pt,@amt,@paid,@dp,@drate,@damt,@cq,@cr,@tx,@fs,@desc,@by,@at)",
            new { mid = merchantId, pt = b.PaymentType, amt = b.Amount, paid = paidAmount, dp = deliveryProfit,
                drate = effectiveRate, damt = deliveryAmount,
                cq = b.PaymentType == 2 ? b.CryptoQuantity : null, cr = b.PaymentType == 2 ? b.CryptoRate : null,
                tx = b.PaymentType == 2 ? b.TxLink : null, fs = b.PaymentType == 2 ? b.FundStorageId : null,
                desc = b.Description, by = actor.UserId, at = PaymentDate(b.PaymentDate) });

        await c.ExecuteAsync("UPDATE merchantUser SET caseNow = caseNow - @amt WHERE id=@id", new { id = merchantId, amt = b.Amount });
        if (b.PaymentType == 2 && b.FundStorageId is not null)
        {
            await c.ExecuteAsync("UPDATE fund_storages SET balance = balance - @amt WHERE id=@fs", new { fs = b.FundStorageId, amt = b.Amount });
            if (deliveryProfit > 0) await c.ExecuteAsync("UPDATE fund_storages SET balance = balance + @p WHERE id=@fs", new { fs = b.FundStorageId, p = deliveryProfit });
            else if (deliveryProfit < 0) await c.ExecuteAsync("UPDATE fund_storages SET balance = balance - @p WHERE id=@fs", new { fs = b.FundStorageId, p = Math.Abs(deliveryProfit.Value) });
        }
        return WriteResult.Ok;
    }

    public async Task<WriteResult> DeleteMerchantPaymentAsync(int merchantId, int paymentId, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var p = await c.QueryFirstOrDefaultAsync("SELECT * FROM merchant_payments WHERE id=@pid AND merchant_id=@mid", new { pid = paymentId, mid = merchantId });
        if (p is null) return WriteResult.Err(404, "Ödeme bulunamadı.");
        if (((DateTime)p.created_at).Date < _clock.Today) return WriteResult.Err(422, "Sadece bugüne ait ödemeler silinebilir.");
        await c.ExecuteAsync("UPDATE merchantUser SET caseNow = caseNow + @amt WHERE id=@mid", new { mid = merchantId, amt = (decimal)p.amount });
        if (p.fund_storage_id is not null)
        {
            double dp = p.delivery_profit is null ? 0 : Convert.ToDouble(p.delivery_profit);
            await c.ExecuteAsync("UPDATE fund_storages SET balance = balance + @amt WHERE id=@fs", new { fs = (int)p.fund_storage_id, amt = (decimal)p.amount });
            if (dp > 0) await c.ExecuteAsync("UPDATE fund_storages SET balance = balance - @p WHERE id=@fs", new { fs = (int)p.fund_storage_id, p = dp });
            else if (dp < 0) await c.ExecuteAsync("UPDATE fund_storages SET balance = balance + @p WHERE id=@fs", new { fs = (int)p.fund_storage_id, p = Math.Abs(dp) });
        }
        await c.ExecuteAsync("DELETE FROM merchant_payments WHERE id=@pid", new { pid = paymentId });
        return WriteResult.Ok;
    }

    public async Task<IReadOnlyList<int>> GetMerchantIdsAsync(int? merchantGroupId, int? firmId, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        if (merchantGroupId is not null)
            return (await c.QueryAsync<int>("SELECT id FROM merchantUser WHERE group_id=@g", new { g = merchantGroupId })).ToList();
        return firmId is not null ? new List<int> { firmId.Value } : new List<int>();
    }

    // ========================= FUND STORAGE =========================
    public async Task<IReadOnlyList<FundStorageRow>> GetStoragesAsync(string statusFilter, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var sql = "SELECT id, name, type, wallet_address, balance, status, created_at, updated_at FROM fund_storages";
        if (statusFilter != "all") sql += " WHERE status=@s";
        sql += " ORDER BY type, name";
        return (await c.QueryAsync<FundStorageRow>(sql, new { s = int.TryParse(statusFilter, out var si) ? si : 1 })).ToList();
    }

    public async Task<FundStorageRow?> GetStorageAsync(int id, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        return await c.QueryFirstOrDefaultAsync<FundStorageRow>(
            "SELECT id, name, type, wallet_address, balance, status, created_at, updated_at FROM fund_storages WHERE id=@id", new { id });
    }

    public async Task<int> CreateStorageAsync(FundStorageBody b, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        return await c.ExecuteScalarAsync<int>(@"INSERT INTO fund_storages (name, type, wallet_address, balance, status, created_at, updated_at)
            VALUES (@name,@type,@wa,@bal,1,@at,@at); SELECT LAST_INSERT_ID();",
            new { name = b.Name, type = b.Type, wa = b.Type == 2 ? b.WalletAddress : null, bal = b.Balance ?? 0, at = NowStr });
    }

    public async Task UpdateStorageAsync(int id, FundStorageBody b, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var sets = new List<string>(); var p = new DynamicParameters(); p.Add("id", id);
        if (b.Name is not null) { sets.Add("name=@name"); p.Add("name", b.Name); }
        if (b.Type is not null) { sets.Add("type=@type"); p.Add("type", b.Type); }
        if (b.Balance is not null) { sets.Add("balance=@bal"); p.Add("bal", b.Balance); }
        if (b.Status is not null) { sets.Add("status=@st"); p.Add("st", b.Status); }
        // type != 2 ise wallet null
        if (b.Type is not null && b.Type != 2) { sets.Add("wallet_address=NULL"); }
        else if (b.WalletAddress is not null) { sets.Add("wallet_address=@wa"); p.Add("wa", b.WalletAddress); }
        sets.Add("updated_at=@at"); p.Add("at", NowStr);
        await c.ExecuteAsync($"UPDATE fund_storages SET {string.Join(",", sets)} WHERE id=@id", p);
    }

    public async Task DisableStorageAsync(int id, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        await c.ExecuteAsync("UPDATE fund_storages SET status=0, updated_at=@at WHERE id=@id", new { id, at = NowStr });
    }

    public async Task<object> FundStorageShowAsync(int id, string? from, string? to, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var dateFrom = from ?? _clock.Today.ToString("yyyy-MM-01");
        var dateTo = to ?? Today;
        var range = new { id, df = dateFrom, dt = dateTo };
        var moves = new List<MovementItem>();

        void Add(string mid, string dir, string src, string? tgt, double amt, string? desc, string? by, DateTime? at)
            => moves.Add(new MovementItem { Id = mid, Direction = dir, Source = src, Target = tgt, Amount = amt, Description = desc, CreatedBy = by, CreatedAt = at });

        foreach (var m in await c.QueryAsync(@"SELECT mp.id, mp.amount, mp.delivery_profit, mp.description, mp.created_at, mu.name AS merchant_name, u.name AS created_by_name
            FROM merchant_payments mp LEFT JOIN merchantUser mu ON mp.merchant_id=mu.id LEFT JOIN users u ON mp.created_by=u.id
            WHERE mp.fund_storage_id=@id AND DATE(mp.created_at) BETWEEN @df AND @dt", range))
        {
            Add("mp_" + (int)m.id, "out", "Merchant Ödemesi", m.merchant_name, Convert.ToDouble(m.amount), m.description, m.created_by_name, m.created_at);
            if (m.delivery_profit is not null && Convert.ToDouble(m.delivery_profit) > 0)
                Add("mpdp_" + (int)m.id, "in", "Teslimat Karı", m.merchant_name, Convert.ToDouble(m.delivery_profit), m.description, m.created_by_name, m.created_at);
        }
        foreach (var m in await c.QueryAsync(@"SELECT ip.id, ip.amount, ip.description, ip.created_at, ni.name AS inter_name, u.name AS created_by_name
            FROM intermediary_payments ip LEFT JOIN new_intermediaries ni ON ip.intermediary_id=ni.id LEFT JOIN users u ON ip.created_by=u.id
            WHERE ip.fund_storage_id=@id AND DATE(ip.created_at) BETWEEN @df AND @dt", range))
            Add("ip_" + (int)m.id, "out", "Aracı Ödemesi", m.inter_name, Convert.ToDouble(m.amount), m.description, m.created_by_name, m.created_at);
        foreach (var m in await c.QueryAsync(@"SELECT pp.id, pp.amount, pp.description, pp.created_at, par.name AS partner_name, u.name AS created_by_name
            FROM paylira_partner_payments pp LEFT JOIN paylira_partners par ON pp.partner_id=par.id LEFT JOIN users u ON pp.created_by=u.id
            WHERE pp.fund_storage_id=@id AND DATE(pp.created_at) BETWEEN @df AND @dt", range))
            Add("pp_" + (int)m.id, "out", "Partner Ödemesi", m.partner_name, Convert.ToDouble(m.amount), m.description, m.created_by_name, m.created_at);
        foreach (var m in await c.QueryAsync(@"SELECT tp.id, tp.amount, tp.description, tp.created_at, t.name AS team_name, u.name AS created_by_name
            FROM team_payments tp LEFT JOIN teams t ON tp.team_id=t.id LEFT JOIN users u ON tp.created_by=u.id
            WHERE tp.fund_storage_id=@id AND DATE(tp.created_at) BETWEEN @df AND @dt", range))
            Add("tp_" + (int)m.id, "in", "Takım Ödemesi", m.team_name, Convert.ToDouble(m.amount), m.description, m.created_by_name, m.created_at);
        foreach (var m in await c.QueryAsync(@"SELECT pc.id, pc.amount, pc.description, pc.created_at, par.name AS partner_name, u.name AS created_by_name
            FROM partner_capitals pc LEFT JOIN paylira_partners par ON pc.partner_id=par.id LEFT JOIN users u ON pc.created_by=u.id
            WHERE pc.fund_storage_id=@id AND DATE(pc.created_at) BETWEEN @df AND @dt", range))
            Add("pc_" + (int)m.id, "in", "Sermaye Ekleme", m.partner_name, Convert.ToDouble(m.amount), m.description, m.created_by_name, m.created_at);
        foreach (var t in await c.QueryAsync(@"SELECT ft.id, ft.amount, ft.commission_amount, ft.to_storage_id, ft.description, ft.created_at, u.name AS created_by_name
            FROM fund_transfers ft LEFT JOIN users u ON ft.created_by=u.id WHERE ft.from_storage_id=@id AND DATE(ft.created_at) BETWEEN @df AND @dt", range))
        {
            var toName = await c.ExecuteScalarAsync<string?>("SELECT name FROM fund_storages WHERE id=@i", new { i = (int)t.to_storage_id });
            Add("fto_" + (int)t.id, "out", "Transfer (Giden)", toName, Convert.ToDouble(t.amount), t.description, t.created_by_name, t.created_at);
        }
        foreach (var t in await c.QueryAsync(@"SELECT ft.id, ft.received_amount, ft.from_storage_id, ft.description, ft.created_at, u.name AS created_by_name
            FROM fund_transfers ft LEFT JOIN users u ON ft.created_by=u.id WHERE ft.to_storage_id=@id AND DATE(ft.created_at) BETWEEN @df AND @dt", range))
        {
            var fromName = await c.ExecuteScalarAsync<string?>("SELECT name FROM fund_storages WHERE id=@i", new { i = (int)t.from_storage_id });
            Add("fti_" + (int)t.id, "in", "Transfer (Gelen)", fromName, Convert.ToDouble(t.received_amount), t.description, t.created_by_name, t.created_at);
        }
        foreach (var s in await c.QueryAsync(@"SELECT s.id, s.amount, s.description, s.created_at, u.name AS created_by_name
            FROM fund_storage_syncs s LEFT JOIN users u ON s.created_by=u.id WHERE s.fund_storage_id=@id AND DATE(s.created_at) BETWEEN @df AND @dt", range))
        {
            var amt = Convert.ToDouble(s.amount);
            Add("sync_" + (int)s.id, amt >= 0 ? "in" : "out", "Senkron", null, Math.Abs(amt), s.description, s.created_by_name, s.created_at);
        }
        foreach (var e in await c.QueryAsync(@"SELECT e.id, e.amount, e.description, e.created_at, u.name AS created_by_name
            FROM paylira_expenses e LEFT JOIN users u ON e.created_by=u.id WHERE e.fund_storage_id=@id AND DATE(e.created_at) BETWEEN @df AND @dt", range))
            Add("pex_" + (int)e.id, "out", "Paylira Masraf", null, Convert.ToDouble(e.amount), e.description, e.created_by_name, e.created_at);

        // başlangıç bakiyesi
        var earliest = moves.Count > 0 ? moves.Min(m => m.CreatedAt) : null;
        var anchorBefore = earliest?.ToString("yyyy-MM-dd") ?? dateFrom;
        var latestSnap = await c.QueryFirstOrDefaultAsync(
            "SELECT amount, snapshot_date FROM daily_case_snapshots WHERE entity_type='fund_storage' AND entity_id=@id AND snapshot_date<@a ORDER BY snapshot_date DESC LIMIT 1",
            new { id, a = anchorBefore });
        double startBalance = latestSnap is null ? 0 : Convert.ToDouble(latestSnap.amount);
        string? snapDate = latestSnap is null ? null : (string)latestSnap.snapshot_date.ToString("yyyy-MM-dd");

        string After(string col) => snapDate is not null ? $" AND DATE({col})>@snap" : "";
        var ap = new { id, df = dateFrom, snap = snapDate };
        startBalance += await Sum(c, $"SELECT COALESCE(SUM(delivery_profit),0) FROM merchant_payments WHERE fund_storage_id=@id AND DATE(created_at)<@df{After("created_at")}", ap);
        startBalance += await Sum(c, $"SELECT COALESCE(SUM(amount),0) FROM team_payments WHERE fund_storage_id=@id AND DATE(created_at)<@df{After("created_at")}", ap);
        startBalance += await Sum(c, $"SELECT COALESCE(SUM(amount),0) FROM partner_capitals WHERE fund_storage_id=@id AND DATE(created_at)<@df{After("created_at")}", ap);
        startBalance += await Sum(c, $"SELECT COALESCE(SUM(received_amount),0) FROM fund_transfers WHERE to_storage_id=@id AND DATE(created_at)<@df{After("created_at")}", ap);
        startBalance -= await Sum(c, $"SELECT COALESCE(SUM(amount),0) FROM merchant_payments WHERE fund_storage_id=@id AND DATE(created_at)<@df{After("created_at")}", ap);
        startBalance -= await Sum(c, $"SELECT COALESCE(SUM(amount),0) FROM intermediary_payments WHERE fund_storage_id=@id AND DATE(created_at)<@df{After("created_at")}", ap);
        startBalance -= await Sum(c, $"SELECT COALESCE(SUM(amount),0) FROM paylira_partner_payments WHERE fund_storage_id=@id AND DATE(created_at)<@df{After("created_at")}", ap);
        startBalance -= await Sum(c, $"SELECT COALESCE(SUM(amount),0) FROM fund_transfers WHERE from_storage_id=@id AND DATE(created_at)<@df{After("created_at")}", ap);
        startBalance -= await Sum(c, $"SELECT COALESCE(SUM(amount),0) FROM paylira_expenses WHERE fund_storage_id=@id AND DATE(created_at)<@df{After("created_at")}", ap);
        startBalance += await Sum(c, $"SELECT COALESCE(SUM(amount),0) FROM fund_storage_syncs WHERE fund_storage_id=@id AND DATE(created_at)<@df{After("created_at")}", ap);

        var asc = moves.OrderBy(m => m.CreatedAt).ToList();
        double running = startBalance;
        foreach (var m in asc)
        {
            m.BalanceBefore = Math.Round(running, 2);
            running += m.Direction == "in" ? m.Amount : -m.Amount;
            m.BalanceAfter = Math.Round(running, 2);
        }
        var sorted = asc.OrderByDescending(m => m.CreatedAt).ToList();
        var totalIn = sorted.Where(m => m.Direction == "in").Sum(m => m.Amount);
        var totalOut = sorted.Where(m => m.Direction == "out").Sum(m => m.Amount);

        var storage = await GetStorageAsync(id, ct);
        return new
        {
            storage,
            movements = sorted.Select(m => new { id = m.Id, direction = m.Direction, source = m.Source, target = m.Target, amount = m.Amount, description = m.Description, created_by = m.CreatedBy, created_at = m.CreatedAt, balance_before = m.BalanceBefore, balance_after = m.BalanceAfter }),
            summary = new { total_in = Math.Round(totalIn, 2), total_out = Math.Round(totalOut, 2), net = Math.Round(totalIn - totalOut, 2), start_balance = Math.Round(startBalance, 2), end_balance = Math.Round(running, 2) },
        };
    }

    public async Task<object> FundTransfersAsync(string? from, string? to, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var dc = from is not null && to is not null ? " AND DATE(ft.created_at) BETWEEN @from AND @to" : "";
        var rows = await c.QueryAsync($@"SELECT ft.*, u.name AS created_by_name,
            (SELECT name FROM fund_storages WHERE id=ft.from_storage_id) AS from_name,
            (SELECT name FROM fund_storages WHERE id=ft.to_storage_id) AS to_name
            FROM fund_transfers ft LEFT JOIN users u ON ft.created_by=u.id WHERE 1=1{dc} ORDER BY ft.created_at DESC LIMIT 200", new { from, to });
        return new { transfers = rows };
    }

    public async Task<WriteResult> CreateFundTransferAsync(FundTransferBody b, ActorInfo actor, CancellationToken ct = default)
    {
        if (b.FromStorageId == b.ToStorageId) return WriteResult.Err(422, "Kaynak ve hedef aynı olamaz.");
        if (b.Amount < 0.01) return WriteResult.Err(422, "Tutar geçersiz.");
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var fromS = await c.QueryFirstOrDefaultAsync("SELECT balance, type FROM fund_storages WHERE id=@id", new { id = b.FromStorageId });
        var toS = await c.QueryFirstOrDefaultAsync("SELECT type FROM fund_storages WHERE id=@id", new { id = b.ToStorageId });
        if (fromS is null || toS is null) return WriteResult.Err(404, "Fon deposu bulunamadı.");
        if (Convert.ToDouble(fromS.balance) < b.Amount) return WriteResult.Err(422, "Kaynak depoda yeterli bakiye yok.");

        var commissionRate = b.CommissionRate ?? 0;
        var isExternalToInternal = (int)fromS.type != 2 && (int)toS.type == 2;
        var commissionAmount = isExternalToInternal ? Math.Round(b.Amount * commissionRate / 100, 2) : 0;
        var received = Math.Round(b.Amount - commissionAmount, 2);

        await c.ExecuteAsync(@"INSERT INTO fund_transfers (from_storage_id, to_storage_id, amount, commission_rate, commission_amount, received_amount, description, created_by, created_at)
            VALUES (@from,@to,@amt,@crate,@camt,@recv,@desc,@by,@at)",
            new { from = b.FromStorageId, to = b.ToStorageId, amt = b.Amount, crate = commissionRate, camt = commissionAmount, recv = received, desc = b.Description, by = actor.UserId, at = string.IsNullOrEmpty(b.TransferDate) ? NowStr : b.TransferDate + " " + _clock.Now.ToString("HH:mm:ss") });
        await c.ExecuteAsync("UPDATE fund_storages SET balance = balance - @amt WHERE id=@id", new { id = b.FromStorageId, amt = b.Amount });
        await c.ExecuteAsync("UPDATE fund_storages SET balance = balance + @amt WHERE id=@id", new { id = b.ToStorageId, amt = received });
        return WriteResult.Ok;
    }

    public async Task<WriteResult> DeleteFundTransferAsync(int id, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var t = await c.QueryFirstOrDefaultAsync("SELECT * FROM fund_transfers WHERE id=@id", new { id });
        if (t is null) return WriteResult.Err(404, "Transfer bulunamadı.");
        if (((DateTime)t.created_at).Date < _clock.Today) return WriteResult.Err(422, "Sadece bugüne ait transferler silinebilir.");
        await c.ExecuteAsync("UPDATE fund_storages SET balance = balance + @amt WHERE id=@id", new { id = (int)t.from_storage_id, amt = (decimal)t.amount });
        await c.ExecuteAsync("UPDATE fund_storages SET balance = balance - @amt WHERE id=@id", new { id = (int)t.to_storage_id, amt = (decimal)t.received_amount });
        await c.ExecuteAsync("DELETE FROM fund_transfers WHERE id=@id", new { id });
        return WriteResult.Ok;
    }

    public async Task<WriteResult> AddFundSyncAsync(FundSyncBody b, ActorInfo actor, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        if (await c.ExecuteScalarAsync<int>("SELECT EXISTS(SELECT 1 FROM fund_storages WHERE id=@id)", new { id = b.FundStorageId }) != 1)
            return WriteResult.Err(404, "Fon deposu bulunamadı.");
        await c.ExecuteAsync(@"INSERT INTO fund_storage_syncs (fund_storage_id, amount, description, created_by, created_at)
            VALUES (@fs,@amt,@desc,@by,@at)", new { fs = b.FundStorageId, amt = b.Amount, desc = b.Description, by = actor.UserId, at = string.IsNullOrEmpty(b.SyncDate) ? NowStr : b.SyncDate + " " + _clock.Now.ToString("HH:mm:ss") });
        await c.ExecuteAsync("UPDATE fund_storages SET balance = balance + @amt WHERE id=@fs", new { fs = b.FundStorageId, amt = b.Amount });
        return WriteResult.Ok;
    }

    public async Task<WriteResult> DeleteFundSyncAsync(int id, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var s = await c.QueryFirstOrDefaultAsync("SELECT * FROM fund_storage_syncs WHERE id=@id", new { id });
        if (s is null) return WriteResult.Err(404, "Senkron bulunamadı.");
        await c.ExecuteAsync("UPDATE fund_storages SET balance = balance - @amt WHERE id=@fs", new { fs = (int)s.fund_storage_id, amt = (decimal)s.amount });
        await c.ExecuteAsync("DELETE FROM fund_storage_syncs WHERE id=@id", new { id });
        return WriteResult.Ok;
    }
}
