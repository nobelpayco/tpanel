using System.Globalization;
using System.Text;
using Dapper;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.Background;

namespace TPanel.Infrastructure.Services;

/// <summary>
/// Günlük merchant mutabakat raporu → Telegram (TAKIM TOPLANTI).
/// Her aktif merchant (Test hariç) için: Yatırım/Çekim (API+Manuel), manuel çekim vurgusu, reddedilen çekim.
/// Hareketsiz merchant atlanır. Hedef chat: system_settings.recon_report_chat_id (varsayılan TAKIM TOPLANTI).
/// </summary>
public class MerchantReconReportJob : IMerchantReconReportJob
{
    private const string DefaultChatId = "-5094660735"; // TAKIM TOPLANTI
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    private readonly IDbConnectionFactory _factory;
    private readonly ITelegramService _telegram;
    private readonly ISystemSettingService _settings;

    public MerchantReconReportJob(IDbConnectionFactory factory, ITelegramService telegram, ISystemSettingService settings)
    {
        _factory = factory; _telegram = telegram; _settings = settings;
    }

    public async Task RunAsync(string date, CancellationToken ct = default)
    {
        var chatId = (await _settings.GetAsync("recon_report_chat_id", ct)) ?? DefaultChatId;
        if (string.IsNullOrWhiteSpace(chatId)) return;

        using var c = await _factory.CreateOpenConnectionAsync(ct);

        // Aktif merchantlar (Test hariç)
        var merchants = (await c.QueryAsync(
            "SELECT id, name FROM merchantUser WHERE status=1 AND name NOT LIKE '%Test%' ORDER BY id")).ToList();
        if (merchants.Count == 0) return;

        // Onaylı yatırım/çekim — kaynak (added_type) kırılımı, finalize_date bazlı
        var aggRows = await c.QueryAsync(@"
            SELECT firm_id,
              COALESCE(SUM(CASE WHEN type=1 AND added_type=1 THEN amount END),0) AS yat_api,
              COALESCE(SUM(CASE WHEN type=1 AND added_type=2 THEN amount END),0) AS yat_man,
              COALESCE(SUM(CASE WHEN type=2 AND added_type=1 THEN amount END),0) AS cek_api,
              COALESCE(SUM(CASE WHEN type=2 AND added_type=2 THEN amount END),0) AS cek_man,
              COALESCE(SUM(CASE WHEN type=2 AND added_type=2 THEN 1 END),0)      AS cek_man_adet
            FROM invest WHERE status=3 AND DATE(finalize_date)=@date GROUP BY firm_id", new { date });
        // Reddedilen çekim — created_at bazlı
        var rejRows = await c.QueryAsync(@"
            SELECT firm_id, COALESCE(SUM(amount),0) AS red, COUNT(*) AS red_adet
            FROM invest WHERE type=2 AND status=4 AND DATE(created_at)=@date GROUP BY firm_id", new { date });

        var agg = aggRows.ToDictionary(r => Convert.ToInt32((object)r.firm_id));
        var rej = rejRows.ToDictionary(r => Convert.ToInt32((object)r.firm_id));

        var sb = new StringBuilder();
        sb.Append($"📊 <b>GÜNLÜK MUTABAKAT — {FormatDate(date)}</b>\n");

        double totYat = 0, totCek = 0; int shown = 0;
        foreach (var m in merchants)
        {
            int mid = Convert.ToInt32((object)m.id);
            agg.TryGetValue(mid, out var a);
            rej.TryGetValue(mid, out var rj);

            double yatApi = a is null ? 0 : Convert.ToDouble((object)a.yat_api);
            double yatMan = a is null ? 0 : Convert.ToDouble((object)a.yat_man);
            double cekApi = a is null ? 0 : Convert.ToDouble((object)a.cek_api);
            double cekMan = a is null ? 0 : Convert.ToDouble((object)a.cek_man);
            int cekManAdet = a is null ? 0 : Convert.ToInt32((object)a.cek_man_adet);
            double red = rj is null ? 0 : Convert.ToDouble((object)rj.red);
            int redAdet = rj is null ? 0 : Convert.ToInt32((object)rj.red_adet);

            double yatT = yatApi + yatMan, cekT = cekApi + cekMan;
            if (yatT == 0 && cekT == 0 && red == 0) continue; // hareketsiz → atla

            shown++; totYat += yatT; totCek += cekT;
            sb.Append($"\n🏬 <b>{Esc((string)m.name)}</b>\n");
            sb.Append($"   Yatırım: <b>{M(yatT)}</b>  (API {M(yatApi)} · Manuel {(yatMan > 0 ? M(yatMan) : "—")})\n");
            sb.Append($"   Çekim:   <b>{M(cekT)}</b>  (API {M(cekApi)} · Manuel {(cekMan > 0 ? M(cekMan) : "—")})\n");
            if (cekMan > 0)
                sb.Append($"   ⚠️ Manuel çekim: {cekManAdet} adet / {M(cekMan)}\n");
            sb.Append($"   Reddedilen çekim: {(red > 0 ? $"{M(red)} ({redAdet} adet)" : "—")}\n");
        }

        if (shown == 0) sb.Append("\n(Bu güne ait hareket yok.)\n");
        else sb.Append($"\n──────────────\n<b>TOPLAM</b>  Yatırım {M(totYat)} · Çekim {M(totCek)}");

        await _telegram.SendTextAsync(chatId, sb.ToString(), "HTML", ct: ct);
    }

    private static string M(double v) => "₺" + Math.Round(v).ToString("#,##0", Tr);

    private static string FormatDate(string date) =>
        DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd.MM.yyyy") : date;

    private static string Esc(string? s) => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
