namespace TPanel.Domain.Entities;

/// <summary>Operasyon takımı — `teams` tablosu. IBAN havuzu + kasa (overturn) sahibi.</summary>
public class Team
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>0=silinmiş, 1=aktif, 2=pasif, 3=bloklu.</summary>
    public int Status { get; set; }

    public int IsFast { get; set; } = 1;
    public double DailyLimit { get; set; }
    public double MinInvest { get; set; } = 100;
    public double MaxInvest { get; set; } = 50000;
    public int AccountPerm { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Note { get; set; }
    public string AllowedCustomers { get; set; } = string.Empty;
    public double Commission { get; set; }
    public int WaitLimit { get; set; } = 5;
    public int StatusReason { get; set; }
    public int Overturn { get; set; }
    public int Withdraw { get; set; }
    public int WithdrawNow { get; set; }
    public int WithdrawNowCount { get; set; }
    public string? EggsId { get; set; }
    public string? EggsIdAll { get; set; }
    public double? WithdrawAll { get; set; }
    public double WinRate { get; set; }
    public double TeamNow { get; set; }
    public int IsWallet { get; set; }
    public DateTime? LastPayOut { get; set; }
    public int? Provider { get; set; }
    public double MaxCase { get; set; } = 500000;
    public bool AllowDuplicateIban { get; set; }
    public bool BlockWhenFull { get; set; } = true;

    // ---- Telegram ----
    public bool TelegramEnabled { get; set; }
    public string? TelegramChatId { get; set; }
    public DateTime? WithdrawAlertAt { get; set; }
    public bool TelegramWithdrawEnabled { get; set; }
    public string? TelegramWithdrawChatId { get; set; }
    public string? TelegramReconciliationChatId { get; set; }
    public DateTime? LimitAlertAt { get; set; }
    public bool LimitCheckEnabled { get; set; }
    public bool TelegramCreditLowEnabled { get; set; }
    public decimal? TelegramCreditLowThreshold { get; set; }
    public bool TelegramCreditLowState { get; set; }
    public bool TelegramPendingInvestEnabled { get; set; }
    public bool TelegramMissingReceiptEnabled { get; set; }
    public DateTime? TelegramCreditLowEnabledAt { get; set; }
    public DateTime? TelegramPendingInvestEnabledAt { get; set; }
    public DateTime? TelegramMissingReceiptEnabledAt { get; set; }
    public bool TelegramCashReportEnabled { get; set; }
    public bool TelegramMaxCaseState { get; set; }
    public bool TelegramWithdrawAssignedEnabled { get; set; }
    public DateTime? TelegramWithdrawAssignedEnabledAt { get; set; }
}
