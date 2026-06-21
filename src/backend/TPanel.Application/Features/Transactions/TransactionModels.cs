using TPanel.Application.Common;

namespace TPanel.Application.Features.Transactions;

// ---- Filtre parametreleri ----
public class TxFilter
{
    public int? Merchant { get; set; }
    public int? Team { get; set; }
    public int? Bank { get; set; }
    public string? Name { get; set; }
    public string? PlayerId { get; set; }
    public string? OrderId { get; set; }
    public string? UId { get; set; }
    public int? Id { get; set; }
    public int? Status { get; set; }
    public double? MinAmount { get; set; }
    public double? MaxAmount { get; set; }
    public string? DateFrom { get; set; }
    public string? DateTo { get; set; }
    public bool ConvertedOnly { get; set; }
    public bool MissingReceipt { get; set; }
    public int? AddedType { get; set; }   // 1=Otomatik(API), 2=Manuel, null/0=Hepsi
    public int Page { get; set; } = 1;
    public int PerPage { get; set; } = 50;
}

// ---- Liste satırları (join sonrası ham) ----
public class DepositListRow
{
    public int Id { get; set; }
    public string Status { get; set; } = "";
    public string? Name { get; set; }
    public double Amount { get; set; }
    public decimal? OriginalAmount { get; set; }
    public int AmountChanged { get; set; }
    public string? OrderId { get; set; }
    public string? PlayerId { get; set; }
    public string? UId { get; set; }
    public string? ReceiptPath { get; set; }
    public int? AgentId { get; set; }
    public int FirmId { get; set; }
    public int? TeamId { get; set; }
    public int? BankId { get; set; }
    public string? Iban { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? FormAt { get; set; }
    public DateTime? ProcessDate { get; set; }
    public DateTime? FinalizeDate { get; set; }
    public int? RejectType { get; set; }
    public string? MerchantName { get; set; }
    public string? TeamName { get; set; }
    public string? AccountHolder { get; set; }
    public string? AccountIban { get; set; }
    public string? BankName { get; set; }
    public string? BankLogo { get; set; }
    public string? AgentName { get; set; }
    // hesaplanan
    public int? TrustRate { get; set; }
    public int TrustCount { get; set; }
}

public class WithdrawListRow
{
    public int Id { get; set; }
    public string Status { get; set; } = "";
    public string? Name { get; set; }
    public double Amount { get; set; }
    public string? OrderId { get; set; }
    public string? PlayerId { get; set; }
    public string? UId { get; set; }
    public string? Iban { get; set; }
    public int? AgentId { get; set; }
    public int FirmId { get; set; }
    public int? TeamId { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? FormAt { get; set; }
    public DateTime? ProcessDate { get; set; }
    public DateTime? FinalizeDate { get; set; }
    public int? RejectType { get; set; }
    public string? MerchantName { get; set; }
    public string? TeamName { get; set; }
    public string? BankName { get; set; }
    public string? AgentName { get; set; }
    public long ReceiptCount { get; set; }
    public DateTime? TelegramMissingReceiptEnabledAt { get; set; }
}

public class PlayerHistoryRow
{
    public int Id { get; set; }
    public string Type { get; set; } = "";
    public string Status { get; set; } = "";
    public double Amount { get; set; }
    public string? Name { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? FinalizeDate { get; set; }
    public int? RejectType { get; set; }
}

public class ReceiptRow
{
    public int Id { get; set; }
    public string FilePath { get; set; } = "";
    public string? OriginalName { get; set; }
    public string? MimeType { get; set; }
    public long FileSize { get; set; }
    public DateTime? UploadedAt { get; set; }
    public string? VerificationStatus { get; set; }
    public int? VerificationScore { get; set; }
    public string? VerificationData { get; set; }
    public string? VerificationNotes { get; set; }
    public string? MetadataFlags { get; set; }
    public DateTime? VerifiedAt { get; set; }
    public int? ManualVerifiedBy { get; set; }
    public string? UploadedByName { get; set; }
    public string? ManualVerifierName { get; set; }
    public string? PerceptualHash { get; set; }
    public string? FileHash { get; set; }
}

public class OptionRow
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int? Status { get; set; }
}
