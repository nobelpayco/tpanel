<?php

namespace App\Console\Commands;

use App\Services\MerchantBankService;
use App\Services\TelegramService;
use App\Support\TrustScore;
use Illuminate\Console\Command;
use Illuminate\Support\Facades\DB;

class CheckPendingNotifications extends Command
{
    protected $signature = 'telegram:check-pending';
    protected $description = 'Bekleyen yatırımlar için Telegram uyarıları gönder';

    public function handle(): int
    {
        // Hesap eksikliği uyarısı (dakikada 1 kez, döngü dışında)
        $this->checkAccountAvailability();

        // Cron dakikada bir tetikler — 6 kez kontrol et (~10 sn aralıklarla)
        $totalUnfinalized = 0;
        $totalMissingReceipt = 0;

        for ($i = 0; $i < 6; $i++) {
            [$f, $mr] = $this->runCheck();
            $totalUnfinalized += $f;
            $totalMissingReceipt += $mr;
            if ($i < 5) sleep(10);
        }

        // Kredi azaldı: tek kez yeterli (state-based, edge-trigger)
        $creditLow = $this->checkCreditLow();
        $maxCaseBlocked = $this->checkMaxCase();

        $this->info("Telegram check tamamlandı: {$totalUnfinalized} sonuçlandırılmadı, {$totalMissingReceipt} dekont yok, {$creditLow} kredi azaldı, {$maxCaseBlocked} maks kasa pasifleştirildi.");
        return self::SUCCESS;
    }

    private function checkAccountAvailability(): void
    {
        $chatId = config('services.telegram.payroute_chat_id');
        if (! $chatId) return;

        $settings = DB::table('system_settings')->pluck('value', 'key');

        // Aktif/pasif kontrolü
        if ((int) ($settings['payroute_alert_enabled'] ?? 1) !== 1) return;

        $threshold = (float) ($settings['min_notify_amount'] ?? 0);
        if ($threshold <= 0) return;

        // 5 dk throttle: son uyarıdan 5 dk geçmediyse atlat
        $lastAlertAt = $settings['payroute_last_alert_at'] ?? null;
        if ($lastAlertAt && now()->diffInSeconds($lastAlertAt) < 300) return;

        // Aktif takımların aktif hesapları
        $base = DB::table('bankAccounts')
            ->join('teams', 'bankAccounts.team_id', '=', 'teams.id')
            ->where('bankAccounts.status', 1)
            ->where('teams.status', '!=', 0);

        $totalActive = (clone $base)->count();
        $eligible = (clone $base)
            ->where('bankAccounts.min_invest', '<=', $threshold)
            ->where('bankAccounts.max_invest', '>=', $threshold)
            ->count();

        if ($eligible > 0) return;

        $tFmt = TelegramService::escape('₺' . number_format($threshold, 0, ',', '.'));

        if ($totalActive === 0) {
            $msg = "🚨🚨🚨 *DİKKAT* 🚨🚨🚨\n\n"
                 . "*SİSTEMDE HİÇ AKTİF HESAP YOK\\!*\n\n"
                 . "_Yatırım alınmıyor\\._";
        } else {
            $minMin = (float) (clone $base)->min('bankAccounts.min_invest');
            $minFmt = TelegramService::escape('₺' . number_format($minMin, 0, ',', '.'));
            $msg = "🚨🚨🚨 *DİKKAT* 🚨🚨🚨\n\n"
                 . "*Sistemde {$tFmt} tutarında hesap yok\\!*\n"
                 . "_Yatırım alınmıyor\\._\n\n"
                 . "*Minimum Tutarlı Hesap:* {$minFmt}";
        }

        if (TelegramService::send($chatId, $msg)) {
            DB::table('system_settings')->updateOrInsert(
                ['key' => 'payroute_last_alert_at'],
                ['value' => now()->toDateTimeString(), 'updated_at' => now()]
            );
        }
    }

    private function runCheck(): array
    {

        // İşlem agent tarafından "üzerine alındıysa" process_date set olur — onun üzerinden 630sn ölç
        // Agent üzerine almadan direkt sonuçlandırıyorsa process_date NULL — created_at üzerinden ölç
        $threshold = now()->subSeconds(630);
        $unfinalized = DB::table('invest')
            ->join('teams', 'invest.team_id', '=', 'teams.id')
            ->leftJoin('users as agent', 'invest.agent_id', '=', 'agent.id')
            ->leftJoin('telegram_notifications as tn', function ($j) {
                $j->on('tn.invest_id', '=', 'invest.id')->where('tn.type', 'unfinalized');
            })
            ->where('invest.type', '1')
            ->whereIn('invest.status', ['1', '2'])
            ->where('teams.telegram_enabled', 1)
            ->where('teams.telegram_pending_invest_enabled', 1)
            ->whereNotNull('teams.telegram_chat_id')
            ->whereNotNull('teams.telegram_pending_invest_enabled_at')
            ->whereColumn('invest.created_at', '>=', 'teams.telegram_pending_invest_enabled_at')
            ->whereNull('tn.id')
            ->where(function ($q) use ($threshold) {
                $q->where(function ($qq) use ($threshold) {
                    $qq->whereNotNull('invest.process_date')
                       ->where('invest.process_date', '<=', $threshold);
                })->orWhere(function ($qq) use ($threshold) {
                    $qq->whereNull('invest.process_date')
                       ->where('invest.created_at', '<=', $threshold);
                });
            })
            ->select(
                'invest.id', 'invest.order_id', 'invest.player_id', 'invest.amount', 'invest.name', 'invest.process_date',
                'teams.telegram_chat_id', 'teams.name as team_name', 'agent.name as agent_name'
            )
            ->get();

        foreach ($unfinalized as $row) {
            $orderId = $row->order_id ?: $row->id;
            [$rate, $cnt] = TrustScore::calculate($row->player_id, $row->id);
            $score = $rate === null ? '—' : "%{$rate} ({$cnt} işlem)";

            $msg = "⏰ *SONUÇLANDIRILMADI* — `#" . TelegramService::escape((string) $orderId) . "`\n"
                 . "*Takım:* " . TelegramService::escape($row->team_name) . "\n"
                 . "*Tutar:* " . TelegramService::escape('₺' . number_format((float) $row->amount, 0, ',', '.')) . "\n"
                 . "*Üye:* " . TelegramService::escape($row->name ?: '-') . "\n"
                 . "*Agent:* " . TelegramService::escape($row->agent_name ?: '-') . "\n"
                 . "*Üye Skoru:* " . TelegramService::escape($score) . "\n"
                 . "_10 dakika 30 saniyedir işlemde, sonuçlandırılması bekleniyor\\._";

            if (TelegramService::send($row->telegram_chat_id, $msg)) {
                DB::table('telegram_notifications')->insertOrIgnore([
                    'invest_id' => $row->id,
                    'type'      => 'unfinalized',
                    'sent_at'   => now(),
                ]);
            }
        }

        // Dekont yüklenmedi — onaylı (status=3) ama 10dk geçmiş ve invest_receipts boş
        $missingReceipts = DB::table('invest')
            ->join('teams', 'invest.team_id', '=', 'teams.id')
            ->leftJoin('telegram_notifications as tn', function ($j) {
                $j->on('tn.invest_id', '=', 'invest.id')->where('tn.type', 'missing_receipt');
            })
            ->where('invest.type', '2')
            ->where('invest.status', '3')
            ->where('teams.telegram_enabled', 1)
            ->where('teams.telegram_missing_receipt_enabled', 1)
            ->whereNotNull('teams.telegram_withdraw_chat_id')
            ->whereNotNull('teams.telegram_missing_receipt_enabled_at')
            ->whereColumn('invest.finalize_date', '>=', 'teams.telegram_missing_receipt_enabled_at')
            ->whereNull('tn.id')
            ->where('invest.finalize_date', '<=', now()->subMinutes(15))
            ->whereRaw('NOT EXISTS (SELECT 1 FROM invest_receipts WHERE invest_receipts.invest_id = invest.id)')
            ->select(
                'invest.id', 'invest.order_id',
                'teams.telegram_withdraw_chat_id'
            )
            ->get();

        foreach ($missingReceipts as $row) {
            $orderId = $row->order_id ?: $row->id;
            $msg = "⏰ *DEKONT YÜKLENMEDİ* — `#" . TelegramService::escape((string) $orderId) . "`\n"
                 . "_15 dakikadır dekont yüklenmedi\\. Lütfen dekont yükleyin\\!_";

            if (TelegramService::send($row->telegram_withdraw_chat_id, $msg)) {
                DB::table('telegram_notifications')->insertOrIgnore([
                    'invest_id' => $row->id,
                    'type'      => 'missing_receipt',
                    'sent_at'   => now(),
                ]);
            }
        }

        return [$unfinalized->count(), $missingReceipts->count()];
    }

    private function checkCreditLow(): int
    {
        $teams = DB::table('teams')
            ->where('telegram_enabled', 1)
            ->where('telegram_credit_low_enabled', 1)
            ->whereNotNull('telegram_chat_id')
            ->whereNotNull('telegram_credit_low_threshold')
            ->where('telegram_credit_low_threshold', '>', 0)
            ->where('maxCase', '!=', 0)
            ->where('status', 1)
            ->select('id', 'name', 'maxCase', 'telegram_chat_id', 'telegram_credit_low_threshold', 'telegram_credit_low_state')
            ->get();

        if ($teams->isEmpty()) return 0;

        $cashes = app(MerchantBankService::class)->currentCashForTeams($teams->pluck('id')->all());

        $alertCount = 0;
        foreach ($teams as $t) {
            $current = (float) ($cashes[(int) $t->id] ?? 0);
            $threshold = (float) $t->telegram_credit_low_threshold;
            $maxCase = (float) $t->maxCase;
            // current maxCase'e yaklaşıyorsa (veya geçtiyse) LOW: current >= maxCase - threshold
            $isLow = $current >= ($maxCase - $threshold);
            $state = (int) $t->telegram_credit_low_state;

            if ($isLow && $state === 0) {
                $msg = "⚠️ *KREDİ AZALDI* — `" . TelegramService::escape($t->name) . "`\n"
                     . "*Kasanız:* " . TelegramService::escape('₺' . number_format($current, 2, ',', '.')) . " TRY";
                $sent = TelegramService::send($t->telegram_chat_id, $msg);
                // Sistem ayarlarındaki chat'e de gönder
                $systemChatId = DB::table('system_settings')->where('key', 'telegram_chat_id')->value('value');
                if ($systemChatId && $systemChatId !== $t->telegram_chat_id) {
                    TelegramService::send($systemChatId, $msg);
                }
                if ($sent) {
                    DB::table('teams')->where('id', $t->id)->update(['telegram_credit_low_state' => 1]);
                    $alertCount++;
                }
            } elseif (! $isLow && $state === 1) {
                DB::table('teams')->where('id', $t->id)->update(['telegram_credit_low_state' => 0]);
            }
        }

        return $alertCount;
    }

    private function checkMaxCase(): int
    {
        $teamIds = DB::table('teams')
            ->where('block_when_full', 1)
            ->where('status', 1)
            ->where('maxCase', '!=', 0)
            ->pluck('id')->all();

        return count(app(MerchantBankService::class)->enforceMaxCase($teamIds));
    }
}
