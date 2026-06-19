namespace PayDoPay.Application.Features.Cases;

/// <summary>TRON zincir sorguları (TronGrid / TronScan).</summary>
public interface ITronService
{
    Task<double?> GetUsdtBalanceAsync(string walletAddress, CancellationToken ct = default);
    Task<(double? Quantity, string? Type, string? Error)> TxLookupAsync(string txLink, CancellationToken ct = default);
}
