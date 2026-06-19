<?php

namespace App\Console\Commands;

use App\Services\TelegramService;
use Illuminate\Console\Command;
use Illuminate\Support\Facades\Http;
use Illuminate\Support\Str;

class SetTelegramWebhook extends Command
{
    protected $signature = 'telegram:set-webhook {--url= : Override webhook URL (defaults to APP_URL/api/telegram/webhook)} {--clear : Sadece mevcut webhook\'u sil}';
    protected $description = 'Telegram bot webhook ayarlar (incoming message dinleyici)';

    public function handle(): int
    {
        $token = TelegramService::botToken();
        if (! $token) {
            $this->error('TELEGRAM_BOT_TOKEN tanımlı değil.');
            return self::FAILURE;
        }

        if ($this->option('clear')) {
            $res = Http::post("https://api.telegram.org/bot{$token}/deleteWebhook", [
                'drop_pending_updates' => true,
            ]);
            $this->info('deleteWebhook: ' . $res->body());
            return self::SUCCESS;
        }

        $url = $this->option('url') ?: rtrim((string) config('app.url'), '/') . '/api/telegram/webhook';
        if (! Str::startsWith($url, 'https://')) {
            $this->error('Webhook URL https:// ile başlamalı. APP_URL doğru mu kontrol edin.');
            return self::FAILURE;
        }

        $secret = config('services.telegram.webhook_secret');
        if (! $secret) {
            $this->warn('TELEGRAM_WEBHOOK_SECRET .env\'de tanımlı değil. Lütfen önce şu satırı .env\'e ekleyin:');
            $this->line('TELEGRAM_WEBHOOK_SECRET=' . Str::random(48));
            $this->line('Sonra `php artisan config:cache` çalıştırıp komutu tekrar deneyin.');
            return self::FAILURE;
        }

        $payload = [
            'url'                  => $url,
            'secret_token'         => $secret,
            'allowed_updates'      => ['message', 'edited_message', 'callback_query'],
            'drop_pending_updates' => true,
        ];

        $res = Http::post("https://api.telegram.org/bot{$token}/setWebhook", $payload);

        if (! $res->successful() || ! ($res->json('ok') ?? false)) {
            $this->error('setWebhook başarısız: ' . $res->body());
            return self::FAILURE;
        }

        $this->info('Webhook ayarlandı: ' . $url);

        // Mevcut webhook bilgisini de göster
        $info = Http::get("https://api.telegram.org/bot{$token}/getWebhookInfo");
        $this->line('getWebhookInfo: ' . $info->body());

        return self::SUCCESS;
    }
}
