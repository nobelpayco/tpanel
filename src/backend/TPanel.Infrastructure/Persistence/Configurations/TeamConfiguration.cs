using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TPanel.Domain.Entities;

namespace TPanel.Infrastructure.Persistence.Configurations;

public class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> b)
    {
        b.ToTable("teams");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.Name).HasColumnName("name");
        b.Property(x => x.Status).HasColumnName("status");
        b.Property(x => x.IsFast).HasColumnName("is_fast");
        b.Property(x => x.DailyLimit).HasColumnName("daily_limit");
        b.Property(x => x.MinInvest).HasColumnName("min_invest");
        b.Property(x => x.MaxInvest).HasColumnName("max_invest");
        b.Property(x => x.AccountPerm).HasColumnName("account_perm");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.Note).HasColumnName("note");
        b.Property(x => x.AllowedCustomers).HasColumnName("allowed_customers");
        b.Property(x => x.Commission).HasColumnName("commission");
        b.Property(x => x.WaitLimit).HasColumnName("wait_limit");
        b.Property(x => x.StatusReason).HasColumnName("statusReason");
        b.Property(x => x.Overturn).HasColumnName("overturn");
        b.Property(x => x.Withdraw).HasColumnName("withdraw");
        b.Property(x => x.WithdrawNow).HasColumnName("withdrawNow");
        b.Property(x => x.WithdrawNowCount).HasColumnName("withdrawNowCount");
        b.Property(x => x.EggsId).HasColumnName("eggsID");
        b.Property(x => x.EggsIdAll).HasColumnName("eggsIDAll");
        b.Property(x => x.WithdrawAll).HasColumnName("withdrawAll");
        b.Property(x => x.WinRate).HasColumnName("winRate");
        b.Property(x => x.TeamNow).HasColumnName("teamNow");
        b.Property(x => x.IsWallet).HasColumnName("isWallet");
        b.Property(x => x.LastPayOut).HasColumnName("lastPayOut");
        b.Property(x => x.Provider).HasColumnName("provider");
        b.Property(x => x.MaxCase).HasColumnName("maxCase");
        b.Property(x => x.AllowDuplicateIban).HasColumnName("allow_duplicate_iban");
        b.Property(x => x.BlockWhenFull).HasColumnName("block_when_full");
        b.Property(x => x.TelegramEnabled).HasColumnName("telegram_enabled");
        b.Property(x => x.TelegramChatId).HasColumnName("telegram_chat_id");
        b.Property(x => x.WithdrawAlertAt).HasColumnName("withdraw_alert_at");
        b.Property(x => x.TelegramWithdrawEnabled).HasColumnName("telegram_withdraw_enabled");
        b.Property(x => x.TelegramWithdrawChatId).HasColumnName("telegram_withdraw_chat_id");
        b.Property(x => x.TelegramReconciliationChatId).HasColumnName("telegram_reconciliation_chat_id");
        b.Property(x => x.LimitAlertAt).HasColumnName("limit_alert_at");
        b.Property(x => x.LimitCheckEnabled).HasColumnName("limit_check_enabled");
        b.Property(x => x.TelegramCreditLowEnabled).HasColumnName("telegram_credit_low_enabled");
        b.Property(x => x.TelegramCreditLowThreshold).HasColumnName("telegram_credit_low_threshold");
        b.Property(x => x.TelegramCreditLowState).HasColumnName("telegram_credit_low_state");
        b.Property(x => x.TelegramPendingInvestEnabled).HasColumnName("telegram_pending_invest_enabled");
        b.Property(x => x.TelegramMissingReceiptEnabled).HasColumnName("telegram_missing_receipt_enabled");
        b.Property(x => x.TelegramCreditLowEnabledAt).HasColumnName("telegram_credit_low_enabled_at");
        b.Property(x => x.TelegramPendingInvestEnabledAt).HasColumnName("telegram_pending_invest_enabled_at");
        b.Property(x => x.TelegramMissingReceiptEnabledAt).HasColumnName("telegram_missing_receipt_enabled_at");
        b.Property(x => x.TelegramCashReportEnabled).HasColumnName("telegram_cash_report_enabled");
        b.Property(x => x.TelegramMaxCaseState).HasColumnName("telegram_max_case_state");
        b.Property(x => x.TelegramWithdrawAssignedEnabled).HasColumnName("telegram_withdraw_assigned_enabled");
        b.Property(x => x.TelegramWithdrawAssignedEnabledAt).HasColumnName("telegram_withdraw_assigned_enabled_at");
    }
}
