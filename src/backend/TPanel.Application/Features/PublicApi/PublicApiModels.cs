namespace TPanel.Application.Features.PublicApi;

/// <summary>{code,status,message,data} zarfı + HTTP durum kodu.</summary>
public record V1Result(int HttpStatus, object Body)
{
    public static V1Result Error(int code, string message) =>
        new(code, new { code, status = false, message });
}

/// <summary>HMAC ile doğrulanmış merchant (merchantUser satırı, ihtiyaç duyulan alanlar).</summary>
public record MerchantContext(
    int Id,
    string Status,
    string Name,
    decimal MinDeposit,
    decimal MaxDeposit,
    decimal Commission,
    double WithdrawCommission,
    string ApiKey,
    string? ApiSecret,
    bool NewApi);

/// <summary>Dashboard "kullanılabilir IBAN" göstergesi için uygun IBAN + limitleri.</summary>
public record EligibleIban(int Id, int TeamId, double MinInvest, double MaxInvest, double MaxAmount,
    int DailyCountLimit, double TeamMin, double TeamMax, int TeamWaitLimit);

/// <summary>Banka seçim motorunun döndürdüğü uygun IBAN.</summary>
public record BankOption(
    int Id,
    string AccountHolder,
    string AccountIban,
    int TeamId,
    int SortOrder,
    string BankName);

/// <summary>invest satırı (okuma).</summary>
public class InvestRow
{
    public int Id { get; set; }
    public string Type { get; set; } = "1";
    public string Status { get; set; } = "0";
    public string Name { get; set; } = string.Empty;
    public double Amount { get; set; }
    public string? UId { get; set; }
    public string? CallbackUrl { get; set; }
    public string? CallbackOkUrl { get; set; }
    public string? CallbackFailUrl { get; set; }
    public int FirmId { get; set; }
    public int? TeamId { get; set; }
    public int? BankId { get; set; }
    public string? PlayerId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? FormAt { get; set; }
    public DateTime? ProcessDate { get; set; }
    public DateTime? FinalizeDate { get; set; }
    public int IbanSeen { get; set; }
    public string? ReceiptPath { get; set; }
    public int CallbackSended { get; set; }
}

// ---- İstekler ----
public record DepositApiRequest(string? order_id, decimal? amount, string? player_id, string? name,
    string? callback_url, string? successRedirectUrl, string? failRedirectUrl);

public record DirectDepositApiRequest(string? order_id, decimal? amount, string? player_id, string? name,
    string? callback_url);

public record WithdrawApiRequest(string? order_id, decimal? amount, string? player_id, string? name,
    string? iban, string? callback_url);

public record SelectBankRequest(int? bank_id, decimal? amount);
