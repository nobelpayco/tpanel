using System.Text.Json;
using PayDoPay.Application.Common;
using PayDoPay.Application.Features.Dashboard;
using PayDoPay.Domain.Entities;

namespace PayDoPay.Application.Features.Export;

public interface IExportService
{
    Task<ApiResult> CreateAsync(User u, ExportCreateBody b, CancellationToken ct = default);
    Task<ApiResult> StatusAsync(User u, CancellationToken ct = default);
    Task<ApiResult> ClearAsync(User u, CancellationToken ct = default);
}

public class ExportService : IExportService
{
    private readonly IExportStore _store;
    private readonly IExportQueue _queue;
    private readonly IManagementHelper _mh;

    public ExportService(IExportStore store, IExportQueue queue, IManagementHelper mh)
    {
        _store = store; _queue = queue; _mh = mh;
    }

    public async Task<ApiResult> CreateAsync(User u, ExportCreateBody b, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(b.DateFrom) || string.IsNullOrEmpty(b.DateTo)) return ApiResult.Msg(422, "Tarih aralığı zorunludur.");
        if (string.Compare(b.DateTo, b.DateFrom, StringComparison.Ordinal) < 0) return ApiResult.Msg(422, "Bitiş tarihi başlangıçtan önce olamaz.");

        if (await _store.CountPendingForUserAsync(u.Id, ct) >= 2)
            return ApiResult.Msg(422, "En fazla 2 rapor sıraya alınabilir. Lütfen mevcut raporların tamamlanmasını bekleyin.");

        var filters = new Dictionary<string, object?>
        {
            ["date_from"] = b.DateFrom, ["date_to"] = b.DateTo, ["type"] = b.Type, ["status"] = b.Status,
        };
        if (u.HasMerchantScope)
            filters["merchant_ids"] = await _mh.GetMerchantIdsAsync(u.MerchantGroupId.HasValue ? (int)u.MerchantGroupId : null, u.FirmId, ct);
        else
            filters["merchant_id"] = b.MerchantId;
        if (u.HasTeamScope) filters["team_id"] = u.TeamId;
        else if (b.TeamId is not null) filters["team_id"] = b.TeamId;

        var jobId = await _store.CreateJobAsync(u.Id, JsonSerializer.Serialize(filters), ct);
        _queue.Enqueue(jobId);
        return ApiResult.Ok(new { success = true, job_id = jobId, message = "Export başlatıldı." });
    }

    public async Task<ApiResult> StatusAsync(User u, CancellationToken ct = default) => ApiResult.Ok(await _store.ListJobsAsync(u.Id, ct));
    public async Task<ApiResult> ClearAsync(User u, CancellationToken ct = default) { await _store.ClearForUserAsync(u.Id, ct); return ApiResult.Msg(200, "Bildirimler temizlendi."); }
}
