using System.Data;
using System.Text.Json;
using ClosedXML.Excel;
using Dapper;
using PayDoPay.Application.Common.Interfaces;
using PayDoPay.Application.Features.Management;

namespace PayDoPay.Infrastructure.Services;

/// <summary>Yönetim CRUD veri erişimi (Dapper) — Team/Merchant/Bank/User/Blacklist/Intermediary/Settings.</summary>
public class ManagementStore : IManagementStore
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    private readonly Application.Features.Receipts.IClaudeVisionService _vision;

    public ManagementStore(IDbConnectionFactory factory, IClock clock, Application.Features.Receipts.IClaudeVisionService vision)
    {
        _factory = factory;
        _clock = clock;
        _vision = vision;
    }

    private string NowStr => _clock.Now.ToString("yyyy-MM-dd HH:mm:ss");

    // ========== TEAM ==========
    public async Task<object> TeamsAsync(string statusFilter, string? search, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var w = "WHERE 1=1"; var p = new DynamicParameters();
        if (statusFilter == "every") { }
        else if (statusFilter != "all") { w += " AND status=@s"; p.Add("s", int.Parse(statusFilter)); }
        else w += " AND status<>0";
        if (!string.IsNullOrEmpty(search)) { w += " AND name LIKE @q"; p.Add("q", "%" + search + "%"); }
        return await c.QueryAsync($"SELECT * FROM teams {w} ORDER BY name", p);
    }

    public async Task<object?> TeamAsync(int id, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        return await c.QueryFirstOrDefaultAsync("SELECT * FROM teams WHERE id=@id", new { id });
    }

    public async Task<MgmtResult> CreateTeamAsync(TeamUpsertBody b, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        if (await c.ExecuteScalarAsync<int>("SELECT EXISTS(SELECT 1 FROM teams WHERE name=@n)", new { n = b.Name }) == 1)
            return MgmtResult.Msg(422, "Bu takım adı zaten mevcut.");
        int en(int? v) => v ?? 0;
        var creditEnabled = en(b.TelegramCreditLowEnabled);
        var id = await c.ExecuteScalarAsync<int>(@"INSERT INTO teams
            (name,status,min_invest,max_invest,wait_limit,commission,maxCase,allow_duplicate_iban,block_when_full,account_perm,allowed_customers,overturn,withdraw,
             telegram_enabled,telegram_chat_id,telegram_withdraw_chat_id,telegram_reconciliation_chat_id,
             telegram_credit_low_enabled,telegram_credit_low_threshold,telegram_credit_low_state,telegram_credit_low_enabled_at,
             telegram_pending_invest_enabled,telegram_pending_invest_enabled_at,telegram_missing_receipt_enabled,telegram_missing_receipt_enabled_at,
             telegram_withdraw_assigned_enabled,telegram_withdraw_assigned_enabled_at,telegram_cash_report_enabled,created_at)
            VALUES (@name,@status,@min,@max,@wait,@comm,@maxCase,@adi,@bwf,0,'',0,0,
             @te,@tci,@twci,@trci,
             @cle,@clt,@cls,@clea,
             @tpie,@tpiea,@tmre,@tmrea,
             @twae,@twaea,@tcre,@at); SELECT LAST_INSERT_ID();",
            new
            {
                name = b.Name, status = en(b.Status), min = b.MinInvest ?? 0, max = b.MaxInvest ?? 0, wait = b.WaitLimit ?? 0,
                comm = b.Commission ?? 0, maxCase = b.MaxCase ?? 0, adi = en(b.AllowDuplicateIban), bwf = b.BlockWhenFull ?? 1,
                te = en(b.TelegramEnabled), tci = b.TelegramChatId, twci = b.TelegramWithdrawChatId, trci = b.TelegramReconciliationChatId,
                cle = creditEnabled, clt = b.TelegramCreditLowThreshold, cls = creditEnabled == 1 ? 1 : 0, clea = creditEnabled == 1 ? NowStr : null,
                tpie = en(b.TelegramPendingInvestEnabled), tpiea = en(b.TelegramPendingInvestEnabled) == 1 ? NowStr : null,
                tmre = en(b.TelegramMissingReceiptEnabled), tmrea = en(b.TelegramMissingReceiptEnabled) == 1 ? NowStr : null,
                twae = en(b.TelegramWithdrawAssignedEnabled), twaea = en(b.TelegramWithdrawAssignedEnabled) == 1 ? NowStr : null,
                tcre = en(b.TelegramCashReportEnabled), at = NowStr,
            });
        return MgmtResult.Ok(new { id, message = "Takım oluşturuldu." });
    }

    public async Task<MgmtResult> UpdateTeamAsync(int id, TeamUpsertBody b, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var current = await c.QueryFirstOrDefaultAsync("SELECT * FROM teams WHERE id=@id", new { id });
        if (current is null) return MgmtResult.Msg(404, "Takım bulunamadı.");

        var sets = new List<string>(); var p = new DynamicParameters(); p.Add("id", id);
        void S(string col, object? val) { sets.Add($"`{col}`=@{col}"); p.Add(col, val); }
        if (b.Name is not null) S("name", b.Name);
        if (b.Status is not null) S("status", b.Status);
        if (b.MinInvest is not null) S("min_invest", b.MinInvest);
        if (b.MaxInvest is not null) S("max_invest", b.MaxInvest);
        if (b.WaitLimit is not null) S("wait_limit", b.WaitLimit);
        if (b.Commission is not null) S("commission", b.Commission);
        if (b.MaxCase is not null) S("maxCase", b.MaxCase);
        if (b.AllowDuplicateIban is not null) S("allow_duplicate_iban", b.AllowDuplicateIban);
        if (b.BlockWhenFull is not null) S("block_when_full", b.BlockWhenFull);
        if (b.Overturn is not null) S("overturn", b.Overturn);
        if (b.Withdraw is not null) S("withdraw", b.Withdraw);
        if (b.TelegramEnabled is not null) S("telegram_enabled", b.TelegramEnabled);
        S("telegram_chat_id", b.TelegramChatId);
        S("telegram_withdraw_chat_id", b.TelegramWithdrawChatId);
        S("telegram_reconciliation_chat_id", b.TelegramReconciliationChatId);
        if (b.TelegramCreditLowThreshold is not null) S("telegram_credit_low_threshold", b.TelegramCreditLowThreshold);
        if (b.TelegramCashReportEnabled is not null) S("telegram_cash_report_enabled", b.TelegramCashReportEnabled);

        // credit_low threshold/enabled değişimi → state=1
        if (b.TelegramCreditLowThreshold is not null || b.TelegramCreditLowEnabled is not null)
            S("telegram_credit_low_state", 1);

        // switch 0→1 enabled_at
        void Switch(string field, int? newVal, string tsField, string curField)
        {
            if (newVal is null) return;
            S(field, newVal);
            if (newVal == 1 && Convert.ToInt32(((IDictionary<string, object>)current)[curField] ?? 0) == 0)
                S(tsField, NowStr);
        }
        Switch("telegram_credit_low_enabled", b.TelegramCreditLowEnabled, "telegram_credit_low_enabled_at", "telegram_credit_low_enabled");
        Switch("telegram_pending_invest_enabled", b.TelegramPendingInvestEnabled, "telegram_pending_invest_enabled_at", "telegram_pending_invest_enabled");
        Switch("telegram_missing_receipt_enabled", b.TelegramMissingReceiptEnabled, "telegram_missing_receipt_enabled_at", "telegram_missing_receipt_enabled");
        Switch("telegram_withdraw_assigned_enabled", b.TelegramWithdrawAssignedEnabled, "telegram_withdraw_assigned_enabled_at", "telegram_withdraw_assigned_enabled");

        if (sets.Count > 0)
            await c.ExecuteAsync($"UPDATE teams SET {string.Join(",", sets)} WHERE id=@id", p);
        return MgmtResult.Msg(200, "Takım güncellendi.");
    }

    public async Task DisableTeamAsync(int id, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        await c.ExecuteAsync("UPDATE teams SET status=0 WHERE id=@id", new { id });
    }

    // ========== MERCHANT ==========
    public async Task<object> MerchantsAsync(string statusFilter, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var w = ""; var p = new DynamicParameters();
        if (statusFilter != "all") { w = "WHERE merchantUser.status=@s"; p.Add("s", statusFilter); }
        return await c.QueryAsync($@"SELECT merchantUser.*, merchant_groups.name AS group_name
            FROM merchantUser LEFT JOIN merchant_groups ON merchantUser.group_id=merchant_groups.id
            {w} ORDER BY merchantUser.name", p);
    }

    public async Task<MgmtResult> CreateMerchantAsync(MerchantUpsertBody b, string apiKey, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var id = await c.ExecuteScalarAsync<int>(@"INSERT INTO merchantUser
            (name,email,password,apiKey,commission,withdrawCommission,deliveryCommission,depositLimit,minDeposit,maxDeposit,group_id,approved_ip,status,caseNow,useWallet,created_At)
            VALUES (@name,@email,'',@key,@comm,@wc,@dc,@dl,@minD,@maxD,@gid,@ip,'1',0,0,@at); SELECT LAST_INSERT_ID();",
            new { name = b.Name, email = b.Email, key = apiKey, comm = b.Commission ?? 0, wc = b.WithdrawCommission ?? 0, dc = b.DeliveryCommission ?? 0,
                dl = b.DepositLimit ?? 0, minD = b.MinDeposit ?? 0, maxD = b.MaxDeposit ?? 0, gid = b.GroupId, ip = b.ApprovedIp, at = NowStr });
        return MgmtResult.Ok(new { id, api_key = apiKey, message = "Merchant oluşturuldu." });
    }

    public async Task UpdateMerchantAsync(int id, MerchantUpsertBody b, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var sets = new List<string>(); var p = new DynamicParameters(); p.Add("id", id);
        void S(string col, object? v) { sets.Add($"`{col}`=@{col}"); p.Add(col, v); }
        if (b.Name is not null) S("name", b.Name);
        S("email", b.Email);
        if (b.Commission is not null) S("commission", b.Commission);
        if (b.WithdrawCommission is not null) S("withdrawCommission", b.WithdrawCommission);
        if (b.DeliveryCommission is not null) S("deliveryCommission", b.DeliveryCommission);
        if (b.DepositLimit is not null) S("depositLimit", b.DepositLimit);
        if (b.MinDeposit is not null) S("minDeposit", b.MinDeposit);
        if (b.MaxDeposit is not null) S("maxDeposit", b.MaxDeposit);
        S("group_id", b.GroupId);
        S("approved_ip", b.ApprovedIp);
        if (b.Status is not null) S("status", b.Status);
        if (b.NewApi is not null) S("new_api", b.NewApi);
        if (sets.Count > 0) await c.ExecuteAsync($"UPDATE merchantUser SET {string.Join(",", sets)} WHERE id=@id", p);
    }

    public async Task DisableMerchantAsync(int id, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        await c.ExecuteAsync("UPDATE merchantUser SET status='0' WHERE id=@id", new { id });
    }

    public async Task<object?> ShowCredentialsAsync(int id, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var m = await c.QueryFirstOrDefaultAsync("SELECT id, name, apiKey, apiSecret FROM merchantUser WHERE id=@id", new { id });
        if (m is null) return null;
        return new { id = (int)m.id, name = (string)m.name, api_key = (string)m.apiKey, has_secret = !string.IsNullOrEmpty((string?)m.apiSecret) };
    }

    public async Task<MgmtResult> RotateSecretAsync(int id, string secret, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var m = await c.QueryFirstOrDefaultAsync("SELECT apiKey FROM merchantUser WHERE id=@id", new { id });
        if (m is null) return MgmtResult.Msg(404, "Merchant bulunamadı.");
        await c.ExecuteAsync("UPDATE merchantUser SET apiSecret=@s WHERE id=@id", new { s = secret, id });
        return MgmtResult.Ok(new { message = "Yeni API Secret üretildi. Bu değer bir daha gösterilmez; merchant ile paylaşın.", api_key = (string)m.apiKey, api_secret = secret });
    }

    public async Task<MgmtResult> RotateKeyAsync(int id, string key, string secret, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        if (await c.ExecuteScalarAsync<int>("SELECT EXISTS(SELECT 1 FROM merchantUser WHERE id=@id)", new { id }) != 1)
            return MgmtResult.Msg(404, "Merchant bulunamadı.");
        await c.ExecuteAsync("UPDATE merchantUser SET apiKey=@k, apiSecret=@s WHERE id=@id", new { k = key, s = secret, id });
        return MgmtResult.Ok(new { message = "Yeni API Key + Secret üretildi. Eski apiKey artık geçersiz.", api_key = key, api_secret = secret });
    }

    public async Task<object> GroupsAsync(CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var groups = (await c.QueryAsync("SELECT * FROM merchant_groups WHERE status=1 ORDER BY name")).ToList();
        var result = new List<object>();
        foreach (var g in groups)
        {
            var merchants = await c.QueryAsync("SELECT id, name, status FROM merchantUser WHERE group_id=@g", new { g = (int)g.id });
            result.Add(new { id = (int)g.id, name = (string)g.name, status = (int)g.status, merchants });
        }
        var ungrouped = await c.QueryAsync("SELECT id, name FROM merchantUser WHERE group_id IS NULL AND status='1'");
        return new { groups = result, ungrouped };
    }

    public async Task<MgmtResult> CreateGroupAsync(string name, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var id = await c.ExecuteScalarAsync<int>("INSERT INTO merchant_groups (name,status,created_at) VALUES (@n,1,@at); SELECT LAST_INSERT_ID();", new { n = name, at = NowStr });
        return MgmtResult.Ok(new { id, message = "Grup oluşturuldu." });
    }

    public async Task UpdateGroupAsync(int id, GroupBody b, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var sets = new List<string>(); var p = new DynamicParameters(); p.Add("id", id);
        if (b.Name is not null) { sets.Add("name=@n"); p.Add("n", b.Name); }
        if (b.Status is not null) { sets.Add("status=@s"); p.Add("s", b.Status); }
        if (sets.Count > 0) await c.ExecuteAsync($"UPDATE merchant_groups SET {string.Join(",", sets)} WHERE id=@id", p);
    }

    public async Task DisableGroupAsync(int id, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        await c.ExecuteAsync("UPDATE merchantUser SET group_id=NULL WHERE group_id=@id", new { id });
        await c.ExecuteAsync("UPDATE merchant_groups SET status=0 WHERE id=@id", new { id });
    }

    public async Task AssignGroupAsync(int merchantId, int? groupId, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        await c.ExecuteAsync("UPDATE merchantUser SET group_id=@g WHERE id=@id", new { g = groupId, id = merchantId });
    }

    // ========== BANK ACCOUNT ==========
    public async Task<object> BankAccountsAsync(int? scopeTeamId, string statusFilter, int? bankId, int? teamId, string? search, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var w = "WHERE 1=1"; var p = new DynamicParameters();
        if (scopeTeamId is not null) { w += " AND bankAccounts.team_id=@scope"; p.Add("scope", scopeTeamId); }
        if (statusFilter != "all") { w += " AND bankAccounts.status=@s"; p.Add("s", int.Parse(statusFilter)); }
        else w += " AND bankAccounts.status<>0";
        if (bankId is not null) { w += " AND bankAccounts.bank_id=@bank"; p.Add("bank", bankId); }
        if (teamId is not null) { w += " AND bankAccounts.team_id=@team"; p.Add("team", teamId); }
        if (!string.IsNullOrEmpty(search)) { w += " AND (bankAccounts.account_holder LIKE @q OR bankAccounts.account_iban LIKE @q OR bankAccounts.account_code LIKE @q)"; p.Add("q", "%" + search + "%"); }

        var rows = (await c.QueryAsync($@"SELECT bankAccounts.*, banks.name AS bank_name, banks.code AS bank_code, banks.logo AS bank_logo, teams.name AS team_name
            FROM bankAccounts JOIN banks ON bankAccounts.bank_id=banks.id JOIN teams ON bankAccounts.team_id=teams.id
            {w} ORDER BY bankAccounts.sort_order, bankAccounts.id DESC", p)).ToList();

        var today = _clock.Today.ToString("yyyy-MM-dd");
        foreach (var a in rows)
        {
            var u = await c.QueryFirstOrDefaultAsync("SELECT COUNT(*) AS cnt, COALESCE(SUM(amount),0) AS amt FROM invest WHERE bank_id=@id AND status IN ('1','2','3') AND DATE(created_at)=@d", new { id = (int)a.id, d = today });
            a.daily_count_used = u is null ? 0 : (long)u.cnt;
            a.max_amount_used = u is null ? 0d : Convert.ToDouble(u.amt);
        }
        return rows;
    }

    public async Task<object> BanksAsync(CancellationToken ct = default)
    { using var c = await _factory.CreateOpenConnectionAsync(ct); return await c.QueryAsync("SELECT * FROM banks ORDER BY name"); }

    public async Task<object> TeamsListAsync(CancellationToken ct = default)
    { using var c = await _factory.CreateOpenConnectionAsync(ct); return await c.QueryAsync("SELECT * FROM teams ORDER BY name"); }

    public async Task<object?> BankAccountAsync(int id, int? scopeTeamId, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var a = await c.QueryFirstOrDefaultAsync(@"SELECT bankAccounts.*, banks.name AS bank_name, teams.name AS team_name
            FROM bankAccounts JOIN banks ON bankAccounts.bank_id=banks.id JOIN teams ON bankAccounts.team_id=teams.id WHERE bankAccounts.id=@id", new { id });
        if (a is null) return null;
        if (scopeTeamId is not null && (int)a.team_id != scopeTeamId.Value) return "FORBIDDEN";
        return a;
    }

    public async Task<MgmtResult> IdentifyBankAsync(string iban, CancellationToken ct = default)
    {
        var clean = iban.Replace(" ", "");
        if (clean.Length < 26) return MgmtResult.Msg(422, "Geçersiz IBAN.");
        var code = clean.Substring(5, 4);
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var bank = await c.QueryFirstOrDefaultAsync("SELECT * FROM banks WHERE code=@code", new { code });
        return bank is null ? MgmtResult.Msg(404, "Banka bulunamadı.") : MgmtResult.Ok(new { bank });
    }

    public async Task<MgmtResult> CreateBankAccountAsync(BankAccountUpsertBody b, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var iban = (b.AccountIban ?? "").Replace(" ", "");
        var allowDup = await c.ExecuteScalarAsync<int>("SELECT COALESCE((SELECT allow_duplicate_iban FROM teams WHERE id=@t),0)", new { t = b.TeamId }) == 1;
        if (!allowDup && await c.ExecuteScalarAsync<int>("SELECT EXISTS(SELECT 1 FROM bankAccounts WHERE account_iban=@i AND status<>0)", new { i = iban }) == 1)
            return MgmtResult.Msg(422, "Bu IBAN zaten sistemde kayıtlı. Aynı IBAN ile birden fazla hesap eklenmesine takım ayarından izin verilmiş olmalı.");
        var nextOrder = (await c.ExecuteScalarAsync<int?>("SELECT MAX(sort_order) FROM bankAccounts") ?? 0) + 1;
        var id = await c.ExecuteScalarAsync<int>(@"INSERT INTO bankAccounts
            (bank_id,account_holder,account_iban,account_code,min_invest,max_invest,max_per_invest,max_amount,status,team_id,walletID,daily_count_limit,sort_order,created_at)
            VALUES (@bank,@holder,@iban,@code,@min,@max,@mpi,@maxAmt,@status,@team,@wallet,@dcl,@ord,@at); SELECT LAST_INSERT_ID();",
            new { bank = b.BankId, holder = b.AccountHolder, iban, code = b.AccountCode, min = b.MinInvest ?? 0, max = b.MaxInvest ?? 0,
                mpi = b.MaxPerInvest ?? 0, maxAmt = b.MaxAmount ?? 0, status = b.Status ?? 1, team = b.TeamId, wallet = b.WalletId, dcl = b.DailyCountLimit ?? 0, ord = nextOrder, at = NowStr });
        return MgmtResult.Ok(new { id, message = "Banka hesabı oluşturuldu." });
    }

    public async Task<MgmtResult> UpdateBankAccountAsync(int id, BankAccountUpsertBody b, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var current = await c.QueryFirstOrDefaultAsync("SELECT account_iban, team_id FROM bankAccounts WHERE id=@id", new { id });
        if (current is null) return MgmtResult.Msg(404, "Hesap bulunamadı.");
        var newIban = b.AccountIban is not null ? b.AccountIban.Replace(" ", "") : (string)current.account_iban;
        var newTeam = b.TeamId ?? (int)current.team_id;
        var allowDup = await c.ExecuteScalarAsync<int>("SELECT COALESCE((SELECT allow_duplicate_iban FROM teams WHERE id=@t),0)", new { t = newTeam }) == 1;
        if (!allowDup && await c.ExecuteScalarAsync<int>("SELECT EXISTS(SELECT 1 FROM bankAccounts WHERE account_iban=@i AND status<>0 AND id<>@id)", new { i = newIban, id }) == 1)
            return MgmtResult.Msg(422, "Bu IBAN zaten sistemde kayıtlı. Aynı IBAN ile birden fazla hesap eklenmesine takım ayarından izin verilmiş olmalı.");

        var sets = new List<string>(); var p = new DynamicParameters(); p.Add("id", id);
        void S(string col, object? v) { sets.Add($"`{col}`=@{col}"); p.Add(col, v); }
        if (b.Status is not null) S("status", b.Status);
        if (b.AccountCode is not null) S("account_code", b.AccountCode);
        if (b.AccountHolder is not null) S("account_holder", b.AccountHolder);
        if (b.BankId is not null) S("bank_id", b.BankId);
        if (b.MinInvest is not null) S("min_invest", b.MinInvest);
        if (b.MaxInvest is not null) S("max_invest", b.MaxInvest);
        if (b.MaxPerInvest is not null) S("max_per_invest", b.MaxPerInvest);
        if (b.MaxAmount is not null) S("max_amount", b.MaxAmount);
        if (b.TeamId is not null) S("team_id", b.TeamId);
        S("walletID", b.WalletId);
        if (b.DailyCountLimit is not null) S("daily_count_limit", b.DailyCountLimit);
        if (b.AccountIban is not null) S("account_iban", newIban);
        if (sets.Count > 0) await c.ExecuteAsync($"UPDATE bankAccounts SET {string.Join(",", sets)} WHERE id=@id", p);
        return MgmtResult.Msg(200, "Hesap güncellendi.");
    }

    public async Task DisableBankAccountAsync(int id, CancellationToken ct = default)
    { using var c = await _factory.CreateOpenConnectionAsync(ct); await c.ExecuteAsync("UPDATE bankAccounts SET status=0 WHERE id=@id", new { id }); }

    public async Task ReorderBankAccountsAsync(IReadOnlyList<int> ids, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        for (var i = 0; i < ids.Count; i++)
            await c.ExecuteAsync("UPDATE bankAccounts SET sort_order=@o WHERE id=@id", new { o = i + 1, id = ids[i] });
    }

    public async Task<MgmtResult> SetBankAccountSortAsync(int id, int position, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        if (await c.ExecuteScalarAsync<int>("SELECT EXISTS(SELECT 1 FROM bankAccounts WHERE id=@id)", new { id }) != 1)
            return MgmtResult.Msg(404, "Hesap bulunamadı.");
        await c.ExecuteAsync("UPDATE bankAccounts SET sort_order=999999 WHERE id=@id", new { id });
        await c.ExecuteAsync("UPDATE bankAccounts SET sort_order=sort_order+1 WHERE sort_order>=@p AND id<>@id", new { p = position, id });
        await c.ExecuteAsync("UPDATE bankAccounts SET sort_order=@p WHERE id=@id", new { p = position, id });
        var all = (await c.QueryAsync<int>("SELECT id FROM bankAccounts ORDER BY sort_order, id")).ToList();
        for (var i = 0; i < all.Count; i++)
            await c.ExecuteAsync("UPDATE bankAccounts SET sort_order=@o WHERE id=@id", new { o = i + 1, id = all[i] });
        return MgmtResult.Msg(200, "Öneri sırası güncellendi.");
    }

    // ========== USER ==========
    public async Task<object> UsersAsync(int? teamAdminTeamId, int? teamAdminSelfId, string? userType, string? status, string? search, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var w = "WHERE 1=1"; var p = new DynamicParameters();
        if (teamAdminTeamId is not null) { w += " AND (users.team_id=@tid OR users.id=@self)"; p.Add("tid", teamAdminTeamId); p.Add("self", teamAdminSelfId); }
        if (!string.IsNullOrEmpty(userType) && userType != "all") { w += " AND users.user_type=@ut"; p.Add("ut", int.Parse(userType)); }
        if (!string.IsNullOrEmpty(status) && status != "all") { w += " AND users.status=@st"; p.Add("st", status); }
        if (!string.IsNullOrEmpty(search)) { w += " AND (users.name LIKE @q OR users.username LIKE @q)"; p.Add("q", "%" + search + "%"); }
        var rows = await c.QueryAsync($@"SELECT users.id, users.name, users.username, users.user_type, users.status, users.team_id, users.firm_id,
            users.created_at, users.created_by, users.last_login, teams.name AS team_name, merchantUser.name AS merchant_name
            FROM users LEFT JOIN teams ON users.team_id=teams.id LEFT JOIN merchantUser ON users.firm_id=merchantUser.id
            {w} ORDER BY users.id DESC LIMIT 500", p);
        return rows.Select(u => (object)new
        {
            id = (int)u.id, name = (string)u.name, username = (string)u.username, user_type = (int?)u.user_type, status = (string)u.status,
            team_id = (int)u.team_id, firm_id = (int?)u.firm_id, created_at = u.created_at, created_by = (int?)u.created_by, last_login = u.last_login,
            team_name = (string?)u.team_name, merchant_name = (string?)u.merchant_name, role_label = RoleLabel((int?)u.user_type),
        });
    }

    private static string RoleLabel(int? t) => t switch { 1 => "Super Admin", 2 => "Team Agent", 3 => "Merchant", 4 => "Sub Admin", 5 => "Team Admin", 6 => "Blocked", _ => "-" };

    public async Task<object> TeamsForUserOptionsAsync(CancellationToken ct = default)
    { using var c = await _factory.CreateOpenConnectionAsync(ct); return await c.QueryAsync("SELECT id, name, status FROM teams ORDER BY name"); }
    public async Task<object> MerchantsForUserOptionsAsync(CancellationToken ct = default)
    { using var c = await _factory.CreateOpenConnectionAsync(ct); return await c.QueryAsync("SELECT id, name, status FROM merchantUser ORDER BY name"); }

    public async Task<PayDoPay.Domain.Entities.User?> GetUserEntityAsync(int id, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var r = await c.QueryFirstOrDefaultAsync("SELECT id, team_id, user_type, name, username, status, created_by, firm_id, merchant_group_id FROM users WHERE id=@id", new { id });
        if (r is null) return null;
        return new PayDoPay.Domain.Entities.User
        {
            Id = (int)r.id, TeamId = (int)r.team_id, UserTypeId = (int?)r.user_type, Name = (string)r.name,
            Username = (string)r.username, Status = (string)r.status, CreatedBy = (int?)r.created_by,
            FirmId = (int?)r.firm_id, MerchantGroupId = (uint?)r.merchant_group_id,
        };
    }

    public async Task<bool> UsernameExistsAsync(string username, int? exceptId, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        return await c.ExecuteScalarAsync<int>("SELECT EXISTS(SELECT 1 FROM users WHERE username=@u AND (@ex IS NULL OR id<>@ex))", new { u = username, ex = exceptId }) == 1;
    }
    public async Task<bool> TeamExistsAsync(int id, CancellationToken ct = default)
    { using var c = await _factory.CreateOpenConnectionAsync(ct); return await c.ExecuteScalarAsync<int>("SELECT EXISTS(SELECT 1 FROM teams WHERE id=@id)", new { id }) == 1; }
    public async Task<bool> MerchantExistsAsync(int id, CancellationToken ct = default)
    { using var c = await _factory.CreateOpenConnectionAsync(ct); return await c.ExecuteScalarAsync<int>("SELECT EXISTS(SELECT 1 FROM merchantUser WHERE id=@id)", new { id }) == 1; }

    public async Task<int> CreateUserAsync(string name, string username, string md5Password, int userType, int teamId, int? firmId, int status, int createdBy, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        return await c.ExecuteScalarAsync<int>(@"INSERT INTO users (name,username,password,user_type,team_id,firm_id,merchant_group_id,status,otp_ok,otp_code,created_by,created_at)
            VALUES (@name,@username,@pw,@ut,@team,@firm,NULL,@status,'0','',@by,@at); SELECT LAST_INSERT_ID();",
            new { name, username, pw = md5Password, ut = userType, team = teamId, firm = firmId, status = status.ToString(), by = createdBy, at = NowStr });
    }

    public async Task UpdateUserAsync(int id, IDictionary<string, object?> fields, bool killTokens, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        if (fields.Count > 0)
        {
            var sets = new List<string>(); var p = new DynamicParameters(); p.Add("id", id); var i = 0;
            foreach (var (k, v) in fields) { var pn = "p" + i++; sets.Add($"`{k}`=@{pn}"); p.Add(pn, v); }
            await c.ExecuteAsync($"UPDATE users SET {string.Join(",", sets)} WHERE id=@id", p);
        }
        if (killTokens) await c.ExecuteAsync("DELETE FROM personal_access_tokens WHERE tokenable_id=@id", new { id = (ulong)id });
    }

    public async Task DisableUserAsync(int id, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        await c.ExecuteAsync("UPDATE users SET status='0' WHERE id=@id", new { id });
        await c.ExecuteAsync("DELETE FROM personal_access_tokens WHERE tokenable_id=@id", new { id = (ulong)id });
    }

    // ========== BLACKLIST ==========
    public async Task<object> BlacklistAsync(string? search, string? type, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var w = "WHERE 1=1"; var p = new DynamicParameters();
        if (!string.IsNullOrEmpty(search)) { w += " AND (val LIKE @q OR `desc` LIKE @q)"; p.Add("q", "%" + search + "%"); }
        if (!string.IsNullOrEmpty(type) && type != "all") { w += " AND type=@t"; p.Add("t", int.Parse(type)); }
        return await c.QueryAsync($"SELECT * FROM blacklist {w} ORDER BY id DESC LIMIT 500", p);
    }

    public async Task<MgmtResult> CreateBlacklistAsync(BlacklistStoreBody b, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        if (await c.ExecuteScalarAsync<int>("SELECT EXISTS(SELECT 1 FROM blacklist WHERE type=@t AND val=@v)", new { t = b.Type, v = b.Val }) == 1)
            return MgmtResult.Msg(422, "Bu kayıt zaten mevcut.");
        await c.ExecuteAsync("INSERT INTO blacklist (type,val,`desc`) VALUES (@t,@v,@d)", new { t = b.Type, v = b.Val, d = b.Desc });
        return MgmtResult.Msg(200, "Kara listeye eklendi.");
    }

    public async Task<MgmtResult> UpdateBlacklistAsync(int id, string? desc, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        if (await c.ExecuteScalarAsync<int>("SELECT EXISTS(SELECT 1 FROM blacklist WHERE id=@id)", new { id }) != 1)
            return MgmtResult.Msg(404, "Kayıt bulunamadı.");
        await c.ExecuteAsync("UPDATE blacklist SET `desc`=@d WHERE id=@id", new { d = desc, id });
        return MgmtResult.Msg(200, "Güncellendi.");
    }

    public async Task<MgmtResult> DeleteBlacklistAsync(int id, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var item = await c.QueryFirstOrDefaultAsync("SELECT type, val FROM blacklist WHERE id=@id", new { id });
        if (item is null) return MgmtResult.Msg(404, "Kayıt bulunamadı.");
        var deleted = await c.ExecuteAsync("DELETE FROM blacklist WHERE type=@t AND val=@v", new { t = (int)item.type, v = (string)item.val });
        return MgmtResult.Ok(new { message = deleted > 1 ? $"Kara listeden silindi ({deleted} duplikate kayıt birlikte temizlendi)." : "Kara listeden silindi.", deleted });
    }

    public async Task<bool> BlacklistCheckAsync(string val, CancellationToken ct = default)
    { using var c = await _factory.CreateOpenConnectionAsync(ct); return await c.ExecuteScalarAsync<int>("SELECT EXISTS(SELECT 1 FROM blacklist WHERE val=@v)", new { v = val }) == 1; }

    public async Task<byte[]> ExportBlacklistAsync(string? search, string? type, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var w = "WHERE 1=1"; var p = new DynamicParameters();
        if (!string.IsNullOrEmpty(search)) { w += " AND (val LIKE @q OR `desc` LIKE @q)"; p.Add("q", "%" + search + "%"); }
        if (!string.IsNullOrEmpty(type) && type != "all") { w += " AND type=@t"; p.Add("t", int.Parse(type)); }
        var items = (await c.QueryAsync($"SELECT * FROM blacklist {w} ORDER BY id DESC", p)).ToList();
        var labels = new Dictionary<int, string> { [1] = "Oyuncu", [2] = "IBAN", [3] = "IP", [4] = "E-posta" };

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Kara Liste");
        ws.Cell(1, 1).Value = "#"; ws.Cell(1, 2).Value = "Tip"; ws.Cell(1, 3).Value = "Değer"; ws.Cell(1, 4).Value = "Açıklama";
        var hr = ws.Range("A1:D1");
        hr.Style.Font.Bold = true; hr.Style.Fill.BackgroundColor = XLColor.FromHtml("#E53935"); hr.Style.Font.FontColor = XLColor.White;
        hr.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
        var row = 2;
        foreach (var it in items)
        {
            int t = (int)it.type;
            ws.Cell(row, 1).Value = (int)it.id;
            ws.Cell(row, 2).Value = labels.TryGetValue(t, out var l) ? l : "Tip " + t;
            ws.Cell(row, 3).SetValue((string)it.val);
            ws.Cell(row, 4).Value = (string?)it.@desc ?? "";
            row++;
        }
        ws.Column(1).Width = 8; ws.Column(2).Width = 14; ws.Column(3).Width = 32; ws.Column(4).Width = 60;
        ws.Range($"A1:D{Math.Max(1, row - 1)}").Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
        ws.Range($"A1:D{Math.Max(1, row - 1)}").Style.Border.InsideBorder = XLBorderStyleValues.Thin;
        ws.SheetView.FreezeRows(1);
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    // ========== INTERMEDIARY (management) ==========
    public async Task<object> IntermediariesAsync(string statusFilter, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var w = ""; var p = new DynamicParameters();
        if (statusFilter != "all") { w = "WHERE status=@s"; p.Add("s", int.Parse(statusFilter)); }
        var inters = (await c.QueryAsync($"SELECT * FROM new_intermediaries {w} ORDER BY type, name", p)).ToList();
        var list = new List<object>();
        foreach (var i in inters)
        {
            int id = (int)i.id;
            var merchants = await c.QueryAsync(@"SELECT m.id, m.name, nim.commission_rate, nim.status, nim.id AS pivot_id
                FROM new_intermediary_merchant nim JOIN merchantUser m ON nim.merchant_id=m.id WHERE nim.intermediary_id=@id", new { id });
            var teams = await c.QueryAsync(@"SELECT t.id, t.name, nit.commission_rate, nit.status, nit.id AS pivot_id
                FROM new_intermediary_team nit JOIN teams t ON nit.team_id=t.id WHERE nit.intermediary_id=@id", new { id });
            list.Add(new { id, name = (string)i.name, type = (int)i.type, commission_rate = i.commission_rate, status = (int)i.status, balance = i.balance, created_at = i.created_at, merchants, teams });
        }
        var allMerchants = await c.QueryAsync("SELECT id, name FROM merchantUser WHERE status='1' ORDER BY name");
        var allTeams = await c.QueryAsync("SELECT id, name FROM teams WHERE status<>0 ORDER BY name");
        return new { intermediaries = list, merchants = allMerchants, teams = allTeams };
    }

    public async Task<MgmtResult> CreateIntermediaryAsync(IntermediaryStoreBody b, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var rate = b.Type == 3 ? (b.CommissionRate ?? 0) : 0;
        var id = await c.ExecuteScalarAsync<int>("INSERT INTO new_intermediaries (name,type,commission_rate,status,created_at) VALUES (@n,@t,@r,1,@at); SELECT LAST_INSERT_ID();",
            new { n = b.Name, t = b.Type, r = rate, at = NowStr });
        return MgmtResult.Ok(new { id, message = "Aracı oluşturuldu." });
    }

    public async Task UpdateIntermediaryAsync(int id, IntermediaryUpdateBody b, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var sets = new List<string>(); var p = new DynamicParameters(); p.Add("id", id);
        if (b.Name is not null) { sets.Add("name=@n"); p.Add("n", b.Name); }
        if (b.Type is not null) { sets.Add("type=@t"); p.Add("t", b.Type); }
        if (b.Status is not null) { sets.Add("status=@s"); p.Add("s", b.Status); }
        if (b.CommissionRate is not null) { sets.Add("commission_rate=@r"); p.Add("r", b.CommissionRate); }
        if (sets.Count > 0) await c.ExecuteAsync($"UPDATE new_intermediaries SET {string.Join(",", sets)} WHERE id=@id", p);
    }

    public async Task DisableIntermediaryAsync(int id, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        await c.ExecuteAsync("UPDATE new_intermediary_merchant SET status=0 WHERE intermediary_id=@id", new { id });
        await c.ExecuteAsync("UPDATE new_intermediary_team SET status=0 WHERE intermediary_id=@id", new { id });
        await c.ExecuteAsync("UPDATE new_intermediaries SET status=0 WHERE id=@id", new { id });
    }

    public async Task<MgmtResult> AttachMerchantAsync(AttachMerchantBody b, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        if (await c.ExecuteScalarAsync<int>("SELECT EXISTS(SELECT 1 FROM new_intermediary_merchant WHERE intermediary_id=@i AND merchant_id=@m)", new { i = b.IntermediaryId, m = b.MerchantId }) == 1)
            return MgmtResult.Msg(422, "Bu merchant zaten bağlı.");
        await c.ExecuteAsync("INSERT INTO new_intermediary_merchant (intermediary_id,merchant_id,commission_rate,status,created_at) VALUES (@i,@m,@r,1,@at)",
            new { i = b.IntermediaryId, m = b.MerchantId, r = b.CommissionRate, at = NowStr });
        return MgmtResult.Msg(200, "Merchant bağlandı.");
    }

    public async Task DetachMerchantAsync(int pivotId, CancellationToken ct = default)
    { using var c = await _factory.CreateOpenConnectionAsync(ct); await c.ExecuteAsync("UPDATE new_intermediary_merchant SET status=0 WHERE id=@id", new { id = pivotId }); }

    public async Task UpdateMerchantRateAsync(int pivotId, UpdateRateBody b, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        if (b.Status is not null) await c.ExecuteAsync("UPDATE new_intermediary_merchant SET commission_rate=@r, status=@s WHERE id=@id", new { r = b.CommissionRate, s = b.Status, id = pivotId });
        else await c.ExecuteAsync("UPDATE new_intermediary_merchant SET commission_rate=@r WHERE id=@id", new { r = b.CommissionRate, id = pivotId });
    }

    public async Task<MgmtResult> AttachTeamAsync(AttachTeamBody b, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        if (await c.ExecuteScalarAsync<int>("SELECT EXISTS(SELECT 1 FROM new_intermediary_team WHERE intermediary_id=@i AND team_id=@t)", new { i = b.IntermediaryId, t = b.TeamId }) == 1)
            return MgmtResult.Msg(422, "Bu takım zaten bağlı.");
        await c.ExecuteAsync("INSERT INTO new_intermediary_team (intermediary_id,team_id,commission_rate,status,created_at) VALUES (@i,@t,@r,1,@at)",
            new { i = b.IntermediaryId, t = b.TeamId, r = b.CommissionRate, at = NowStr });
        return MgmtResult.Msg(200, "Takım bağlandı.");
    }

    public async Task DetachTeamAsync(int pivotId, CancellationToken ct = default)
    { using var c = await _factory.CreateOpenConnectionAsync(ct); await c.ExecuteAsync("UPDATE new_intermediary_team SET status=0 WHERE id=@id", new { id = pivotId }); }

    public async Task UpdateTeamRateAsync(int pivotId, UpdateRateBody b, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        if (b.Status is not null) await c.ExecuteAsync("UPDATE new_intermediary_team SET commission_rate=@r, status=@s WHERE id=@id", new { r = b.CommissionRate, s = b.Status, id = pivotId });
        else await c.ExecuteAsync("UPDATE new_intermediary_team SET commission_rate=@r WHERE id=@id", new { r = b.CommissionRate, id = pivotId });
    }

    // ========== SETTINGS ==========
    public async Task<object> SettingsIndexAsync(CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var rows = await c.QueryAsync("SELECT `key`, `value` FROM system_settings");
        var dict = new Dictionary<string, object?>();
        string? anthropic = null;
        foreach (var r in rows) { if ((string)r.key == "anthropic_api_key") anthropic = (string?)r.value; else dict[(string)r.key] = (string?)r.value; }

        if (!string.IsNullOrEmpty(anthropic))
        {
            dict["anthropic_api_key_masked"] = anthropic.Length > 10 ? anthropic[..7] + new string('*', 8) + anthropic[^4..] : new string('*', anthropic.Length);
            dict["anthropic_api_key_set"] = true;
        }
        else dict["anthropic_api_key_set"] = false;

        // AI kullanım (Faz 6 ClaudeVision; cost/model şimdilik placeholder)
        var monthStart = new DateTime(_clock.Today.Year, _clock.Today.Month, 1).ToString("yyyy-MM-dd HH:mm:ss");
        var vdRows = await c.QueryAsync<string>("SELECT verification_data FROM invest_receipts WHERE verification_data IS NOT NULL AND verified_at >= @m", new { m = monthStart });
        long tin = 0, tout = 0; int count = 0; double totalCost = 0;
        foreach (var vd in vdRows)
        {
            try
            {
                using var doc = JsonDocument.Parse(vd);
                if (doc.RootElement.TryGetProperty("_usage", out var u))
                {
                    count++;
                    int i = u.TryGetProperty("input_tokens", out var ie) ? ie.GetInt32() : 0;
                    int o = u.TryGetProperty("output_tokens", out var oe) ? oe.GetInt32() : 0;
                    string? m = u.TryGetProperty("model", out var me) ? me.GetString() : null;
                    tin += i; tout += o;
                    totalCost += _vision.EstimateCost(i, o, m);
                }
            }
            catch { }
        }
        dict["anthropic_usage"] = new
        {
            month = _clock.Today.ToString("yyyy-MM"),
            analysis_count = count, total_input_tokens = tin, total_output_tokens = tout,
            estimated_cost_usd = Math.Round(totalCost, 4), current_model = _vision.Model(),
        };
        return dict;
    }

    public async Task UpdateSettingsAsync(IReadOnlyDictionary<string, string> settings, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        foreach (var (k, v) in settings)
            await c.ExecuteAsync(@"INSERT INTO system_settings (`key`,`value`,updated_at) VALUES (@k,@v,@at)
                ON DUPLICATE KEY UPDATE `value`=@v, updated_at=@at", new { k, v, at = NowStr });
    }

    public async Task<object> LogsAsync(string? direction, string? type, string? q, int page, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        const int perPage = 30;
        var w = "WHERE 1=1"; var p = new DynamicParameters();
        if (!string.IsNullOrEmpty(direction)) { w += " AND l.direction=@dir"; p.Add("dir", direction); }
        if (!string.IsNullOrEmpty(type)) { w += " AND l.type=@type"; p.Add("type", type); }
        if (!string.IsNullOrEmpty(q)) { w += " AND (l.url LIKE @q OR invest.order_id LIKE @q OR merchantUser.name LIKE @q)"; p.Add("q", "%" + q + "%"); }

        var fromJoin = @"FROM api_callback_logs l
            LEFT JOIN invest ON invest.id=l.invest_id
            LEFT JOIN merchantUser ON merchantUser.id=l.merchant_id
            LEFT JOIN users ON users.id=l.triggered_by " + w;
        var total = await c.ExecuteScalarAsync<long>("SELECT COUNT(*) " + fromJoin, p);
        p.Add("off", (page - 1) * perPage); p.Add("lim", perPage);
        var items = await c.QueryAsync($@"SELECT l.id, l.direction, l.type, l.url, l.response_status, l.duration_ms, l.error,
            l.request_payload, l.response_body, l.created_at, l.invest_id,
            invest.order_id AS invest_order_id, merchantUser.name AS merchant_name, users.username AS triggered_by_user
            {fromJoin} ORDER BY l.id DESC LIMIT @lim OFFSET @off", p);
        return new { items, total, page, per_page = perPage, pages = (int)Math.Ceiling((double)total / perPage) };
    }

    public async Task<object?> LogDetailAsync(int id, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        return await c.QueryFirstOrDefaultAsync("SELECT * FROM api_callback_logs WHERE id=@id", new { id });
    }

    public async Task<object> FindChatIdAsync(string? groupName, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        List<object> all;
        try
        {
            all = (await c.QueryAsync("SELECT chat_id, title, type, username, last_seen_at FROM telegram_chats ORDER BY last_seen_at DESC LIMIT 200"))
                .Select(x => (object)new { chat_id = (long)x.chat_id, title = (string?)x.title, type = (string?)x.type, username = (string?)x.username, last_seen_at = x.last_seen_at }).ToList();
        }
        catch { all = new List<object>(); }
        var needle = (groupName ?? "").Trim();
        var matches = string.IsNullOrEmpty(needle) ? new List<object>()
            : all.Where(x => { var t = (string?)((dynamic)x).title; return t is not null && t.Contains(needle, StringComparison.OrdinalIgnoreCase); }).ToList();
        return new
        {
            matches, all_chats = all,
            hint = all.Count == 0 ? "Henüz hiçbir grup kaydedilmedi. Botu gruba ekleyin ve grupta bir mesaj yazın (örn. /start), sonra tekrar deneyin." : null,
        };
    }
}
