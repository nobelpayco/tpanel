using TPanel.Application.Common.Interfaces;

namespace TPanel.Application.Features.PublicApi;

public interface IDepositApiService
{
    Task<V1Result> StoreAsync(MerchantContext merchant, DepositApiRequest req, CancellationToken ct = default);
    Task<V1Result> StoreDirectAsync(MerchantContext merchant, DirectDepositApiRequest req, CancellationToken ct = default);
}

public class DepositApiService : IDepositApiService
{
    private readonly IMerchantApiStore _store;
    private readonly IMerchantBankService _banks;
    private readonly IClock _clock;
    private readonly PublicApiOptions _options;

    public DepositApiService(IMerchantApiStore store, IMerchantBankService banks, IClock clock, PublicApiOptions options)
    {
        _store = store;
        _banks = banks;
        _clock = clock;
        _options = options;
    }

    public async Task<V1Result> StoreAsync(MerchantContext merchant, DepositApiRequest req, CancellationToken ct = default)
    {
        // Validasyon (Laravel validate → 422)
        if (!PublicApiHelpers.IsRequired(req.order_id, 100) || req.amount is null or < 1
            || !PublicApiHelpers.IsRequired(req.player_id, 100) || !PublicApiHelpers.IsRequired(req.name, 255)
            || !PublicApiHelpers.IsValidUrl(req.callback_url) || !PublicApiHelpers.IsValidUrl(req.successRedirectUrl)
            || !PublicApiHelpers.IsValidUrl(req.failRedirectUrl))
            return V1Result.Error(422, "Invalid request parameters.");

        var amount = (double)req.amount.Value;

        var amountError = CheckAmount(merchant, amount);
        if (amountError is not null) return amountError;

        if (await _store.IsBlacklistedAsync(1, req.player_id!, ct))
            return V1Result.Error(403, "Transaction not allowed (blacklist).");

        if (await _store.OrderIdExistsAsync(req.order_id!, ct))
            return V1Result.Error(409, "order_id already exists.");

        if (await _store.HasRecentPendingForPlayerAsync(req.player_id!, _clock.Now.AddMinutes(-10), ct))
            return V1Result.Error(409, "You have a pending order. Please complete it or try again in 10 minutes.");

        var commission = (double)merchant.Commission;
        var commissionAmount = Math.Round(amount * commission / 100, 2);
        var uId = PublicApiHelpers.GenerateUId();

        var data = new InvestInsert(req.name!, amount, uId, req.callback_url!, req.successRedirectUrl,
            req.failRedirectUrl, commissionAmount, (int)commission, merchant.Id, req.player_id!, req.order_id!);

        var investId = await _store.InsertDepositHostedAsync(data, ct);

        // Eligible IBAN yoksa PayRoute'a uyarı
        if (await _banks.PickOneAsync(amount, merchant.Id, ct: ct) is null)
            await _banks.AlertNoIbanAvailableAsync(merchant.Id, amount, investId, req.player_id, req.order_id, req.name, ct);

        return new V1Result(200, new
        {
            code = 200,
            status = true,
            message = "Deposit request created.",
            data = new
            {
                transaction_id = investId,
                order_id = req.order_id,
                u_id = uId,
                amount,
                pay_url = _options.AppUrl.TrimEnd('/') + "/pay/" + uId,
            },
        });
    }

    public async Task<V1Result> StoreDirectAsync(MerchantContext merchant, DirectDepositApiRequest req, CancellationToken ct = default)
    {
        if (!PublicApiHelpers.IsRequired(req.order_id, 100) || req.amount is null or < 1
            || !PublicApiHelpers.IsRequired(req.player_id, 100) || !PublicApiHelpers.IsRequired(req.name, 255)
            || !PublicApiHelpers.IsValidUrl(req.callback_url))
            return V1Result.Error(422, "Invalid request parameters.");

        var amount = (double)req.amount.Value;

        var amountError = CheckAmount(merchant, amount);
        if (amountError is not null) return amountError;

        if (await _store.IsBlacklistedAsync(1, req.player_id!, ct))
            return V1Result.Error(403, "Transaction not allowed (blacklist).");

        if (await _store.OrderIdExistsAsync(req.order_id!, ct))
            return V1Result.Error(409, "order_id already exists.");

        if (await _store.HasRecentPendingForPlayerAsync(req.player_id!, _clock.Now.AddMinutes(-10), ct))
            return V1Result.Error(409, "You have a pending order. Please complete it or try again in 10 minutes.");

        var bank = await _banks.PickForAssignmentAsync(amount, merchant.Id, ct);
        if (bank is null)
        {
            await _banks.AlertNoIbanAvailableAsync(merchant.Id, amount, null, req.player_id, req.order_id, req.name, ct);
            return V1Result.Error(503, "No available bank account for this amount.");
        }

        var commission = (double)merchant.Commission;
        var commissionAmount = Math.Round(amount * commission / 100, 2);
        var uId = PublicApiHelpers.GenerateUId();

        var data = new InvestInsert(req.name!, amount, uId, req.callback_url!, null, null,
            commissionAmount, (int)commission, merchant.Id, req.player_id!, req.order_id!);

        var investId = await _store.InsertDepositDirectAsync(data, bank.Id, bank.TeamId, ct);

        return new V1Result(200, new
        {
            code = 200,
            status = true,
            message = "Deposit request created (H2H).",
            data = new
            {
                transaction_id = investId,
                order_id = req.order_id,
                u_id = uId,
                amount,
                bank = new { id = bank.Id, account_holder = bank.AccountHolder, account_iban = bank.AccountIban, bank_name = bank.BankName },
            },
        });
    }

    private static V1Result? CheckAmount(MerchantContext merchant, double amount)
    {
        if (amount < (double)merchant.MinDeposit)
            return V1Result.Error(422, "Minimum deposit amount: " + merchant.MinDeposit + " TL");
        if (merchant.MaxDeposit != 0 && amount > (double)merchant.MaxDeposit)
            return V1Result.Error(422, "Maximum deposit amount: " + merchant.MaxDeposit + " TL");
        return null;
    }
}
