namespace TPanel.Application.Features.Reports;

/// <summary>Analitik rapor veri erişimi (Dapper). Tarihler 'yyyy-MM-dd', merchant param 'g_N' grup olabilir.</summary>
public interface IReportsStore
{
    // Merchant raporları
    Task<object> MerchantFilterOptionsAsync(CancellationToken ct = default);
    Task<object> VolumePerformanceAsync(string from, string to, string? merchantParam, CancellationToken ct = default);
    Task<object> PlayerAnalysisAsync(string from, string to, string? merchantParam, CancellationToken ct = default);
    Task<object> AmountAnalysisAsync(string from, string to, string? merchantParam, CancellationToken ct = default);
    Task<object> FinancialReportAsync(string from, string to, string? merchantParam, CancellationToken ct = default);
    Task<object> RiskReportAsync(string from, string to, string? merchantParam, CancellationToken ct = default);

    // Takım raporları
    Task<object> TeamFilterOptionsAsync(CancellationToken ct = default);
    Task<object> TeamOverviewAsync(string from, string to, IReadOnlyList<int>? teamIds, CancellationToken ct = default);
    Task<object> TeamTrendsAsync(string from, string to, IReadOnlyList<int>? teamIds, CancellationToken ct = default);
    Task<object> TeamHourlyAsync(string from, string to, IReadOnlyList<int>? teamIds, CancellationToken ct = default);

    // Operasyon raporları
    Task<object> QueueAnalysisAsync(string? merchantParam, CancellationToken ct = default);
    Task<object> PeakHourAnalysisAsync(string from, string to, string? merchantParam, CancellationToken ct = default);
    Task<object> SlaReportAsync(string from, string to, string? merchantParam, CancellationToken ct = default);

    // Dönüşüm
    Task<object> ConversionAsync(string from, string to, string type, IReadOnlyList<int>? userMerchantIds, CancellationToken ct = default);

    // Oyuncu riski
    Task<object> SuspiciousPlayersAsync(string from, string to, string? merchantParam, CancellationToken ct = default);
    Task<object> PlayerSegmentationAsync(string from, string to, string? merchantParam, CancellationToken ct = default);
    Task<object> MultiNamePlayersAsync(string from, string to, string? merchantParam, CancellationToken ct = default);

    // Banka hesabı analizi
    Task<object> BankAccountAnalysisAsync(string from, string to, CancellationToken ct = default);
}
