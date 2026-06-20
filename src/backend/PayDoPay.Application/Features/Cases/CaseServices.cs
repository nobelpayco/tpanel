using PayDoPay.Application.Common;
using PayDoPay.Application.Common.Interfaces;
using PayDoPay.Domain.Entities;

namespace PayDoPay.Application.Features.Cases;

// ===================== TEAM =====================
public interface ITeamCaseService
{
    Task<ApiResult> IndexAsync(CancellationToken ct = default);
    Task<ApiResult> ShowAsync(int id, string? from, string? to, CancellationToken ct = default);
    Task<ApiResult> PaymentsAsync(int id, string? date, string? from, string? to, CancellationToken ct = default);
    Task<ApiResult> AddPaymentAsync(User user, int id, CasePaymentBody b, CancellationToken ct = default);
    Task<ApiResult> DeletePaymentAsync(User user, int id, int paymentId, CancellationToken ct = default);
    Task<ApiResult> AddTransferAsync(User user, int id, TeamTransferBody b, CancellationToken ct = default);
    Task<ApiResult> DeleteTransferAsync(User user, int id, int transferId, CancellationToken ct = default);
    Task<ApiResult> AddSyncAsync(User user, int id, TeamSyncBody b, CancellationToken ct = default);
    Task<ApiResult> DeleteSyncAsync(User user, int id, int syncId, CancellationToken ct = default);
}

public class TeamCaseService : ITeamCaseService
{
    private readonly ICaseStore _store;
    private readonly IMerchantBankService _banks;

    public TeamCaseService(ICaseStore store, IMerchantBankService banks)
    {
        _store = store;
        _banks = banks;
    }

    private static ActorInfo Actor(User u) => new(u.Id, string.IsNullOrEmpty(u.Name) ? u.Username : u.Name);

    public async Task<ApiResult> IndexAsync(CancellationToken ct = default)
    {
        var teams = await _store.GetTeamsBasicAsync(ct);
        var cashes = await _banks.CurrentCashForTeamsAsync(teams.Select(t => t.Id).ToList(), ct);
        var list = teams
            .Select(t => new { id = t.Id, name = t.Name, current_case = Math.Round(cashes.GetValueOrDefault(t.Id, 0), 2) })
            .Where(t => t.current_case != 0)
            .ToList();
        return ApiResult.Ok(new { teams = list, total_case = list.Sum(t => t.current_case) });
    }

    public async Task<ApiResult> ShowAsync(int id, string? from, string? to, CancellationToken ct = default)
    {
        if (await _store.GetTeamMetaAsync(id, ct) is null) return ApiResult.Msg(404, "Takım bulunamadı.");
        return ApiResult.Ok(await _store.TeamShowAsync(id, from, to, ct));
    }

    public async Task<ApiResult> PaymentsAsync(int id, string? date, string? from, string? to, CancellationToken ct = default)
        => ApiResult.Ok(await _store.TeamPaymentsAsync(id, date, from, to, ct));

    public async Task<ApiResult> AddPaymentAsync(User user, int id, CasePaymentBody b, CancellationToken ct = default)
    {
        if (b.PaymentType is not (1 or 2) || b.Amount < 0.01 || b.FundStorageId is null)
            return ApiResult.Msg(422, "Geçersiz ödeme bilgileri.");
        var r = await _store.AddTeamPaymentAsync(id, b, Actor(user), ct);
        if (r.Status == 200) await _banks.EnforceMaxCaseAsync(new[] { id }, ct);
        return r.Status == 200 ? ApiResult.Msg(200, "Ödeme eklendi.") : ApiResult.Msg(r.Status, r.Message);
    }

    public async Task<ApiResult> DeletePaymentAsync(User user, int id, int paymentId, CancellationToken ct = default)
    {
        var r = await _store.DeleteTeamPaymentAsync(id, paymentId, Actor(user), ct);
        return r.Status == 200 ? ApiResult.Msg(200, "Ödeme silindi.") : ApiResult.Msg(r.Status, r.Message);
    }

    public async Task<ApiResult> AddTransferAsync(User user, int id, TeamTransferBody b, CancellationToken ct = default)
    {
        if (b.Amount < 0.01) return ApiResult.Msg(422, "Tutar geçersiz.");
        var r = await _store.AddTeamTransferAsync(id, b, Actor(user), ct);
        if (r.Status == 200) await _banks.EnforceMaxCaseAsync(new[] { id, b.ToTeamId }, ct);
        return r.Status == 200 ? ApiResult.Msg(200, "Transfer yapıldı.") : ApiResult.Msg(r.Status, r.Message);
    }

    public async Task<ApiResult> DeleteTransferAsync(User user, int id, int transferId, CancellationToken ct = default)
    {
        var r = await _store.DeleteTeamTransferAsync(id, transferId, Actor(user), ct);
        return r.Status == 200 ? ApiResult.Msg(200, "Transfer silindi.") : ApiResult.Msg(r.Status, r.Message);
    }

    public async Task<ApiResult> AddSyncAsync(User user, int id, TeamSyncBody b, CancellationToken ct = default)
    {
        var r = await _store.AddTeamSyncAsync(id, b, Actor(user), ct);
        if (r.Status == 200) await _banks.EnforceMaxCaseAsync(new[] { id }, ct);
        return r.Status == 200 ? ApiResult.Msg(200, "Senkron eklendi.") : ApiResult.Msg(r.Status, r.Message);
    }

    public async Task<ApiResult> DeleteSyncAsync(User user, int id, int syncId, CancellationToken ct = default)
    {
        var r = await _store.DeleteTeamSyncAsync(id, syncId, Actor(user), ct);
        return r.Status == 200 ? ApiResult.Msg(200, "Senkron silindi.") : ApiResult.Msg(r.Status, r.Message);
    }
}

// ===================== MERCHANT =====================
public interface IMerchantCaseService
{
    Task<ApiResult> IndexAsync(User user, CancellationToken ct = default);
    Task<ApiResult> ShowAsync(User user, int id, bool isGroup, string? from, string? to, CancellationToken ct = default);
    Task<ApiResult> PayliraDailyNetAsync(User user, string? from, string? to, CancellationToken ct = default);
    Task<ApiResult> PaymentsAsync(User user, int id, bool isGroup, string? date, string? from, string? to, CancellationToken ct = default);
    Task<ApiResult> AddPaymentAsync(User user, int id, MerchantPaymentBody b, CancellationToken ct = default);
    Task<ApiResult> DeletePaymentAsync(User user, int id, int paymentId, CancellationToken ct = default);
}

public class MerchantCaseService : IMerchantCaseService
{
    private readonly ICaseStore _store;

    public MerchantCaseService(ICaseStore store)
    {
        _store = store;
    }

    public async Task<ApiResult> IndexAsync(User user, CancellationToken ct = default)
    {
        if (user.IsMerchant) return ApiResult.Msg(403, "Bu sayfaya erişim yetkiniz yok.");
        return ApiResult.Ok(await _store.MerchantIndexAsync(ct));
    }

    public async Task<ApiResult> ShowAsync(User user, int id, bool isGroup, string? from, string? to, CancellationToken ct = default)
    {
        if (user.IsMerchant && !await IsAllowedAsync(user, id, isGroup, ct))
            return ApiResult.Msg(403, "Bu sayfaya erişim yetkiniz yok.");
        var res = await _store.MerchantShowAsync(id, isGroup, from, to, ct);
        return res is null ? ApiResult.Msg(404, isGroup ? "Grup bulunamadı." : "Merchant bulunamadı.") : ApiResult.Ok(res);
    }

    public async Task<ApiResult> PayliraDailyNetAsync(User user, string? from, string? to, CancellationToken ct = default)
    {
        if (user.IsMerchant) return ApiResult.Msg(403, "Bu sayfaya erişim yetkiniz yok.");
        return ApiResult.Ok(await _store.PayliraDailyNetAsync(from, to, ct));
    }

    public async Task<ApiResult> PaymentsAsync(User user, int id, bool isGroup, string? date, string? from, string? to, CancellationToken ct = default)
    {
        if (user.IsMerchant && !await IsAllowedAsync(user, id, isGroup, ct))
            return ApiResult.Msg(403, "Bu sayfaya erişim yetkiniz yok.");
        return ApiResult.Ok(await _store.MerchantPaymentsAsync(id, isGroup, date, from, to, ct));
    }

    public async Task<ApiResult> AddPaymentAsync(User user, int id, MerchantPaymentBody b, CancellationToken ct = default)
    {
        if (user.IsMerchant) return ApiResult.Msg(403, "Bu sayfaya erişim yetkiniz yok.");
        if (b.PaymentType is not (1 or 2) || b.Amount == 0) return ApiResult.Msg(422, "Geçersiz ödeme bilgileri.");
        var r = await _store.AddMerchantPaymentAsync(id, b, new ActorInfo(user.Id, user.Name), ct);
        return r.Status == 200 ? ApiResult.Msg(200, "Ödeme eklendi.") : ApiResult.Msg(r.Status, r.Message);
    }

    public async Task<ApiResult> DeletePaymentAsync(User user, int id, int paymentId, CancellationToken ct = default)
    {
        if (user.IsMerchant) return ApiResult.Msg(403, "Bu sayfaya erişim yetkiniz yok.");
        var r = await _store.DeleteMerchantPaymentAsync(id, paymentId, ct);
        return r.Status == 200 ? ApiResult.Msg(200, "Ödeme silindi.") : ApiResult.Msg(r.Status, r.Message);
    }

    private async Task<bool> IsAllowedAsync(User user, int id, bool isGroup, CancellationToken ct)
    {
        if (isGroup) return user.MerchantGroupId.HasValue && (int)user.MerchantGroupId.Value == id;
        var ids = await _store.GetMerchantIdsAsync(user.MerchantGroupId.HasValue ? (int)user.MerchantGroupId.Value : null, user.FirmId, ct);
        return ids.Contains(id);
    }
}

// ===================== FUND STORAGE =====================
public interface IFundStorageService
{
    Task<ApiResult> IndexAsync(string statusFilter, CancellationToken ct = default);
    Task<ApiResult> CreateAsync(FundStorageBody b, CancellationToken ct = default);
    Task<ApiResult> UpdateAsync(int id, FundStorageBody b, CancellationToken ct = default);
    Task<ApiResult> DestroyAsync(int id, CancellationToken ct = default);
    Task<ApiResult> ShowAsync(int id, string? from, string? to, CancellationToken ct = default);
    Task<ApiResult> TransfersAsync(string? from, string? to, CancellationToken ct = default);
    Task<ApiResult> CreateTransferAsync(User user, FundTransferBody b, CancellationToken ct = default);
    Task<ApiResult> DeleteTransferAsync(int id, CancellationToken ct = default);
    Task<ApiResult> AddSyncAsync(User user, FundSyncBody b, CancellationToken ct = default);
    Task<ApiResult> DeleteSyncAsync(User user, int id, CancellationToken ct = default);
    Task<ApiResult> TronTxLookupAsync(string? txLink, CancellationToken ct = default);
}

public class FundStorageService : IFundStorageService
{
    private readonly ICaseStore _store;
    private readonly ITronService _tron;

    public FundStorageService(ICaseStore store, ITronService tron)
    {
        _store = store;
        _tron = tron;
    }

    public async Task<ApiResult> IndexAsync(string statusFilter, CancellationToken ct = default)
    {
        var storages = await _store.GetStoragesAsync(statusFilter, ct);
        foreach (var s in storages)
            if (s.Type == 2 && !string.IsNullOrEmpty(s.WalletAddress))
                s.ChainBalance = await _tron.GetUsdtBalanceAsync(s.WalletAddress!, ct);
        return ApiResult.Ok(new { storages, total_balance = storages.Sum(s => s.Balance) });
    }

    public async Task<ApiResult> CreateAsync(FundStorageBody b, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(b.Name) || b.Type is not (1 or 2 or 3 or 4) || b.Balance is null)
            return ApiResult.Msg(422, "Geçersiz bilgiler.");
        var id = await _store.CreateStorageAsync(b, ct);
        return ApiResult.Ok(new { id, message = "Fon deposu oluşturuldu." });
    }

    public async Task<ApiResult> UpdateAsync(int id, FundStorageBody b, CancellationToken ct = default)
    {
        await _store.UpdateStorageAsync(id, b, ct);
        return ApiResult.Msg(200, "Güncellendi.");
    }

    public async Task<ApiResult> DestroyAsync(int id, CancellationToken ct = default)
    {
        await _store.DisableStorageAsync(id, ct);
        return ApiResult.Msg(200, "Devre dışı bırakıldı.");
    }

    public async Task<ApiResult> ShowAsync(int id, string? from, string? to, CancellationToken ct = default)
    {
        if (await _store.GetStorageAsync(id, ct) is null) return ApiResult.Msg(404, "Fon deposu bulunamadı.");
        return ApiResult.Ok(await _store.FundStorageShowAsync(id, from, to, ct));
    }

    public async Task<ApiResult> TransfersAsync(string? from, string? to, CancellationToken ct = default)
        => ApiResult.Ok(await _store.FundTransfersAsync(from, to, ct));

    public async Task<ApiResult> CreateTransferAsync(User user, FundTransferBody b, CancellationToken ct = default)
    {
        var r = await _store.CreateFundTransferAsync(b, new ActorInfo(user.Id, user.Name), ct);
        return r.Status == 200 ? ApiResult.Msg(200, "Transfer tamamlandı.") : ApiResult.Msg(r.Status, r.Message);
    }

    public async Task<ApiResult> DeleteTransferAsync(int id, CancellationToken ct = default)
    {
        var r = await _store.DeleteFundTransferAsync(id, ct);
        return r.Status == 200 ? ApiResult.Msg(200, "Transfer silindi.") : ApiResult.Msg(r.Status, r.Message);
    }

    public async Task<ApiResult> AddSyncAsync(User user, FundSyncBody b, CancellationToken ct = default)
    {
        if (!user.IsAdmin) return ApiResult.Msg(403, "Bu işlem için yetkiniz yok.");
        var r = await _store.AddFundSyncAsync(b, new ActorInfo(user.Id, user.Name), ct);
        return r.Status == 200 ? ApiResult.Msg(200, "Senkron eklendi.") : ApiResult.Msg(r.Status, r.Message);
    }

    public async Task<ApiResult> DeleteSyncAsync(User user, int id, CancellationToken ct = default)
    {
        if (!user.IsAdmin) return ApiResult.Msg(403, "Bu işlem için yetkiniz yok.");
        var r = await _store.DeleteFundSyncAsync(id, ct);
        return r.Status == 200 ? ApiResult.Msg(200, "Senkron silindi.") : ApiResult.Msg(r.Status, r.Message);
    }

    public async Task<ApiResult> TronTxLookupAsync(string? txLink, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(txLink)) return ApiResult.Msg(422, "İşlem linki zorunludur.");
        var (quantity, type, error) = await _tron.TxLookupAsync(txLink, ct);
        if (error is not null) return ApiResult.Msg(422, error);
        return ApiResult.Ok(new { quantity, type });
    }
}
