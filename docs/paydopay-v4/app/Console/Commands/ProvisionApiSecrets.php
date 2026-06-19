<?php

namespace App\Console\Commands;

use Illuminate\Console\Command;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;

/**
 * Eksik apiSecret'i olan merchant'lara 64 karakterlik secret üret.
 *
 * Kullanım:
 *   php artisan apikeys:provision-secrets               → eksik olanları doldurur
 *   php artisan apikeys:provision-secrets --force       → hepsini yeniden üretir (DİKKAT)
 *   php artisan apikeys:provision-secrets --merchant=42 → tek bir merchant
 */
class ProvisionApiSecrets extends Command
{
    protected $signature = 'apikeys:provision-secrets {--force} {--merchant=}';
    protected $description = 'Merchant API v1 için apiSecret üretir';

    public function handle(): int
    {
        $query = DB::table('merchantUser');

        if ($id = $this->option('merchant')) {
            $query->where('id', (int) $id);
        }
        if (! $this->option('force')) {
            $query->where(function ($q) {
                $q->whereNull('apiSecret')->orWhere('apiSecret', '');
            });
        }

        $merchants = $query->select('id', 'name', 'apiKey')->get();

        if ($merchants->isEmpty()) {
            $this->info('Doldurulacak merchant bulunamadı.');
            return self::SUCCESS;
        }

        $this->info($merchants->count() . ' merchant için apiSecret üretilecek.');

        foreach ($merchants as $m) {
            $secret = Str::random(64);
            DB::table('merchantUser')->where('id', $m->id)->update(['apiSecret' => $secret]);
            $this->line(sprintf('  #%d %s  apiKey=%s  apiSecret=%s', $m->id, $m->name, $m->apiKey, $secret));
        }

        $this->newLine();
        $this->warn('YUKARIDAKI BİLGİLERİ MERCHANTLARA İLETİN. Bu komut tekrar çalıştırıldığında apiSecret kaybolur.');

        return self::SUCCESS;
    }
}
