using PayDoPay.Application.Common;
using PayDoPay.Domain.Entities;

namespace PayDoPay.Application.Features.Cases;

// ===================== INTERMEDIARY =====================
public interface IIntermediaryCaseService
{
    Task<ApiResult> IndexAsync(CancellationToken ct = default);
    Task<ApiResult> ShowAsync(int id, string? from, string? to, CancellationToken ct = default);
    Task<ApiResult> PaymentsAsync(int id, string? date, string? from, string? to, CancellationToken ct = default);
    Task<ApiResult> AddPaymentAsync(User user, int id, IntermediaryPaymentBody b, CancellationToken ct = default);
    Task<ApiResult> DeletePaymentAsync(int id, int paymentId, CancellationToken ct = default);
}

public class IntermediaryCaseService : IIntermediaryCaseService
{
    private readonly ICaseStore _store;
    public IntermediaryCaseService(ICaseStore store) => _store = store;

    public async Task<ApiResult> IndexAsync(CancellationToken ct = default) => ApiResult.Ok(await _store.IntermediaryIndexAsync(ct));
    public async Task<ApiResult> ShowAsync(int id, string? from, string? to, CancellationToken ct = default)
    {
        var r = await _store.IntermediaryShowAsync(id, from, to, ct);
        return r is null ? ApiResult.Msg(404, "Aracı bulunamadı.") : ApiResult.Ok(r);
    }
    public async Task<ApiResult> PaymentsAsync(int id, string? date, string? from, string? to, CancellationToken ct = default)
        => ApiResult.Ok(await _store.IntermediaryPaymentsAsync(id, date, from, to, ct));
    public async Task<ApiResult> AddPaymentAsync(User user, int id, IntermediaryPaymentBody b, CancellationToken ct = default)
    {
        var r = await _store.AddIntermediaryPaymentAsync(id, b, new ActorInfo(user.Id, user.Name), ct);
        return r.Status == 200 ? ApiResult.Msg(200, "Ödeme eklendi.") : ApiResult.Msg(r.Status, r.Message);
    }
    public async Task<ApiResult> DeletePaymentAsync(int id, int paymentId, CancellationToken ct = default)
    {
        var r = await _store.DeleteIntermediaryPaymentAsync(id, paymentId, ct);
        return r.Status == 200 ? ApiResult.Msg(200, "Ödeme silindi.") : ApiResult.Msg(r.Status, r.Message);
    }
}

// ===================== PARTNER =====================
public interface IPartnerCaseService
{
    Task<ApiResult> IndexAsync(CancellationToken ct = default);
    Task<ApiResult> ShowAsync(int id, string? from, string? to, CancellationToken ct = default);
    Task<ApiResult> PaymentsAsync(int id, string? date, string? from, string? to, CancellationToken ct = default);
    Task<ApiResult> AddPaymentAsync(User user, int id, PartnerPaymentBody b, CancellationToken ct = default);
    Task<ApiResult> DeletePaymentAsync(int id, int paymentId, CancellationToken ct = default);
    Task<ApiResult> CapitalsAsync(int id, string? from, string? to, CancellationToken ct = default);
    Task<ApiResult> AddCapitalAsync(User user, int id, CapitalBody b, CancellationToken ct = default);
    Task<ApiResult> DeleteCapitalAsync(int id, int capitalId, CancellationToken ct = default);
    Task<ApiResult> ExpensesAsync(string? from, string? to, CancellationToken ct = default);
    Task<ApiResult> AddExpenseAsync(User user, ExpenseBody b, CancellationToken ct = default);
    Task<ApiResult> DeleteExpenseAsync(User user, int id, CancellationToken ct = default);
    Task<ApiResult> AllPartnerPaymentsAsync(string? from, string? to, CancellationToken ct = default);
    Task<ApiResult> AddTransferAsync(User user, int id, PartnerTransferBody b, CancellationToken ct = default);
    Task<ApiResult> DeleteTransferAsync(int id, int transferId, CancellationToken ct = default);
}

public class PartnerCaseService : IPartnerCaseService
{
    private readonly ICaseStore _store;
    public PartnerCaseService(ICaseStore store) => _store = store;
    private static ActorInfo A(User u) => new(u.Id, u.Name);

    public async Task<ApiResult> IndexAsync(CancellationToken ct = default) => ApiResult.Ok(await _store.PartnerIndexAsync(ct));
    public async Task<ApiResult> ShowAsync(int id, string? from, string? to, CancellationToken ct = default)
    {
        var r = await _store.PartnerShowAsync(id, from, to, ct);
        return r is null ? ApiResult.Msg(404, "Ortak bulunamadı.") : ApiResult.Ok(r);
    }
    public async Task<ApiResult> PaymentsAsync(int id, string? date, string? from, string? to, CancellationToken ct = default)
        => ApiResult.Ok(await _store.PartnerPaymentsAsync(id, date, from, to, ct));
    public async Task<ApiResult> AddPaymentAsync(User user, int id, PartnerPaymentBody b, CancellationToken ct = default)
    { var r = await _store.AddPartnerPaymentAsync(id, b, A(user), ct); return r.Status == 200 ? ApiResult.Msg(200, "Ödeme eklendi.") : ApiResult.Msg(r.Status, r.Message); }
    public async Task<ApiResult> DeletePaymentAsync(int id, int paymentId, CancellationToken ct = default)
    { var r = await _store.DeletePartnerPaymentAsync(id, paymentId, ct); return r.Status == 200 ? ApiResult.Msg(200, "Ödeme silindi.") : ApiResult.Msg(r.Status, r.Message); }
    public async Task<ApiResult> CapitalsAsync(int id, string? from, string? to, CancellationToken ct = default)
        => ApiResult.Ok(await _store.CapitalsAsync(id, from, to, ct));
    public async Task<ApiResult> AddCapitalAsync(User user, int id, CapitalBody b, CancellationToken ct = default)
    { var r = await _store.AddCapitalAsync(id, b, A(user), ct); return r.Status == 200 ? ApiResult.Msg(200, "Sermaye eklendi.") : ApiResult.Msg(r.Status, r.Message); }
    public async Task<ApiResult> DeleteCapitalAsync(int id, int capitalId, CancellationToken ct = default)
    { var r = await _store.DeleteCapitalAsync(id, capitalId, ct); return r.Status == 200 ? ApiResult.Msg(200, "Sermaye silindi.") : ApiResult.Msg(r.Status, r.Message); }
    public async Task<ApiResult> ExpensesAsync(string? from, string? to, CancellationToken ct = default)
        => ApiResult.Ok(await _store.ExpensesAsync(from, to, ct));
    public async Task<ApiResult> AddExpenseAsync(User user, ExpenseBody b, CancellationToken ct = default)
    { var r = await _store.AddExpenseAsync(b, A(user), ct); return r.Status == 200 ? ApiResult.Msg(200, "Masraf eklendi.") : ApiResult.Msg(r.Status, r.Message); }
    public async Task<ApiResult> DeleteExpenseAsync(User user, int id, CancellationToken ct = default)
    { var r = await _store.DeleteExpenseAsync(id, A(user), ct); return r.Status == 200 ? ApiResult.Msg(200, "Masraf silindi.") : ApiResult.Msg(r.Status, r.Message); }
    public async Task<ApiResult> AllPartnerPaymentsAsync(string? from, string? to, CancellationToken ct = default)
        => ApiResult.Ok(await _store.AllPartnerPaymentsAsync(from, to, ct));
    public async Task<ApiResult> AddTransferAsync(User user, int id, PartnerTransferBody b, CancellationToken ct = default)
    { var r = await _store.AddPartnerTransferAsync(id, b, A(user), ct); return r.Status == 200 ? ApiResult.Msg(200, "Partner transferi yapıldı.") : ApiResult.Msg(r.Status, r.Message); }
    public async Task<ApiResult> DeleteTransferAsync(int id, int transferId, CancellationToken ct = default)
    { var r = await _store.DeletePartnerTransferAsync(id, transferId, ct); return r.Status == 200 ? ApiResult.Msg(200, "Transfer silindi.") : ApiResult.Msg(r.Status, r.Message); }
}

// ===================== CASE REPORT =====================
public interface ICaseReportService
{
    Task<ApiResult> SummaryAsync(User user, string? from, string? to, CancellationToken ct = default);
    Task<ApiResult> IndexAsync(User user, string? from, string? to, CancellationToken ct = default);
}

public class CaseReportService : ICaseReportService
{
    private readonly ICaseStore _store;
    public CaseReportService(ICaseStore store) => _store = store;

    public async Task<ApiResult> SummaryAsync(User user, string? from, string? to, CancellationToken ct = default)
    {
        if (user.IsMerchant) return ApiResult.Msg(403, "Bu sayfaya erişim yetkiniz yok.");
        return ApiResult.Ok(await _store.CaseReportSummaryAsync(from, to, ct));
    }
    public async Task<ApiResult> IndexAsync(User user, string? from, string? to, CancellationToken ct = default)
    {
        if (user.IsMerchant) return ApiResult.Msg(403, "Bu sayfaya erişim yetkiniz yok.");
        return ApiResult.Ok(await _store.CaseReportIndexAsync(from, to, ct));
    }
}

// ===================== INITIAL BALANCE =====================
public interface IInitialBalanceService
{
    Task<ApiResult> EntitiesAsync(CancellationToken ct = default);
    Task<ApiResult> SaveAsync(User user, InitialBalanceSaveBody b, CancellationToken ct = default);
    Task<ApiResult> ResetAsync(User user, string? date, CancellationToken ct = default);
}

public class InitialBalanceService : IInitialBalanceService
{
    private readonly ICaseStore _store;
    public InitialBalanceService(ICaseStore store) => _store = store;

    public async Task<ApiResult> EntitiesAsync(CancellationToken ct = default) => ApiResult.Ok(await _store.InitialBalanceEntitiesAsync(ct));

    public async Task<ApiResult> SaveAsync(User user, InitialBalanceSaveBody b, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(b.Date) || b.Entities is null || b.Entities.Count == 0)
            return ApiResult.Msg(422, "Tarih ve varlık listesi zorunludur.");
        await _store.SaveInitialBalanceAsync(b.Date, b.Entities, user.Name, ct);
        return ApiResult.Msg(200, "Başlangıç bakiyeleri kaydedildi.");
    }

    public async Task<ApiResult> ResetAsync(User user, string? date, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(date)) return ApiResult.Msg(422, "Tarih zorunludur.");
        var r = await _store.ResetInitialBalanceAsync(date, user.Name, ct);
        return r.Status == 200 ? ApiResult.Msg(200, "Snapshot'lar sıfırlandı ve başlangıç bakiyeleri geri yüklendi.") : ApiResult.Msg(r.Status, r.Message);
    }
}
