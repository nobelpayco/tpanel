using System.Globalization;
using System.Text;
using System.Threading.Channels;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.Export;

namespace TPanel.Infrastructure.Services;

/// <summary>Channel tabanlı export kuyruğu (singleton).</summary>
public class ExportQueue : IExportQueue
{
    private readonly Channel<long> _channel = Channel.CreateUnbounded<long>();
    public void Enqueue(long jobId) => _channel.Writer.TryWrite(jobId);
    public IAsyncEnumerable<long> DequeueAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}

/// <summary>Kuyruktan export job'larını işleyen arka plan servisi.</summary>
public class ExportBackgroundService : BackgroundService
{
    private readonly IExportQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExportBackgroundService> _logger;

    public ExportBackgroundService(IExportQueue queue, IServiceScopeFactory scopeFactory, ILogger<ExportBackgroundService> logger)
    {
        _queue = queue; _scopeFactory = scopeFactory; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var jobId in _queue.DequeueAllAsync(stoppingToken))
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var store = scope.ServiceProvider.GetRequiredService<IExportStore>();
                await store.ProcessJobAsync(jobId, stoppingToken);
            }
            catch (Exception e) { _logger.LogError(e, "Export job {Id} failed", jobId); }
        }
    }
}

public class ExportStore : IExportStore
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    private readonly string _exportDir;

    public ExportStore(IDbConnectionFactory factory, IClock clock, IConfiguration config, IWebHostEnvironmentMarker env)
    {
        _factory = factory; _clock = clock;
        var basePath = config["Storage:LocalDiskPath"] ?? "../../../docs/paydopay-v4/storage/app";
        _exportDir = Path.GetFullPath(Path.Combine(env.ContentRootPath, basePath, "exports"));
    }

    public string ResolveExportPath(string filename) => Path.Combine(_exportDir, filename);

    public async Task<int> CountPendingForUserAsync(int userId, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        return await c.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM export_jobs WHERE user_id=@u AND status IN ('pending','processing')", new { u = (ulong)userId });
    }

    public async Task<long> CreateJobAsync(int userId, string filtersJson, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        return await c.ExecuteScalarAsync<long>("INSERT INTO export_jobs (user_id,status,filters,created_at) VALUES (@u,'pending',@f,@at); SELECT LAST_INSERT_ID();",
            new { u = (ulong)userId, f = filtersJson, at = _clock.Now });
    }

    public async Task<object> ListJobsAsync(int userId, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var rows = await c.QueryAsync("SELECT id, user_id, filename, status, filters, created_at, completed_at FROM export_jobs WHERE user_id=@u ORDER BY created_at DESC LIMIT 20", new { u = (ulong)userId });
        return rows.Select(r => (object)new
        {
            id = (long)r.id, status = (string)r.status, filename = (string?)r.filename,
            filters = ParseJson((string?)r.filters), created_at = r.created_at, completed_at = r.completed_at,
        }).ToList();
    }

    public async Task<(bool, string, string?, int)> GetJobAsync(long id, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var j = await c.QueryFirstOrDefaultAsync("SELECT status, filename, user_id FROM export_jobs WHERE id=@id", new { id });
        if (j is null) return (false, "", null, 0);
        return (true, (string)j.status, (string?)j.filename, (int)(ulong)j.user_id);
    }

    public async Task ClearForUserAsync(int userId, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var files = await c.QueryAsync<string?>("SELECT filename FROM export_jobs WHERE user_id=@u", new { u = (ulong)userId });
        foreach (var f in files) if (!string.IsNullOrEmpty(f)) { try { File.Delete(ResolveExportPath(f)); } catch { } }
        await c.ExecuteAsync("DELETE FROM export_jobs WHERE user_id=@u", new { u = (ulong)userId });
    }

    public async Task CleanupOldAsync(int thresholdMinutes, CancellationToken ct = default)
    {
        var threshold = _clock.Now.AddMinutes(-thresholdMinutes);
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var files = await c.QueryAsync<string?>("SELECT filename FROM export_jobs WHERE created_at < @th", new { th = threshold });
        foreach (var f in files) if (!string.IsNullOrEmpty(f)) { try { File.Delete(ResolveExportPath(f)); } catch { } }
        await c.ExecuteAsync("DELETE FROM export_jobs WHERE created_at < @th", new { th = threshold });
    }

    public async Task<int?> ResolveTokenUserAsync(string plainToken, CancellationToken ct = default)
    {
        var idx = plainToken.IndexOf('|');
        var random = idx >= 0 ? plainToken[(idx + 1)..] : plainToken;
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(random)));
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var uid = await c.ExecuteScalarAsync<ulong?>("SELECT tokenable_id FROM personal_access_tokens WHERE token=@h LIMIT 1", new { h = hash });
        return uid is null ? null : (int)uid.Value;
    }

    public async Task ProcessJobAsync(long jobId, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var job = await c.QueryFirstOrDefaultAsync("SELECT id, user_id, filters FROM export_jobs WHERE id=@id", new { id = jobId });
        if (job is null) return;
        await c.ExecuteAsync("UPDATE export_jobs SET status='processing' WHERE id=@id", new { id = jobId });

        try
        {
            var filters = ParseFilters((string?)job.filters);
            var filename = $"export_{jobId}_{_clock.Now:yyyyMMdd_HHmmss}.csv";
            Directory.CreateDirectory(_exportDir);
            var path = Path.Combine(_exportDir, filename);

            var ownerType = await c.ExecuteScalarAsync<int?>("SELECT user_type FROM users WHERE id=@id", new { id = (int)(ulong)job.user_id }) ?? 0;
            var showMerchant = ownerType is 1 or 4;

            var (whereSql, p) = BuildWhere(filters);
            var rows = await c.QueryAsync($@"SELECT invest.id, merchantUser.name AS merchant_name, teams.name AS team_name, invest.order_id,
                invest.name AS sender_name, invest.amount, bankAccounts.account_holder, banks.name AS bank_name, invest.iban,
                invest.status, invest.created_at, invest.finalize_date
                FROM invest LEFT JOIN merchantUser ON invest.firm_id=merchantUser.id LEFT JOIN teams ON invest.team_id=teams.id
                LEFT JOIN bankAccounts ON invest.bank_id=bankAccounts.id LEFT JOIN banks ON bankAccounts.bank_id=banks.id
                {whereSql} ORDER BY invest.id DESC", p);

            var labels = new Dictionary<string, string> { ["0"] = "İptal", ["1"] = "Beklemede", ["2"] = "İşlemde", ["3"] = "Onaylandı", ["4"] = "Reddedildi" };
            var sb = new StringBuilder();
            sb.Append('﻿'); // BOM
            var header = new List<string> { "Id" };
            if (showMerchant) header.Add("Musteri");
            header.AddRange(new[] { "Takim", "Islem ID", "Ad Soyad", "Tutar", "Hesap Sahibi", "Banka", "IBAN", "Durum", "Islem Tarihi", "Onay Tarihi" });
            sb.AppendLine(string.Join(";", header.Select(Csv)));

            foreach (var r in rows)
            {
                var line = new List<string> { ((int)r.id).ToString() };
                if (showMerchant) line.Add((string?)r.merchant_name ?? "");
                line.AddRange(new[]
                {
                    (string?)r.team_name ?? "", (string?)r.order_id ?? "", (string?)r.sender_name ?? "",
                    Convert.ToString((object?)r.amount ?? 0, CultureInfo.InvariantCulture) ?? "0",
                    (string?)r.account_holder ?? "", (string?)r.bank_name ?? "", (string?)r.iban ?? "",
                    labels.GetValueOrDefault((string)r.status, (string)r.status),
                    ((DateTime?)r.created_at)?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                    ((DateTime?)r.finalize_date)?.ToString("yyyy-MM-dd HH:mm:ss") ?? "",
                });
                sb.AppendLine(string.Join(";", line.Select(Csv)));
            }

            await File.WriteAllTextAsync(path, sb.ToString(), new UTF8Encoding(false), ct);
            await c.ExecuteAsync("UPDATE export_jobs SET status='completed', filename=@f, completed_at=@at WHERE id=@id", new { f = filename, at = _clock.Now, id = jobId });
        }
        catch
        {
            await c.ExecuteAsync("UPDATE export_jobs SET status='failed' WHERE id=@id", new { id = jobId });
            throw;
        }
    }

    private static (string, DynamicParameters) BuildWhere(Dictionary<string, System.Text.Json.JsonElement> f)
    {
        var w = new StringBuilder("WHERE 1=1"); var p = new DynamicParameters();
        string? Str(string k) => f.TryGetValue(k, out var v) && v.ValueKind == System.Text.Json.JsonValueKind.String ? v.GetString() : null;

        var df = Str("date_from"); var dt = Str("date_to");
        if (!string.IsNullOrEmpty(df)) { w.Append(" AND COALESCE(invest.finalize_date, invest.created_at) >= @df"); p.Add("df", df + " 00:00:00"); }
        if (!string.IsNullOrEmpty(dt)) { w.Append(" AND COALESCE(invest.finalize_date, invest.created_at) <= @dt"); p.Add("dt", dt + " 23:59:59"); }
        var type = Str("type"); if (!string.IsNullOrEmpty(type) && type != "all") { w.Append(" AND invest.type=@type"); p.Add("type", type); }
        var status = Str("status"); if (!string.IsNullOrEmpty(status) && status != "all") { w.Append(" AND invest.status=@status"); p.Add("status", status); }

        if (f.TryGetValue("merchant_ids", out var mids) && mids.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            var ids = mids.EnumerateArray().Select(x => x.GetInt32()).ToList();
            if (ids.Count == 0) w.Append(" AND 1=0"); else { w.Append(" AND invest.firm_id IN @mids"); p.Add("mids", ids); }
        }
        else
        {
            var mid = Str("merchant_id");
            if (!string.IsNullOrEmpty(mid))
            {
                if (mid.StartsWith("g_")) { w.Append(" AND invest.firm_id IN (SELECT id FROM merchantUser WHERE group_id=@gid)"); p.Add("gid", int.Parse(mid[2..])); }
                else { w.Append(" AND invest.firm_id=@mid"); p.Add("mid", int.Parse(mid)); }
            }
        }
        if (f.TryGetValue("team_id", out var tid) && tid.ValueKind == System.Text.Json.JsonValueKind.Number) { w.Append(" AND invest.team_id=@tid"); p.Add("tid", tid.GetInt32()); }
        return (w.ToString(), p);
    }

    private static Dictionary<string, System.Text.Json.JsonElement> ParseFilters(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new();
        try { return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(json) ?? new(); } catch { return new(); }
    }
    private static object? ParseJson(string? json) { if (string.IsNullOrEmpty(json)) return null; try { return System.Text.Json.JsonSerializer.Deserialize<object>(json); } catch { return null; } }
    private static string Csv(string v) => v.Contains(';') || v.Contains('"') || v.Contains('\n') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
}
