<?php

namespace App\Services;

use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Http;
use Illuminate\Support\Facades\Log;

class TelegramService
{
    /** Bot token'ı önce sistem ayarlarından oku (panel'den yönetilebilir), yoksa env fallback */
    public static function botToken(): ?string
    {
        $token = DB::table('system_settings')->where('key', 'telegram_bot_token')->value('value');
        return $token ?: config('services.telegram.bot_token');
    }

    /**
     * @return bool — başarı durumu. Message ID lazımsa sendReturnId() kullan.
     */
    public static function send(string $chatId, string $message, string $parseMode = 'MarkdownV2', ?int $replyToMessageId = null, ?array $replyMarkup = null): bool
    {
        return self::sendReturnId($chatId, $message, $parseMode, $replyToMessageId, $replyMarkup) !== null;
    }

    /**
     * Gönderir; başarılıysa Telegram message_id (int), aksi halde null döner.
     */
    public static function sendReturnId(string $chatId, string $message, string $parseMode = 'MarkdownV2', ?int $replyToMessageId = null, ?array $replyMarkup = null): ?int
    {
        $token = self::botToken();
        if (! $token || ! $chatId) return null;

        try {
            $payload = [
                'chat_id'    => $chatId,
                'text'       => $message,
                'parse_mode' => $parseMode,
            ];
            if ($replyToMessageId !== null) {
                $payload['reply_parameters'] = ['message_id' => $replyToMessageId, 'allow_sending_without_reply' => true];
            }
            if ($replyMarkup !== null) {
                $payload['reply_markup'] = json_encode($replyMarkup, JSON_UNESCAPED_UNICODE);
            }

            $res = Http::timeout(8)->post("https://api.telegram.org/bot{$token}/sendMessage", $payload);

            if (! $res->successful()) {
                // Otomatik supergroup migration: chat upgrade edildiyse Telegram yeni chat_id verir.
                $migrated = $res->json('parameters.migrate_to_chat_id');
                if ($migrated) {
                    $newChatId = (string) $migrated;
                    Log::info('Telegram chat migrated, updating DB', ['old' => $chatId, 'new' => $newChatId]);
                    self::migrateChatId($chatId, $newChatId);

                    // Yeni chat_id ile retry
                    $payload['chat_id'] = $newChatId;
                    $retry = Http::timeout(8)->post("https://api.telegram.org/bot{$token}/sendMessage", $payload);
                    if ($retry->successful()) return $retry->json('result.message_id');
                    Log::warning('Telegram retry failed after migration', ['chat_id' => $newChatId, 'response' => $retry->body()]);
                    return null;
                }

                Log::warning('Telegram send failed', ['chat_id' => $chatId, 'response' => $res->body()]);
                return null;
            }

            return $res->json('result.message_id');
        } catch (\Throwable $e) {
            Log::warning('Telegram send exception: ' . $e->getMessage());
            return null;
        }
    }

    /** Gönderilmiş bir mesajın metnini güncelle. Inline keyboard kaldırılır. */
    public static function editMessageText(string $chatId, int $messageId, string $text, string $parseMode = 'HTML'): bool
    {
        $token = self::botToken();
        if (! $token) return false;

        try {
            $res = Http::timeout(8)->post("https://api.telegram.org/bot{$token}/editMessageText", [
                'chat_id'    => $chatId,
                'message_id' => $messageId,
                'text'       => $text,
                'parse_mode' => $parseMode,
            ]);
            if (! $res->successful()) {
                Log::warning('Telegram editMessageText failed', ['chat_id' => $chatId, 'message_id' => $messageId, 'response' => substr($res->body(), 0, 300)]);
            }
            return $res->successful();
        } catch (\Throwable $e) {
            Log::warning('Telegram editMessageText exception: ' . $e->getMessage());
            return false;
        }
    }

    /** Mesaj metnini güncelle + inline keyboard'u koru/yenile. */
    public static function editMessageTextWithMarkup(string $chatId, int $messageId, string $text, string $parseMode = 'HTML', ?array $replyMarkup = null): bool
    {
        $token = self::botToken();
        if (! $token) return false;

        try {
            $payload = [
                'chat_id'    => $chatId,
                'message_id' => $messageId,
                'text'       => $text,
                'parse_mode' => $parseMode,
            ];
            if ($replyMarkup !== null) {
                $payload['reply_markup'] = json_encode($replyMarkup, JSON_UNESCAPED_UNICODE);
            }
            $res = Http::timeout(8)->post("https://api.telegram.org/bot{$token}/editMessageText", $payload);
            if (! $res->successful()) {
                Log::warning('Telegram editMessageTextWithMarkup failed', ['chat_id' => $chatId, 'message_id' => $messageId, 'response' => substr($res->body(), 0, 300)]);
            }
            return $res->successful();
        } catch (\Throwable $e) {
            Log::warning('Telegram editMessageTextWithMarkup exception: ' . $e->getMessage());
            return false;
        }
    }

    /** Callback button tıklamasına Telegram'a cevap (loading kapansın, opsiyonel toast). */
    public static function answerCallbackQuery(string $callbackQueryId, ?string $text = null, bool $showAlert = false): bool
    {
        $token = self::botToken();
        if (! $token) return false;

        try {
            $payload = ['callback_query_id' => $callbackQueryId];
            if ($text !== null) {
                $payload['text'] = $text;
                $payload['show_alert'] = $showAlert;
            }
            $res = Http::timeout(8)->post("https://api.telegram.org/bot{$token}/answerCallbackQuery", $payload);
            return $res->successful();
        } catch (\Throwable $e) {
            Log::warning('Telegram answerCallbackQuery exception: ' . $e->getMessage());
            return false;
        }
    }

    /** Tüm teams.telegram_* alanlarında eski chat_id'yi yeni chat_id ile değiştir. */
    private static function migrateChatId(string $oldChatId, string $newChatId): void
    {
        foreach (['telegram_chat_id', 'telegram_withdraw_chat_id', 'telegram_reconciliation_chat_id'] as $col) {
            DB::table('teams')->where($col, $oldChatId)->update([$col => $newChatId]);
        }
    }

    /**
     * Telegram bot API'sinden dosya indir.
     * @return array{binary: string, size: int, path: string, ext: string}|null
     */
    public static function downloadFile(string $fileId): ?array
    {
        $token = self::botToken();
        if (! $token) return null;

        try {
            $info = Http::timeout(10)->get("https://api.telegram.org/bot{$token}/getFile", ['file_id' => $fileId]);
            if (! $info->successful() || ! ($info->json('ok') ?? false)) {
                Log::warning('Telegram getFile failed', ['file_id' => $fileId, 'response' => $info->body()]);
                return null;
            }
            $filePath = $info->json('result.file_path');
            if (! $filePath) return null;

            $bin = Http::timeout(30)->get("https://api.telegram.org/file/bot{$token}/{$filePath}");
            if (! $bin->successful()) {
                Log::warning('Telegram file download failed', ['file_path' => $filePath, 'status' => $bin->status()]);
                return null;
            }

            $body = $bin->body();
            $ext = pathinfo($filePath, PATHINFO_EXTENSION) ?: 'bin';

            return ['binary' => $body, 'size' => strlen($body), 'path' => $filePath, 'ext' => strtolower($ext)];
        } catch (\Throwable $e) {
            Log::warning('Telegram downloadFile exception: ' . $e->getMessage());
            return null;
        }
    }

    /** MarkdownV2 reserved chars'ı kaçır */
    public static function escape(?string $s): string
    {
        if ($s === null || $s === '') return '';
        return preg_replace('/([_*\[\]()~`>#+\-=|{}.!\\\\])/u', '\\\\$1', $s);
    }
}
