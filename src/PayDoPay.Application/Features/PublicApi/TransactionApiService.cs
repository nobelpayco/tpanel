using PayDoPay.Application.Common.Interfaces;

namespace PayDoPay.Application.Features.PublicApi;

public interface ITransactionApiService
{
    Task<V1Result> ShowAsync(MerchantContext merchant, string orderId, CancellationToken ct = default);
}

public class TransactionApiService : ITransactionApiService
{
    private static readonly Dictionary<string, string> StatusLabel = new()
    {
        ["0"] = "waiting", ["1"] = "waiting", ["2"] = "waiting", ["3"] = "approved", ["4"] = "rejected",
    };

    private readonly IMerchantApiStore _store;

    public TransactionApiService(IMerchantApiStore store) => _store = store;

    public async Task<V1Result> ShowAsync(MerchantContext merchant, string orderId, CancellationToken ct = default)
    {
        var tx = await _store.GetByOrderOrUidForMerchantAsync(merchant.Id, orderId, ct);
        if (tx is null)
            return new V1Result(404, new { code = 404, status = false, message = "Transaction not found." });

        var statusCode = int.Parse(tx.Status);
        var statusLabel = StatusLabel.GetValueOrDefault(statusCode.ToString(), "unknown");

        int code; bool? status; string message;
        if (statusLabel == "approved") { code = 200; status = true; message = "Transaction approved"; }
        else if (statusLabel == "rejected") { code = 201; status = false; message = "Transaction rejected"; }
        else { code = 202; status = null; message = "Transaction is still being processed"; }

        var body = new
        {
            code,
            status,
            message,
            data = new
            {
                order_id = tx.OrderId,
                u_id = tx.UId,
                status = statusLabel,
                status_code = statusCode,
                type = tx.Type == "1" ? "deposit" : "withdraw",
                amount = tx.Amount,
                name = tx.Name,
                player_id = tx.PlayerId,
                created_at = tx.CreatedAt,
                finalized_at = tx.FinalizeDate,
            },
        };

        // PHP: code===202 ise HTTP 200, aksi halde code
        return new V1Result(code == 202 ? 200 : code, body);
    }
}
