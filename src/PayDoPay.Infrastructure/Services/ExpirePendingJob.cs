using Dapper;
using PayDoPay.Application.Common.Interfaces;
using PayDoPay.Application.Features.Background;
using PayDoPay.Application.Features.PublicApi;

namespace PayDoPay.Infrastructure.Services;

/// <summary>Süre dolan pending yatırımları otomatik reddet + fail callback (invest:expire-pending).</summary>
public class ExpirePendingJob : IExpirePendingJob
{
    private readonly IDbConnectionFactory _factory;
    private readonly ISystemSettingService _settings;
    private readonly ICallbackService _callbacks;
    private readonly IClock _clock;

    public ExpirePendingJob(IDbConnectionFactory factory, ISystemSettingService settings, ICallbackService callbacks, IClock clock)
    {
        _factory = factory; _settings = settings; _callbacks = callbacks; _clock = clock;
    }

    public async Task<int> RunAsync(CancellationToken ct = default)
    {
        if (await _settings.GetAsync("pay_link_expiry_enabled", ct) != "1") return 0;
        if (!int.TryParse(await _settings.GetAsync("pay_link_expiry_minutes", ct), out var minutes) || minutes <= 0) return 0;

        var cutoff = _clock.Now.AddMinutes(-minutes);
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var expired = (await c.QueryAsync<InvestRow>(
            @"SELECT id, type, status, name, amount, u_id, callbackUrl, callbackOkUrl, callbackFailUrl, firm_id, team_id, bank_id,
                     player_id, order_id, created_at, form_at, process_date, finalize_date, ibanSeen, receipt_path, callbackSended
              FROM invest WHERE type='1' AND status IN ('0','1','2') AND created_at < @cutoff", new { cutoff })).ToList();
        if (expired.Count == 0) return 0;

        foreach (var tx in expired)
        {
            await c.ExecuteAsync("UPDATE invest SET status='4', finalize_date=@now WHERE id=@id", new { now = _clock.Now, id = tx.Id });
            await c.ExecuteAsync(@"INSERT INTO investLog (investID, userID, ip, status, createdAt, detail) VALUES (@id, NULL, '127.0.0.1', 4, @now, 'Link süresi doldu (auto-expire)')",
                new { id = tx.Id, now = _clock.Now });
            tx.Status = "4";
            await _callbacks.SendExpireAsync(tx, ct: ct);
        }
        return expired.Count;
    }
}
