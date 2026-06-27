using TPanel.Application.Common;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.PublicApi;
using TPanel.Domain.Entities;

namespace TPanel.Application.Features.Transactions;

public interface IDepositAdminService
{
    Task<ApiResult> PendingAsync(User user, TxFilter f, CancellationToken ct = default);
    Task<ApiResult> AllAsync(User user, TxFilter f, CancellationToken ct = default);
    Task<ApiResult> DetailAsync(User user, int id, CancellationToken ct = default);
    Task<ApiResult> ApproveAsync(User user, int id, double? amount, string ip, CancellationToken ct = default);
    Task<ApiResult> RejectAsync(User user, int id, int rejectType, string ip, CancellationToken ct = default);
    /// <summary>Onaylı (status=3) yatırımı reddet — yalnızca Süper Admin, sebep zorunlu, CALLBACK GÖNDERİLMEZ.</summary>
    Task<ApiResult> ForceRejectAsync(User user, int id, string reason, string ip, CancellationToken ct = default);
    Task<ApiResult> FilterMetaAsync(User user, CancellationToken ct = default);
    Task<ApiResult> ResendCallbackAsync(User user, int id, CancellationToken ct = default);
}

public class DepositAdminService : IDepositAdminService
{
    private readonly ITransactionAdminStore _store;
    private readonly ICallbackService _callbacks;
    private readonly IMerchantBankService _banks;
    private readonly IClock _clock;
    private readonly TPanel.Application.Features.Audit.IAuditContext _audit;

    public DepositAdminService(ITransactionAdminStore store, ICallbackService callbacks, IMerchantBankService banks, IClock clock, TPanel.Application.Features.Audit.IAuditContext audit)
    {
        _store = store;
        _callbacks = callbacks;
        _banks = banks;
        _clock = clock;
        _audit = audit;
    }

    private async Task<QueryScope> ScopeAsync(User user, CancellationToken ct)
    {
        if (user.IsTeamMember) return new QueryScope(ScopeKind.Team, user.TeamId);
        if (user.IsMerchant)
        {
            var ids = await _store.GetMerchantIdsForUserAsync(user.MerchantGroupId.HasValue ? (int)user.MerchantGroupId : null, user.FirmId, ct);
            return new QueryScope(ScopeKind.Merchant, MerchantIds: ids);
        }
        return QueryScope.Global;
    }

    public async Task<ApiResult> PendingAsync(User user, TxFilter f, CancellationToken ct = default)
    {
        var scope = await ScopeAsync(user, ct);
        var rows = await _store.GetDepositsPendingAsync(scope, f, ct);
        var list = rows.Select(d => MapPending(d, user));
        return ApiResult.Ok(list);
    }

    public async Task<ApiResult> AllAsync(User user, TxFilter f, CancellationToken ct = default)
    {
        var scope = await ScopeAsync(user, ct);
        var (rows, total, totalAmount) = await _store.GetDepositsAllAsync(scope, f, ct);
        var list = rows.Select(d => MapAll(d, user));
        return ApiResult.Ok(new { deposits = list, total, total_amount = totalAmount, page = f.Page, per_page = f.PerPage });
    }

    public async Task<ApiResult> DetailAsync(User user, int id, CancellationToken ct = default)
    {
        var d = await _store.GetDepositDetailAsync(id, ct);
        if (d is null) return ApiResult.Msg(404, "İşlem bulunamadı.");
        if (user.IsTeamMember && d.TeamId != user.TeamId) return ApiResult.Msg(403, "Yetki yok.");
        if (user.IsMerchant)
        {
            var allowed = await _store.GetMerchantIdsForUserAsync(user.MerchantGroupId.HasValue ? (int)user.MerchantGroupId : null, user.FirmId, ct);
            if (!allowed.Contains(d.FirmId)) return ApiResult.Msg(403, "Yetki yok.");
        }

        var scope = await ScopeAsync(user, ct);
        var history = await _store.GetPlayerHistoryAsync(d.PlayerId ?? "", d.Id, 1, scope, ct);

        return ApiResult.Ok(new
        {
            deposit = new
            {
                id = d.Id,
                status = int.Parse(d.Status),
                name = d.Name,
                amount = d.Amount,
                original_amount = (double?)d.OriginalAmount,
                amount_changed = d.AmountChanged == 1,
                order_id = d.OrderId,
                player_id = d.PlayerId,
                merchant_name = d.MerchantName,
                team_name = user.HasMerchantScope ? null : d.TeamName,
                account_holder = d.AccountHolder,
                account_iban = d.AccountIban,
                bank_name = d.BankName,
                bank_logo = d.BankLogo,
                agent_name = user.HasMerchantScope ? null : d.AgentName,
                created_at = d.CreatedAt,
                form_at = d.FormAt,
                process_date = d.ProcessDate,
                iban = d.Iban,
                trust_rate = d.TrustRate,
                trust_count = d.TrustCount,
                receipt_path = d.ReceiptPath,
                receipt_url = string.IsNullOrEmpty(d.ReceiptPath) ? null : $"/api/deposits/{d.Id}/receipt",
            },
            history,
        });
    }

    public async Task<ApiResult> ApproveAsync(User user, int id, double? amount, string ip, CancellationToken ct = default)
    {
        if (!user.CanApproveTransactions) return ApiResult.Msg(403, "Bu işlem için yetkiniz yok.");

        var invest = await _store.GetInvestRawAsync(id, ct);
        if (invest is null) return ApiResult.Msg(404, "İşlem bulunamadı.");
        if (int.Parse(invest.Status) is not (1 or 2)) return ApiResult.Msg(422, "Bu işlem zaten sonuçlandırılmış.");
        if (!user.IsAdmin && invest.TeamId != user.TeamId) return ApiResult.Msg(403, "Bu işlemi onaylama yetkiniz yok.");

        var fields = new Dictionary<string, object?>
        {
            ["status"] = 3,
            ["agent_id"] = user.Id,
            ["finalize_date"] = _clock.Now,
        };
        if (int.Parse(invest.Status) == 1) fields["process_date"] = _clock.Now;
        if (amount is not null && amount.Value != invest.Amount)
        {
            fields["amount"] = amount.Value;
            fields["amountChanged"] = 1;
        }

        await _store.UpdateInvestAsync(id, fields, ct);
        await _store.InsertInvestLogAsync(id, user.Id, ip, 3, "İşlem onaylandı", ct);

        var updated = await _store.GetInvestAsync(id, ct);
        if (updated is not null) await _callbacks.SendForInvestAsync(updated, true, triggeredBy: user.Id, ct: ct);

        if (invest.TeamId is not null) await _banks.EnforceMaxCaseAsync(new[] { invest.TeamId.Value }, ct);

        var finalAmount = (amount is not null && amount.Value != invest.Amount) ? amount.Value : invest.Amount;
        _audit.Set($"Yatırım onaylandı — #{id} (₺{finalAmount:N2})", "invest", id.ToString(),
            new { status = invest.Status, amount = invest.Amount },
            new { status = "3", amount = finalAmount });

        return ApiResult.Msg(200, "İşlem onaylandı.");
    }

    public async Task<ApiResult> ForceRejectAsync(User user, int id, string reason, string ip, CancellationToken ct = default)
    {
        if (!user.IsSuperAdmin) return ApiResult.Msg(403, "Bu işlem yalnızca Süper Admin yetkisindedir.");
        if (string.IsNullOrWhiteSpace(reason)) return ApiResult.Msg(422, "Ret sebebi zorunludur.");

        var invest = await _store.GetInvestRawAsync(id, ct);
        if (invest is null) return ApiResult.Msg(404, "İşlem bulunamadı.");
        if (invest.Type != "1") return ApiResult.Msg(422, "Bu bir yatırım işlemi değil.");
        if (invest.Status != "3") return ApiResult.Msg(422, "Yalnızca onaylı işlemler bu şekilde reddedilebilir.");

        await _store.UpdateInvestAsync(id, new Dictionary<string, object?>
        {
            ["status"] = 4, ["agent_id"] = user.Id, ["finalize_date"] = _clock.Now,
        }, ct);
        await _store.InsertInvestLogAsync(id, user.Id, ip, 4, $"Onaylı yatırım reddedildi (Süper Admin) — Sebep: {reason}", ct);
        // CALLBACK GÖNDERİLMEZ (kasıtlı)
        if (invest.TeamId is not null) await _banks.EnforceMaxCaseAsync(new[] { invest.TeamId.Value }, ct);

        _audit.Set($"Onaylı yatırım reddedildi — #{id} (₺{invest.Amount:N2}) — Sebep: {reason}", "invest", id.ToString(),
            new { status = "3" }, new { status = "4", reason, callback = "gönderilmedi" });

        return ApiResult.Msg(200, "İşlem reddedildi (callback gönderilmedi).");
    }

    public async Task<ApiResult> RejectAsync(User user, int id, int rejectType, string ip, CancellationToken ct = default)
    {
        if (rejectType is not (1 or 2)) return ApiResult.Msg(422, "Geçersiz red tipi.");
        if (!user.CanApproveTransactions) return ApiResult.Msg(403, "Bu işlem için yetkiniz yok.");

        var invest = await _store.GetInvestRawAsync(id, ct);
        if (invest is null) return ApiResult.Msg(404, "İşlem bulunamadı.");
        if (int.Parse(invest.Status) is not (1 or 2)) return ApiResult.Msg(422, "Bu işlem zaten sonuçlandırılmış.");
        if (!user.IsAdmin && invest.TeamId != user.TeamId) return ApiResult.Msg(403, "Bu işlemi reddetme yetkiniz yok.");

        // 10 dk kuralı (admin hariç)
        if (!user.IsAdmin && invest.FormAt is not null && invest.FormAt.Value >= _clock.Now.AddMinutes(-10))
            return ApiResult.Msg(422, "Erken ret! İşlem en az 10 dakika beklemelidir.");

        var rejectMessages = new Dictionary<int, string> { [1] = "Ödeme bulunamadı", [2] = "Tekrarlanan talep" };

        await _store.UpdateInvestAsync(id, new Dictionary<string, object?>
        {
            ["status"] = 4,
            ["rejectType"] = rejectType,
            ["agent_id"] = user.Id,
            ["finalize_date"] = _clock.Now,
        }, ct);
        await _store.InsertInvestLogAsync(id, user.Id, ip, 4, rejectMessages[rejectType], ct);

        var updated = await _store.GetInvestAsync(id, ct);
        if (updated is not null) await _callbacks.SendForInvestAsync(updated, false, rejectMessages[rejectType], user.Id, ct: ct);

        _audit.Set($"Yatırım reddedildi — #{id} ({rejectMessages[rejectType]})", "invest", id.ToString(),
            new { status = invest.Status },
            new { status = "4", rejectType });

        return ApiResult.Msg(200, "İşlem reddedildi.");
    }

    public async Task<ApiResult> FilterMetaAsync(User user, CancellationToken ct = default)
    {
        var merchants = user.IsTeamMember ? Array.Empty<OptionRow>() : (await _store.GetActiveMerchantsAsync(ct)).ToArray();
        var teams = user.IsAdmin ? (await _store.GetTeamsForFilterAsync(ct)).ToArray() : Array.Empty<OptionRow>();
        var banks = await _store.GetBanksAsync(ct);
        return ApiResult.Ok(new
        {
            merchants = merchants.Select(m => new { id = m.Id, name = m.Name }),
            teams = teams.Select(t => new { id = t.Id, name = t.Name, status = t.Status }),
            banks = banks.Select(b => new { id = b.Id, name = b.Name }),
        });
    }

    public async Task<ApiResult> ResendCallbackAsync(User user, int id, CancellationToken ct = default)
    {
        if (!user.IsSuperAdmin) return ApiResult.Msg(403, "Bu işlem için yetkiniz yok.");
        var invest = await _store.GetInvestAsync(id, ct);
        if (invest is null) return ApiResult.Msg(404, "İşlem bulunamadı.");
        if (int.Parse(invest.Status) is not (3 or 4)) return ApiResult.Msg(422, "Yalnızca sonuçlanmış işlemler için tekrar gönderilebilir.");

        var approved = invest.Status == "3";
        var result = await _callbacks.SendForInvestAsync(invest, approved, approved ? "" : "Manuel yeniden gönderim",
            user.Id, force: true, type: "manual_resend", ct: ct);

        return new ApiResult(result.Success ? 200 : 502, new
        {
            message = result.Success ? "Callback yeniden gönderildi." : "Callback gönderilemedi.",
            result,
        });
    }

    private static object MapPending(DepositListRow d, User user) => new
    {
        id = d.Id,
        status = int.Parse(d.Status),
        name = d.Name,
        amount = d.Amount,
        original_amount = (double?)d.OriginalAmount,
        amount_changed = d.AmountChanged == 1,
        order_id = d.OrderId,
        player_id = d.PlayerId,
        merchant_name = user.IsTeamMember ? null : d.MerchantName,
        team_name = user.HasMerchantScope ? null : d.TeamName,
        account_holder = d.AccountHolder,
        account_iban = d.AccountIban,
        bank_name = d.BankName,
        agent_name = user.HasMerchantScope ? null : d.AgentName,
        agent_id = user.HasMerchantScope ? (int?)null : d.AgentId,
        created_at = d.CreatedAt,
        form_at = d.FormAt,
        process_date = d.ProcessDate,
        trust_rate = d.TrustRate,
        trust_count = d.TrustCount,
        has_receipt = !string.IsNullOrEmpty(d.ReceiptPath),
    };

    private static object MapAll(DepositListRow d, User user) => new
    {
        id = d.Id,
        status = int.Parse(d.Status),
        name = d.Name,
        amount = d.Amount,
        original_amount = (double?)d.OriginalAmount,
        amount_changed = d.AmountChanged == 1,
        order_id = d.OrderId,
        player_id = d.PlayerId,
        u_id = d.UId,
        merchant_name = user.IsTeamMember ? null : d.MerchantName,
        team_name = user.HasMerchantScope ? null : d.TeamName,
        account_holder = d.AccountHolder,
        account_iban = d.AccountIban,
        bank_name = d.BankName,
        agent_name = user.HasMerchantScope ? null : d.AgentName,
        reject_type = d.RejectType,
        has_receipt = !string.IsNullOrEmpty(d.ReceiptPath),
        created_at = d.CreatedAt,
        finalize_date = d.FinalizeDate,
        trust_rate = d.TrustRate,
        trust_count = d.TrustCount,
    };
}
