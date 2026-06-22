using Dapper;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.PublicApi;

namespace TPanel.Infrastructure.Services;

/// <summary>
/// Merchant API IBAN seçim mantığı — PHP MerchantBankService'in birebir Dapper karşılığı.
/// Tüm "bugün" sınırları İstanbul saatine (IClock) göre.
/// </summary>
public class MerchantBankService : IMerchantBankService
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;
    private readonly ISystemSettingService _settings;
    private readonly ITelegramService _telegram;
    private readonly PublicApiOptions _options;

    public MerchantBankService(IDbConnectionFactory factory, IClock clock, ISystemSettingService settings,
        ITelegramService telegram, PublicApiOptions options)
    {
        _factory = factory;
        _clock = clock;
        _settings = settings;
        _telegram = telegram;
        _options = options;
    }

    public async Task<IReadOnlyList<BankOption>> AvailableForAmountAsync(double amount, int merchantId, int? forcedTeamId = null, CancellationToken ct = default)
    {
        var todayStart = _clock.Today;
        var tomorrow = _clock.Today.AddDays(1);

        using var conn = await _factory.CreateOpenConnectionAsync(ct);

        var sql = @"
            SELECT bankAccounts.id, bankAccounts.account_holder, bankAccounts.account_iban,
                   bankAccounts.team_id, bankAccounts.sort_order, banks.name AS bank_name
            FROM bankAccounts
            JOIN banks ON bankAccounts.bank_id = banks.id
            JOIN teams ON teams.id = bankAccounts.team_id
            WHERE bankAccounts.status = 1
              AND teams.status = 1
              AND bankAccounts.min_invest <= @amount
              AND bankAccounts.max_invest >= @amount
              AND teams.min_invest <= @amount
              AND teams.max_invest >= @amount
              AND teams.wait_limit >= (SELECT COUNT(*) FROM invest WHERE status IN (1,2) AND team_id = teams.id)
              AND (bankAccounts.daily_count_limit = 0 OR bankAccounts.daily_count_limit > (
                    SELECT COUNT(*) FROM invest
                    WHERE invest.bank_id = bankAccounts.id AND invest.status IN ('1','2','3')
                      AND invest.created_at >= @todayStart AND invest.created_at < @tomorrow))
              AND (bankAccounts.max_amount = 0 OR bankAccounts.max_amount > (
                    SELECT COALESCE(SUM(amount), 0) FROM invest
                    WHERE invest.bank_id = bankAccounts.id AND invest.status IN ('1','2','3')
                      AND invest.created_at >= @todayStart AND invest.created_at < @tomorrow))";

        if (forcedTeamId is not null)
            sql += " AND bankAccounts.team_id = @forced";

        sql += " ORDER BY bankAccounts.sort_order, RAND()";

        var rows = (await conn.QueryAsync(sql, new { amount, todayStart, tomorrow, forced = forcedTeamId })).ToList();

        var accounts = rows.Select(r => new BankOption(
            (int)r.id, (string)r.account_holder, (string)r.account_iban,
            (int)r.team_id, (int)r.sort_order, (string)r.bank_name)).ToList();

        if (accounts.Count == 0) return accounts;

        // maxCase dolu takımları filtrele
        var teamIds = accounts.Select(a => a.TeamId).Distinct().ToList();
        var blocked = await TeamsAtFullCaseAsync(teamIds, amount, ct);
        var filtered = accounts.Where(a => !blocked.Contains(a.TeamId)).ToList();
        if (filtered.Count == 0) return filtered;

        // Round-robin: sort_order grubu içinde, bugünkü işlem sayısı az takım önce
        var teamTodayCount = await TeamTodayCountAsync(conn, filtered.Select(a => a.TeamId).Distinct().ToList(), todayStart, tomorrow);

        var result = new List<BankOption>();
        foreach (var group in filtered.GroupBy(a => a.SortOrder).OrderBy(g => g.Key))
        {
            var byTeam = group
                .GroupBy(a => a.TeamId)
                .OrderBy(g => teamTodayCount.GetValueOrDefault(g.Key, 0))
                .Select(g => g.ToList())
                .ToList();

            var pointers = new int[byTeam.Count];
            var totalRemaining = byTeam.Sum(l => l.Count);
            while (totalRemaining > 0)
            {
                for (var i = 0; i < byTeam.Count; i++)
                {
                    if (pointers[i] < byTeam[i].Count)
                    {
                        result.Add(byTeam[i][pointers[i]]);
                        pointers[i]++;
                        totalRemaining--;
                    }
                }
            }
        }

        return result;
    }

    public async Task<BankOption?> PickOneAsync(double amount, int merchantId, int? forcedTeamId = null, CancellationToken ct = default)
        => (await AvailableForAmountAsync(amount, merchantId, forcedTeamId, ct)).FirstOrDefault();

    public async Task<BankOption?> ValidateAsync(int bankId, double amount, int merchantId, CancellationToken ct = default)
        => (await AvailableForAmountAsync(amount, merchantId, ct: ct)).FirstOrDefault(b => b.Id == bankId);

    public async Task<BankOption?> PickForAssignmentAsync(double amount, int merchantId, CancellationToken ct = default)
    {
        // 1) FİLTRELER (tutar/aktiflik/kuyruk/günlük limit/maxCase) — aynen uygulanır.
        var eligible = await AvailableForAmountAsync(amount, merchantId, ct: ct);
        if (eligible.Count == 0) return null;

        // 2) Dağıtım modu (varsayılan: rotation)
        var mode = (await _settings.GetAsync("deposit_distribution_mode", ct)) ?? "rotation";
        if (!string.Equals(mode, "rotation", StringComparison.OrdinalIgnoreCase))
            return eligible[0];   // "priority" → eski davranış (sort_order önceliği)

        // 3) Uygun takımları team_id'ye göre sırala — tek takım varsa rotasyona gerek yok
        var teamsOrdered = eligible.Select(a => a.TeamId).Distinct().OrderBy(x => x).ToList();
        if (teamsOrdered.Count == 1) return eligible[0];

        // 4) Kalıcı pointer'ı atomik oku+ilerlet → last'tan sonraki UYGUN takım (döngüsel)
        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        using var tx = conn.BeginTransaction();
        var lastStr = await conn.ExecuteScalarAsync<string?>(
            "SELECT `value` FROM system_settings WHERE `key` = 'deposit_rotation_last_team' FOR UPDATE",
            transaction: tx);
        var last = int.TryParse(lastStr, out var l) ? l : 0;

        var next = teamsOrdered.FirstOrDefault(t => t > last);   // last'tan büyük ilk uygun takım
        if (next == 0) next = teamsOrdered[0];                   // yoksa başa dön (en küçük team_id)

        await conn.ExecuteAsync(
            @"INSERT INTO system_settings (`key`, `value`, updated_at)
              VALUES ('deposit_rotation_last_team', @v, @now)
              ON DUPLICATE KEY UPDATE `value` = @v, updated_at = @now",
            new { v = next.ToString(), now = _clock.Now }, tx);
        tx.Commit();

        // 5) Seçilen takımın (mevcut sıralamadaki ilk) uygun hesabını döndür
        return eligible.First(a => a.TeamId == next);
    }

    private static async Task<Dictionary<int, int>> TeamTodayCountAsync(System.Data.IDbConnection conn, List<int> teamIds, DateTime todayStart, DateTime tomorrow)
    {
        if (teamIds.Count == 0) return new();
        var rows = await conn.QueryAsync(
            @"SELECT team_id AS Id, COUNT(*) AS C FROM invest
              WHERE team_id IN @ids AND status IN ('1','2','3') AND created_at >= @todayStart AND created_at < @tomorrow
              GROUP BY team_id", new { ids = teamIds, todayStart, tomorrow });
        return rows.ToDictionary(r => (int)r.Id, r => (int)(long)r.C);
    }

    public async Task<IReadOnlyDictionary<int, double>> CurrentCashForTeamsAsync(IReadOnlyCollection<int> teamIds, CancellationToken ct = default)
    {
        var result = new Dictionary<int, double>();
        if (teamIds.Count == 0) return result;

        var todayStart = _clock.Today;
        var tomorrow = _clock.Today.AddDays(1);
        var todayDate = _clock.Today.ToString("yyyy-MM-dd");
        var ids = teamIds.ToList();

        using var conn = await _factory.CreateOpenConnectionAsync(ct);

        var teams = (await conn.QueryAsync(
            "SELECT id, overturn, commission FROM teams WHERE id IN @ids", new { ids })).ToList();
        if (teams.Count == 0) return result;

        // Onaylı yatırım/çekim (status=3, finalize_date bugün)
        var deposits = new Dictionary<int, double>();
        var withdrawals = new Dictionary<int, double>();
        var investRows = await conn.QueryAsync(
            @"SELECT team_id AS Id, type AS Type, COALESCE(SUM(amount),0) AS Total FROM invest
              WHERE team_id IN @ids AND status = '3' AND finalize_date >= @todayStart AND finalize_date < @tomorrow
              GROUP BY team_id, type", new { ids, todayStart, tomorrow });
        foreach (var r in investRows)
        {
            var tid = (int)r.Id;
            if ((string)r.Type == "1") deposits[tid] = Convert.ToDouble(r.Total);
            else if ((string)r.Type == "2") withdrawals[tid] = Convert.ToDouble(r.Total);
        }

        async Task<Dictionary<int, double>> SumByTeam(string table, string col)
        {
            var rows = await conn.QueryAsync(
                $@"SELECT {col} AS Id, COALESCE(SUM(amount),0) AS Total FROM {table}
                   WHERE {col} IN @ids AND created_at >= @todayStart AND created_at < @tomorrow GROUP BY {col}",
                new { ids, todayStart, tomorrow });
            return rows.ToDictionary(r => (int)r.Id, r => Convert.ToDouble((object)r.Total));
        }

        var teamPayments = await SumByTeam("team_payments", "team_id");
        var expenses = await SumByTeam("paylira_expenses", "team_id");
        var syncs = await SumByTeam("team_syncs", "team_id");

        async Task<Dictionary<int, double>> SumByTeamTyped(string table)
        {
            var rows = await conn.QueryAsync(
                $@"SELECT team_id AS Id, COALESCE(SUM(amount),0) AS Total FROM {table}
                   WHERE team_id IN @ids AND payment_type = '3' AND created_at >= @todayStart AND created_at < @tomorrow GROUP BY team_id",
                new { ids, todayStart, tomorrow });
            return rows.ToDictionary(r => (int)r.Id, r => Convert.ToDouble((object)r.Total));
        }

        var partnerPay = await SumByTeamTyped("paylira_partner_payments");
        var interPay = await SumByTeamTyped("intermediary_payments");

        var transferOut = (await conn.QueryAsync(
            @"SELECT from_team_id AS Id, COALESCE(SUM(amount),0) AS Total FROM team_transfers
              WHERE from_team_id IN @ids AND created_at >= @todayStart AND created_at < @tomorrow GROUP BY from_team_id",
            new { ids, todayStart, tomorrow })).ToDictionary(r => (int)r.Id, r => Convert.ToDouble((object)r.Total));
        var transferIn = (await conn.QueryAsync(
            @"SELECT to_team_id AS Id, COALESCE(SUM(amount),0) AS Total FROM team_transfers
              WHERE to_team_id IN @ids AND created_at >= @todayStart AND created_at < @tomorrow GROUP BY to_team_id",
            new { ids, todayStart, tomorrow })).ToDictionary(r => (int)r.Id, r => Convert.ToDouble((object)r.Total));

        // Son snapshot (bugünden önce, entity başına max snapshot_date)
        var snapshots = (await conn.QueryAsync(
            @"SELECT s.entity_id AS Id, s.amount AS Amount FROM daily_case_snapshots s
              WHERE s.entity_type = 'team' AND s.entity_id IN @ids AND s.snapshot_date < @today
                AND s.snapshot_date = (SELECT MAX(snapshot_date) FROM daily_case_snapshots
                    WHERE entity_type = 'team' AND entity_id = s.entity_id AND snapshot_date < @today)",
            new { ids, today = todayDate })).ToDictionary(r => (int)r.Id, r => Convert.ToDouble((object)r.Amount));

        foreach (var t in teams)
        {
            int id = (int)t.id;
            double lastSnap = snapshots.TryGetValue(id, out var snap) ? snap : Convert.ToDouble(t.overturn);
            double dep = deposits.GetValueOrDefault(id, 0);
            double wd = withdrawals.GetValueOrDefault(id, 0);
            double teamComm = dep * Convert.ToDouble(t.commission) / 100;
            result[id] = lastSnap + dep - teamComm - wd
                - teamPayments.GetValueOrDefault(id, 0)
                - expenses.GetValueOrDefault(id, 0)
                - partnerPay.GetValueOrDefault(id, 0)
                - interPay.GetValueOrDefault(id, 0)
                - transferOut.GetValueOrDefault(id, 0)
                + transferIn.GetValueOrDefault(id, 0)
                - syncs.GetValueOrDefault(id, 0);
        }
        return result;
    }

    public async Task<IReadOnlyList<EligibleIban>> EligibleIbansAsync(CancellationToken ct = default)
    {
        var todayStart = _clock.Today;
        var tomorrow = _clock.Today.AddDays(1);
        using var c = await _factory.CreateOpenConnectionAsync(ct);

        var todayStats = (await c.QueryAsync(
            @"SELECT bank_id AS Id, COUNT(*) AS Cnt, COALESCE(SUM(amount),0) AS Sm FROM invest
              WHERE status IN ('1','2','3') AND created_at >= @s AND created_at < @e AND bank_id IS NOT NULL GROUP BY bank_id",
            new { s = todayStart, e = tomorrow })).ToDictionary(r => (int)r.Id, r => ((long)r.Cnt, Convert.ToDouble((object)r.Sm)));
        var teamInFlight = (await c.QueryAsync(
            "SELECT team_id AS Id, COUNT(*) AS C FROM invest WHERE status IN ('1','2') AND team_id IS NOT NULL GROUP BY team_id"))
            .ToDictionary(r => (int)r.Id, r => (long)r.C);

        var accounts = (await c.QueryAsync<EligibleIban>(
            @"SELECT bankAccounts.id AS Id, bankAccounts.team_id AS TeamId, bankAccounts.min_invest AS MinInvest,
                     bankAccounts.max_invest AS MaxInvest, bankAccounts.max_amount AS MaxAmount, bankAccounts.daily_count_limit AS DailyCountLimit,
                     teams.min_invest AS TeamMin, teams.max_invest AS TeamMax, teams.wait_limit AS TeamWaitLimit
              FROM bankAccounts JOIN banks ON bankAccounts.bank_id=banks.id JOIN teams ON teams.id=bankAccounts.team_id
              WHERE bankAccounts.status=1 AND teams.status=1")).ToList();

        var filtered = accounts.Where(a =>
        {
            var inFlight = teamInFlight.GetValueOrDefault(a.TeamId, 0);
            if (a.TeamWaitLimit < inFlight) return false;
            var stat = todayStats.GetValueOrDefault(a.Id);
            long todayCount = stat.Item1;
            double todaySum = stat.Item2;
            if (a.DailyCountLimit > 0 && todayCount >= a.DailyCountLimit) return false;
            if (a.MaxAmount > 0 && todaySum >= a.MaxAmount) return false;
            return true;
        }).ToList();

        var blocked = await TeamsAtFullCaseAsync(filtered.Select(a => a.TeamId).Distinct().ToList(), 0, ct);
        return filtered.Where(a => !blocked.Contains(a.TeamId)).ToList();
    }

    private async Task<HashSet<int>> TeamsAtFullCaseAsync(List<int> teamIds, double amount, CancellationToken ct)
    {
        var blocked = new HashSet<int>();
        if (teamIds.Count == 0) return blocked;

        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var teams = (await conn.QueryAsync(
            "SELECT id, maxCase FROM teams WHERE id IN @ids AND block_when_full = 1 AND maxCase != 0",
            new { ids = teamIds })).ToList();
        if (teams.Count == 0) return blocked;

        var cashes = await CurrentCashForTeamsAsync(teams.Select(t => (int)t.id).ToList(), ct);
        foreach (var t in teams)
        {
            int id = (int)t.id;
            double current = cashes.GetValueOrDefault(id, 0);
            double maxCase = Convert.ToDouble(t.maxCase);
            if ((current + amount) > maxCase || current >= maxCase)
                blocked.Add(id);
        }
        return blocked;
    }

    public async Task<IReadOnlyList<int>> EnforceMaxCaseAsync(IReadOnlyCollection<int> teamIds, CancellationToken ct = default)
    {
        var pasif = new List<int>();
        if (teamIds.Count == 0) return pasif;

        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var teams = (await conn.QueryAsync(
            "SELECT id, name, status, maxCase, telegram_max_case_state FROM teams WHERE id IN @ids AND block_when_full = 1 AND maxCase != 0",
            new { ids = teamIds.ToList() })).ToList();
        if (teams.Count == 0) return pasif;

        var cashes = await CurrentCashForTeamsAsync(teams.Select(t => (int)t.id).ToList(), ct);
        var systemChatId = await _settings.GetAsync("telegram_chat_id", ct);

        foreach (var t in teams)
        {
            int id = (int)t.id;
            double current = cashes.GetValueOrDefault(id, 0);
            double maxCase = Convert.ToDouble(t.maxCase);
            bool isFull = current >= maxCase;
            int state = Convert.ToInt32(t.telegram_max_case_state);

            if (isFull && state == 0)
            {
                await conn.ExecuteAsync("UPDATE teams SET status = 2, telegram_max_case_state = 1 WHERE id = @id", new { id });
                if (!string.IsNullOrEmpty(systemChatId))
                {
                    var msg = "🛑 *MAKS KASAYA ULAŞTI* — `" + ITelegramService.Escape((string)t.name) + "`\n"
                            + "*Takım Kasası:* " + ITelegramService.Escape(current.ToString("N2")) + " TL\n\n"
                            + "_Pasife alındı\\. Kasa düştüğünde manuel aktif etmeyi unutmayın\\!_";
                    await _telegram.SendAsync(systemChatId!, msg, ct);
                }
                pasif.Add(id);
            }
            else if (!isFull && state == 1)
            {
                await conn.ExecuteAsync("UPDATE teams SET telegram_max_case_state = 0 WHERE id = @id", new { id });
            }
        }
        return pasif;
    }

    public async Task AlertNoIbanAvailableAsync(int merchantId, double amount, int? investId = null,
        string? playerId = null, string? orderId = null, string? playerName = null, CancellationToken ct = default)
    {
        var chatId = _options.PayrouteChatId;
        if (string.IsNullOrEmpty(chatId)) return;

        var last = await _settings.GetAsync("payroute_no_iban_last_alert_at", ct);
        if (!string.IsNullOrEmpty(last) && DateTime.TryParse(last, out var lastDt)
            && (_clock.Now - lastDt).TotalSeconds < 300)
            return;

        using var conn = await _factory.CreateOpenConnectionAsync(ct);
        var merchant = await conn.ExecuteScalarAsync<string?>(
            "SELECT name FROM merchantUser WHERE id = @id", new { id = merchantId }) ?? "?";

        var fmtAmt = "₺" + amount.ToString("N0");
        var msg = "🚫 *KULLANILABİLİR IBAN YOK*\n"
                + "*Merchant:* " + ITelegramService.Escape(merchant) + "\n"
                + "*Tutar:* " + ITelegramService.Escape(fmtAmt) + "\n"
                + "*Oyuncu:* " + ITelegramService.Escape((playerName ?? "-") + " (" + (playerId ?? "-") + ")") + "\n"
                + (orderId is not null ? "*Order ID:* `" + ITelegramService.Escape(orderId) + "`\n" : "")
                + "*Zaman:* " + ITelegramService.Escape(_clock.Now.ToString("dd.MM.yyyy HH:mm:ss"));

        if (await _telegram.SendAsync(chatId, msg, ct))
            await _settings.SetAsync("payroute_no_iban_last_alert_at", _clock.Now.ToString("yyyy-MM-dd HH:mm:ss"), ct);
    }
}
