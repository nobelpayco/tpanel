using PayDoPay.Application.Features.PublicApi;

namespace PayDoPay.Application.Common.Interfaces;

/// <summary>Merchant API IBAN seçim mantığı (eski Api_model::getBank karşılığı).</summary>
public interface IMerchantBankService
{
    Task<IReadOnlyList<BankOption>> AvailableForAmountAsync(double amount, int merchantId, int? forcedTeamId = null, CancellationToken ct = default);
    Task<BankOption?> PickOneAsync(double amount, int merchantId, int? forcedTeamId = null, CancellationToken ct = default);
    Task<BankOption?> ValidateAsync(int bankId, double amount, int merchantId, CancellationToken ct = default);

    Task<IReadOnlyDictionary<int, double>> CurrentCashForTeamsAsync(IReadOnlyCollection<int> teamIds, CancellationToken ct = default);

    /// <summary>Tutar filtresi olmadan tüm uygun (limitlere uyan) IBAN'lar — dashboard göstergesi.</summary>
    Task<IReadOnlyList<EligibleIban>> EligibleIbansAsync(CancellationToken ct = default);

    /// <summary>maxCase'ini geçen takımları pasife alır (status=2) + Telegram bildirimi. Yeni pasifleşenleri döner.</summary>
    Task<IReadOnlyList<int>> EnforceMaxCaseAsync(IReadOnlyCollection<int> teamIds, CancellationToken ct = default);

    /// <summary>PayRoute grubuna "kullanılabilir IBAN yok" uyarısı (5 dk throttle).</summary>
    Task AlertNoIbanAvailableAsync(int merchantId, double amount, int? investId = null,
        string? playerId = null, string? orderId = null, string? playerName = null, CancellationToken ct = default);
}
