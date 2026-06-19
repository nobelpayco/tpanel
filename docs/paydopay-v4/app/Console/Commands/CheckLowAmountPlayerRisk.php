<?php

namespace App\Console\Commands;

use App\Services\TelegramService;
use Illuminate\Console\Command;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Log;

/**
 * Her dakika çalışır. Yeni onaylanan / reddedilen yatırımları cursor üzerinden tarar.
 *
 * Şüpheli oyuncu aktivitesi tek kural ile yakalanır (pencere: son 10 yatırım):
 *   - Approved >= 1 AND max(approved.amount) < 1000      → "küçük tutarlı bot şüphesi"
 *   - Approved = 0  AND rejected >= 2                    → "sürekli red bot şüphesi"
 *
 * Karma vakalar (örn. 2 onaylı küçük + 8 red) ilk kural ile yakalanır.
 *
 * Sistem chat'ine (TELEGRAM_PAYROUTE_CHAT_ID) inline button'lı (Engelle/Vazgeç) bildirim atar.
 * Callback'ler TelegramWebhookController::handleCallbackQuery() tarafında işlenir.
 */
class CheckLowAmountPlayerRisk extends Command
{
    protected $signature = 'risk:check-low-amount';
    protected $description = 'Şüpheli oyuncu aktivitesi (düşük tutarlı veya sürekli red) için sistem chat\'ine inline button\'lı bildirim';

    private const BATCH_LIMIT = 200;
    private const SPAM_GUARD_MINUTES = 5;
    private const AMOUNT_THRESHOLD = 1000.0;
    private const MIN_COUNT = 3;
    private const WINDOW_SIZE = 10;

    public function handle(): int
    {
        $chatId = config('services.telegram.payroute_chat_id');
        if (! $chatId) {
            $this->info('TELEGRAM_PAYROUTE_CHAT_ID tanımlı değil, atlanıyor.');
            return self::SUCCESS;
        }

        $cursor = (int) DB::table('system_settings')->where('key', 'risk_check_last_invest_id')->value('value');

        // Hem onay hem red event'lerini takip (cursor invest.id ilerletir)
        $events = DB::table('invest')
            ->where('id', '>', $cursor)
            ->where('type', 1)
            ->whereIn('status', [3, 4])
            ->whereNotNull('player_id')
            ->where('player_id', '!=', '')
            ->orderBy('id')
            ->limit(self::BATCH_LIMIT)
            ->get(['id', 'player_id']);

        if ($events->isEmpty()) {
            return self::SUCCESS;
        }

        $maxId = (int) $events->max('id');
        $playerIds = $events->pluck('player_id')->unique()->values()->all();

        foreach ($playerIds as $playerId) {
            try {
                $this->evaluate((string) $playerId, (string) $chatId);
            } catch (\Throwable $e) {
                Log::warning('risk:check-low-amount player error', ['player_id' => $playerId, 'error' => $e->getMessage()]);
            }
        }

        DB::table('system_settings')
            ->where('key', 'risk_check_last_invest_id')
            ->update(['value' => (string) $maxId]);

        $this->info("Processed " . count($playerIds) . " unique players up to invest #{$maxId}");
        return self::SUCCESS;
    }

    private function evaluate(string $playerId, string $chatId): void
    {
        // Blacklist'te ise atla
        if (DB::table('blacklist')->where('type', 1)->where('val', $playerId)->exists()) {
            return;
        }

        // Son 10 işlem (type=1, status IN (3,4))
        $rows = DB::table('invest')
            ->where('player_id', $playerId)
            ->where('type', 1)
            ->whereIn('status', [3, 4])
            ->orderByDesc('id')
            ->limit(self::WINDOW_SIZE)
            ->get(['id', 'amount', 'status', 'name', 'firm_id', 'finalize_date', 'created_at']);

        $total = $rows->count();
        if ($total < self::MIN_COUNT) return;

        $approvedRows = $rows->where('status', 3);
        $rejectedRows = $rows->where('status', 4);
        $approved = $approvedRows->count();
        $rejected = $rejectedRows->count();
        $maxApproved = $approved > 0 ? (float) $approvedRows->max('amount') : null;
        $totalApproved = $approved > 0 ? (float) $approvedRows->sum('amount') : 0;

        // Trigger kuralı
        $trigger =
            ($approved >= 1 && $maxApproved < self::AMOUNT_THRESHOLD)
            || ($approved === 0 && $rejected >= self::MIN_COUNT);
        if (! $trigger) return;

        $last = $rows->first(); // en yeni
        $lastTimestamp = $last->finalize_date ?: $last->created_at;
        $merchantName = DB::table('merchantUser')->where('id', $last->firm_id)->value('name') ?: '-';

        $text = $this->buildMessage($playerId, $last->name, $merchantName, $approved, $rejected, $totalApproved, $maxApproved, (int) $last->status, $lastTimestamp);

        // Mevcut pending notification var mı?
        $existing = DB::table('player_risk_notifications')
            ->where('player_id', $playerId)
            ->where('decision', 'pending')
            ->orderByDesc('id')
            ->first();

        if ($existing) {
            // Aynı invest_id ise zaten güncel — atla
            if ((int) $existing->invest_id === (int) $last->id) return;

            // Yeni veri var → mevcut mesajı edit et (yeni mesaj atma — spam koruması)
            $keyboard = [
                'inline_keyboard' => [[
                    ['text' => '🚫 Engelle', 'callback_data' => "risk_block:{$existing->id}"],
                    ['text' => '⏭️ Vazgeç',  'callback_data' => "risk_dismiss:{$existing->id}"],
                ]],
            ];
            if ($existing->message_id) {
                TelegramService::editMessageTextWithMarkup((string) $existing->chat_id, (int) $existing->message_id, $text, 'HTML', $keyboard);
            }
            DB::table('player_risk_notifications')
                ->where('id', $existing->id)
                ->update(['invest_id' => $last->id, 'notified_at' => now()]);
            return;
        }

        // Yeni bildirim
        $notifId = DB::table('player_risk_notifications')->insertGetId([
            'player_id'  => $playerId,
            'invest_id'  => $last->id,
            'chat_id'    => $chatId,
            'message_id' => null,
            'reason'     => 'suspicious_activity',
            'decision'   => 'pending',
            'notified_at'=> now(),
        ]);

        $keyboard = [
            'inline_keyboard' => [[
                ['text' => '🚫 Engelle', 'callback_data' => "risk_block:{$notifId}"],
                ['text' => '⏭️ Vazgeç',  'callback_data' => "risk_dismiss:{$notifId}"],
            ]],
        ];

        $messageId = TelegramService::sendReturnId($chatId, $text, 'HTML', null, $keyboard);
        if ($messageId) {
            DB::table('player_risk_notifications')->where('id', $notifId)->update(['message_id' => $messageId]);
        } else {
            DB::table('player_risk_notifications')->where('id', $notifId)->delete();
            Log::warning('risk:check-low-amount: Telegram send failed', ['player_id' => $playerId]);
        }
    }

    private function buildMessage(string $playerId, ?string $name, string $merchant, int $approved, int $rejected, float $totalApproved, ?float $maxApproved, int $lastStatus, ?string $lastTime): string
    {
        $esc = fn($v) => htmlspecialchars((string) ($v ?? '-'), ENT_QUOTES, 'UTF-8');
        $fmt = fn($n) => number_format((float) $n, 2, ',', '.');
        $lastFmt = $lastTime ? date('d.m.Y H:i', strtotime($lastTime)) : '-';
        $lastStatusLabel = $lastStatus === 3 ? 'onaylı' : 'red';

        $approvedLine = "   ✅ Onaylı: <b>" . $approved . "</b>";
        if ($approved > 0) {
            $approvedLine .= " (toplam " . $fmt($totalApproved) . " TL, max " . $fmt($maxApproved) . " TL)";
        }

        return "🚨 <b>Şüpheli Oyuncu Aktivitesi</b>\n\n"
             . "Player ID: <code>" . $esc($playerId) . "</code>\n"
             . "Ad Soyad: <b>" . $esc($name) . "</b>\n"
             . "Mağaza Adı: " . $esc($merchant) . "\n\n"
             . "Son " . ($approved + $rejected) . " İşlem:\n"
             . $approvedLine . "\n"
             . "   ❌ Reddedilen: <b>" . $rejected . "</b>\n\n"
             . "Son işlem: " . $esc($lastFmt) . " <i>(" . $lastStatusLabel . ")</i>";
    }
}
