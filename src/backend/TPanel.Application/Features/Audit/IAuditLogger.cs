namespace TPanel.Application.Features.Audit;

/// <summary>Tek bir denetim (audit) kaydı.</summary>
public record AuditEntry(
    int? UserId,
    string? UserName,
    string? Ip,
    string? Method,
    string? Path,
    string? Action,
    string? EntityType,
    string? EntityId,
    string? Description,
    int? StatusCode,
    string? Meta = null);

/// <summary>Denetim İzleri sorgu filtresi.</summary>
public record AuditQuery(
    string? Search,
    int? UserId,
    string? Method,
    string? Action,
    string? From,
    string? To,
    int Page,
    int PerPage);

public record AuditPage(IReadOnlyList<object> Items, long Total);

/// <summary>Audit log yazma + sorgulama. Yazma asla isteği kırmamalı (hata yutulur).</summary>
public interface IAuditLogger
{
    Task WriteAsync(AuditEntry entry, CancellationToken ct = default);
    Task<AuditPage> QueryAsync(AuditQuery q, CancellationToken ct = default);
    Task<int> CleanupAsync(int days, CancellationToken ct = default);
}
