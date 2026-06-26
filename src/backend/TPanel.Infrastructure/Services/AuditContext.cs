using System.Text.Json;
using TPanel.Application.Features.Audit;

namespace TPanel.Infrastructure.Services;

/// <summary>Scoped audit bağlamı — kritik aksiyonlar açıklama + before/after bırakır.</summary>
public class AuditContext : IAuditContext
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public bool HasEntry { get; private set; }
    public string? Description { get; private set; }
    public string? EntityType { get; private set; }
    public string? EntityId { get; private set; }
    public string? Meta { get; private set; }

    public void Set(string description, string? entityType = null, string? entityId = null, object? before = null, object? after = null)
    {
        HasEntry = true;
        Description = description;
        EntityType = entityType;
        EntityId = entityId;
        if (before is not null || after is not null)
        {
            try { Meta = JsonSerializer.Serialize(new { before, after }, JsonOpts); }
            catch { Meta = null; }
        }
    }
}
