<?php

namespace App\Console\Commands;

use App\Services\CallbackService;
use Illuminate\Console\Command;
use Illuminate\Support\Facades\DB;

/**
 * Pay link süresi dolan bekleyen invest'leri otomatik reddet + fail callback gönder.
 * Her dakika çalışır (routes/console.php'de schedule edilir).
 */
class ExpirePendingInvests extends Command
{
    protected $signature = 'invest:expire-pending';
    protected $description = 'Süre dolan pending invest\'leri otomatik reddet ve merchant callback gönder';

    public function handle(CallbackService $callbacks): int
    {
        $enabled = (string) DB::table('system_settings')->where('key', 'pay_link_expiry_enabled')->value('value') === '1';
        if (! $enabled) {
            $this->info('Link expiry disabled, skipping.');
            return self::SUCCESS;
        }

        $minutes = (int) DB::table('system_settings')->where('key', 'pay_link_expiry_minutes')->value('value');
        if ($minutes <= 0) {
            $this->info('Expiry minutes <= 0, skipping.');
            return self::SUCCESS;
        }

        $cutoff = now()->subMinutes($minutes);

        // Sadece YATIRIM (type=1) işlemlerini etkile; çekim (type=2) link süresi sınırı kapsamı dışı.
        $expired = DB::table('invest')
            ->where('type', '1')
            ->whereIn('status', ['0', '1', '2'])
            ->where('created_at', '<', $cutoff)
            ->get();

        if ($expired->isEmpty()) {
            $this->info('No expired pending invests found.');
            return self::SUCCESS;
        }

        $this->info("Found {$expired->count()} expired pending invest(s).");

        foreach ($expired as $tx) {
            DB::table('invest')->where('id', $tx->id)->update([
                'status'        => '4',
                'finalize_date' => now(),
            ]);
            DB::table('investLog')->insert([
                'investID'  => $tx->id,
                'userID'    => null,
                'ip'        => '127.0.0.1',
                'status'    => 4,
                'createdAt' => now(),
                'detail'    => 'Link süresi doldu (auto-expire)',
            ]);
            $tx->status = '4';
            $callbacks->sendExpire($tx);
            $this->line(" - invest#{$tx->id} order={$tx->order_id} expired");
        }

        return self::SUCCESS;
    }
}
