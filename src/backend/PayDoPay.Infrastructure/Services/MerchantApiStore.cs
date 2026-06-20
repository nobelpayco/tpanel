using Dapper;
using PayDoPay.Application.Common.Interfaces;
using PayDoPay.Application.Features.PublicApi;

namespace PayDoPay.Infrastructure.Services;

/// <summary>invest/blacklist/merchantUser veri erişimi (Dapper) — PHP DB::table sorgularının birebir karşılığı.</summary>
public class MerchantApiStore : IMerchantApiStore
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public MerchantApiStore(IDbConnectionFactory factory, IClock clock)
    {
        _factory = factory;
        _clock = clock;
    }

    public async Task<MerchantContext?> FindMerchantByApiKeyAsync(string apiKey, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync(
            @"SELECT id, status, name, minDeposit, maxDeposit, commission, withdrawCommission, apiKey, apiSecret, new_api
              FROM merchantUser WHERE apiKey = @k LIMIT 1", new { k = apiKey });
        if (row is null) return null;
        return new MerchantContext(
            (int)row.id,
            (string)row.status,
            (string)row.name,
            (decimal)row.minDeposit,
            (decimal)row.maxDeposit,
            (decimal)row.commission,
            Convert.ToDouble(row.withdrawCommission),
            (string)row.apiKey,
            row.apiSecret as string,
            Convert.ToInt32(row.new_api) == 1);
    }

    public async Task<bool> IsBlacklistedAsync(int type, string value, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(
            "SELECT EXISTS(SELECT 1 FROM blacklist WHERE type = @t AND val = @v)", new { t = type, v = value }) == 1;
    }

    public async Task<bool> OrderIdExistsAsync(string orderOrUid, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(
            "SELECT EXISTS(SELECT 1 FROM invest WHERE order_id = @o OR u_id = @o)", new { o = orderOrUid }) == 1;
    }

    public async Task<bool> HasRecentPendingForPlayerAsync(string playerId, DateTime since, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<int>(
            @"SELECT EXISTS(SELECT 1 FROM invest
              WHERE player_id = @p AND status IN ('1','2') AND ibanSeen = 1 AND created_at >= @since)",
            new { p = playerId, since }) == 1;
    }

    private const string InvestSelect =
        @"SELECT id, type, status, name, amount, u_id, callbackUrl, callbackOkUrl, callbackFailUrl,
                 firm_id, team_id, bank_id, player_id, order_id, created_at, form_at, process_date,
                 finalize_date, ibanSeen, receipt_path, callbackSended
          FROM invest";

    public async Task<InvestRow?> GetByUidAsync(string uId, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<InvestRow>(
            InvestSelect + " WHERE u_id = @uId LIMIT 1", new { uId });
    }

    public async Task<InvestRow?> GetByOrderOrUidForMerchantAsync(int merchantId, string orderOrUid, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<InvestRow>(
            InvestSelect + " WHERE firm_id = @m AND (order_id = @o OR u_id = @o) LIMIT 1",
            new { m = merchantId, o = orderOrUid });
    }

    public async Task<BankOption?> GetBankAccountAsync(int bankId, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var row = await conn.QueryFirstOrDefaultAsync(
            @"SELECT ba.id, ba.account_holder, ba.account_iban, ba.team_id, ba.sort_order, b.name AS bank_name
              FROM bankAccounts ba JOIN banks b ON ba.bank_id = b.id
              WHERE ba.id = @id LIMIT 1", new { id = bankId });
        if (row is null) return null;
        return new BankOption((int)row.id, (string)row.account_holder, (string)row.account_iban,
            (int)row.team_id, (int)row.sort_order, (string)row.bank_name);
    }

    public async Task<int> InsertDepositHostedAsync(InvestInsert d, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var sql = @"INSERT INTO invest
            (type, status, name, amount, original_amount, u_id, callbackUrl, callbackOkUrl, callbackFailUrl,
             panel_commissin_amount, payed_amount, panel_commission_percent, api_id, firm_id, team_id, bank_id,
             player_id, order_id, added_type, created_at, ibanSeen, callbackSended, isControled, isConverted,
             walletInvest, transaction_type, amountChanged)
            VALUES
            ('1','0',@name,@amount,@amount,@uId,@cb,@ok,@fail,
             @comm,@amount,@pct,@mid,@mid,NULL,NULL,
             @player,@order,'1',@now,0,0,0,0,
             0,1,0);
            SELECT LAST_INSERT_ID();";
        return await conn.ExecuteScalarAsync<int>(sql, new
        {
            name = d.Name, amount = d.Amount, uId = d.UId, cb = d.CallbackUrl, ok = d.CallbackOkUrl,
            fail = d.CallbackFailUrl, comm = d.CommissionAmount, pct = d.CommissionPercent,
            mid = d.MerchantId, player = d.PlayerId, order = d.OrderId, now = _clock.Now,
        });
    }

    public async Task<int> InsertDepositDirectAsync(InvestInsert d, int bankId, int teamId, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var sql = @"INSERT INTO invest
            (type, status, name, amount, original_amount, u_id, callbackUrl,
             panel_commissin_amount, payed_amount, panel_commission_percent, api_id, firm_id, team_id, bank_id,
             player_id, order_id, added_type, created_at, form_at, ibanSeen, callbackSended, isControled, isConverted,
             walletInvest, transaction_type, amountChanged)
            VALUES
            ('1','1',@name,@amount,@amount,@uId,@cb,
             @comm,@amount,@pct,@mid,@mid,@team,@bank,
             @player,@order,'1',@now,@now,1,0,0,0,
             0,1,0);
            SELECT LAST_INSERT_ID();";
        return await conn.ExecuteScalarAsync<int>(sql, new
        {
            name = d.Name, amount = d.Amount, uId = d.UId, cb = d.CallbackUrl, comm = d.CommissionAmount,
            pct = d.CommissionPercent, mid = d.MerchantId, team = teamId, bank = bankId,
            player = d.PlayerId, order = d.OrderId, now = _clock.Now,
        });
    }

    public async Task<int> InsertWithdrawAsync(InvestInsert d, string ibanUpper, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var sql = @"INSERT INTO invest
            (type, status, name, amount, u_id, callbackUrl,
             panel_commissin_amount, payed_amount, panel_commission_percent, iban, api_id, firm_id, team_id, bank_id,
             player_id, order_id, added_type, created_at, ibanSeen, callbackSended, isControled, isConverted,
             walletInvest, transaction_type, amountChanged)
            VALUES
            ('2','0',@name,@amount,@uId,@cb,
             @comm,@amount,@pct,@iban,@mid,@mid,NULL,NULL,
             @player,@order,'1',@now,0,0,0,0,
             0,1,0);
            SELECT LAST_INSERT_ID();";
        return await conn.ExecuteScalarAsync<int>(sql, new
        {
            name = d.Name, amount = d.Amount, uId = d.UId, cb = d.CallbackUrl, comm = d.CommissionAmount,
            pct = d.CommissionPercent, iban = ibanUpper, mid = d.MerchantId,
            player = d.PlayerId, order = d.OrderId, now = _clock.Now,
        });
    }

    public async Task UpdateAsync(string uId, IDictionary<string, object?> fields, CancellationToken ct = default)
    {
        if (fields.Count == 0) return;
        using var conn = await _factory.CreateOpenConnectionAsync(ct);

        var setParts = new List<string>();
        var parms = new DynamicParameters();
        var i = 0;
        foreach (var (col, val) in fields)
        {
            var p = "p" + i++;
            setParts.Add($"`{col}` = @{p}");
            parms.Add(p, val);
        }
        parms.Add("uId", uId);

        var sql = $"UPDATE invest SET {string.Join(", ", setParts)} WHERE u_id = @uId";
        await conn.ExecuteAsync(sql, parms);
    }
}
