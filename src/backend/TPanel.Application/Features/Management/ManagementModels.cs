using JP = System.Text.Json.Serialization.JsonPropertyNameAttribute;

namespace TPanel.Application.Features.Management;

public record MgmtResult(int Status, object Body)
{
    public static MgmtResult Ok(object body) => new(200, body);
    public static MgmtResult Msg(int status, string message) => new(status, new { message });
}

// ---- Team ----
public record TeamUpsertBody(
    [property: JP("name")] string? Name,
    [property: JP("status")] int? Status,
    [property: JP("min_invest")] double? MinInvest,
    [property: JP("max_invest")] double? MaxInvest,
    [property: JP("wait_limit")] int? WaitLimit,
    [property: JP("commission")] double? Commission,
    [property: JP("maxCase")] double? MaxCase,
    [property: JP("allow_duplicate_iban")] int? AllowDuplicateIban,
    [property: JP("block_when_full")] int? BlockWhenFull,
    [property: JP("overturn")] double? Overturn,
    [property: JP("withdraw")] double? Withdraw,
    [property: JP("telegram_enabled")] int? TelegramEnabled,
    [property: JP("telegram_chat_id")] string? TelegramChatId,
    [property: JP("telegram_withdraw_chat_id")] string? TelegramWithdrawChatId,
    [property: JP("telegram_reconciliation_chat_id")] string? TelegramReconciliationChatId,
    [property: JP("telegram_credit_low_enabled")] int? TelegramCreditLowEnabled,
    [property: JP("telegram_credit_low_threshold")] double? TelegramCreditLowThreshold,
    [property: JP("telegram_pending_invest_enabled")] int? TelegramPendingInvestEnabled,
    [property: JP("telegram_missing_receipt_enabled")] int? TelegramMissingReceiptEnabled,
    [property: JP("telegram_withdraw_assigned_enabled")] int? TelegramWithdrawAssignedEnabled,
    [property: JP("telegram_cash_report_enabled")] int? TelegramCashReportEnabled,
    [property: JP("merchant_ids")] List<int>? MerchantIds);

// ---- Merchant ----
public record MerchantUpsertBody(
    [property: JP("name")] string? Name,
    [property: JP("email")] string? Email,
    [property: JP("commission")] double? Commission,
    [property: JP("withdrawCommission")] double? WithdrawCommission,
    [property: JP("deliveryCommission")] double? DeliveryCommission,
    [property: JP("depositLimit")] double? DepositLimit,
    [property: JP("minDeposit")] double? MinDeposit,
    [property: JP("maxDeposit")] double? MaxDeposit,
    [property: JP("group_id")] int? GroupId,
    [property: JP("approved_ip")] string? ApprovedIp,
    [property: JP("status")] string? Status,
    [property: JP("new_api")] int? NewApi);

public record GroupBody([property: JP("name")] string? Name, [property: JP("status")] int? Status);
public record AssignGroupBody([property: JP("merchant_id")] int MerchantId, [property: JP("group_id")] int? GroupId);

// ---- BankAccount ----
public record BankAccountUpsertBody(
    [property: JP("status")] int? Status,
    [property: JP("account_code")] string? AccountCode,
    [property: JP("account_holder")] string? AccountHolder,
    [property: JP("account_iban")] string? AccountIban,
    [property: JP("bank_id")] int? BankId,
    [property: JP("min_invest")] double? MinInvest,
    [property: JP("max_invest")] double? MaxInvest,
    [property: JP("max_per_invest")] double? MaxPerInvest,
    [property: JP("max_amount")] double? MaxAmount,
    [property: JP("team_id")] int? TeamId,
    [property: JP("walletID")] int? WalletId,
    [property: JP("daily_count_limit")] int? DailyCountLimit);

public record ReorderBody([property: JP("ids")] List<int>? Ids);
public record SetSortBody([property: JP("position")] int Position);
public record IdentifyIbanBody([property: JP("iban")] string? Iban);

// ---- User ----
public record UserCreateBody(
    [property: JP("name")] string? Name,
    [property: JP("username")] string? Username,
    [property: JP("password")] string? Password,
    [property: JP("user_type")] int? UserType,
    [property: JP("team_id")] int? TeamId,
    [property: JP("firm_id")] int? FirmId,
    [property: JP("status")] int? Status,
    [property: JP("two_factor")] bool? TwoFactor);

public record UserUpdateBody(
    [property: JP("name")] string? Name,
    [property: JP("username")] string? Username,
    [property: JP("password")] string? Password,
    [property: JP("status")] int? Status,
    [property: JP("user_type")] int? UserType,
    [property: JP("team_id")] int? TeamId,
    [property: JP("firm_id")] int? FirmId,
    [property: JP("two_factor")] bool? TwoFactor);

// ---- Blacklist ----
public record BlacklistStoreBody([property: JP("type")] int Type, [property: JP("val")] string? Val, [property: JP("desc")] string? Desc);
public record BlacklistUpdateBody([property: JP("desc")] string? Desc);
public record BlacklistCheckBody([property: JP("val")] string? Val);

// ---- Intermediary management ----
public record IntermediaryStoreBody([property: JP("name")] string? Name, [property: JP("type")] int Type, [property: JP("commission_rate")] double? CommissionRate);
public record IntermediaryUpdateBody([property: JP("name")] string? Name, [property: JP("type")] int? Type, [property: JP("status")] int? Status, [property: JP("commission_rate")] double? CommissionRate);
public record AttachMerchantBody([property: JP("intermediary_id")] int IntermediaryId, [property: JP("merchant_id")] int MerchantId, [property: JP("commission_rate")] double CommissionRate);
public record AttachTeamBody([property: JP("intermediary_id")] int IntermediaryId, [property: JP("team_id")] int TeamId, [property: JP("commission_rate")] double CommissionRate);
public record UpdateRateBody([property: JP("commission_rate")] double CommissionRate, [property: JP("status")] int? Status);

// ---- Settings ----
public record SettingsUpdateBody([property: JP("settings")] Dictionary<string, System.Text.Json.JsonElement>? Settings);
public record FindChatIdBody([property: JP("group_name")] string? GroupName);
