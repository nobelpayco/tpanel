using System.Text.RegularExpressions;
using TPanel.Application.Common.Interfaces;

namespace TPanel.Application.Features.PublicApi;

public interface IWithdrawApiService
{
    Task<V1Result> StoreAsync(MerchantContext merchant, WithdrawApiRequest req, CancellationToken ct = default);
}

public class WithdrawApiService : IWithdrawApiService
{
    private static readonly Regex IbanRegex = new(@"^TR\d{24}$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private readonly IMerchantApiStore _store;

    public WithdrawApiService(IMerchantApiStore store) => _store = store;

    public async Task<V1Result> StoreAsync(MerchantContext merchant, WithdrawApiRequest req, CancellationToken ct = default)
    {
        if (!PublicApiHelpers.IsRequired(req.order_id, 100) || req.amount is null or < 1
            || !PublicApiHelpers.IsRequired(req.player_id, 100) || !PublicApiHelpers.IsRequired(req.name, 255)
            || string.IsNullOrWhiteSpace(req.iban) || !IbanRegex.IsMatch(req.iban)
            || !PublicApiHelpers.IsValidUrl(req.callback_url))
            return V1Result.Error(422, "Invalid request parameters.");

        var amount = (double)req.amount.Value;

        if (await _store.IsBlacklistedAsync(1, req.player_id!, ct) || await _store.IsBlacklistedAsync(2, req.name!.Trim(), ct))
            return V1Result.Error(403, "Transaction not allowed (blacklist).");

        if (await _store.OrderIdExistsAsync(req.order_id!, ct))
            return V1Result.Error(409, "order_id already exists.");

        var commission = merchant.WithdrawCommission;
        var commissionAmount = Math.Round(amount * commission / 100, 2);
        var uId = PublicApiHelpers.GenerateUId();

        var data = new InvestInsert(req.name!, amount, uId, req.callback_url!, null, null,
            commissionAmount, (int)commission, merchant.Id, req.player_id!, req.order_id!);

        var investId = await _store.InsertWithdrawAsync(data, req.iban!.ToUpperInvariant(), ct);

        return new V1Result(200, new
        {
            code = 200,
            status = true,
            message = "Withdrawal request created.",
            data = new { transaction_id = investId, order_id = req.order_id, u_id = uId, amount, status = "pending" },
        });
    }
}
