using Dapper;
using TPanel.Application.Common.Interfaces;

namespace TPanel.Infrastructure.Services;

/// <summary>system_settings (key/value) erişimi.</summary>
public class SystemSettingService : ISystemSettingService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public SystemSettingService(IDbConnectionFactory factory, IClock clock)
    {
        _factory = factory;
        _clock = clock;
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<string?>(
            "SELECT value FROM system_settings WHERE `key` = @key LIMIT 1", new { key });
    }

    public async Task SetAsync(string key, string? value, CancellationToken ct = default)
    {
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        await conn.ExecuteAsync(
            @"INSERT INTO system_settings (`key`, `value`, updated_at) VALUES (@key, @value, @now)
              ON DUPLICATE KEY UPDATE `value` = @value, updated_at = @now",
            new { key, value, now = _clock.Now });
    }
}
