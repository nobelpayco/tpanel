namespace TPanel.Application.Features.Audit;

/// <summary>
/// İstek başına (scoped) audit zenginleştirme bağlamı. Kritik servis aksiyonları
/// Set(...) ile açıklama + eski/yeni değer bırakır; AuditActionFilter bunu okuyup
/// jenerik kayıt yerine zengin kayıt yazar.
/// </summary>
public interface IAuditContext
{
    bool HasEntry { get; }
    string? Description { get; }
    string? EntityType { get; }
    string? EntityId { get; }
    /// <summary>{"before":..,"after":..} JSON (set edilmişse).</summary>
    string? Meta { get; }

    void Set(string description, string? entityType = null, string? entityId = null, object? before = null, object? after = null);
}
