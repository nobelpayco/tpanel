using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.Background;
using TPanel.Application.Features.Export;

namespace TPanel.Infrastructure.Services;

/// <summary>
/// Laravel scheduler karşılığı — dakikalık tick.
/// 00:05'te bir önceki günün snapshot'ı; her dakika expire-pending + telegram/risk; 5 dakikada export temizliği.
/// </summary>
public class SchedulerHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IClock _clock;
    private readonly ILogger<SchedulerHostedService> _logger;
    private string? _lastSnapshotRunDate;
    private string? _lastReportRunDate;
    private int _pendingRunning; // CheckPending çakışma koruması (6×10sn döngü tick'i bloklamasın)

    public SchedulerHostedService(IServiceScopeFactory scopeFactory, IClock clock, ILogger<SchedulerHostedService> logger)
    {
        _scopeFactory = scopeFactory; _clock = clock; _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // İlk tick'i bir sonraki dakika başına hizala
        try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); } catch { return; }
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            await TickAsync(stoppingToken);
        } while (await SafeWait(timer, stoppingToken));
    }

    private static async Task<bool> SafeWait(PeriodicTimer t, CancellationToken ct)
    {
        try { return await t.WaitForNextTickAsync(ct); } catch (OperationCanceledException) { return false; }
    }

    private async Task TickAsync(CancellationToken ct)
    {
        var now = _clock.Now;
        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;

        // Her dakika — süre dolan invest'ler
        await Run("expire-pending", async () => await sp.GetRequiredService<IExpirePendingJob>().RunAsync(ct));

        // Telegram bekleyen kontrolü — 6×10sn iç döngüsü var, tick'i bloklamasın diye çakışma korumalı fire-and-forget
        if (Interlocked.CompareExchange(ref _pendingRunning, 1, 0) == 0)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var s = _scopeFactory.CreateScope();
                    await s.ServiceProvider.GetRequiredService<ICheckPendingNotificationsJob>().RunAsync(ct);
                }
                catch (Exception e) { _logger.LogError(e, "telegram-pending failed"); }
                finally { Interlocked.Exchange(ref _pendingRunning, 0); }
            }, ct);
        }

        // Risk taraması — hızlı, inline
        await Run("risk-low-amount", () => sp.GetRequiredService<ICheckLowAmountRiskJob>().RunAsync(ct));

        // 5 dakikada export temizliği
        if (now.Minute % 5 == 0)
            await Run("export-cleanup", async () => await sp.GetRequiredService<IExportStore>().CleanupOldAsync(30, ct));

        // 00:05'te (veya servis sonradan başlarsa ilk fırsatta) bir önceki günün snapshot'ı
        var today = now.ToString("yyyy-MM-dd");
        var afterWindow = now.Hour > 0 || (now.Hour == 0 && now.Minute >= 5);
        if (afterWindow && _lastSnapshotRunDate != today)
        {
            var yesterday = now.Date.AddDays(-1).ToString("yyyy-MM-dd");
            await Run($"snapshot:{yesterday}", async () => await sp.GetRequiredService<IDailyCaseSnapshotJob>().RunAsync(yesterday, ct));
            _lastSnapshotRunDate = today;
        }

        // 00:15'te bir önceki günün merchant mutabakat raporu → Telegram (TAKIM TOPLANTI)
        var afterReportWindow = now.Hour > 0 || (now.Hour == 0 && now.Minute >= 15);
        if (afterReportWindow && _lastReportRunDate != today)
        {
            var yesterday = now.Date.AddDays(-1).ToString("yyyy-MM-dd");
            await Run($"recon-report:{yesterday}", async () => await sp.GetRequiredService<IMerchantReconReportJob>().RunAsync(yesterday, ct));
            _lastReportRunDate = today;
        }
    }

    private async Task Run(string name, Func<Task> action)
    {
        try { await action(); }
        catch (Exception e) { _logger.LogError(e, "Scheduled job {Name} failed", name); }
    }
}
