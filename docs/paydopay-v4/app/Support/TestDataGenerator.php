<?php

namespace App\Support;

use Carbon\Carbon;
use Illuminate\Support\Facades\DB;

/**
 * Test data generator — paydopay (and only paydopay) için.
 * Hem backfill (geçmiş gün) hem live (şu an) modunu destekler.
 */
class TestDataGenerator
{
    public array $merchants;          // [['id','commission','withdrawCommission']]
    public array $teams;              // [['id','name','commission','agents','banks']]
    public array $players;            // [merchant_id => [['name','id'], ...]]
    public bool $verbose = false;

    private array $firstNames = [
        'Ahmet','Mehmet','Ali','Hasan','Hüseyin','İbrahim','Mustafa','Emre','Burak','Cem',
        'Deniz','Eren','Furkan','Gökhan','Halil','İlker','Kemal','Mert','Nuri','Onur',
        'Osman','Ömer','Recep','Sami','Selim','Serkan','Şahin','Tarık','Tolga','Uğur',
        'Volkan','Yılmaz','Yusuf','Zafer','Anıl','Berke','Caner','Doğan','Erkan','Fatih',
        'Ayşe','Fatma','Zeynep','Elif','Hatice','Sevgi','Selma','Pınar','Neslihan','Merve',
    ];
    private array $lastNames = [
        'Yılmaz','Demir','Kaya','Şahin','Çelik','Öztürk','Aydın','Doğan','Arslan','Yıldız',
        'Polat','Aksoy','Koç','Can','Erdoğan','Şimşek','Acar','Tan','Güneş','Tekin',
        'Aslan','Çetin','Erol','Kara','Kurt','Aydoğan','Altın','Karadeniz','Solmaz','Ünlü',
        'Tunç','Vural','Yavuz','Yener','Zorlu','Karadağ','Babacan','Çakır','Demirci','Erkan',
    ];

    public function __construct()
    {
        $this->loadMerchants();
        $this->loadTeams();
        $this->generatePlayerPools();
    }

    private function loadMerchants(): void
    {
        $rows = DB::table('merchantUser')
            ->whereIn('name', ['BETS10', '1WIN', '1XBET'])
            ->select('id', 'name', 'commission', 'withdrawCommission')
            ->get();

        $this->merchants = $rows->map(fn ($r) => [
            'id' => $r->id,
            'name' => $r->name,
            'commission' => (float) $r->commission,
            'withdrawCommission' => (float) $r->withdrawCommission,
        ])->values()->all();

        if (empty($this->merchants)) {
            throw new \RuntimeException('Merchant bulunamadı (BETS10/1WIN/1XBET)');
        }
    }

    private function loadTeams(): void
    {
        $teamRows = DB::table('teams')
            ->where('status', 1)
            ->where('name', 'LIKE', 'E-%')
            ->select('id', 'name', 'commission')
            ->get();

        $this->teams = [];
        foreach ($teamRows as $t) {
            $banks = DB::table('bankAccounts')->where('team_id', $t->id)->where('status', 1)->pluck('id')->toArray();
            if (empty($banks)) continue;  // aktif hesabı olmayan takımlar atla
            $agents = DB::table('users')->where('team_id', $t->id)->where('user_type', 2)->where('status', '1')->pluck('id')->toArray();
            $this->teams[] = [
                'id' => $t->id,
                'name' => $t->name,
                'commission' => (float) $t->commission,
                'banks' => $banks,
                'agents' => $agents,
            ];
        }

        if (empty($this->teams)) {
            throw new \RuntimeException('Aktif hesabı olan takım bulunamadı');
        }
    }

    private function generatePlayerPools(): void
    {
        $this->players = [];
        foreach ($this->merchants as $m) {
            $pool = [];
            $usedIds = [];
            for ($i = 0; $i < 1000; $i++) {
                do {
                    $pid = (string) mt_rand(10_000_000, 99_999_999);
                } while (isset($usedIds[$pid]));
                $usedIds[$pid] = true;
                $pool[] = [
                    'id' => $pid,
                    'name' => $this->firstNames[array_rand($this->firstNames)] . ' ' . $this->lastNames[array_rand($this->lastNames)],
                ];
            }
            $this->players[$m['id']] = $pool;
        }
    }

    /** Yeni player ekle (forward generation'da %2 şans) */
    public function maybeAddNewPlayer(int $merchantId): void
    {
        if (mt_rand(1, 100) > 2) return;
        $pid = (string) mt_rand(10_000_000, 99_999_999);
        $this->players[$merchantId][] = [
            'id' => $pid,
            'name' => $this->firstNames[array_rand($this->firstNames)] . ' ' . $this->lastNames[array_rand($this->lastNames)],
        ];
    }

    /** Random TR + 24 hane IBAN (test için, check digit doğrulanmaz) */
    public function randomIban(): string
    {
        $iban = 'TR';
        for ($j = 0; $j < 24; $j++) $iban .= mt_rand(0, 9);
        return $iban;
    }

    /** Skewed amount: %92 küçük (1k-4.5k), %7 orta (4.5k-15k), %1 büyük (15k-100k) */
    public function randomAmount(): int
    {
        $r = mt_rand(1, 100);
        if ($r <= 92) return mt_rand(1000, 4500);
        if ($r <= 99) return mt_rand(4500, 15000);
        return mt_rand(15000, 100000);
    }

    /**
     * Bir gün için toplu üretim (backfill).
     * @return int eklenen tx sayısı
     */
    public function generateDay(string $date): int
    {
        $totalCount = 0;

        foreach ($this->merchants as $m) {
            // Yatırım
            $depositVolume = mt_rand(20_000_000, 50_000_000);
            $totalCount += $this->generateForMerchant($m, 1, $depositVolume, $date, false);

            // Çekim
            $withdrawPct = mt_rand(60, 95) / 100;
            $withdrawVolume = (int) ($depositVolume * $withdrawPct);
            $totalCount += $this->generateForMerchant($m, 2, $withdrawVolume, $date, false);
        }

        // Bu güne ait status=3 yatırımlardan random N tane isConverted=1
        $convertCount = mt_rand(50, 200);
        DB::statement("
            UPDATE invest SET isConverted = 1
            WHERE id IN (
                SELECT id FROM (
                    SELECT id FROM invest
                    WHERE DATE(created_at) = ? AND type = '1' AND status = '3'
                    ORDER BY RAND() LIMIT $convertCount
                ) t
            )
        ", [$date]);

        return $totalCount;
    }

    /**
     * Belirli bir merchant + tip için üretim, batch insert ile DB'ye yazar.
     * $isLive=true ise zaman aralığını "şu an" yapar (forward generation için).
     */
    public function generateForMerchant(array $merchant, int $type, int $targetVolume, string $date, bool $isLive): int
    {
        $count = 0;
        $sum = 0;
        $batch = [];
        $dayStart = Carbon::parse($date . ' 00:00:00', config('app.timezone'));

        while ($sum < $targetVolume) {
            $tx = $this->buildTx($merchant, $type, $dayStart, $isLive);
            $batch[] = $tx;
            $sum += $tx['amount'];
            $count++;

            if (count($batch) >= 500) {
                DB::table('invest')->insert($batch);
                $batch = [];
            }
        }

        if (!empty($batch)) {
            DB::table('invest')->insert($batch);
        }

        if ($this->verbose) {
            echo "  - {$merchant['name']} type=$type: $count tx, vol=" . number_format($sum, 0, ',', '.') . "\n";
        }

        return $count;
    }

    /** Tek bir tx kaydı oluştur (insert yok, dizi döner) */
    public function buildTx(array $merchant, int $type, Carbon $dayStart, bool $isLive, ?int $forceStatus = null): array
    {
        $amount = $this->randomAmount();
        $team = $this->teams[array_rand($this->teams)];
        $bankId = $team['banks'][array_rand($team['banks'])];
        $agentId = !empty($team['agents']) ? $team['agents'][array_rand($team['agents'])] : null;

        // %80 mevcut player havuzundan, %20 yeni player (havuza da eklenir ki tekrar gelebilsin)
        if (mt_rand(1, 100) <= 20) {
            $player = [
                'id'   => (string) mt_rand(10_000_000, 99_999_999),
                'name' => $this->firstNames[array_rand($this->firstNames)] . ' ' . $this->lastNames[array_rand($this->lastNames)],
            ];
            $this->players[$merchant['id']][] = $player;
        } else {
            $pool = $this->players[$merchant['id']];
            $player = $pool[array_rand($pool)];
        }

        // Status seçimi
        if ($forceStatus !== null) {
            $status = (string) $forceStatus;
        } elseif ($isLive) {
            $r = mt_rand(1, 100);
            if ($type === 1) {
                // Yatırımlar: üstlenme akışı yok, sadece beklemede (1) / onaylı (3) / reddedilmiş (4)
                $status = $r <= 15 ? '1' : (mt_rand(1, 100) <= 80 ? '3' : '4');
            } else {
                // Çekimler: üstlenme akışı korunuyor → status 2 hala geçerli
                $status = $r <= 5 ? '1' : ($r <= 15 ? '2' : (mt_rand(1, 100) <= 80 ? '3' : '4'));
            }
        } else {
            $status = mt_rand(1, 100) <= 80 ? '3' : '4';
        }

        // Zamanlama
        if ($isLive) {
            $createdAt = Carbon::now()->subSeconds(mt_rand(0, 50));
        } else {
            $createdAt = $dayStart->copy()->addSeconds(mt_rand(0, 86399));
        }

        $formAt = $createdAt->copy()->addSeconds(mt_rand(5, 30));
        $processDate = null;
        $finalizeDate = null;
        $finalizeTime = null;
        $rejectType = null;

        if ($status === '2' || $status === '3' || $status === '4') {
            $processDate = $formAt->copy()->addSeconds(mt_rand(5, 300));
        }
        if ($status === '3' || $status === '4') {
            $finalizeDate = $processDate->copy()->addSeconds(mt_rand(30, 120));
            $finalizeTime = $finalizeDate->diffInSeconds($processDate);
            if ($status === '4') $rejectType = mt_rand(1, 5);
        }

        $merchCommissionRate = $type === 1 ? $merchant['commission'] : $merchant['withdrawCommission'];
        $teamCommissionRate = $team['commission'];

        return [
            'type' => $type === 1 ? '1' : '2',
            'status' => $status,
            'name' => $player['name'],
            'player_id' => $player['id'],
            'amount' => $amount,
            'payed_amount' => $amount,
            'panel_commission_percent' => (int) $merchCommissionRate,
            'panel_commissin_amount' => round($amount * $merchCommissionRate / 100, 2),
            'team_commission_percent' => (int) $teamCommissionRate,
            'team_commissin_amount' => round($amount * $teamCommissionRate / 100, 2),
            'created_at' => $createdAt->format('Y-m-d H:i:s'),
            'form_at' => $formAt->format('Y-m-d H:i:s'),
            'process_date' => $processDate?->format('Y-m-d H:i:s'),
            'finalize_date' => $finalizeDate?->format('Y-m-d H:i:s'),
            'finalize_time' => $finalizeTime,
            'firm_id' => $merchant['id'],
            'team_id' => $team['id'],
            'agent_id' => $status === '1' ? null : $agentId,
            'bank_id' => $bankId,
            'callbackUrl' => '',
            'api_id' => 'TEST_' . bin2hex(random_bytes(8)),
            'order_id' => ($type === 1 ? 'D' : 'W') . str_pad((string) mt_rand(1_000_000_000, 9_999_999_999), 10, '0', STR_PAD_LEFT),
            'rejectType' => $rejectType,
            'isControled' => 1,
            'callbackSended' => $status === '3' ? 1 : 0,
            'isConverted' => 0,
            'walletInvest' => 0,
            'transaction_type' => 1,
            'added_type' => '1',
            'amountChanged' => 0,
            'ibanSeen' => 0,
            'iban' => $type === 2 ? $this->randomIban() : null,
        ];
    }
}
