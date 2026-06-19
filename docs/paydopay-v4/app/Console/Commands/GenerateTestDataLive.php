<?php

namespace App\Console\Commands;

use App\Support\TestDataGenerator;
use App\Support\TestMovementGenerator;
use Carbon\Carbon;
use Illuminate\Console\Command;

/**
 * Forward live test data generator. Sadece TEST_DATA_ENABLED=true ortamlarda çalışır.
 * Her dakika cron tetikler, içeride 6 tick (10 sn aralık) ile dakikada ~12-24 tx ekler.
 */
class GenerateTestDataLive extends Command
{
    protected $signature = 'testdata:generate-live';
    protected $description = 'paydopay test verisi — her dakika 12-24 tx ekler (env-gated)';

    public function handle(): int
    {
        if (! env('TEST_DATA_ENABLED')) {
            return self::SUCCESS;
        }

        // 1) 10 dk+ eski pending'leri finalize et (sweep)
        $this->finalizeOldPending();

        // 2) 490k üstündeki takım kasalarını trim et (test sınırı)
        $this->trimTeamBalances();

        $gen = new TestDataGenerator();
        $today = Carbon::now()->toDateString();
        $dayStart = Carbon::parse($today . ' 00:00:00', config('app.timezone'));

        $totalInserted = 0;

        // 6 tick × 10 sn = ~50 saniye
        for ($i = 0; $i < 6; $i++) {
            $batch = [];
            $count = mt_rand(2, 4);

            for ($k = 0; $k < $count; $k++) {
                $merchant = $gen->merchants[array_rand($gen->merchants)];
                // Yatırım veya çekim (70% yatırım)
                $type = mt_rand(1, 100) <= 70 ? 1 : 2;
                // buildTx içinde 80/20 (mevcut/yeni player) dağılımı uygulanıyor
                $batch[] = $gen->buildTx($merchant, $type, $dayStart, true);
            }

            if (!empty($batch)) {
                \Illuminate\Support\Facades\DB::table('invest')->insert($batch);
                $totalInserted += count($batch);
            }

            if ($i < 5) sleep(10);
        }

        // Hareket (movement): %15 şans her cron run → 4 dakikada 1 hareket avg
        // Saatte ~15, günde ~360 (sparse, doğal görünüm)
        $movementMsg = '';
        if (mt_rand(1, 100) <= 15) {
            $movGen = new TestMovementGenerator();
            $movementMsg = $movGen->generateRandomMovement() ?? '';
        }

        $this->info("Test data live: {$totalInserted} tx" . ($movementMsg ? " + movement: $movementMsg" : ""));
        return self::SUCCESS;
    }

    /** Takım bakiyesi 490k'yı aşarsa fazlasını team_payments ile drain et. */
    private function trimTeamBalances(): void
    {
        $sourceStorageId = \Illuminate\Support\Facades\DB::table('fund_storages')->where('type', 1)->where('status', 1)->value('id');
        if (! $sourceStorageId) return;

        $teams = \Illuminate\Support\Facades\DB::table('teams')->where('status', 1)->where('name', 'LIKE', 'E-%')
            ->select('id', 'commission')->get();

        foreach ($teams as $team) {
            $tid = $team->id;
            // Son snapshot bakiyesi
            $lastSnap = (float) \Illuminate\Support\Facades\DB::table('daily_case_snapshots')
                ->where('entity_type', 'team')->where('entity_id', $tid)
                ->orderByDesc('snapshot_date')->value('amount') ?? 0;
            $sinceDate = \Illuminate\Support\Facades\DB::table('daily_case_snapshots')
                ->where('entity_type', 'team')->where('entity_id', $tid)
                ->orderByDesc('snapshot_date')->value('snapshot_date') ?? '1970-01-01';
            $sinceDt = $sinceDate . ' 23:59:59';

            $depAmt = (float) \Illuminate\Support\Facades\DB::table('invest')
                ->where('team_id', $tid)->where('type', '1')->where('status', '3')
                ->where('finalize_date', '>', $sinceDt)->sum('amount');
            $wdAmt = (float) \Illuminate\Support\Facades\DB::table('invest')
                ->where('team_id', $tid)->where('type', '2')->where('status', '3')
                ->where('finalize_date', '>', $sinceDt)->sum('amount');
            $teamCom = $depAmt * $team->commission / 100;
            $payAmt = (float) \Illuminate\Support\Facades\DB::table('team_payments')
                ->where('team_id', $tid)->where('created_at', '>', $sinceDt)->sum('amount');

            $current = $lastSnap + $depAmt - $teamCom - $wdAmt - $payAmt;
            if ($current <= 490_000) continue;

            $target = mt_rand(100_000, 490_000);
            $diff = round($current - $target, 2);

            \Illuminate\Support\Facades\DB::table('team_payments')->insert([
                'team_id' => $tid, 'payment_type' => 1, 'amount' => $diff,
                'fund_storage_id' => $sourceStorageId,
                'description' => 'Auto-trim',
                'created_at' => now(),
            ]);
        }
    }

    /** 10 dk'dan eski pending kayıtları finalize et (status 1 ve 2, %20 red %80 onay). */
    private function finalizeOldPending(): void
    {
        // status=1 → process_date + finalize_date set, status 3/4
        \Illuminate\Support\Facades\DB::update("
            UPDATE invest SET
                process_date = created_at + INTERVAL FLOOR(5 + RAND() * 60) SECOND,
                finalize_date = created_at + INTERVAL FLOOR(60 + RAND() * 540) SECOND,
                finalize_time = FLOOR(30 + RAND() * 91),
                status = IF(RAND() < 0.2, '4', '3'),
                rejectType = IF(RAND() < 0.2, FLOOR(1 + RAND() * 5), NULL),
                callbackSended = 1
            WHERE status = '1' AND created_at < NOW() - INTERVAL 10 MINUTE
        ");

        // status=2 → finalize_date set
        \Illuminate\Support\Facades\DB::update("
            UPDATE invest SET
                finalize_date = process_date + INTERVAL FLOOR(30 + RAND() * 91) SECOND,
                finalize_time = FLOOR(30 + RAND() * 91),
                status = IF(RAND() < 0.2, '4', '3'),
                rejectType = IF(RAND() < 0.2, FLOOR(1 + RAND() * 5), NULL),
                callbackSended = 1
            WHERE status = '2' AND process_date IS NOT NULL AND created_at < NOW() - INTERVAL 10 MINUTE
        ");
    }
}
