using System.Data;
using System.Text;
using Dapper;
using TPanel.Application.Common;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.PublicApi;
using TPanel.Application.Features.Transactions;

namespace TPanel.Infrastructure.Services;

/// <summary>Admin Deposit/Withdraw veri erişimi (Dapper) — PHP controller SQL'leri birebir.</summary>
public class TransactionAdminStore : ITransactionAdminStore
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public TransactionAdminStore(IDbConnectionFactory factory, IClock clock)
    {
        _factory = factory;
        _clock = clock;
    }

    public async Task<IReadOnlyList<int>> GetMerchantIdsForUserAsync(int? merchantGroupId, int? firmId, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        if (merchantGroupId is not null)
        {
            var ids = await conn.QueryAsync<int>("SELECT id FROM merchantUser WHERE group_id = @g", new { g = merchantGroupId });
            return ids.ToList();
        }
        return firmId is not null ? new List<int> { firmId.Value } : new List<int>();
    }

    // ---------- DEPOSITS ----------
    private const string DepositSelect = @"
        SELECT invest.id, invest.status, invest.name, invest.amount, invest.original_amount, invest.amountChanged,
               invest.order_id, invest.player_id, invest.u_id, invest.receipt_path,
               invest.agent_id, invest.firm_id, invest.team_id, invest.bank_id, invest.iban,
               invest.created_at, invest.form_at, invest.process_date, invest.finalize_date, invest.rejectType,
               merchantUser.name AS merchant_name, teams.name AS team_name,
               bankAccounts.account_holder, bankAccounts.account_iban,
               banks.name AS bank_name, banks.logo AS bank_logo, agent.name AS agent_name
        FROM invest
        LEFT JOIN merchantUser ON invest.firm_id = merchantUser.id
        LEFT JOIN teams ON invest.team_id = teams.id
        LEFT JOIN bankAccounts ON invest.bank_id = bankAccounts.id
        LEFT JOIN banks ON bankAccounts.bank_id = banks.id
        LEFT JOIN users AS agent ON invest.agent_id = agent.id";

    public async Task<IReadOnlyList<DepositListRow>> GetDepositsPendingAsync(QueryScope scope, TxFilter f, CancellationToken ct = default)
    {
        var p = new DynamicParameters();
        var w = new StringBuilder(" WHERE invest.type = 1 AND invest.status IN ('1','2')");
        AppendScope(w, p, scope);
        AppendCommonFilters(w, p, f, includeStatus: false);

        var sql = DepositSelect + w + " ORDER BY invest.id DESC LIMIT 200";
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var rows = (await conn.QueryAsync<DepositListRow>(sql, p)).ToList();
        await FillTrustAsync(conn, rows);
        return rows;
    }

    public async Task<(IReadOnlyList<DepositListRow>, long, double)> GetDepositsAllAsync(QueryScope scope, TxFilter f, CancellationToken ct = default)
    {
        var p = new DynamicParameters();
        var w = new StringBuilder(" WHERE invest.type = 1");
        if (f.ConvertedOnly) w.Append(" AND invest.isConverted = 1");
        AppendScope(w, p, scope);
        AppendCommonFilters(w, p, f, includeStatus: true);

        var fromWhere = @"FROM invest
            LEFT JOIN merchantUser ON invest.firm_id = merchantUser.id
            LEFT JOIN teams ON invest.team_id = teams.id
            LEFT JOIN bankAccounts ON invest.bank_id = bankAccounts.id
            LEFT JOIN banks ON bankAccounts.bank_id = banks.id
            LEFT JOIN users AS agent ON invest.agent_id = agent.id" + w;

        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var total = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) " + fromWhere, p);
        var totalAmount = await conn.ExecuteScalarAsync<double?>("SELECT COALESCE(SUM(invest.amount),0) " + fromWhere, p) ?? 0;

        p.Add("off", (f.Page - 1) * f.PerPage);
        p.Add("lim", f.PerPage);
        var rows = (await conn.QueryAsync<DepositListRow>(DepositSelect + w + " ORDER BY invest.id DESC LIMIT @lim OFFSET @off", p)).ToList();
        await FillTrustAsync(conn, rows);
        return (rows, total, totalAmount);
    }

    public async Task<DepositListRow?> GetDepositDetailAsync(int id, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync<DepositListRow>(DepositSelect + " WHERE invest.id = @id", new { id });
        if (row is not null) await FillTrustAsync(conn, new[] { row });
        return row;
    }

    // ---------- WITHDRAWALS ----------
    private const string WithdrawSelectBase = @"
        FROM invest
        LEFT JOIN merchantUser ON invest.firm_id = merchantUser.id
        LEFT JOIN teams ON invest.team_id = teams.id
        LEFT JOIN users AS agent ON invest.agent_id = agent.id
        LEFT JOIN banks ON banks.code = SUBSTRING(REPLACE(invest.iban,' ',''),6,4)";

    private const string WithdrawCols = @"
        SELECT invest.id, invest.status, invest.name, invest.amount, invest.order_id, invest.player_id,
               invest.iban, invest.u_id, invest.agent_id, invest.firm_id, invest.team_id,
               invest.created_at, invest.form_at, invest.process_date, invest.finalize_date, invest.rejectType,
               merchantUser.name AS merchant_name, teams.name AS team_name, agent.name AS agent_name, banks.name AS bank_name,
               teams.telegram_missing_receipt_enabled_at,
               (SELECT COUNT(*) FROM invest_receipts WHERE invest_receipts.invest_id = invest.id) AS receipt_count ";

    public async Task<IReadOnlyList<WithdrawListRow>> GetWithdrawalsPendingAsync(QueryScope scope, TxFilter f, CancellationToken ct = default)
    {
        var p = new DynamicParameters();
        var w = new StringBuilder(" WHERE invest.type = '2' AND invest.status IN ('0','1','2')");

        if (scope.Kind == ScopeKind.Team)
        {
            p.Add("team", scope.TeamId);
            w.Append(@" AND (invest.status = '0'
                        OR (invest.status IN ('1','2') AND invest.team_id IS NULL)
                        OR (invest.status IN ('1','2') AND invest.team_id = @team))");
            // Merchant→takım ataması: takıma merchant atanmışsa yalnızca o merchant'ların
            // çekimleri (havuz dahil) görünür; atama yoksa hepsi (geriye dönük uyumlu).
            w.Append(@" AND (NOT EXISTS (SELECT 1 FROM team_merchant tm WHERE tm.team_id = @team)
                        OR invest.firm_id IN (SELECT tm.merchant_id FROM team_merchant tm WHERE tm.team_id = @team))");
        }
        else if (scope.Kind == ScopeKind.Merchant)
        {
            AppendMerchantScope(w, p, scope);
        }
        AppendCommonFilters(w, p, f, includeStatus: false, withdraw: true);

        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<WithdrawListRow>(WithdrawCols + WithdrawSelectBase + w + " ORDER BY invest.id DESC LIMIT 200", p);
        return rows.ToList();
    }

    public async Task<(IReadOnlyList<WithdrawListRow>, long, double)> GetWithdrawalsAllAsync(QueryScope scope, TxFilter f, CancellationToken ct = default)
    {
        var p = new DynamicParameters();
        var w = new StringBuilder(" WHERE invest.type = '2'");
        AppendScope(w, p, scope);
        AppendCommonFilters(w, p, f, includeStatus: true, withdraw: true);

        if (f.MissingReceipt)
            w.Append(@" AND invest.status = 3
                        AND (SELECT COUNT(*) FROM invest_receipts WHERE invest_receipts.invest_id = invest.id) = 0
                        AND teams.telegram_missing_receipt_enabled_at IS NOT NULL
                        AND invest.finalize_date IS NOT NULL
                        AND invest.finalize_date >= teams.telegram_missing_receipt_enabled_at");

        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var total = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*) " + WithdrawSelectBase + w, p);
        var totalAmount = await conn.ExecuteScalarAsync<double?>("SELECT COALESCE(SUM(invest.amount),0) " + WithdrawSelectBase + w, p) ?? 0;

        p.Add("off", (f.Page - 1) * f.PerPage);
        p.Add("lim", f.PerPage);
        var rows = await conn.QueryAsync<WithdrawListRow>(WithdrawCols + WithdrawSelectBase + w + " ORDER BY invest.id DESC LIMIT @lim OFFSET @off", p);
        return (rows.ToList(), total, totalAmount);
    }

    public async Task<WithdrawListRow?> GetWithdrawDetailAsync(int id, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<WithdrawListRow>(
            WithdrawCols + WithdrawSelectBase + " WHERE invest.id = @id AND invest.type = '2'", new { id });
    }

    // ---------- COMMON ----------
    public async Task<InvestRow?> GetInvestAsync(int id, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<InvestRow>(
            @"SELECT id, type, status, name, amount, u_id, callbackUrl, callbackOkUrl, callbackFailUrl,
                     firm_id, team_id, bank_id, player_id, order_id, created_at, form_at, process_date,
                     finalize_date, ibanSeen, receipt_path, callbackSended
              FROM invest WHERE id = @id LIMIT 1", new { id });
    }

    public async Task<InvestRaw?> GetInvestRawAsync(int id, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var r = await conn.QueryFirstOrDefaultAsync(
            @"SELECT id, type, status, amount, firm_id, team_id, agent_id, form_at, order_id, u_id, name, callbackSended, callbackUrl
              FROM invest WHERE id = @id LIMIT 1", new { id });
        if (r is null) return null;
        return new InvestRaw((int)r.id, (string)r.type, (string)r.status, Convert.ToDouble(r.amount),
            (int)r.firm_id, (int?)r.team_id, (int?)r.agent_id, (DateTime?)r.form_at, (string?)r.order_id,
            (string?)r.u_id, (string?)r.name, Convert.ToInt32(r.callbackSended), (string?)r.callbackUrl);
    }

    public async Task UpdateInvestAsync(int id, IDictionary<string, object?> fields, CancellationToken ct = default)
    {
        if (fields.Count == 0) return;
        var p = new DynamicParameters();
        var sets = new List<string>();
        var i = 0;
        foreach (var (col, val) in fields) { var pn = "p" + i++; sets.Add($"`{col}` = @{pn}"); p.Add(pn, val); }
        p.Add("id", id);
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        await conn.ExecuteAsync($"UPDATE invest SET {string.Join(", ", sets)} WHERE id = @id", p);
    }

    public async Task InsertInvestLogAsync(int investId, int userId, string ip, int status, string detail, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            @"INSERT INTO investLog (investID, userID, ip, status, createdAt, detail)
              VALUES (@investId, @userId, @ip, @status, @now, @detail)",
            new { investId, userId, ip, status, now = _clock.Now, detail });
    }

    public async Task<IReadOnlyList<PlayerHistoryRow>> GetPlayerHistoryAsync(string playerId, int excludeId, int type, QueryScope scope, CancellationToken ct = default)
    {
        var p = new DynamicParameters();
        p.Add("pid", playerId); p.Add("ex", excludeId); p.Add("type", type);
        var w = new StringBuilder(" WHERE player_id = @pid AND id <> @ex AND type = @type");
        if (scope.Kind == ScopeKind.Team) { p.Add("team", scope.TeamId); w.Append(" AND team_id = @team"); }
        else if (scope.Kind == ScopeKind.Merchant) { AppendMerchantScope(w, p, scope, qualify: false); }

        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var rows = await conn.QueryAsync<PlayerHistoryRow>(
            "SELECT id, type, status, amount, name, created_at, finalize_date, rejectType FROM invest" + w + " ORDER BY id DESC LIMIT 10", p);
        return rows.ToList();
    }

    public async Task<IReadOnlyList<OptionRow>> GetActiveMerchantsAsync(CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        return (await conn.QueryAsync<OptionRow>("SELECT id, name FROM merchantUser WHERE status = '1' ORDER BY name")).ToList();
    }

    public async Task<IReadOnlyList<OptionRow>> GetTeamsForFilterAsync(CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        return (await conn.QueryAsync<OptionRow>("SELECT id, name, status FROM teams WHERE status <> 0 ORDER BY name")).ToList();
    }

    public async Task<IReadOnlyList<OptionRow>> GetBanksAsync(CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        return (await conn.QueryAsync<OptionRow>("SELECT id, name FROM banks ORDER BY name")).ToList();
    }

    // ---------- MANUEL YATIRIM ----------
    public async Task<IReadOnlyList<OptionRow>> GetTeamBankAccountsAsync(int teamId, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        return (await conn.QueryAsync<OptionRow>(
            @"SELECT ba.id, CONCAT(b.name, ' — ', ba.account_holder) AS name
              FROM bankAccounts ba JOIN banks b ON ba.bank_id = b.id
              WHERE ba.team_id = @t AND ba.status <> 0
              ORDER BY ba.sort_order, ba.id", new { t = teamId })).ToList();
    }

    public async Task<IReadOnlyList<OptionRow>> GetTeamAgentsAsync(int teamId, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        return (await conn.QueryAsync<OptionRow>(
            "SELECT id, name FROM users WHERE team_id = @t AND user_type IN (2,5) AND status = '1' ORDER BY name",
            new { t = teamId })).ToList();
    }

    public async Task<int> CreateManualDepositAsync(int merchantId, int teamId, int? bankId, int? agentId,
        string name, double amount, int userId, string ip, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);

        // Merchant komisyon oranı → panel komisyonu
        var pct = await conn.ExecuteScalarAsync<double?>(
            "SELECT commission FROM merchantUser WHERE id = @m", new { m = merchantId }) ?? 0d;
        var commAmount = Math.Round(amount * pct / 100d, 2);

        var now = _clock.Now;
        var orderId = "MAN" + now.ToString("yyMMddHHmmssfff");
        var uId = Guid.NewGuid().ToString("N");

        // type=1 yatırım, status=3 onaylı, added_type=2 manuel; callbackSended=1 → callback gönderilmez.
        var sql = @"INSERT INTO invest
            (type, status, name, amount, original_amount, u_id, callbackUrl,
             panel_commissin_amount, payed_amount, panel_commission_percent, api_id, firm_id, team_id, bank_id, agent_id,
             player_id, order_id, added_type, created_at, form_at, process_date, finalize_date,
             ibanSeen, callbackSended, isControled, isConverted, walletInvest, transaction_type, amountChanged)
            VALUES
            ('1','3',@name,@amount,@amount,@uId,'',
             @comm,@amount,@pct,@mid,@mid,@team,@bank,@agent,
             '',@order,'2',@now,@now,@now,@now,
             1,1,1,0,0,1,0);
            SELECT LAST_INSERT_ID();";

        var id = await conn.ExecuteScalarAsync<int>(sql, new
        {
            name, amount, uId, comm = commAmount, pct, mid = merchantId,
            team = teamId, bank = bankId, agent = agentId, order = orderId, now,
        });

        await conn.ExecuteAsync(
            @"INSERT INTO investLog (investID, userID, ip, status, createdAt, detail)
              VALUES (@id, @uid, @ip, 3, @now, @detail)",
            new { id, uid = userId, ip, now, detail = "Manuel yatırım eklendi (onaylı)." });

        return id;
    }

    public async Task<int> CreateManualWithdrawAsync(int merchantId, int teamId, int? bankId, int? agentId,
        string name, double amount, string iban, int userId, string ip, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);

        // Merchant çekim komisyon oranı → panel komisyonu
        var pct = await conn.ExecuteScalarAsync<double?>(
            "SELECT withdrawCommission FROM merchantUser WHERE id = @m", new { m = merchantId }) ?? 0d;
        var commAmount = Math.Round(amount * pct / 100d, 2);

        var now = _clock.Now;
        var orderId = "MANW" + now.ToString("yyMMddHHmmssfff");
        var uId = Guid.NewGuid().ToString("N");

        // type=2 çekim, status=3 onaylı(ödenmiş), added_type=2 manuel; callbackSended=1 → callback yok.
        var sql = @"INSERT INTO invest
            (type, status, name, amount, original_amount, u_id, callbackUrl,
             panel_commissin_amount, payed_amount, panel_commission_percent, iban, api_id, firm_id, team_id, bank_id, agent_id,
             player_id, order_id, added_type, created_at, form_at, process_date, finalize_date,
             ibanSeen, callbackSended, isControled, isConverted, walletInvest, transaction_type, amountChanged)
            VALUES
            ('2','3',@name,@amount,@amount,@uId,'',
             @comm,@amount,@pct,@iban,@mid,@mid,@team,@bank,@agent,
             '',@order,'2',@now,@now,@now,@now,
             1,1,1,0,0,1,0);
            SELECT LAST_INSERT_ID();";

        var id = await conn.ExecuteScalarAsync<int>(sql, new
        {
            name, amount, uId, comm = commAmount, pct, iban = iban.ToUpperInvariant(), mid = merchantId,
            team = teamId, bank = bankId, agent = agentId, order = orderId, now,
        });

        await conn.ExecuteAsync(
            @"INSERT INTO investLog (investID, userID, ip, status, createdAt, detail)
              VALUES (@id, @uid, @ip, 3, @now, @detail)",
            new { id, uid = userId, ip, now, detail = "Manuel çekim eklendi (onaylı)." });

        return id;
    }

    // ---------- RECEIPTS ----------
    public async Task<bool> HasReceiptAsync(int investId, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>("SELECT EXISTS(SELECT 1 FROM invest_receipts WHERE invest_id = @id)", new { id = investId }) == 1;
    }

    public async Task<(bool HasVerified, bool HasPending, bool HasBad)> GetReceiptVerifySummaryAsync(int investId, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync(@"
            SELECT
              SUM(verification_status IN ('verified','manually_verified')) AS verified,
              SUM(verification_status = 'pending') AS pending,
              SUM(verification_status IN ('suspicious','rejected')) AS bad
            FROM invest_receipts WHERE invest_id = @id", new { id = investId });
        if (row is null) return (false, false, false);
        return (Convert.ToInt32(row.verified ?? 0) > 0, Convert.ToInt32(row.pending ?? 0) > 0, Convert.ToInt32(row.bad ?? 0) > 0);
    }

    public async Task<IReadOnlyList<ReceiptRow>> GetReceiptsAsync(int investId, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        return (await conn.QueryAsync<ReceiptRow>(
            @"SELECT ir.id, ir.file_path, ir.original_name, ir.mime_type, ir.file_size, ir.uploaded_at,
                     ir.verification_status, ir.verification_score, ir.verification_data, ir.verification_notes,
                     ir.metadata_flags, ir.verified_at, ir.manual_verified_by,
                     u.name AS uploaded_by_name, mv.name AS manual_verifier_name,
                     ir.perceptual_hash, ir.file_hash
              FROM invest_receipts ir
              LEFT JOIN users u ON ir.uploaded_by = u.id
              LEFT JOIN users mv ON ir.manual_verified_by = mv.id
              WHERE ir.invest_id = @id ORDER BY ir.id DESC", new { id = investId })).ToList();
    }

    public async Task<ReceiptRow?> GetReceiptAsync(int investId, int receiptId, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<ReceiptRow>(
            @"SELECT id, file_path, original_name, mime_type, file_size, uploaded_at, verification_status,
                     verification_score, verification_data, verification_notes, metadata_flags, verified_at,
                     manual_verified_by, perceptual_hash, file_hash
              FROM invest_receipts WHERE id = @rid AND invest_id = @id LIMIT 1", new { rid = receiptId, id = investId });
    }

    public async Task<int> InsertReceiptAsync(ReceiptInsert d, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(
            @"INSERT INTO invest_receipts (invest_id, file_path, original_name, mime_type, file_size, file_hash, perceptual_hash, uploaded_by, uploaded_at)
              VALUES (@InvestId, @FilePath, @OriginalName, @MimeType, @FileSize, @FileHash, @PerceptualHash, @UploadedBy, @now);
              SELECT LAST_INSERT_ID();",
            new { d.InvestId, d.FilePath, d.OriginalName, d.MimeType, d.FileSize, d.FileHash, d.PerceptualHash, d.UploadedBy, now = _clock.Now });
    }

    public async Task UpdateReceiptAsync(int receiptId, IDictionary<string, object?> fields, CancellationToken ct = default)
    {
        if (fields.Count == 0) return;
        var p = new DynamicParameters();
        var sets = new List<string>();
        var i = 0;
        foreach (var (col, val) in fields) { var pn = "p" + i++; sets.Add($"`{col}` = @{pn}"); p.Add(pn, val); }
        p.Add("rid", receiptId);
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        await conn.ExecuteAsync($"UPDATE invest_receipts SET {string.Join(", ", sets)} WHERE id = @rid", p);
    }

    public async Task<int> InsertFakeTemplateAsync(int receiptId, int investId, string perceptualHash, string? fileHash, string? reason, int reportedBy, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(
            @"INSERT INTO fake_receipt_templates (receipt_id, invest_id, perceptual_hash, file_hash, reason, reported_by, reported_at)
              VALUES (@receiptId, @investId, @perceptualHash, @fileHash, @reason, @reportedBy, @now);
              SELECT LAST_INSERT_ID();",
            new { receiptId, investId, perceptualHash, fileHash, reason, reportedBy, now = _clock.Now });
    }

    // ---------- BULK ASSIGN / TEAMS ----------
    public async Task<TeamRow?> GetTeamAsync(int teamId, bool requireActive, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var sql = @"SELECT id, name, status, telegram_enabled, telegram_withdraw_chat_id,
                           telegram_withdraw_assigned_enabled, telegram_missing_receipt_enabled_at
                    FROM teams WHERE id = @id" + (requireActive ? " AND status = 1" : "");
        var r = await conn.QueryFirstOrDefaultAsync(sql, new { id = teamId });
        if (r is null) return null;
        return new TeamRow((int)r.id, (string)r.name, (int)r.status, Convert.ToInt32(r.telegram_enabled) == 1,
            (string?)r.telegram_withdraw_chat_id, Convert.ToInt32(r.telegram_withdraw_assigned_enabled) == 1,
            (DateTime?)r.telegram_missing_receipt_enabled_at);
    }

    public async Task<UserRow?> GetFirstActiveTeamUserAsync(int teamId, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var r = await conn.QueryFirstOrDefaultAsync(
            "SELECT id, name FROM users WHERE team_id = @t AND user_type IN (2,5) AND status = '1' ORDER BY id LIMIT 1", new { t = teamId });
        return r is null ? null : new UserRow((int)r.id, (string)r.name);
    }

    public async Task<IReadOnlyList<int>> FilterEligibleForAssignAsync(IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        return (await conn.QueryAsync<int>(
            "SELECT id FROM invest WHERE id IN @ids AND type = '2' AND status IN ('0','1')", new { ids })).ToList();
    }

    public async Task BulkAssignAsync(IReadOnlyList<int> ids, int teamId, int agentId, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            "UPDATE invest SET status = 2, team_id = @teamId, agent_id = @agentId, process_date = @now WHERE id IN @ids",
            new { ids, teamId, agentId, now = _clock.Now });
    }

    public async Task<(bool ok, string message)> MoveDepositTeamAsync(int id, int teamId, int bankId, int actorUserId, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var dep = await conn.QueryFirstOrDefaultAsync("SELECT id, type, status, team_id FROM invest WHERE id=@id", new { id });
        if (dep is null) return (false, "Yatırım bulunamadı.");
        if (Convert.ToString((object)dep.type) != "1" || Convert.ToString((object)dep.status) != "1")
            return (false, "Yalnızca bekleyen yatırımlar taşınabilir.");
        // IBAN seçilen takıma ait + hesap aktif mi (takım PASİF olabilir — maxCase ile pasife alınmış takıma da taşınabilsin)
        var bankOk = await conn.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM bankAccounts ba JOIN teams t ON ba.team_id=t.id WHERE ba.id=@bid AND ba.team_id=@tid AND ba.status=1 AND t.status<>0",
            new { bid = bankId, tid = teamId });
        if (bankOk == 0) return (false, "Seçilen IBAN bu takıma ait veya aktif değil.");

        var oldTeam = Convert.ToInt32((object)dep.team_id);
        await conn.ExecuteAsync("UPDATE invest SET team_id=@tid, bank_id=@bid, agent_id=NULL WHERE id=@id",
            new { tid = teamId, bid = bankId, id });
        await conn.ExecuteAsync(
            "INSERT INTO investLog (investID, userID, ip, status, createdAt, detail) VALUES (@iid,@uid,'',@st,@at,@detail)",
            new { iid = id, uid = actorUserId, st = "1", at = _clock.Now, detail = $"Takım taşındı: #{oldTeam} → #{teamId} (IBAN #{bankId})" });
        return (true, "Yatırım yeni takıma taşındı.");
    }

    public async Task<(bool ok, string message)> MoveWithdrawTeamAsync(int id, int teamId, int actorUserId, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var w = await conn.QueryFirstOrDefaultAsync("SELECT id, type, status, team_id FROM invest WHERE id=@id", new { id });
        if (w is null) return (false, "Çekim bulunamadı.");
        if (Convert.ToString((object)w.type) != "2" || Convert.ToString((object)w.status) is not ("1" or "2"))
            return (false, "Yalnızca bekleyen çekimler taşınabilir.");
        // Hedef takım var + devre dışı değil (PASİF olabilir)
        var teamOk = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM teams WHERE id=@tid AND status<>0", new { tid = teamId });
        if (teamOk == 0) return (false, "Hedef takım bulunamadı.");

        var oldTeam = w.team_id is null ? (int?)null : Convert.ToInt32((object)w.team_id);
        await conn.ExecuteAsync("UPDATE invest SET team_id=@tid, agent_id=NULL WHERE id=@id", new { tid = teamId, id });
        await conn.ExecuteAsync(
            "INSERT INTO investLog (investID, userID, ip, status, createdAt, detail) VALUES (@iid,@uid,'',@st,@at,@detail)",
            new { iid = id, uid = actorUserId, st = Convert.ToString((object)w.status), at = _clock.Now, detail = $"Çekim takım taşındı: #{oldTeam} → #{teamId}" });
        return (true, "Çekim yeni takıma taşındı.");
    }

    public async Task<IReadOnlyList<MissingReceiptRow>> GetMissingReceiptsAsync(int teamId, DateTime enabledAt, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var rows = await conn.QueryAsync(
            @"SELECT invest.id, invest.order_id, invest.finalize_date
              FROM invest
              WHERE invest.team_id = @t AND invest.type = '2' AND invest.status = 3
                AND invest.finalize_date >= @en AND invest.finalize_date IS NOT NULL
                AND (SELECT COUNT(*) FROM invest_receipts WHERE invest_receipts.invest_id = invest.id) = 0
              ORDER BY invest.finalize_date", new { t = teamId, en = enabledAt });
        return rows.Select(r => new MissingReceiptRow((int)r.id, (string?)r.order_id, (DateTime)r.finalize_date)).ToList();
    }

    public async Task InsertTelegramNotificationsIgnoreAsync(IReadOnlyList<int> investIds, string type, DateTime sentAt, CancellationToken ct = default)
    {
        if (investIds.Count == 0) return;
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        foreach (var iid in investIds)
            await conn.ExecuteAsync(
                "INSERT IGNORE INTO telegram_notifications (invest_id, type, sent_at) VALUES (@iid, @type, @sentAt)",
                new { iid, type, sentAt });
    }

    public async Task<(IReadOnlyList<ReceiptReviewRow>, long, IReadOnlyDictionary<string, long>)>
        GetReceiptReviewAsync(string? statusFilter, int page, int perPage, CancellationToken ct = default)
    {
        const string latestSub = "(SELECT MAX(id) FROM invest_receipts ir2 WHERE ir2.invest_id = invest_receipts.invest_id)";
        var p = new DynamicParameters();
        var w = new StringBuilder(
            @" FROM invest_receipts
               JOIN invest ON invest.id = invest_receipts.invest_id
               LEFT JOIN teams ON invest.team_id = teams.id
               LEFT JOIN merchantUser ON invest.firm_id = merchantUser.id
               LEFT JOIN users AS agent ON invest.agent_id = agent.id
               LEFT JOIN users AS mverifier ON invest_receipts.manual_verified_by = mverifier.id
               WHERE invest.type = '2' AND invest_receipts.verification_status <> 'pending'
                 AND invest_receipts.id = " + latestSub);

        if (statusFilter is "verified" or "suspicious" or "rejected")
        {
            if (statusFilter == "verified")
                w.Append(" AND invest_receipts.verification_status IN ('verified','manually_verified')");
            else { p.Add("st", statusFilter); w.Append(" AND invest_receipts.verification_status = @st"); }
        }

        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var total = await conn.ExecuteScalarAsync<long>("SELECT COUNT(*)" + w, p);

        p.Add("off", (page - 1) * perPage); p.Add("lim", perPage);
        var rows = (await conn.QueryAsync(
            @"SELECT invest.id AS invest_id, invest.order_id, invest.amount, invest.name AS recipient, invest.iban,
                     invest.status AS invest_status, invest.finalize_date,
                     invest_receipts.id AS receipt_id, invest_receipts.verification_status,
                     invest_receipts.verification_score, invest_receipts.verification_data,
                     invest_receipts.verification_notes, invest_receipts.verified_at,
                     mverifier.name AS manual_verifier_name, teams.name AS team_name,
                     merchantUser.name AS merchant_name, agent.name AS agent_name" + w
              + " ORDER BY invest_receipts.verified_at DESC LIMIT @lim OFFSET @off", p))
            .Select(r => new ReceiptReviewRow(
                (int)r.invest_id, (string?)r.order_id, Convert.ToDouble(r.amount), (string?)r.recipient, (string?)r.iban,
                Convert.ToInt32(r.invest_status), (DateTime?)r.finalize_date, (string?)r.team_name, (string?)r.merchant_name,
                (string?)r.agent_name, (int)r.receipt_id, (string?)r.verification_status,
                r.verification_score is null ? null : (int?)Convert.ToInt32(r.verification_score),
                (string?)r.verification_data, (string?)r.verification_notes, (DateTime?)r.verified_at, (string?)r.manual_verifier_name))
            .ToList();

        // counts (en son dekontlar)
        var countRows = await conn.QueryAsync(
            @"SELECT invest_receipts.verification_status AS s, COUNT(*) AS c
              FROM invest_receipts JOIN invest ON invest.id = invest_receipts.invest_id
              WHERE invest.type = '2' AND invest_receipts.id = " + latestSub +
            " GROUP BY invest_receipts.verification_status");
        var counts = countRows.ToDictionary(r => (string)r.s, r => (long)r.c);

        return (rows, total, counts);
    }

    // ---------- helpers ----------
    private static void AppendScope(StringBuilder w, DynamicParameters p, QueryScope scope)
    {
        if (scope.Kind == ScopeKind.Team) { p.Add("scopeTeam", scope.TeamId); w.Append(" AND invest.team_id = @scopeTeam"); }
        else if (scope.Kind == ScopeKind.Merchant) AppendMerchantScope(w, p, scope);
    }

    private static void AppendMerchantScope(StringBuilder w, DynamicParameters p, QueryScope scope, bool qualify = true)
    {
        var col = qualify ? "invest.firm_id" : "firm_id";
        var ids = scope.MerchantIds ?? Array.Empty<int>();
        if (ids.Count == 0) { w.Append(" AND 1 = 0"); return; }
        if (ids.Count == 1) { p.Add("scopeFirm", ids[0]); w.Append($" AND {col} = @scopeFirm"); }
        else { p.Add("scopeFirms", ids); w.Append($" AND {col} IN @scopeFirms"); }
    }

    private static void AppendCommonFilters(StringBuilder w, DynamicParameters p, TxFilter f, bool includeStatus, bool withdraw = false)
    {
        if (f.Id is not null) { p.Add("fId", f.Id); w.Append(" AND invest.id = @fId"); }
        if (includeStatus && f.Status is not null && f.Status != 0) { p.Add("fStatus", f.Status); w.Append(" AND invest.status = @fStatus"); }
        if (f.Merchant is not null) { p.Add("fMerchant", f.Merchant); w.Append(" AND invest.firm_id = @fMerchant"); }
        if (f.Team is not null) { p.Add("fTeam", f.Team); w.Append(" AND invest.team_id = @fTeam"); }
        if (!withdraw && f.Bank is not null) { p.Add("fBank", f.Bank); w.Append(" AND invest.bank_id = @fBank"); }
        if (!string.IsNullOrEmpty(f.Name)) { p.Add("fName", "%" + f.Name + "%"); w.Append(" AND invest.name LIKE @fName"); }
        if (!string.IsNullOrEmpty(f.PlayerId)) { p.Add("fPlayer", f.PlayerId); w.Append(" AND invest.player_id = @fPlayer"); }
        if (!string.IsNullOrEmpty(f.OrderId)) { p.Add("fOrder", f.OrderId); w.Append(" AND invest.order_id = @fOrder"); }
        if (!string.IsNullOrEmpty(f.UId)) { p.Add("fUid", f.UId); w.Append(" AND invest.u_id = @fUid"); }
        if (f.MinAmount is not null) { p.Add("fMin", f.MinAmount); w.Append(" AND invest.amount >= @fMin"); }
        if (f.MaxAmount is not null) { p.Add("fMax", f.MaxAmount); w.Append(" AND invest.amount <= @fMax"); }
        if (!string.IsNullOrEmpty(f.DateFrom)) { p.Add("fFrom", f.DateFrom); w.Append(" AND DATE(invest.created_at) >= @fFrom"); }
        if (!string.IsNullOrEmpty(f.DateTo)) { p.Add("fTo", f.DateTo); w.Append(" AND DATE(invest.created_at) <= @fTo"); }
        if (f.AddedType is 1 or 2) { p.Add("fAdded", f.AddedType); w.Append(" AND invest.added_type = @fAdded"); }
    }

    private async Task FillTrustAsync(IDbConnection conn, IReadOnlyList<DepositListRow> rows)
    {
        foreach (var r in rows)
        {
            if (string.IsNullOrEmpty(r.PlayerId)) { r.TrustRate = null; r.TrustCount = 0; continue; }
            var statuses = (await conn.QueryAsync<string>(
                @"SELECT status FROM invest WHERE player_id = @pid AND type = 1 AND status IN ('3','4') AND id < @id
                  ORDER BY id DESC LIMIT 10", new { pid = r.PlayerId, id = r.Id })).ToList();
            (r.TrustRate, r.TrustCount) = ComputeTrust(statuses);
        }
    }

    private static (int?, int) ComputeTrust(List<string> lastTen)
    {
        if (lastTen.Count == 0) return (null, 0);
        var approved = lastTen.Count(s => s == "3");
        var count = lastTen.Count;
        var rawRate = (double)approved / count;
        var weight = Math.Min(count / 10.0, 1);
        var rate = (int)Math.Round((0.75 * (1 - weight) + rawRate * weight) * 100);
        return (rate, count);
    }
}
