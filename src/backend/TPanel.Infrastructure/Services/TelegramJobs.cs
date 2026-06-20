using System.Data;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.Background;

namespace TPanel.Infrastructure.Services;

/// <summary>Bayesian güven skoru (son 10 yatırım) — TrustScore karşılığı.</summary>
internal static class TrustScore
{
    public static async Task<(int? Rate, int Count)> CalculateAsync(IDbConnection c, string? playerId, int? beforeId)
    {
        if (string.IsNullOrEmpty(playerId)) return (null, 0);
        var sql = "SELECT status FROM invest WHERE player_id=@p AND type=1 AND status IN ('3','4')" + (beforeId is null ? "" : " AND id<@b") + " ORDER BY id DESC LIMIT 10";
        var statuses = (await c.QueryAsync<string>(sql, new { p = playerId, b = beforeId })).ToList();
        if (statuses.Count == 0) return (null, 0);
        var approved = statuses.Count(s => s == "3");
        var count = statuses.Count;
        var rawRate = approved / (double)count;
        var weight = Math.Min(count / 10.0, 1);
        var rate = (int)Math.Round((0.75 * (1 - weight) + rawRate * weight) * 100);
        return (rate, count);
    }
}

/// <summary>risk:check-low-amount — şüpheli oyuncu aktivitesi, sistem chat'ine inline button'lı bildirim.</summary>
public class CheckLowAmountRiskJob : ICheckLowAmountRiskJob
{
    private const int BatchLimit = 200, WindowSize = 10, MinCount = 3;
    private const double AmountThreshold = 1000.0;
    private readonly IDbConnectionFactory _factory;
    private readonly ITelegramService _telegram;
    private readonly IClock _clock;
    private readonly string? _chatId;
    private readonly ILogger<CheckLowAmountRiskJob> _logger;

    public CheckLowAmountRiskJob(IDbConnectionFactory factory, ITelegramService telegram, IClock clock, IConfiguration config, ILogger<CheckLowAmountRiskJob> logger)
    {
        _factory = factory; _telegram = telegram; _clock = clock; _chatId = config["Telegram:PayrouteChatId"]; _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(_chatId)) return;
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var cursor = int.TryParse(await c.ExecuteScalarAsync<string?>("SELECT value FROM system_settings WHERE `key`='risk_check_last_invest_id'"), out var cv) ? cv : 0;
        var events = (await c.QueryAsync("SELECT id, player_id FROM invest WHERE id>@cur AND type=1 AND status IN (3,4) AND player_id IS NOT NULL AND player_id<>'' ORDER BY id LIMIT @lim", new { cur = cursor, lim = BatchLimit })).ToList();
        if (events.Count == 0) return;
        var maxId = events.Max(e => (int)e.id);
        foreach (var pid in events.Select(e => (string)e.player_id).Distinct())
        {
            try { await Evaluate(c, pid, _chatId!); }
            catch (Exception e) { _logger.LogWarning(e, "risk player {Pid}", pid); }
        }
        await c.ExecuteAsync("UPDATE system_settings SET value=@v WHERE `key`='risk_check_last_invest_id'", new { v = maxId.ToString() });
    }

    private async Task Evaluate(IDbConnection c, string playerId, string chatId)
    {
        if (await c.ExecuteScalarAsync<int>("SELECT EXISTS(SELECT 1 FROM blacklist WHERE type=1 AND val=@v)", new { v = playerId }) == 1) return;
        var rows = (await c.QueryAsync("SELECT id, amount, status, name, firm_id, finalize_date, created_at FROM invest WHERE player_id=@p AND type=1 AND status IN (3,4) ORDER BY id DESC LIMIT @w", new { p = playerId, w = WindowSize })).ToList();
        if (rows.Count < MinCount) return;
        var approvedRows = rows.Where(r => (int)r.status == 3).ToList();
        var approved = approvedRows.Count;
        var rejected = rows.Count(r => (int)r.status == 4);
        double? maxApproved = approved > 0 ? approvedRows.Max(r => Convert.ToDouble(r.amount)) : null;
        var totalApproved = approved > 0 ? approvedRows.Sum(r => Convert.ToDouble(r.amount)) : 0;
        var trigger = (approved >= 1 && maxApproved < AmountThreshold) || (approved == 0 && rejected >= MinCount);
        if (!trigger) return;

        var last = rows[0];
        var lastTime = (DateTime?)(last.finalize_date ?? last.created_at);
        var merchant = await c.ExecuteScalarAsync<string?>("SELECT name FROM merchantUser WHERE id=@id", new { id = (int)last.firm_id }) ?? "-";
        var text = BuildMessage(playerId, (string?)last.name, merchant, approved, rejected, totalApproved, maxApproved, (int)last.status, lastTime);

        var existing = await c.QueryFirstOrDefaultAsync("SELECT id, invest_id, chat_id, message_id FROM player_risk_notifications WHERE player_id=@p AND decision='pending' ORDER BY id DESC LIMIT 1", new { p = playerId });
        if (existing is not null)
        {
            if ((int)existing.invest_id == (int)last.id) return;
            if (existing.message_id is not null)
            {
                var kb = Keyboard((int)existing.id);
                await _telegram.EditMessageTextWithMarkupAsync(existing.chat_id.ToString(), Convert.ToInt64(existing.message_id), text, "HTML", kb);
            }
            await c.ExecuteAsync("UPDATE player_risk_notifications SET invest_id=@iv, notified_at=@n WHERE id=@id", new { iv = (int)last.id, n = _clock.Now, id = (int)existing.id });
            return;
        }

        var notifId = await c.ExecuteScalarAsync<int>(@"INSERT INTO player_risk_notifications (player_id, invest_id, chat_id, message_id, reason, decision, notified_at) VALUES (@p,@iv,@cid,NULL,'suspicious_activity','pending',@n); SELECT LAST_INSERT_ID();",
            new { p = playerId, iv = (int)last.id, cid = chatId, n = _clock.Now });
        var messageId = await _telegram.SendReturnIdAsync(chatId, text, "HTML", Keyboard(notifId));
        if (messageId is not null) await c.ExecuteAsync("UPDATE player_risk_notifications SET message_id=@m WHERE id=@id", new { m = messageId, id = notifId });
        else await c.ExecuteAsync("DELETE FROM player_risk_notifications WHERE id=@id", new { id = notifId });
    }

    private static object Keyboard(int notifId) => new
    {
        inline_keyboard = new[] { new object[] {
            new { text = "🚫 Engelle", callback_data = $"risk_block:{notifId}" },
            new { text = "⏭️ Vazgeç", callback_data = $"risk_dismiss:{notifId}" } } }
    };

    private static string Esc(string? v) => System.Net.WebUtility.HtmlEncode(v ?? "-");
    private static string Fmt(double n) => n.ToString("#,##0.00", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));

    private static string BuildMessage(string playerId, string? name, string merchant, int approved, int rejected, double totalApproved, double? maxApproved, int lastStatus, DateTime? lastTime)
    {
        var lastFmt = lastTime?.ToString("dd.MM.yyyy HH:mm") ?? "-";
        var statusLabel = lastStatus == 3 ? "onaylı" : "red";
        var approvedLine = $"   ✅ Onaylı: <b>{approved}</b>";
        if (approved > 0) approvedLine += $" (toplam {Fmt(totalApproved)} TL, max {Fmt(maxApproved ?? 0)} TL)";
        return "🚨 <b>Şüpheli Oyuncu Aktivitesi</b>\n\n"
            + $"Player ID: <code>{Esc(playerId)}</code>\n"
            + $"Ad Soyad: <b>{Esc(name)}</b>\n"
            + $"Mağaza Adı: {Esc(merchant)}\n\n"
            + $"Son {approved + rejected} İşlem:\n{approvedLine}\n"
            + $"   ❌ Reddedilen: <b>{rejected}</b>\n\n"
            + $"Son işlem: {Esc(lastFmt)} <i>({statusLabel})</i>";
    }
}

/// <summary>telegram:check-pending — sonuçlandırılmayan yatırım/dekont + kredi azaldı + maks kasa.</summary>
public class CheckPendingNotificationsJob : ICheckPendingNotificationsJob
{
    private readonly IDbConnectionFactory _factory;
    private readonly ITelegramService _telegram;
    private readonly IMerchantBankService _bank;
    private readonly IClock _clock;
    private readonly string? _payrouteChatId;

    public CheckPendingNotificationsJob(IDbConnectionFactory factory, ITelegramService telegram, IMerchantBankService bank, IClock clock, IConfiguration config)
    {
        _factory = factory; _telegram = telegram; _bank = bank; _clock = clock; _payrouteChatId = config["Telegram:PayrouteChatId"];
    }

    private static string E(string? v) => ITelegramService.Escape(v ?? "-");
    private static string Money0(double n) => "₺" + n.ToString("#,##0", System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));

    public async Task RunAsync(CancellationToken ct = default)
    {
        await CheckAccountAvailability(ct);
        // Cron dakikada bir tetikler — 6 kez kontrol (~10 sn aralık)
        for (var i = 0; i < 6; i++)
        {
            await RunCheck(ct);
            if (i < 5) { try { await Task.Delay(TimeSpan.FromSeconds(10), ct); } catch { return; } }
        }
        await CheckCreditLow(ct);
        await CheckMaxCase(ct);
    }

    private async Task CheckAccountAvailability(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_payrouteChatId)) return;
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var settings = (await c.QueryAsync("SELECT `key`, `value` FROM system_settings")).ToDictionary(r => (string)r.key, r => (string?)r.value);
        if (settings.GetValueOrDefault("payroute_alert_enabled", "1") != "1") return;
        if (!double.TryParse(settings.GetValueOrDefault("min_notify_amount"), out var threshold) || threshold <= 0) return;
        if (settings.TryGetValue("payroute_last_alert_at", out var la) && DateTime.TryParse(la, out var laDt) && (_clock.Now - laDt).TotalSeconds < 300) return;

        const string baseWhere = "FROM bankAccounts ba JOIN teams t ON ba.team_id=t.id WHERE ba.status=1 AND t.status<>0";
        var totalActive = await c.ExecuteScalarAsync<int>($"SELECT COUNT(*) {baseWhere}");
        var eligible = await c.ExecuteScalarAsync<int>($"SELECT COUNT(*) {baseWhere} AND ba.min_invest<=@th AND ba.max_invest>=@th", new { th = threshold });
        if (eligible > 0) return;

        var tFmt = ITelegramService.Escape(Money0(threshold));
        string msg;
        if (totalActive == 0)
            msg = "🚨🚨🚨 *DİKKAT* 🚨🚨🚨\n\n*SİSTEMDE HİÇ AKTİF HESAP YOK\\!*\n\n_Yatırım alınmıyor\\._";
        else
        {
            var minMin = await c.ExecuteScalarAsync<double?>($"SELECT MIN(ba.min_invest) {baseWhere}") ?? 0;
            var minFmt = ITelegramService.Escape(Money0(minMin));
            msg = $"🚨🚨🚨 *DİKKAT* 🚨🚨🚨\n\n*Sistemde {tFmt} tutarında hesap yok\\!*\n_Yatırım alınmıyor\\._\n\n*Minimum Tutarlı Hesap:* {minFmt}";
        }
        if (await _telegram.SendAsync(_payrouteChatId!, msg, ct))
            await c.ExecuteAsync("INSERT INTO system_settings (`key`,`value`,updated_at) VALUES ('payroute_last_alert_at',@v,@v) ON DUPLICATE KEY UPDATE `value`=@v, updated_at=@v", new { v = _clock.Now });
    }

    private async Task RunCheck(CancellationToken ct)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var threshold = _clock.Now.AddSeconds(-630);

        var unfinalized = await c.QueryAsync(@"
            SELECT invest.id, invest.order_id, invest.player_id, invest.amount, invest.name, invest.process_date,
                   teams.telegram_chat_id, teams.name AS team_name, agent.name AS agent_name
            FROM invest JOIN teams ON invest.team_id=teams.id
            LEFT JOIN users agent ON invest.agent_id=agent.id
            LEFT JOIN telegram_notifications tn ON tn.invest_id=invest.id AND tn.type='unfinalized'
            WHERE invest.type='1' AND invest.status IN ('1','2') AND teams.telegram_enabled=1 AND teams.telegram_pending_invest_enabled=1
              AND teams.telegram_chat_id IS NOT NULL AND teams.telegram_pending_invest_enabled_at IS NOT NULL
              AND invest.created_at>=teams.telegram_pending_invest_enabled_at AND tn.id IS NULL
              AND ((invest.process_date IS NOT NULL AND invest.process_date<=@th) OR (invest.process_date IS NULL AND invest.created_at<=@th))",
            new { th = threshold });

        foreach (var row in unfinalized)
        {
            var orderId = string.IsNullOrEmpty((string?)row.order_id) ? ((int)row.id).ToString() : (string)row.order_id;
            var (rate, cnt) = await TrustScore.CalculateAsync(c, (string?)row.player_id, (int)row.id);
            var score = rate is null ? "—" : $"%{rate} ({cnt} işlem)";
            var msg = $"⏰ *SONUÇLANDIRILMADI* — `#{E(orderId)}`\n"
                + $"*Takım:* {E((string?)row.team_name)}\n"
                + $"*Tutar:* {E(Money0(Convert.ToDouble(row.amount)))}\n"
                + $"*Üye:* {E((string?)row.name)}\n"
                + $"*Agent:* {E((string?)row.agent_name)}\n"
                + $"*Üye Skoru:* {E(score)}\n"
                + "_10 dakika 30 saniyedir işlemde, sonuçlandırılması bekleniyor\\._";
            if (await _telegram.SendAsync(row.telegram_chat_id.ToString(), msg, ct))
                await c.ExecuteAsync("INSERT IGNORE INTO telegram_notifications (invest_id, type, sent_at) VALUES (@id,'unfinalized',@n)", new { id = (int)row.id, n = _clock.Now });
        }

        var missing = await c.QueryAsync(@"
            SELECT invest.id, invest.order_id, teams.telegram_withdraw_chat_id
            FROM invest JOIN teams ON invest.team_id=teams.id
            LEFT JOIN telegram_notifications tn ON tn.invest_id=invest.id AND tn.type='missing_receipt'
            WHERE invest.type='2' AND invest.status='3' AND teams.telegram_enabled=1 AND teams.telegram_missing_receipt_enabled=1
              AND teams.telegram_withdraw_chat_id IS NOT NULL AND teams.telegram_missing_receipt_enabled_at IS NOT NULL
              AND invest.finalize_date>=teams.telegram_missing_receipt_enabled_at AND tn.id IS NULL
              AND invest.finalize_date<=@th AND NOT EXISTS (SELECT 1 FROM invest_receipts WHERE invest_receipts.invest_id=invest.id)",
            new { th = _clock.Now.AddMinutes(-15) });

        foreach (var row in missing)
        {
            var orderId = string.IsNullOrEmpty((string?)row.order_id) ? ((int)row.id).ToString() : (string)row.order_id;
            var msg = $"⏰ *DEKONT YÜKLENMEDİ* — `#{E(orderId)}`\n_15 dakikadır dekont yüklenmedi\\. Lütfen dekont yükleyin\\!_";
            if (await _telegram.SendAsync(row.telegram_withdraw_chat_id.ToString(), msg, ct))
                await c.ExecuteAsync("INSERT IGNORE INTO telegram_notifications (invest_id, type, sent_at) VALUES (@id,'missing_receipt',@n)", new { id = (int)row.id, n = _clock.Now });
        }
    }

    private async Task CheckCreditLow(CancellationToken ct)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var teams = (await c.QueryAsync(@"SELECT id, name, maxCase, telegram_chat_id, telegram_credit_low_threshold, telegram_credit_low_state
            FROM teams WHERE telegram_enabled=1 AND telegram_credit_low_enabled=1 AND telegram_chat_id IS NOT NULL
              AND telegram_credit_low_threshold IS NOT NULL AND telegram_credit_low_threshold>0 AND maxCase<>0 AND status=1")).ToList();
        if (teams.Count == 0) return;
        var cashes = await _bank.CurrentCashForTeamsAsync(teams.Select(t => (int)t.id).ToList(), ct);
        var systemChatId = await c.ExecuteScalarAsync<string?>("SELECT value FROM system_settings WHERE `key`='telegram_chat_id'");

        foreach (var t in teams)
        {
            var current = cashes.GetValueOrDefault((int)t.id, 0);
            var thr = Convert.ToDouble(t.telegram_credit_low_threshold);
            var maxCase = Convert.ToDouble(t.maxCase);
            var isLow = current >= maxCase - thr;
            var state = (int)t.telegram_credit_low_state;
            if (isLow && state == 0)
            {
                var msg = $"⚠️ *KREDİ AZALDI* — `{E((string?)t.name)}`\n*Kasanız:* {E(Money0(current))} TRY";
                var sent = await _telegram.SendAsync(t.telegram_chat_id.ToString(), msg, ct);
                if (!string.IsNullOrEmpty(systemChatId) && systemChatId != t.telegram_chat_id.ToString()) await _telegram.SendAsync(systemChatId, msg, ct);
                if (sent) await c.ExecuteAsync("UPDATE teams SET telegram_credit_low_state=1 WHERE id=@id", new { id = (int)t.id });
            }
            else if (!isLow && state == 1)
                await c.ExecuteAsync("UPDATE teams SET telegram_credit_low_state=0 WHERE id=@id", new { id = (int)t.id });
        }
    }

    private async Task CheckMaxCase(CancellationToken ct)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var teamIds = (await c.QueryAsync<int>("SELECT id FROM teams WHERE block_when_full=1 AND status=1 AND maxCase<>0")).ToList();
        if (teamIds.Count > 0) await _bank.EnforceMaxCaseAsync(teamIds, ct);
    }
}
