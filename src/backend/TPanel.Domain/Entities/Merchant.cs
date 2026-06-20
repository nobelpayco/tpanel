namespace TPanel.Domain.Entities;

/// <summary>Dış müşteri (merchant) — `merchantUser` tablosu. HMAC API kimliği burada.</summary>
public class Merchant
{
    public int Id { get; set; }
    public uint? GroupId { get; set; }

    /// <summary>'1' aktif (varchar).</summary>
    public string Status { get; set; } = "1";

    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string? ApiSecret { get; set; }
    public decimal DepositLimit { get; set; }
    public decimal MinDeposit { get; set; }
    public decimal MaxDeposit { get; set; }
    public decimal Commission { get; set; }
    public double WithdrawCommission { get; set; }
    public decimal DeliveryCommission { get; set; }
    public DateTime CreatedAt { get; set; }
    public double CaseNow { get; set; }
    public int UseWallet { get; set; } = 1;
    public string? ApprovedIp { get; set; }

    /// <summary>1 ise callback gövdesi JSON, 0 ise form-encoded.</summary>
    public bool NewApi { get; set; }
}
