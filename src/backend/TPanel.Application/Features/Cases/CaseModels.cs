using JP = System.Text.Json.Serialization.JsonPropertyNameAttribute;

namespace TPanel.Application.Features.Cases;

/// <summary>daily_case_snapshots ham satırı.</summary>
public class SnapshotRow
{
    public string SnapshotDate { get; set; } = "";
    public decimal Amount { get; set; }
    public string? Details { get; set; }
}

/// <summary>Genel para hareketi (fund storage / team payments listesi).</summary>
public class MovementItem
{
    // Global JSON politikası PascalCase'i koruyor (PropertyNamingPolicy=null);
    // frontend snake_case okuduğu için açıkça eşle (yoksa p.amount=undefined → NaN).
    [JP("id")] public string Id { get; set; } = "";
    [JP("source")] public string? Source { get; set; }
    [JP("direction")] public string? Direction { get; set; }
    [JP("target")] public string? Target { get; set; }
    [JP("amount")] public double Amount { get; set; }
    [JP("description")] public string? Description { get; set; }
    [JP("created_by")] public string? CreatedBy { get; set; }
    [JP("created_at")] public DateTime? CreatedAt { get; set; }
    [JP("balance_before")] public double? BalanceBefore { get; set; }
    [JP("balance_after")] public double? BalanceAfter { get; set; }
}

public class FundStorageRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int Type { get; set; }
    public string? WalletAddress { get; set; }
    public decimal Balance { get; set; }
    public int Status { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public double? ChainBalance { get; set; }
}

// ---- İstek gövdeleri (frontend snake_case) ----
public record CasePaymentBody(
    [property: JP("payment_type")] int PaymentType,
    [property: JP("amount")] double Amount,
    [property: JP("crypto_quantity")] double? CryptoQuantity,
    [property: JP("crypto_rate")] double? CryptoRate,
    [property: JP("tx_link")] string? TxLink,
    [property: JP("fund_storage_id")] int? FundStorageId,
    [property: JP("description")] string? Description,
    [property: JP("payment_date")] string? PaymentDate);

public record TeamTransferBody(
    [property: JP("to_team_id")] int ToTeamId,
    [property: JP("amount")] double Amount,
    [property: JP("description")] string? Description,
    [property: JP("payment_date")] string? PaymentDate);

public record TeamSyncBody(
    [property: JP("amount")] double Amount,
    [property: JP("description")] string? Description,
    [property: JP("payment_date")] string? PaymentDate);

public record MerchantPaymentBody(
    [property: JP("payment_type")] int PaymentType,
    [property: JP("amount")] double Amount,
    [property: JP("crypto_quantity")] double? CryptoQuantity,
    [property: JP("crypto_rate")] double? CryptoRate,
    [property: JP("paid_amount")] double? PaidAmount,
    [property: JP("tx_link")] string? TxLink,
    [property: JP("fund_storage_id")] int? FundStorageId,
    [property: JP("target_merchant_id")] int? TargetMerchantId,
    [property: JP("description")] string? Description,
    [property: JP("payment_date")] string? PaymentDate,
    [property: JP("is_group")] bool? IsGroup);

public record FundStorageBody(
    [property: JP("name")] string? Name,
    [property: JP("type")] int? Type,
    [property: JP("balance")] double? Balance,
    [property: JP("status")] int? Status,
    [property: JP("wallet_address")] string? WalletAddress);

public record FundTransferBody(
    [property: JP("from_storage_id")] int FromStorageId,
    [property: JP("to_storage_id")] int ToStorageId,
    [property: JP("amount")] double Amount,
    [property: JP("commission_rate")] double? CommissionRate,
    [property: JP("description")] string? Description,
    [property: JP("transfer_date")] string? TransferDate);

public record FundSyncBody(
    [property: JP("fund_storage_id")] int FundStorageId,
    [property: JP("amount")] double Amount,
    [property: JP("description")] string? Description,
    [property: JP("sync_date")] string? SyncDate);

// ---- Faz 4b ----
public record IntermediaryPaymentBody(
    [property: JP("payment_type")] int PaymentType,
    [property: JP("amount")] double Amount,
    [property: JP("crypto_quantity")] double? CryptoQuantity,
    [property: JP("crypto_rate")] double? CryptoRate,
    [property: JP("tx_link")] string? TxLink,
    [property: JP("fund_storage_id")] int? FundStorageId,
    [property: JP("team_id")] int? TeamId,
    [property: JP("description")] string? Description,
    [property: JP("payment_date")] string? PaymentDate);

public record PartnerPaymentBody(
    [property: JP("payment_type")] int PaymentType,
    [property: JP("amount")] double Amount,
    [property: JP("crypto_quantity")] double? CryptoQuantity,
    [property: JP("crypto_rate")] double? CryptoRate,
    [property: JP("tx_link")] string? TxLink,
    [property: JP("fund_storage_id")] int? FundStorageId,
    [property: JP("team_id")] int? TeamId,
    [property: JP("description")] string? Description,
    [property: JP("payment_date")] string? PaymentDate,
    [property: JP("is_capital")] bool? IsCapital);

public record CapitalBody(
    [property: JP("payment_type")] int PaymentType,
    [property: JP("amount")] double Amount,
    [property: JP("crypto_quantity")] double? CryptoQuantity,
    [property: JP("crypto_rate")] double? CryptoRate,
    [property: JP("tx_link")] string? TxLink,
    [property: JP("fund_storage_id")] int FundStorageId,
    [property: JP("description")] string? Description,
    [property: JP("payment_date")] string? PaymentDate);

public record ExpenseShareInput(
    [property: JP("partner_id")] int PartnerId,
    [property: JP("amount")] double Amount);

public record ExpenseBody(
    [property: JP("amount")] double Amount,
    [property: JP("description")] string? Description,
    [property: JP("team_id")] int? TeamId,
    [property: JP("fund_storage_id")] int? FundStorageId,
    [property: JP("shares")] List<ExpenseShareInput>? Shares,
    [property: JP("payment_date")] string? PaymentDate);

public record PartnerTransferBody(
    [property: JP("to_partner_id")] int ToPartnerId,
    [property: JP("amount")] double Amount,
    [property: JP("description")] string? Description,
    [property: JP("payment_date")] string? PaymentDate);

public record InitialEntityInput(
    [property: JP("type")] string Type,
    [property: JP("id")] int? Id,
    [property: JP("name")] string Name,
    [property: JP("amount")] double Amount);

public record InitialBalanceSaveBody(
    [property: JP("date")] string? Date,
    [property: JP("entities")] List<InitialEntityInput>? Entities);

public record InitialBalanceResetBody([property: JP("date")] string? Date);
