<?php

namespace App\Services;

use App\Services\TelegramService;
use Illuminate\Support\Collection;
use Illuminate\Support\Facades\DB;

/**
 * Merchant API IBAN seçim mantığı — eski Paylira API'sinin `Api_model::getBank` SQL'inin Laravel karşılığı.
 *
 * Kurallar (eski sistemden birebir):
 *   1. bankAccounts.status = 1
 *   2. teams.status = 1
 *   3. bankAccounts.min_invest <= amount <= bankAccounts.max_invest
 *   4. teams.min_invest <= amount <= teams.max_invest
 *   5. Takım kapasitesi: teams.wait_limit >= o takıma ait pending (status IN 1,2) invest sayısı
 *   6. Günlük adet limiti: bankAccounts.daily_count_limit = 0 (sınırsız) veya bugünkü (status 1/2/3) sayı limitten az
 *   7. Günlük toplam tutar limiti: bankAccounts.max_amount = 0 (sınırsız) veya bugünkü (status 1/2/3) toplam tutar limitten az
 *   8. Opsiyonel forced team (config/merchant_overrides.php'den)
 *   9. Sıralama: önce bankAccounts.sort_order, eşitlikte inRandomOrder()
 */
class MerchantBankService
{
    public function availableForAmount(float $amount, int $merchantId, ?int $forcedTeamId = null): Collection
    {
        $forcedTeam = $forcedTeamId ?? config("merchant_overrides.{$merchantId}.forced_team_id");

        $query = DB::table('bankAccounts')
            ->join('banks', 'bankAccounts.bank_id', '=', 'banks.id')
            ->join('teams', 'teams.id', '=', 'bankAccounts.team_id')
            ->where('bankAccounts.status', 1)
            ->where('teams.status', 1)
            ->where('bankAccounts.min_invest', '<=', $amount)
            ->where('bankAccounts.max_invest', '>=', $amount)
            ->where('teams.min_invest', '<=', $amount)
            ->where('teams.max_invest', '>=', $amount)
            ->whereRaw('teams.wait_limit >= (SELECT COUNT(*) FROM invest WHERE status IN (1,2) AND team_id = teams.id)')
            // Günlük adet limiti: 0 = sınırsız. Limit > 0 ve bugünün (status 1/2/3) sayısı limiti aşmışsa hariç tut.
            // DATE() yerine range filter — (bank_id, status, created_at) index'i kullanılabilsin diye
            ->whereRaw('(bankAccounts.daily_count_limit = 0 OR bankAccounts.daily_count_limit > (
                SELECT COUNT(*) FROM invest
                WHERE invest.bank_id = bankAccounts.id
                  AND invest.status IN ("1","2","3")
                  AND invest.created_at >= ? AND invest.created_at < ?
            ))', [$todayStart = now()->startOfDay()->toDateTimeString(), now()->startOfDay()->addDay()->toDateTimeString()])
            // Günlük toplam tutar limiti (max_amount): 0 = sınırsız. Limit > 0 ve bugünün toplamı limiti aşmışsa hariç tut.
            ->whereRaw('(bankAccounts.max_amount = 0 OR bankAccounts.max_amount > (
                SELECT COALESCE(SUM(amount), 0) FROM invest
                WHERE invest.bank_id = bankAccounts.id
                  AND invest.status IN ("1","2","3")
                  AND invest.created_at >= ? AND invest.created_at < ?
            ))', [$todayStart, now()->startOfDay()->addDay()->toDateTimeString()]);

        if ($forcedTeam) {
            $query->where('bankAccounts.team_id', (int) $forcedTeam);
        }

        $accounts = $query
            ->select(
                'bankAccounts.id',
                'bankAccounts.account_holder',
                'bankAccounts.account_iban',
                'bankAccounts.team_id',
                'bankAccounts.sort_order',
                'banks.name as bank_name',
            )
            ->orderBy('bankAccounts.sort_order')   // önce öneri sırası
            ->inRandomOrder()                        // tie-breaker
            ->get();

        // Anlık kasa + bu invest tutarı maxCase'i aşacaksa o takımı filtrele (block_when_full=1 ise)
        $teamIds = $accounts->pluck('team_id')->unique()->values()->all();
        $blockedTeamIds = $this->teamsAtFullCase($teamIds, $amount);
        $filtered = $accounts->reject(fn ($a) => in_array((int) $a->team_id, $blockedTeamIds, true))->values();

        if ($filtered->isEmpty()) return $filtered;

        // Round-robin: aynı sort_order grubu içinde her takımdan sırayla bir IBAN
        // Takım sırası: bugünkü invest sayısı az olan takım önce (en az kullanılan).
        // whereDate yerine range — (team_id, status) index'i kullanılabilsin diye
        $teamTodayCount = DB::table('invest')
            ->whereIn('team_id', $filtered->pluck('team_id')->unique()->values()->all())
            ->whereIn('status', ['1', '2', '3'])
            ->where('created_at', '>=', now()->startOfDay())
            ->where('created_at', '<', now()->startOfDay()->addDay())
            ->groupBy('team_id')
            ->select('team_id', DB::raw('COUNT(*) AS c'))
            ->pluck('c', 'team_id');

        return $filtered
            ->groupBy('sort_order')
            ->flatMap(function ($group) use ($teamTodayCount) {
                $byTeam = $group->groupBy('team_id')
                    ->sortBy(fn ($_, $teamId) => (int) ($teamTodayCount[$teamId] ?? 0))
                    ->map(fn ($ibans) => $ibans->values()->all());

                $result = [];
                $teamIds = array_keys($byTeam->all());
                $pointers = array_fill_keys($teamIds, 0);
                $totalRemaining = array_sum(array_map(fn ($a) => count($a), $byTeam->all()));

                while ($totalRemaining > 0) {
                    foreach ($teamIds as $tid) {
                        $list = $byTeam[$tid];
                        if ($pointers[$tid] < count($list)) {
                            $result[] = $list[$pointers[$tid]];
                            $pointers[$tid]++;
                            $totalRemaining--;
                        }
                    }
                }
                return collect($result);
            })
            ->values();
    }

    /**
     * Verilen team_id'lerden block_when_full=1 olup anlık kasası maxCase'e ulaşmış olanları döner.
     * Anlık kasa = TeamCaseController'daki current_case formülü:
     *   son snapshot (yoksa overturn) + bugünkü onaylı yatırımlar*(1-komisyon)
     *   - bugünkü onaylı çekimler - team_payments - paylira_expenses
     *   - partner_payments(type=3) - intermediary_payments(type=3) - team_transfers OUT + team_transfers IN
     */
    /**
     * Verilen team_id'ler için anlık kasa değerlerini hesaplar.
     * Snapshot formülüyle birebir aynı: Devir + Yatırım − Çekim − Komisyon − Manuel Ödeme − Giderler ± Transferler − Sync.
     * @return array<int, float> team_id => current_cash
     */
    public function currentCashForTeams(array $teamIds): array
    {
        if (empty($teamIds)) return [];

        $today = now()->toDateString();
        $todayStart = now()->startOfDay()->toDateTimeString();
        $tomorrowStart = now()->startOfDay()->addDay()->toDateTimeString();

        $teams = DB::table('teams')->whereIn('id', $teamIds)->select('id', 'overturn', 'commission')->get();
        if ($teams->isEmpty()) return [];

        $investRows = DB::table('invest')->whereIn('team_id', $teamIds)
            ->where('status', '3')->where('finalize_date', '>=', $todayStart)->where('finalize_date', '<', $tomorrowStart)
            ->groupBy('team_id', 'type')->select('team_id', 'type', DB::raw('COALESCE(SUM(amount), 0) AS total'))->get();
        $deposits = []; $withdrawals = [];
        foreach ($investRows as $r) {
            if ($r->type == '1') $deposits[$r->team_id] = (float) $r->total;
            elseif ($r->type == '2') $withdrawals[$r->team_id] = (float) $r->total;
        }

        $sumByTeam = fn ($table, $col) => DB::table($table)->whereIn($col, $teamIds)
            ->where('created_at', '>=', $todayStart)->where('created_at', '<', $tomorrowStart)
            ->groupBy($col)->select($col, DB::raw('COALESCE(SUM(amount), 0) AS total'))->pluck('total', $col);

        $teamPayments = $sumByTeam('team_payments', 'team_id');
        $expenses = $sumByTeam('paylira_expenses', 'team_id');
        $syncs = $sumByTeam('team_syncs', 'team_id');
        $partnerPay = DB::table('paylira_partner_payments')->whereIn('team_id', $teamIds)
            ->where('payment_type', '3')->where('created_at', '>=', $todayStart)->where('created_at', '<', $tomorrowStart)
            ->groupBy('team_id')->select('team_id', DB::raw('COALESCE(SUM(amount), 0) AS total'))->pluck('total', 'team_id');
        $interPay = DB::table('intermediary_payments')->whereIn('team_id', $teamIds)
            ->where('payment_type', '3')->where('created_at', '>=', $todayStart)->where('created_at', '<', $tomorrowStart)
            ->groupBy('team_id')->select('team_id', DB::raw('COALESCE(SUM(amount), 0) AS total'))->pluck('total', 'team_id');
        $transferOut = DB::table('team_transfers')->whereIn('from_team_id', $teamIds)
            ->where('created_at', '>=', $todayStart)->where('created_at', '<', $tomorrowStart)
            ->groupBy('from_team_id')->select('from_team_id as team_id', DB::raw('COALESCE(SUM(amount), 0) AS total'))->pluck('total', 'team_id');
        $transferIn = DB::table('team_transfers')->whereIn('to_team_id', $teamIds)
            ->where('created_at', '>=', $todayStart)->where('created_at', '<', $tomorrowStart)
            ->groupBy('to_team_id')->select('to_team_id as team_id', DB::raw('COALESCE(SUM(amount), 0) AS total'))->pluck('total', 'team_id');

        $snapshots = DB::table('daily_case_snapshots as s')
            ->where('entity_type', 'team')->whereIn('entity_id', $teamIds)->where('snapshot_date', '<', $today)
            ->whereRaw('snapshot_date = (SELECT MAX(snapshot_date) FROM daily_case_snapshots WHERE entity_type = "team" AND entity_id = s.entity_id AND snapshot_date < ?)', [$today])
            ->pluck('amount', 'entity_id');

        $result = [];
        foreach ($teams as $t) {
            $lastSnap = (float) ($snapshots[$t->id] ?? $t->overturn);
            $dep = (float) ($deposits[$t->id] ?? 0);
            $wd = (float) ($withdrawals[$t->id] ?? 0);
            $teamComm = $dep * ((float) $t->commission) / 100;
            $result[(int) $t->id] = $lastSnap + $dep - $teamComm - $wd
                - (float) ($teamPayments[$t->id] ?? 0)
                - (float) ($expenses[$t->id] ?? 0)
                - (float) ($partnerPay[$t->id] ?? 0)
                - (float) ($interPay[$t->id] ?? 0)
                - (float) ($transferOut[$t->id] ?? 0)
                + (float) ($transferIn[$t->id] ?? 0)
                - (float) ($syncs[$t->id] ?? 0);
        }
        return $result;
    }

    private function teamsAtFullCase(array $teamIds, float $amount = 0): array
    {
        if (empty($teamIds)) return [];

        $teams = DB::table('teams')
            ->whereIn('id', $teamIds)
            ->where('block_when_full', 1)
            ->where('maxCase', '!=', 0)
            ->select('id', 'maxCase')
            ->get();

        if ($teams->isEmpty()) return [];

        $cashes = $this->currentCashForTeams($teams->pluck('id')->all());

        $blocked = [];
        foreach ($teams as $t) {
            $current = (float) ($cashes[(int) $t->id] ?? 0);
            $maxCase = (float) $t->maxCase;
            // Mevcut anlık kasa zaten doluysa VEYA bu invest tutar eklenince maxCase'i aşıyorsa engelle
            $wouldOverflow = ($current + $amount) > $maxCase || $current >= $maxCase;
            if ($wouldOverflow) {
                $blocked[] = (int) $t->id;
            }
        }
        return $blocked;
    }

    /**
     * Verilen takımlardan maxCase'ini geçenleri pasife alır (status=2) + sistem chat'e bildirim.
     * Edge-trigger: bir kez pasifleştirilen takım, kasa eşik altına düşene kadar tekrar müdahale edilmez.
     * Hem cron'dan hem invest onayı sonrasında çağırılabilir.
     * @return array<int> bu çağrıda yeni pasifleştirilen takım id'leri
     */
    public function enforceMaxCase(array $teamIds): array
    {
        if (empty($teamIds)) return [];

        // status filter YOK: state=1 takım manuel aktif edilmiş olabilir, yine takip etmeli
        $teams = DB::table('teams')
            ->whereIn('id', $teamIds)
            ->where('block_when_full', 1)
            ->where('maxCase', '!=', 0)
            ->select('id', 'name', 'status', 'maxCase', 'telegram_max_case_state')
            ->get();

        if ($teams->isEmpty()) return [];

        $cashes = $this->currentCashForTeams($teams->pluck('id')->all());
        $systemChatId = DB::table('system_settings')->where('key', 'telegram_chat_id')->value('value');

        $pasifIds = [];
        foreach ($teams as $t) {
            $current = (float) ($cashes[(int) $t->id] ?? 0);
            $maxCase = (float) $t->maxCase;
            $isFull = $current >= $maxCase;
            $state = (int) $t->telegram_max_case_state;

            if ($isFull && $state === 0) {
                // Yeni pasifleştirme: status=2 + state=1 + mesaj
                DB::table('teams')->where('id', $t->id)->update([
                    'status'                  => 2,
                    'telegram_max_case_state' => 1,
                ]);
                if ($systemChatId) {
                    $msg = "🛑 *MAKS KASAYA ULAŞTI* — `" . TelegramService::escape($t->name) . "`\n"
                         . "*Takım Kasası:* " . TelegramService::escape(number_format($current, 2, ',', '.')) . " TL\n\n"
                         . "_Pasife alındı\\. Kasa düştüğünde manuel aktif etmeyi unutmayın\\!_";
                    TelegramService::send($systemChatId, $msg);
                }
                $pasifIds[] = (int) $t->id;
            } elseif (! $isFull && $state === 1) {
                // Kasa eşik altına düştü: state'i sıfırla (manuel aktif sonrası yeniden tetiklenebilir hale gelir)
                DB::table('teams')->where('id', $t->id)->update(['telegram_max_case_state' => 0]);
            }
        }

        return $pasifIds;
    }

    public function validate(int $bankId, float $amount, int $merchantId): ?object
    {
        return $this->availableForAmount($amount, $merchantId)
            ->firstWhere('id', $bankId);
    }

    /**
     * Amount filter olmadan tüm uygun (üyelere gösterilebilir) IBAN'ları döner.
     * Dashboard "kullanılabilir IBAN" göstergesi için kullanılır.
     */
    public function eligibleIbans(): Collection
    {
        $todayStart = now()->startOfDay()->toDateTimeString();
        $tomorrowStart = now()->startOfDay()->addDay()->toDateTimeString();

        // Bugünün bank bazlı toplam sayımı ve tutarı — tek aggregated query
        $todayStats = DB::table('invest')
            ->whereIn('status', ['1', '2', '3'])
            ->where('created_at', '>=', $todayStart)
            ->where('created_at', '<', $tomorrowStart)
            ->whereNotNull('bank_id')
            ->groupBy('bank_id')
            ->select('bank_id', DB::raw('COUNT(*) AS today_count'), DB::raw('COALESCE(SUM(amount), 0) AS today_sum'))
            ->get()
            ->keyBy('bank_id');

        // Takım bazlı in-flight (status 1,2) sayım — tek aggregated query
        $teamInFlight = DB::table('invest')
            ->whereIn('status', ['1', '2'])
            ->whereNotNull('team_id')
            ->groupBy('team_id')
            ->select('team_id', DB::raw('COUNT(*) AS in_flight'))
            ->get()
            ->keyBy('team_id');

        $accounts = DB::table('bankAccounts')
            ->join('banks', 'bankAccounts.bank_id', '=', 'banks.id')
            ->join('teams', 'teams.id', '=', 'bankAccounts.team_id')
            ->where('bankAccounts.status', 1)
            ->where('teams.status', 1)
            ->select(
                'bankAccounts.id',
                'bankAccounts.team_id',
                'bankAccounts.min_invest',
                'bankAccounts.max_invest',
                'bankAccounts.max_amount',
                'bankAccounts.daily_count_limit',
                'teams.min_invest as team_min',
                'teams.max_invest as team_max',
                'teams.wait_limit as team_wait_limit',
            )
            ->get()
            ->filter(function ($a) use ($todayStats, $teamInFlight) {
                $inFlight = (int) ($teamInFlight[$a->team_id]->in_flight ?? 0);
                if ((int) $a->team_wait_limit < $inFlight) return false;

                $todayCount = (int) ($todayStats[$a->id]->today_count ?? 0);
                $todaySum = (float) ($todayStats[$a->id]->today_sum ?? 0);

                if ((int) $a->daily_count_limit > 0 && $todayCount >= (int) $a->daily_count_limit) return false;
                if ((float) $a->max_amount > 0 && $todaySum >= (float) $a->max_amount) return false;

                return true;
            })
            ->values();

        $teamIds = $accounts->pluck('team_id')->unique()->values()->all();
        $blockedTeamIds = $this->teamsAtFullCase($teamIds);

        return $accounts->reject(fn ($a) => in_array((int) $a->team_id, $blockedTeamIds, true))->values();
    }

    public function pickOne(float $amount, int $merchantId, ?int $forcedTeamId = null): ?object
    {
        return $this->availableForAmount($amount, $merchantId, $forcedTeamId)->first();
    }

    /**
     * PayRoute grubuna "kullanılabilir IBAN yok" uyarısı gönderir.
     * 5 dk throttle: system_settings.payroute_no_iban_last_alert_at.
     */
    public static function alertNoIbanAvailable(
        int $merchantId,
        float $amount,
        ?int $investId = null,
        ?string $playerId = null,
        ?string $orderId = null,
        ?string $playerName = null,
    ): void {
        $chatId = config('services.telegram.payroute_chat_id');
        if (! $chatId) return;

        $last = DB::table('system_settings')->where('key', 'payroute_no_iban_last_alert_at')->value('value');
        if ($last && now()->diffInSeconds($last) < 300) return;

        $merchant = DB::table('merchantUser')->where('id', $merchantId)->value('name') ?? '?';
        $fmtAmt = '₺' . number_format($amount, 0, ',', '.');

        $msg = "🚫 *KULLANILABİLİR IBAN YOK*\n"
             . "*Merchant:* " . TelegramService::escape($merchant) . "\n"
             . "*Tutar:* " . TelegramService::escape($fmtAmt) . "\n"
             . "*Oyuncu:* " . TelegramService::escape(($playerName ?: '-') . ' (' . ($playerId ?: '-') . ')') . "\n"
             . ($orderId ? "*Order ID:* `" . TelegramService::escape($orderId) . "`\n" : '')
             . "*Zaman:* " . TelegramService::escape(now()->format('d.m.Y H:i:s'));

        if (TelegramService::send($chatId, $msg)) {
            DB::table('system_settings')->updateOrInsert(
                ['key' => 'payroute_no_iban_last_alert_at'],
                ['value' => now()->toDateTimeString(), 'updated_at' => now()]
            );
        }
    }
}
