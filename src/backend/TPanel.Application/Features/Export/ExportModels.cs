using JP = System.Text.Json.Serialization.JsonPropertyNameAttribute;

namespace TPanel.Application.Features.Export;

public record ExportCreateBody(
    [property: JP("date_from")] string? DateFrom,
    [property: JP("date_to")] string? DateTo,
    [property: JP("type")] string? Type,
    [property: JP("merchant_id")] string? MerchantId,
    [property: JP("team_id")] int? TeamId,
    [property: JP("status")] string? Status);

/// <summary>Arka plan export kuyruğu.</summary>
public interface IExportQueue
{
    void Enqueue(long jobId);
    IAsyncEnumerable<long> DequeueAllAsync(CancellationToken ct);
}

/// <summary>Export job veri erişimi + CSV üretimi.</summary>
public interface IExportStore
{
    Task<int> CountPendingForUserAsync(int userId, CancellationToken ct = default);
    Task<long> CreateJobAsync(int userId, string filtersJson, CancellationToken ct = default);
    Task<object> ListJobsAsync(int userId, CancellationToken ct = default);
    Task<(bool Found, string Status, string? Filename, int OwnerId)> GetJobAsync(long id, CancellationToken ct = default);
    Task ClearForUserAsync(int userId, CancellationToken ct = default);
    Task ProcessJobAsync(long jobId, CancellationToken ct = default);
    Task CleanupOldAsync(int thresholdMinutes, CancellationToken ct = default);
    string ResolveExportPath(string filename);
    Task<int?> ResolveTokenUserAsync(string plainToken, CancellationToken ct = default);
}
