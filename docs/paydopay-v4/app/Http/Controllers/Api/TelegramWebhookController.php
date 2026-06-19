<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use App\Services\TelegramService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Log;

class TelegramWebhookController extends Controller
{
    public function handle(Request $request): JsonResponse
    {
        // Secret token kontrol (Telegram her request'te bu header'ı gönderir)
        $expectedSecret = config('services.telegram.webhook_secret');
        if ($expectedSecret) {
            $received = $request->header('X-Telegram-Bot-Api-Secret-Token');
            if (! hash_equals((string) $expectedSecret, (string) $received)) {
                return response()->json(['ok' => false], 403);
            }
        }

        // Inline button callback'leri — message blok'undan önce kontrol
        $callback = $request->input('callback_query');
        if (is_array($callback)) {
            $this->handleCallbackQuery($callback);
            return response()->json(['ok' => true]);
        }

        $msg = $request->input('message') ?? $request->input('edited_message');
        if (! is_array($msg)) return response()->json(['ok' => true]);

        $chatId = $msg['chat']['id'] ?? null;
        // Medya mesajlarında metin caption field'ında, düz mesajlarda text'te
        $text = trim($msg['text'] ?? $msg['caption'] ?? '');
        $isBot = (bool) ($msg['from']['is_bot'] ?? false);
        $messageId = isset($msg['message_id']) ? (int) $msg['message_id'] : null;

        // Bot'un dahil olduğu grupları/kanal'ları kayda al (chat-ID-bul özelliği için)
        $chat = $msg['chat'] ?? [];
        $chatType = $chat['type'] ?? null;
        if ($chatId && in_array($chatType, ['group', 'supergroup', 'channel'], true)) {
            DB::table('telegram_chats')->updateOrInsert(
                ['chat_id' => $chatId],
                [
                    'title'        => $chat['title'] ?? null,
                    'type'         => $chatType,
                    'username'     => $chat['username'] ?? null,
                    'last_seen_at' => now(),
                ]
            );
        }

        if (! $chatId || $isBot || $text === '') {
            return response()->json(['ok' => true]);
        }

        // "#fin" etiketi — çekim chat grubunda dekont yükleme (Türkçe İ/ı varyantları dahil)
        if (preg_match('/(?:^|\s)#f[iİı]n\b/iu', $text)) {
            $this->handleFinTag($msg, (string) $chatId, $text, $messageId);
            return response()->json(['ok' => true]);
        }

        // "kasa" komutu — yalnızca bot etiketlenmişse çalışır (@botname kasa)
        if (! preg_match('/@\w+\s+kasa\b/iu', $text)) {
            return response()->json(['ok' => true]);
        }

        $team = DB::table('teams')
            ->where('telegram_reconciliation_chat_id', (string) $chatId)
            ->where('telegram_enabled', 1)
            ->where('telegram_cash_report_enabled', 1)
            ->first();

        if (! $team) {
            return response()->json(['ok' => true]);
        }

        try {
            $message = $this->buildCashReport($team);
            TelegramService::send((string) $chatId, $message, 'HTML');
        } catch (\Throwable $e) {
            Log::warning('Telegram cash report failed: ' . $e->getMessage());
        }

        return response()->json(['ok' => true]);
    }

    /**
     * Inline button (callback_query) işleme — şu an sadece risk bildirim butonları.
     * callback_data format: "risk_block:{notif_id}" veya "risk_dismiss:{notif_id}"
     */
    private function handleCallbackQuery(array $callback): void
    {
        $callbackId = (string) ($callback['id'] ?? '');
        $data = (string) ($callback['data'] ?? '');
        $fromUser = $callback['from'] ?? [];
        $clickerName = trim(($fromUser['first_name'] ?? '') . ' ' . ($fromUser['last_name'] ?? ''));
        if ($clickerName === '' && isset($fromUser['username'])) $clickerName = '@' . $fromUser['username'];
        if ($clickerName === '') $clickerName = 'admin';

        if (! preg_match('/^(risk_block|risk_dismiss):(\d+)$/', $data, $m)) {
            TelegramService::answerCallbackQuery($callbackId);
            return;
        }
        $action = $m[1];
        $notifId = (int) $m[2];

        $notif = DB::table('player_risk_notifications')->where('id', $notifId)->first();
        if (! $notif) {
            TelegramService::answerCallbackQuery($callbackId, 'Bildirim bulunamadı.', true);
            return;
        }

        if ($notif->decision !== 'pending') {
            TelegramService::answerCallbackQuery($callbackId, 'Bu uyarı zaten kapatıldı.', true);
            return;
        }

        $newDecision = $action === 'risk_block' ? 'blocked' : 'dismissed';
        $stamp = now()->format('d.m.Y H:i');

        if ($newDecision === 'blocked') {
            $existing = DB::table('blacklist')->where('type', 1)->where('val', $notif->player_id)->first();
            if (! $existing) {
                DB::table('blacklist')->insert([
                    'type' => 1,
                    'val'  => $notif->player_id,
                    'desc' => 'Şüpheli oyuncu aktivitesi — risk:check · ' . $stamp . ' · ' . $clickerName,
                ]);
            }
        }

        DB::table('player_risk_notifications')->where('id', $notifId)->update([
            'decision'   => $newDecision,
            'decided_at' => now(),
            'decided_by' => $clickerName,
        ]);

        // Mesajı edit et — orijinal text + karar satırı
        $originalMessage = $callback['message'] ?? [];
        $originalText = (string) ($originalMessage['text'] ?? '');
        $label = $newDecision === 'blocked' ? '🚫 Engellendi' : '⏭️ Vazgeçildi';
        $newText = htmlspecialchars($originalText, ENT_QUOTES, 'UTF-8')
                 . "\n\n✅ <b>" . $label . "</b> · " . htmlspecialchars($clickerName, ENT_QUOTES, 'UTF-8') . " · " . $stamp;

        if (! empty($notif->chat_id) && ! empty($notif->message_id)) {
            TelegramService::editMessageText($notif->chat_id, (int) $notif->message_id, $newText, 'HTML');

            // Geri bildirim — ayrı bir mesaj olarak gruba düşsün (orijinal mesaja reply)
            $confirmIcon = $newDecision === 'blocked' ? '🚫' : '⏭️';
            $confirmLabel = $newDecision === 'blocked' ? 'Engellendi' : 'Vazgeçildi';
            $confirmMsg = $confirmIcon . ' <b>' . $confirmLabel . '</b>'
                        . "\n👤 Player ID: <code>" . htmlspecialchars($notif->player_id, ENT_QUOTES, 'UTF-8') . "</code>"
                        . "\n👮 İşlem: " . htmlspecialchars($clickerName, ENT_QUOTES, 'UTF-8')
                        . "\n🕐 " . $stamp;
            TelegramService::send($notif->chat_id, $confirmMsg, 'HTML', (int) $notif->message_id);
        }

        TelegramService::answerCallbackQuery($callbackId, $newDecision === 'blocked' ? '🚫 Engellendi' : '⏭️ Vazgeçildi');
    }

    private function handleFinTag(array $msg, string $chatId, string $text, ?int $messageId): void
    {
        // 1. Çekim ID çıkar (W + rakam)
        if (! preg_match('/\bW\d+\b/i', $text, $m)) {
            TelegramService::send($chatId, "❓ Çekim ID bulunamadı. Format: <code>#fin W1234567</code>", 'HTML', $messageId);
            return;
        }
        $orderId = strtoupper($m[0]);

        // 2. Çekimi bul (type=2 sadece çekim)
        $invest = DB::table('invest')
            ->where('order_id', $orderId)
            ->where('type', '2')
            ->select('id', 'order_id', 'team_id', 'status')
            ->first();

        if (! $invest) {
            TelegramService::send($chatId, "❌ Çekim bulunamadı: <code>{$orderId}</code>", 'HTML', $messageId);
            return;
        }

        // 3. Grup eşleşme — invest'in takımının telegram_withdraw_chat_id'si bu chat olmalı
        $team = DB::table('teams')->where('id', $invest->team_id)->select('telegram_withdraw_chat_id')->first();
        if (! $team || (string) $team->telegram_withdraw_chat_id !== $chatId) {
            Log::warning('finTag: chat mismatch', [
                'chat_id' => $chatId, 'order_id' => $orderId,
                'invest_team_id' => $invest->team_id,
                'expected_chat' => $team->telegram_withdraw_chat_id ?? null,
            ]);
            return;
        }

        // 4. Medya tespit
        $fileId = null;
        $mimeType = null;
        $originalName = null;

        if (! empty($msg['photo']) && is_array($msg['photo'])) {
            $photo = end($msg['photo']); // en yüksek resolution
            $fileId = $photo['file_id'] ?? null;
            $mimeType = 'image/jpeg';
            $originalName = 'telegram-' . now()->format('YmdHis') . '.jpg';
        } elseif (! empty($msg['document']) && is_array($msg['document'])) {
            $doc = $msg['document'];
            $fileId = $doc['file_id'] ?? null;
            $mimeType = $doc['mime_type'] ?? null;
            $originalName = $doc['file_name'] ?? ('telegram-' . now()->format('YmdHis'));
        }

        if (! $fileId) {
            TelegramService::send($chatId, "📎 Lütfen mesaja foto veya PDF ekleyin.", 'HTML', $messageId);
            return;
        }

        // 5. MIME kontrolü
        $allowed = ['image/jpeg', 'image/png', 'image/webp', 'application/pdf'];
        if (! in_array($mimeType, $allowed, true)) {
            TelegramService::send($chatId, "❌ Sadece PDF/JPG/PNG/WEBP kabul edilir. (Aldığım: <code>" . htmlspecialchars($mimeType ?: 'bilinmiyor', ENT_QUOTES, 'UTF-8') . "</code>)", 'HTML', $messageId);
            return;
        }

        // 6. Dosyayı indir
        $download = TelegramService::downloadFile($fileId);
        if (! $download) {
            TelegramService::send($chatId, "⚠️ Dosya indirilemedi, lütfen tekrar deneyin.", 'HTML', $messageId);
            return;
        }

        if ($download['size'] > 10 * 1024 * 1024) {
            TelegramService::send($chatId, "❌ Dosya 10 MB'ı aşamaz.", 'HTML', $messageId);
            return;
        }

        // 7. Storage'a kaydet
        $extMap = ['image/jpeg' => 'jpg', 'image/png' => 'png', 'image/webp' => 'webp', 'application/pdf' => 'pdf'];
        $ext = $extMap[$mimeType] ?? ($download['ext'] ?: 'bin');
        $name = \Illuminate\Support\Str::uuid()->toString() . '.' . $ext;
        $path = "receipts/withdrawals/{$invest->id}/{$name}";
        \Illuminate\Support\Facades\Storage::disk('public')->put($path, $download['binary']);
        $fileHash = hash('sha256', $download['binary']);

        // 8. DB insert
        $receiptId = DB::table('invest_receipts')->insertGetId([
            'invest_id'     => $invest->id,
            'file_path'     => $path,
            'original_name' => $originalName,
            'mime_type'     => $mimeType,
            'file_size'     => $download['size'],
            'file_hash'     => $fileHash,
            'uploaded_by'   => null,
            'uploaded_at'   => now(),
        ]);

        // AI doğrulama job'ı dispatch
        \App\Jobs\VerifyReceiptJob::dispatch($receiptId);

        $sender = $msg['from']['username'] ?? $msg['from']['first_name'] ?? 'unknown';
        DB::table('investLog')->insert([
            'investID'  => $invest->id,
            'userID'    => null,
            'ip'        => '',
            'status'    => $invest->status,
            'createdAt' => now(),
            'detail'    => "Dekont yüklendi (Telegram: @{$sender} #fin)",
        ]);

        TelegramService::send($chatId, "✅ Dekont yüklendi — <b>{$orderId}</b>", 'HTML', $messageId);
    }

    private function buildCashReport(object $team): string
    {
        $today = now()->toDateString();
        $dayStart = now()->startOfDay()->toDateTimeString();
        $tomorrowStart = now()->startOfDay()->addDay()->toDateTimeString();
        $tid = (int) $team->id;

        $deposits = (float) DB::table('invest')->where('team_id', $tid)->where('type', '1')->where('status', '3')
            ->where('finalize_date', '>=', $dayStart)->where('finalize_date', '<', $tomorrowStart)->sum('amount');
        $withdrawals = (float) DB::table('invest')->where('team_id', $tid)->where('type', '2')->where('status', '3')
            ->where('finalize_date', '>=', $dayStart)->where('finalize_date', '<', $tomorrowStart)->sum('amount');
        $commission = $deposits * (float) $team->commission / 100;
        $payments = (float) DB::table('team_payments')->where('team_id', $tid)
            ->where('created_at', '>=', $dayStart)->where('created_at', '<', $tomorrowStart)->sum('amount');
        $expenses = (float) DB::table('paylira_expenses')->where('team_id', $tid)
            ->where('created_at', '>=', $dayStart)->where('created_at', '<', $tomorrowStart)->sum('amount');
        $partnerPay = (float) DB::table('paylira_partner_payments')->where('team_id', $tid)->where('payment_type', '3')
            ->where('created_at', '>=', $dayStart)->where('created_at', '<', $tomorrowStart)->sum('amount');
        $interPay = (float) DB::table('intermediary_payments')->where('team_id', $tid)->where('payment_type', '3')
            ->where('created_at', '>=', $dayStart)->where('created_at', '<', $tomorrowStart)->sum('amount');
        $transferIn = (float) DB::table('team_transfers')->where('to_team_id', $tid)
            ->where('created_at', '>=', $dayStart)->where('created_at', '<', $tomorrowStart)->sum('amount');
        $transferOut = (float) DB::table('team_transfers')->where('from_team_id', $tid)
            ->where('created_at', '>=', $dayStart)->where('created_at', '<', $tomorrowStart)->sum('amount');
        $syncs = (float) DB::table('team_syncs')->where('team_id', $tid)
            ->where('created_at', '>=', $dayStart)->where('created_at', '<', $tomorrowStart)->sum('amount');

        $previousBalance = DB::table('daily_case_snapshots')
            ->where('entity_type', 'team')->where('entity_id', $tid)
            ->where('snapshot_date', '<', $today)
            ->orderByDesc('snapshot_date')->value('amount');
        $previousBalance = $previousBalance !== null ? (float) $previousBalance : (float) $team->overturn;

        $totalReinforcement = -$expenses - $partnerPay - $interPay - $transferOut + $transferIn - $syncs;
        $endOfDay = $previousBalance + $deposits - $withdrawals - $commission - $payments + $totalReinforcement;

        $fmt = fn (float $v) => number_format($v, 2, ',', '.');
        $name = htmlspecialchars($team->name, ENT_QUOTES, 'UTF-8');
        $dateStr = now()->format('d.m.Y');

        return "<b>{$name} - {$dateStr}</b>\n\n"
             . "<b>Devir:</b> " . $fmt($previousBalance) . " TL\n"
             . "<b>Yatırım:</b> " . $fmt($deposits) . " TL\n"
             . "<b>Çekim:</b> " . $fmt($withdrawals) . " TL\n"
             . "<b>Komisyon:</b> " . $fmt($commission) . " TL\n"
             . "<b>Manuel Ödeme:</b> " . $fmt($payments) . " TL\n"
             . "<b>Toplam Takviye:</b> " . $fmt($totalReinforcement) . " TL\n\n"
             . "<b>Gün Sonu:</b> " . $fmt($endOfDay) . " TL";
    }
}
