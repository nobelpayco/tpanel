<?php

namespace App\Support;

use Carbon\Carbon;
use Illuminate\Support\Facades\DB;

/**
 * Test movements: takım ödemeleri, fund transferleri, merchant ödemeleri,
 * partner sermaye düşümü, paylira giderleri vb.
 * Sadece TEST_DATA_ENABLED=true ortamlarda kullanılır.
 *
 * Mantık (paydopay için):
 *  - 2 fund storage var: Kapalı Çarşı (id=5, type=1=dış), Paydo Cüzdan (id=6, type=2=iç)
 *  - Akış: Takım → Kapalı Çarşı (team_payments) → Paydo Cüzdan (fund_transfer) → Merchant (merchant_payments)
 */
class TestMovementGenerator
{
    public array $teams = [];          // [['id', 'name', 'commission']]
    public array $merchants = [];       // [['id', 'commission', 'withdrawCommission']]
    public array $partners = [];        // [['id', 'name']]
    public ?int $sourceStorageId = null;  // type=1 (dövizci)
    public ?int $sinkStorageId = null;    // type=2 (cüzdan)

    public function __construct()
    {
        $this->teams = DB::table('teams')->where('status', 1)->where('name', 'LIKE', 'E-%')
            ->select('id', 'name', 'commission')->get()->all();
        $this->merchants = DB::table('merchantUser')->whereIn('name', ['BETS10', '1WIN', '1XBET'])
            ->select('id', 'name', 'commission', 'withdrawCommission')->get()->all();
        $this->partners = DB::table('paylira_partners')->where('status', 1)->select('id', 'name')->get()->all();

        $storages = DB::table('fund_storages')->where('status', 1)->select('id', 'name', 'type')->get();
        foreach ($storages as $s) {
            if ($s->type == 1 && $this->sourceStorageId === null) $this->sourceStorageId = $s->id;
            if ($s->type == 2 && $this->sinkStorageId === null)   $this->sinkStorageId = $s->id;
        }
    }

    private function randomDateTime(string $date): string
    {
        $base = Carbon::parse($date . ' 00:00:00');
        return $base->copy()->addSeconds(mt_rand(0, 86399))->format('Y-m-d H:i:s');
    }

    /** Per-team net = deposits - team_commission - withdrawals (TL bazında) */
    private function calcTeamNet(int $teamId, string $date, float $teamCommission): float
    {
        $start = $date . ' 00:00:00';
        $end = date('Y-m-d 00:00:00', strtotime($date . ' +1 day'));

        $deposits = (float) DB::table('invest')
            ->where('team_id', $teamId)->where('type', '1')->where('status', '3')
            ->where('finalize_date', '>=', $start)->where('finalize_date', '<', $end)
            ->sum('amount');

        $withdrawals = (float) DB::table('invest')
            ->where('team_id', $teamId)->where('type', '2')->where('status', '3')
            ->where('finalize_date', '>=', $start)->where('finalize_date', '<', $end)
            ->sum('amount');

        $teamCom = $deposits * $teamCommission / 100;
        return $deposits - $teamCom - $withdrawals;
    }

    /** Per-merchant net = deposits*(1-commission) - withdrawals*(1+wcom) */
    private function calcMerchantNet(int $merchantId, string $date, float $commission, float $withdrawCommission): float
    {
        $start = $date . ' 00:00:00';
        $end = date('Y-m-d 00:00:00', strtotime($date . ' +1 day'));

        $deposits = (float) DB::table('invest')
            ->where('firm_id', $merchantId)->where('type', '1')->where('status', '3')
            ->where('finalize_date', '>=', $start)->where('finalize_date', '<', $end)
            ->sum('amount');

        $withdrawals = (float) DB::table('invest')
            ->where('firm_id', $merchantId)->where('type', '2')->where('status', '3')
            ->where('finalize_date', '>=', $start)->where('finalize_date', '<', $end)
            ->sum('amount');

        return $deposits * (1 - $commission / 100) - $withdrawals * (1 + $withdrawCommission / 100);
    }

    /** Backfill: bir günün tüm hareketlerini üret. */
    public function generateDayMovements(string $date): array
    {
        if (! $this->sourceStorageId || ! $this->sinkStorageId) return ['error' => 'no fund storages'];

        $stats = ['team_payments' => 0, 'fund_transfers' => 0, 'merchant_payments' => 0,
                  'partner_payments' => 0, 'expenses' => 0, 'team_transfers' => 0, 'intermediary_payments' => 0];

        $totalToFundStorage = 0;

        // 1) Takım → Kapalı Çarşı (team_payments). Her takımın günlük net'inin %70-90'ı drain.
        foreach ($this->teams as $team) {
            $net = $this->calcTeamNet($team->id, $date, (float) $team->commission);
            if ($net < 50_000) continue; // küçük net'leri atla
            // Günlük net'in büyük kısmı drain, küçük rastgele kısmı takımda kalır (kümülatif 100k-700k civarına gitsin)
            $drainPct = mt_rand(9920, 9995) / 10000; // %99.20..%99.95
            $drain = round($net * $drainPct, 2);

            $payCount = mt_rand(1, 2);
            $remaining = $drain;
            for ($i = 0; $i < $payCount; $i++) {
                $amt = $i === $payCount - 1 ? $remaining : round($remaining * mt_rand(40, 60) / 100, 2);
                if ($amt < 1000) continue;
                DB::table('team_payments')->insert([
                    'team_id' => $team->id, 'payment_type' => 1, 'amount' => $amt,
                    'crypto_quantity' => null, 'crypto_rate' => null,
                    'fund_storage_id' => $this->sourceStorageId,
                    'description' => 'Test ödeme',
                    'created_at' => $this->randomDateTime($date),
                ]);
                $totalToFundStorage += $amt;
                $remaining -= $amt;
                $stats['team_payments']++;
            }
        }

        // 2) Kapalı Çarşı → Paydo Cüzdan (fund_transfers). Drain'in ~80'i.
        if ($totalToFundStorage > 100_000) {
            $transferAmt = round($totalToFundStorage * mt_rand(92, 100) / 100, 2);
            $commRate = round(mt_rand(200, 350) / 100, 2);  // %2-3.5
            $commAmt = round($transferAmt * $commRate / 100, 2);
            DB::table('fund_transfers')->insert([
                'from_storage_id' => $this->sourceStorageId, 'to_storage_id' => $this->sinkStorageId,
                'amount' => $transferAmt, 'commission_rate' => $commRate,
                'commission_amount' => $commAmt, 'received_amount' => $transferAmt - $commAmt,
                'description' => 'Tether alımı',
                'created_at' => $this->randomDateTime($date),
            ]);
            $stats['fund_transfers']++;
        }

        // 3) Paydo Cüzdan → Merchant (merchant_payments). Her merchant'a günlük net'in 60-80'i.
        foreach ($this->merchants as $m) {
            $net = $this->calcMerchantNet($m->id, $date, (float) $m->commission, (float) $m->withdrawCommission);
            if ($net < 100_000) continue;
            $payAmt = round($net * mt_rand(92, 100) / 100, 2);
            $deliveryRate = round(mt_rand(50, 150) / 100, 2);  // %0.5-1.5
            $deliveryAmt = round($payAmt * $deliveryRate / 100, 2);
            DB::table('merchant_payments')->insert([
                'merchant_id' => $m->id, 'payment_type' => 1, 'amount' => $payAmt,
                'paid_amount' => $payAmt - $deliveryAmt, 'delivery_profit' => $deliveryAmt,
                'delivery_commission_rate' => $deliveryRate, 'delivery_commission_amount' => $deliveryAmt,
                'fund_storage_id' => $this->sinkStorageId, 'description' => 'Test ödeme',
                'created_at' => $this->randomDateTime($date),
            ]);
            $stats['merchant_payments']++;
        }

        // 4) Partner sermaye düşümü (paylira_partner_payments). 0-2 per day random partner.
        $partnerPayCount = mt_rand(0, 2);
        for ($i = 0; $i < $partnerPayCount; $i++) {
            if (empty($this->partners)) break;
            $p = $this->partners[array_rand($this->partners)];
            $amt = round(mt_rand(5000, 100000), 2);
            DB::table('paylira_partner_payments')->insert([
                'partner_id' => $p->id, 'payment_type' => 1, 'amount' => $amt,
                'fund_storage_id' => $this->sinkStorageId, 'description' => 'Test ortak ödemesi',
                'created_at' => $this->randomDateTime($date), 'is_capital' => 0,
            ]);
            $stats['partner_payments']++;
        }

        // 5) Paylira gider (paylira_expenses). 0-1 per day.
        if (mt_rand(1, 100) <= 60) {
            $amt = round(mt_rand(2000, 25000), 2);
            DB::table('paylira_expenses')->insert([
                'amount' => $amt, 'description' => 'Test gider',
                'fund_storage_id' => $this->sinkStorageId,
                'created_at' => $this->randomDateTime($date),
            ]);
            $stats['expenses']++;
        }

        // 6) Takımlar arası transfer (team_transfers). %15 şans, en aktif iki takım arası.
        if (mt_rand(1, 100) <= 15 && count($this->teams) >= 2) {
            $shuffled = $this->teams;
            shuffle($shuffled);
            [$from, $to] = [$shuffled[0], $shuffled[1]];
            $amt = round(mt_rand(20_000, 200_000), 2);
            DB::table('team_transfers')->insert([
                'from_team_id' => $from->id, 'to_team_id' => $to->id,
                'amount' => $amt, 'description' => 'Test transfer',
                'created_at' => $this->randomDateTime($date),
            ]);
            $stats['team_transfers']++;
        }

        return $stats;
    }

    /** Forward (live): "şu an" timestamp ile rastgele bir hareket ekle. */
    public function generateRandomMovement(): ?string
    {
        if (! $this->sourceStorageId || ! $this->sinkStorageId) return null;

        $now = Carbon::now()->format('Y-m-d H:i:s');
        $r = mt_rand(1, 100);

        if ($r <= 50) {
            // Takım → Kapalı Çarşı (en sık)
            if (empty($this->teams)) return null;
            $team = $this->teams[array_rand($this->teams)];
            $amt = round(mt_rand(50_000, 500_000), 2);
            DB::table('team_payments')->insert([
                'team_id' => $team->id, 'payment_type' => 1, 'amount' => $amt,
                'fund_storage_id' => $this->sourceStorageId,
                'description' => 'Test ödeme', 'created_at' => $now,
            ]);
            return "team_payment {$team->name} ₺$amt";
        }

        if ($r <= 65) {
            // Kapalı Çarşı → Paydo Cüzdan
            $amt = round(mt_rand(200_000, 2_000_000), 2);
            $commRate = round(mt_rand(200, 350) / 100, 2);
            $commAmt = round($amt * $commRate / 100, 2);
            DB::table('fund_transfers')->insert([
                'from_storage_id' => $this->sourceStorageId, 'to_storage_id' => $this->sinkStorageId,
                'amount' => $amt, 'commission_rate' => $commRate,
                'commission_amount' => $commAmt, 'received_amount' => $amt - $commAmt,
                'description' => 'Tether alımı', 'created_at' => $now,
            ]);
            return "fund_transfer ₺$amt";
        }

        if ($r <= 80) {
            // Paydo Cüzdan → Merchant
            if (empty($this->merchants)) return null;
            $m = $this->merchants[array_rand($this->merchants)];
            $amt = round(mt_rand(100_000, 1_000_000), 2);
            $deliveryRate = round(mt_rand(50, 150) / 100, 2);
            $deliveryAmt = round($amt * $deliveryRate / 100, 2);
            DB::table('merchant_payments')->insert([
                'merchant_id' => $m->id, 'payment_type' => 1, 'amount' => $amt,
                'paid_amount' => $amt - $deliveryAmt, 'delivery_profit' => $deliveryAmt,
                'delivery_commission_rate' => $deliveryRate, 'delivery_commission_amount' => $deliveryAmt,
                'fund_storage_id' => $this->sinkStorageId, 'description' => 'Test ödeme',
                'created_at' => $now,
            ]);
            return "merchant_payment {$m->name} ₺$amt";
        }

        if ($r <= 90) {
            // Partner ödemesi
            if (empty($this->partners)) return null;
            $p = $this->partners[array_rand($this->partners)];
            $amt = round(mt_rand(5000, 80000), 2);
            DB::table('paylira_partner_payments')->insert([
                'partner_id' => $p->id, 'payment_type' => 1, 'amount' => $amt,
                'fund_storage_id' => $this->sinkStorageId,
                'description' => 'Test ortak ödemesi', 'created_at' => $now, 'is_capital' => 0,
            ]);
            return "partner_payment {$p->name} ₺$amt";
        }

        if ($r <= 97) {
            // Paylira gider
            $amt = round(mt_rand(2000, 20000), 2);
            DB::table('paylira_expenses')->insert([
                'amount' => $amt, 'description' => 'Test gider',
                'fund_storage_id' => $this->sinkStorageId, 'created_at' => $now,
            ]);
            return "expense ₺$amt";
        }

        // Takımlar arası transfer (rare)
        if (count($this->teams) < 2) return null;
        $shuffled = $this->teams;
        shuffle($shuffled);
        [$from, $to] = [$shuffled[0], $shuffled[1]];
        $amt = round(mt_rand(20_000, 200_000), 2);
        DB::table('team_transfers')->insert([
            'from_team_id' => $from->id, 'to_team_id' => $to->id,
            'amount' => $amt, 'description' => 'Test transfer', 'created_at' => $now,
        ]);
        return "team_transfer {$from->name}→{$to->name} ₺$amt";
    }
}
