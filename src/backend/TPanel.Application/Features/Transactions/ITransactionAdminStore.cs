using TPanel.Application.Common;
using TPanel.Application.Features.PublicApi;

namespace TPanel.Application.Features.Transactions;

/// <summary>Admin Deposit/Withdraw ekranları için veri erişimi (Dapper).</summary>
public interface ITransactionAdminStore
{
    // Merchant scope çözümü (merchant_group_id → tüm grup, yoksa firm_id)
    Task<IReadOnlyList<int>> GetMerchantIdsForUserAsync(int? merchantGroupId, int? firmId, CancellationToken ct = default);

    // ---- Deposit ----
    Task<IReadOnlyList<DepositListRow>> GetDepositsPendingAsync(QueryScope scope, TxFilter f, CancellationToken ct = default);
    Task<(IReadOnlyList<DepositListRow> Rows, long Total, double TotalAmount)> GetDepositsAllAsync(QueryScope scope, TxFilter f, CancellationToken ct = default);
    Task<DepositListRow?> GetDepositDetailAsync(int id, CancellationToken ct = default);

    // ---- Withdraw ----
    Task<IReadOnlyList<WithdrawListRow>> GetWithdrawalsPendingAsync(QueryScope scope, TxFilter f, CancellationToken ct = default);
    Task<(IReadOnlyList<WithdrawListRow> Rows, long Total, double TotalAmount)> GetWithdrawalsAllAsync(QueryScope scope, TxFilter f, CancellationToken ct = default);
    Task<WithdrawListRow?> GetWithdrawDetailAsync(int id, CancellationToken ct = default);

    // ---- Ortak ----
    Task<InvestRow?> GetInvestAsync(int id, CancellationToken ct = default);
    Task<InvestRaw?> GetInvestRawAsync(int id, CancellationToken ct = default);
    Task UpdateInvestAsync(int id, IDictionary<string, object?> fields, CancellationToken ct = default);
    Task InsertInvestLogAsync(int investId, int userId, string ip, int status, string detail, CancellationToken ct = default);
    Task<IReadOnlyList<PlayerHistoryRow>> GetPlayerHistoryAsync(string playerId, int excludeId, int type, QueryScope scope, CancellationToken ct = default);

    // Filter meta
    Task<IReadOnlyList<OptionRow>> GetActiveMerchantsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<OptionRow>> GetTeamsForFilterAsync(CancellationToken ct = default);
    Task<IReadOnlyList<OptionRow>> GetBanksAsync(CancellationToken ct = default);

    // ---- Manuel yatırım (added_type=2, status=3 onaylı, callback gönderilmez) ----
    Task<IReadOnlyList<OptionRow>> GetTeamBankAccountsAsync(int teamId, CancellationToken ct = default);
    Task<IReadOnlyList<OptionRow>> GetTeamAgentsAsync(int teamId, CancellationToken ct = default);
    Task<int> CreateManualDepositAsync(int merchantId, int teamId, int? bankId, int? agentId, string name, double amount, int userId, string ip, CancellationToken ct = default);
    Task<int> CreateManualWithdrawAsync(int merchantId, int teamId, int? bankId, int? agentId, string name, double amount, string iban, int userId, string ip, CancellationToken ct = default);

    // ---- Withdraw receipts ----
    Task<bool> HasReceiptAsync(int investId, CancellationToken ct = default);
    Task<IReadOnlyList<ReceiptRow>> GetReceiptsAsync(int investId, CancellationToken ct = default);
    Task<ReceiptRow?> GetReceiptAsync(int investId, int receiptId, CancellationToken ct = default);
    Task<int> InsertReceiptAsync(ReceiptInsert data, CancellationToken ct = default);
    Task UpdateReceiptAsync(int receiptId, IDictionary<string, object?> fields, CancellationToken ct = default);
    Task<int> InsertFakeTemplateAsync(int receiptId, int investId, string perceptualHash, string? fileHash, string? reason, int reportedBy, CancellationToken ct = default);

    // ---- Bulk assign / teams ----
    Task<TeamRow?> GetTeamAsync(int teamId, bool requireActive, CancellationToken ct = default);
    Task<UserRow?> GetFirstActiveTeamUserAsync(int teamId, CancellationToken ct = default);
    Task<IReadOnlyList<int>> FilterEligibleForAssignAsync(IReadOnlyList<int> ids, CancellationToken ct = default);
    Task BulkAssignAsync(IReadOnlyList<int> ids, int teamId, int agentId, CancellationToken ct = default);
    /// <summary>Bekleyen yatırımı (type=1, status=1) başka takıma + o takımın IBAN'ına taşır.</summary>
    Task<(bool ok, string message)> MoveDepositTeamAsync(int id, int teamId, int bankId, int actorUserId, CancellationToken ct = default);

    // notify-missing-receipts
    Task<IReadOnlyList<MissingReceiptRow>> GetMissingReceiptsAsync(int teamId, DateTime enabledAt, CancellationToken ct = default);
    Task InsertTelegramNotificationsIgnoreAsync(IReadOnlyList<int> investIds, string type, DateTime sentAt, CancellationToken ct = default);

    // receipt-review
    Task<(IReadOnlyList<ReceiptReviewRow> Rows, long Total, IReadOnlyDictionary<string, long> Counts)>
        GetReceiptReviewAsync(string? statusFilter, int page, int perPage, CancellationToken ct = default);
}

public record ReceiptReviewRow(
    int InvestId, string? OrderId, double Amount, string? Recipient, string? Iban, int InvestStatus,
    DateTime? FinalizeDate, string? TeamName, string? MerchantName, string? AgentName,
    int ReceiptId, string? VerificationStatus, int? VerificationScore, string? VerificationData,
    string? VerificationNotes, DateTime? VerifiedAt, string? ManualVerifierName);

public record InvestRaw(
    int Id, string Type, string Status, double Amount, int FirmId, int? TeamId, int? AgentId,
    DateTime? FormAt, string? OrderId, string? UId, string? Name, int CallbackSended, string? CallbackUrl);

public record ReceiptInsert(int InvestId, string FilePath, string? OriginalName, string? MimeType,
    long FileSize, string FileHash, string PerceptualHash, int UploadedBy);

public record TeamRow(int Id, string Name, int Status, bool TelegramEnabled, string? TelegramWithdrawChatId,
    bool TelegramWithdrawAssignedEnabled, DateTime? TelegramMissingReceiptEnabledAt);

public record UserRow(int Id, string Name);

public record MissingReceiptRow(int Id, string? OrderId, DateTime FinalizeDate);
