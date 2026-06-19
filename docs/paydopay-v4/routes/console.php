<?php

use Illuminate\Support\Facades\Schedule;

// Gece 00:05'te bir önceki günün snapshot'ını al
Schedule::call(function () {
    \Illuminate\Support\Facades\Artisan::call('snapshot:daily', ['date' => now()->subDay()->toDateString()]);
})->dailyAt('00:05');

// Her dakika queue job'larını işle
Schedule::command('queue:work --once --timeout=300')->everyMinute()->withoutOverlapping();

// Her 5 dakikada eski export dosyalarını temizle (30 dk sonra)
Schedule::call(function () {
    \App\Http\Controllers\Api\ExportController::cleanup();
})->everyFiveMinutes();

// Telegram: bekleyen yatırımlar için takım uyarıları (her dakika)
// withoutOverlapping kullanılmıyor — komut idempotent (telegram_notifications.UNIQUE key ile dedupe)
Schedule::command('telegram:check-pending')->everyMinute();

// Pay link süresi dolan pending invest'leri otomatik reddet + fail callback (her dakika)
Schedule::command('invest:expire-pending')->everyMinute()->withoutOverlapping();

// Düşük tutar şüpheli oyuncu tespiti — son 10 onaylı yatırımı tamamı 1k altı olanları sistem chat'e bildirir
Schedule::command('risk:check-low-amount')->everyMinute()->withoutOverlapping();

// Test data live generator — SADECE TEST_DATA_ENABLED=true ortamlarda çalışır (paydopay)
// Çift koruma: schedule kaydı env-gate, komut içinde de env-gate var
if (env('TEST_DATA_ENABLED') === true || env('TEST_DATA_ENABLED') === 'true') {
    Schedule::command('testdata:generate-live')->everyMinute();
}
