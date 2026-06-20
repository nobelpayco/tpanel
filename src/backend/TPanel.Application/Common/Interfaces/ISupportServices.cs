using TPanel.Application.Features.PublicApi;

namespace TPanel.Application.Common.Interfaces;

/// <summary>system_settings anahtar/değer erişimi.</summary>
public interface ISystemSettingService
{
    Task<string?> GetAsync(string key, CancellationToken ct = default);
    Task SetAsync(string key, string? value, CancellationToken ct = default);
}

/// <summary>Telegram bildirim gönderimi (Faz 6'da tam entegrasyon).</summary>
public interface ITelegramService
{
    Task<bool> SendAsync(string chatId, string markdownMessage, CancellationToken ct = default);

    /// <summary>Mesaj gönder, message_id döndür (parse mode + opsiyonel inline keyboard).</summary>
    Task<long?> SendReturnIdAsync(string chatId, string text, string parseMode = "HTML", object? replyMarkup = null, CancellationToken ct = default);

    /// <summary>Var olan mesajın metnini ve inline keyboard'unu güncelle.</summary>
    Task<bool> EditMessageTextWithMarkupAsync(string chatId, long messageId, string text, string parseMode, object? replyMarkup, CancellationToken ct = default);

    /// <summary>MarkdownV2 özel karakter kaçışı.</summary>
    static string Escape(string text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? string.Empty;
        const string special = "_*[]()~`>#+-=|{}.!";
        var sb = new System.Text.StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (special.IndexOf(c) >= 0) sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}

/// <summary>
/// Merchant API & public pay için invest/blacklist veri erişimi (Dapper).
/// PHP'deki DB::table('invest')/('blacklist') sorgularının birebir karşılığı.
/// </summary>
public interface IMerchantApiStore
{
    Task<MerchantContext?> FindMerchantByApiKeyAsync(string apiKey, CancellationToken ct = default);

    Task<bool> IsBlacklistedAsync(int type, string value, CancellationToken ct = default);
    Task<bool> OrderIdExistsAsync(string orderOrUid, CancellationToken ct = default);
    Task<bool> HasRecentPendingForPlayerAsync(string playerId, DateTime since, CancellationToken ct = default);

    Task<InvestRow?> GetByUidAsync(string uId, CancellationToken ct = default);
    Task<InvestRow?> GetByOrderOrUidForMerchantAsync(int merchantId, string orderOrUid, CancellationToken ct = default);
    Task<BankOption?> GetBankAccountAsync(int bankId, CancellationToken ct = default);

    Task<int> InsertDepositHostedAsync(InvestInsert data, CancellationToken ct = default);
    Task<int> InsertDepositDirectAsync(InvestInsert data, int bankId, int teamId, CancellationToken ct = default);
    Task<int> InsertWithdrawAsync(InvestInsert data, string ibanUpper, CancellationToken ct = default);

    Task UpdateAsync(string uId, IDictionary<string, object?> fields, CancellationToken ct = default);
}

/// <summary>invest insert için ortak alanlar.</summary>
public record InvestInsert(
    string Name,
    double Amount,
    string UId,
    string CallbackUrl,
    string? CallbackOkUrl,
    string? CallbackFailUrl,
    double CommissionAmount,
    int CommissionPercent,
    int MerchantId,
    string PlayerId,
    string OrderId);
