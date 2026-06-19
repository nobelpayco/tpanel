<?php

namespace App\Console\Commands;

use Illuminate\Console\Command;
use Illuminate\Support\Facades\DB;

class DailyCaseSnapshot extends Command
{
    protected $signature = 'snapshot:daily {date?}';
    protected $description = 'Gün sonu kasa raporlarını kaydet';

    private string $dayStart = '';
    private string $dayEnd = '';

    public function handle(): void
    {
        $date = $this->argument('date') ?? now()->toDateString();

        // Index-friendly range borders (whereDate yerine kullan)
        $this->dayStart = $date . ' 00:00:00';
        $this->dayEnd = date('Y-m-d 00:00:00', strtotime($date . ' +1 day'));

        $this->info("Snapshot tarihi: {$date}");

        $this->snapshotMerchants($date);
        $this->snapshotIntermediaries($date);
        $this->snapshotTeams($date);
        $this->snapshotPaylira($date);
        $this->snapshotPartners($date);
        $this->snapshotFundStorages($date);

        $this->info('Günlük snapshot tamamlandı.');
    }

    private function snapshotFundStorages(string $date): void
    {
        $storages = DB::table('fund_storages')->get();

        foreach ($storages as $storage) {
            $sid = $storage->id;

            // Önceki snapshot
            $prev = (float) DB::table('daily_case_snapshots')
                ->where('entity_type', 'fund_storage')
                ->where('entity_id', $sid)
                ->where('snapshot_date', '<', $date)
                ->orderByDesc('snapshot_date')
                ->value('amount') ?? 0;

            // Çıkışlar
            $out = 0;
            $out += (float) DB::table('merchant_payments')->where('fund_storage_id', $sid)->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)->sum('amount');
            $out += (float) DB::table('intermediary_payments')->where('fund_storage_id', $sid)->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)->sum('amount');
            $out += (float) DB::table('paylira_partner_payments')->where('fund_storage_id', $sid)->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)->sum('amount');
            $out += (float) DB::table('fund_transfers')->where('from_storage_id', $sid)->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)->sum('amount');
            $out += (float) DB::table('paylira_expenses')->where('fund_storage_id', $sid)->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)->sum('amount');

            // Senkron hareketleri (pozitif=giriş, negatif=çıkış)
            $syncTotal = (float) DB::table('fund_storage_syncs')->where('fund_storage_id', $sid)->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)->sum('amount');

            // Girişler
            $in = 0;
            $in += (float) DB::table('merchant_payments')->where('fund_storage_id', $sid)->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)->sum('delivery_profit');
            $in += (float) DB::table('team_payments')->where('fund_storage_id', $sid)->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)->sum('amount');
            $in += (float) DB::table('partner_capitals')->where('fund_storage_id', $sid)->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)->sum('amount');
            $in += (float) DB::table('fund_transfers')->where('to_storage_id', $sid)->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)->sum('received_amount');

            $balance = $prev + $in - $out + $syncTotal;

            $this->upsertSnapshot($date, 'fund_storage', $sid, $storage->name, round($balance, 2), [
                'previous_balance' => round($prev, 2),
                'in'               => round($in, 2),
                'out'              => round($out, 2),
                'type'             => $storage->type,
                'wallet_address'   => $storage->wallet_address,
            ]);
        }

        $this->info("  Fon Deposu: {$storages->count()} kayıt");
    }

    private function snapshotMerchants(string $date): void
    {
        $allMerchants = DB::table('merchantUser')
            ->where('status', '1')
            ->select('id', 'name', 'caseNow', 'commission', 'withdrawCommission', 'group_id')
            ->get();

        $groups = DB::table('merchant_groups')->where('status', 1)->get()->keyBy('id');
        $processedGroupIds = [];
        $count = 0;

        foreach ($allMerchants as $m) {
            // Grup varsa birleştir
            if ($m->group_id && isset($groups[$m->group_id])) {
                if (in_array($m->group_id, $processedGroupIds)) continue;
                $processedGroupIds[] = $m->group_id;

                $groupMerchants = $allMerchants->where('group_id', $m->group_id);
                $groupIds = $groupMerchants->pluck('id')->toArray();
                $groupName = $groups[$m->group_id]->name;
                $entityType = 'merchant_group';
                $entityId = $m->group_id;
                $totalCaseNow = $groupMerchants->sum('caseNow');
            } else {
                $groupIds = [$m->id];
                $groupName = $m->name;
                $entityType = 'merchant';
                $entityId = $m->id;
                $totalCaseNow = $m->caseNow;
            }

            $deposits = 0; $withdrawals = 0; $netDeposit = 0; $netWithdraw = 0;
            $payments = 0; $paymentCommissions = 0;
            $depositCommissionAmount = 0; $withdrawCommissionAmount = 0;

            foreach ($allMerchants->whereIn('id', $groupIds) as $gm) {
                $dep = (float) DB::table('invest')
                    ->where('firm_id', $gm->id)->where('type', 1)->where('status', 3)
                    ->where('finalize_date', '>=', $this->dayStart)->where('finalize_date', '<', $this->dayEnd)->sum('amount');
                $wd = (float) DB::table('invest')
                    ->where('firm_id', $gm->id)->where('type', 2)->where('status', 3)
                    ->where('finalize_date', '>=', $this->dayStart)->where('finalize_date', '<', $this->dayEnd)->sum('amount');

                $depComm = $dep * $gm->commission / 100;
                $wdComm = $wd * $gm->withdrawCommission / 100;

                $deposits += $dep;
                $withdrawals += $wd;
                $depositCommissionAmount += $depComm;
                $withdrawCommissionAmount += $wdComm;
                $netDeposit += $dep - $depComm;
                $netWithdraw += $wd + $wdComm;

                $payments += (float) DB::table('merchant_payments')
                    ->where('merchant_id', $gm->id)->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)->sum('amount');
                $paymentCommissions += (float) DB::table('merchant_payments')
                    ->where('merchant_id', $gm->id)->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)->sum('delivery_commission_amount');
            }

            $dailyChange = $netDeposit - $netWithdraw - $payments;

            $previousBalance = (float) DB::table('daily_case_snapshots')
                ->where('entity_type', $entityType)
                ->where('entity_id', $entityId)
                ->where('snapshot_date', '<', $date)
                ->orderByDesc('snapshot_date')
                ->value('amount') ?? $totalCaseNow;

            $caseBalance = $previousBalance + $dailyChange;

            $this->upsertSnapshot($date, $entityType, $entityId, $groupName, round($caseBalance, 2), [
                'previous_balance'        => round($previousBalance, 2),
                'merchant_ids'            => $groupIds,
                'deposits'                => round($deposits, 2),
                'withdrawals'             => round($withdrawals, 2),
                'net_deposit'             => round($netDeposit, 2),
                'net_withdraw'            => round($netWithdraw, 2),
                'deposit_commission_amount'  => round($depositCommissionAmount, 2),
                'withdraw_commission_amount' => round($withdrawCommissionAmount, 2),
                'payments'                => round($payments, 2),
                'payment_commissions'     => round($paymentCommissions, 2),
                'daily_change'            => round($dailyChange, 2),
            ]);
            $count++;
        }

        $this->info("  Merchant: {$count} kayıt (grup dahil)");
    }

    private function snapshotIntermediaries(string $date): void
    {
        $intermediaries = DB::table('new_intermediaries')
            ->where('status', 1)
            ->select('id', 'name', 'type', 'balance')
            ->get();

        foreach ($intermediaries as $inter) {
            $total = 0;
            $details = [];

            // Merchant bağlantıları
            $merchantRates = DB::table('new_intermediary_merchant')
                ->join('merchantUser', 'new_intermediary_merchant.merchant_id', '=', 'merchantUser.id')
                ->where('new_intermediary_merchant.intermediary_id', $inter->id)
                ->where('new_intermediary_merchant.status', 1)
                ->select('merchantUser.id', 'merchantUser.name', 'new_intermediary_merchant.commission_rate')
                ->get();

            foreach ($merchantRates as $mr) {
                $deposits = (float) DB::table('invest')
                    ->where('firm_id', $mr->id)->where('type', 1)->where('status', 3)
                    ->where('finalize_date', '>=', $this->dayStart)->where('finalize_date', '<', $this->dayEnd)->sum('amount');
                $commission = $deposits * $mr->commission_rate / 100;
                $total += $commission;
                if ($commission > 0) {
                    $details[] = ['name' => $mr->name, 'type' => 'merchant', 'rate' => $mr->commission_rate, 'deposits' => round($deposits, 2), 'commission' => round($commission, 2)];
                }
            }

            // Takım bağlantıları
            $teamRates = DB::table('new_intermediary_team')
                ->join('teams', 'new_intermediary_team.team_id', '=', 'teams.id')
                ->where('new_intermediary_team.intermediary_id', $inter->id)
                ->where('new_intermediary_team.status', 1)
                ->select('teams.id', 'teams.name', 'new_intermediary_team.commission_rate')
                ->get();

            foreach ($teamRates as $tr) {
                $deposits = (float) DB::table('invest')
                    ->where('team_id', $tr->id)->where('type', 1)->where('status', 3)
                    ->where('finalize_date', '>=', $this->dayStart)->where('finalize_date', '<', $this->dayEnd)->sum('amount');
                $commission = $deposits * $tr->commission_rate / 100;
                $total += $commission;
                if ($commission > 0) {
                    $details[] = ['name' => $tr->name, 'type' => 'team', 'rate' => $tr->commission_rate, 'deposits' => round($deposits, 2), 'commission' => round($commission, 2)];
                }
            }

            // Günlük ödemeler
            $payments = (float) DB::table('intermediary_payments')
                ->where('intermediary_id', $inter->id)
                ->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)
                ->sum('amount');

            // Önceki günün snapshot'ı (devirli)
            $previousBalance = (float) DB::table('daily_case_snapshots')
                ->where('entity_type', 'intermediary')
                ->where('entity_id', $inter->id)
                ->where('snapshot_date', '<', $date)
                ->orderByDesc('snapshot_date')
                ->value('amount') ?? (float) $inter->balance;

            $caseBalance = $previousBalance + $total - $payments;

            // Aracı bakiyesini güncelle (sadece bugün için)
            if ($date === now()->toDateString()) {
                DB::table('new_intermediaries')->where('id', $inter->id)->update(['balance' => round($caseBalance, 2)]);
            }

            $this->upsertSnapshot($date, 'intermediary', $inter->id, $inter->name, round($caseBalance, 2), [
                'intermediary_type'  => $inter->type,
                'previous_balance'   => round($previousBalance, 2),
                'daily_commission'   => round($total, 2),
                'payments'           => round($payments, 2),
                'breakdowns'         => $details,
            ]);
        }

        $this->info("  Aracı: {$intermediaries->count()} kayıt");
    }

    private function snapshotTeams(string $date): void
    {
        // Pasif takımlar da bakiyesi varsa snapshot'lanmalı
        $teams = DB::table('teams')
            ->select('id', 'name', 'overturn', 'commission')
            ->get();

        foreach ($teams as $team) {
            // PLH1 (team_id=1) her zaman 0 olarak kaydedilsin
            if ($team->id == 1) {
                $this->upsertSnapshot($date, 'team', $team->id, $team->name, 0, ['manual_zero' => true]);
                continue;
            }

            $deposits = (float) DB::table('invest')
                ->where('team_id', $team->id)->where('type', 1)->where('status', 3)
                ->where('finalize_date', '>=', $this->dayStart)->where('finalize_date', '<', $this->dayEnd)->sum('amount');

            $withdrawals = (float) DB::table('invest')
                ->where('team_id', $team->id)->where('type', 2)->where('status', 3)
                ->where('finalize_date', '>=', $this->dayStart)->where('finalize_date', '<', $this->dayEnd)->sum('amount');

            $teamCommission = $deposits * $team->commission / 100;

            $payments = (float) DB::table('team_payments')
                ->where('team_id', $team->id)
                ->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)
                ->sum('amount');

            // Bu takıma yansıtılan paylira masrafları
            $teamExpenses = (float) DB::table('paylira_expenses')
                ->where('team_id', $team->id)
                ->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)
                ->sum('amount');

            // Bu takıma yansıtılan partner sermaye düşümleri (payment_type=3)
            $teamPartnerPayments = (float) DB::table('paylira_partner_payments')
                ->where('team_id', $team->id)
                ->where('payment_type', '3')
                ->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)
                ->sum('amount');

            // Bu takıma yansıtılan aracı ödemeleri (payment_type=3)
            $teamIntermediaryPayments = (float) DB::table('intermediary_payments')
                ->where('team_id', $team->id)
                ->where('payment_type', '3')
                ->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)
                ->sum('amount');

            // Takım fon transferleri
            $transferOut = (float) DB::table('team_transfers')
                ->where('from_team_id', $team->id)
                ->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)
                ->sum('amount');
            $transferIn = (float) DB::table('team_transfers')
                ->where('to_team_id', $team->id)
                ->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)
                ->sum('amount');

            $teamSyncs = (float) DB::table('team_syncs')
                ->where('team_id', $team->id)
                ->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)
                ->sum('amount');

            $accumulated = $deposits - $teamCommission - $withdrawals - $payments - $teamExpenses - $teamPartnerPayments - $teamIntermediaryPayments - $transferOut + $transferIn - $teamSyncs;

            // Devirli: önceki snapshot + bugünkü değişim
            $previousBalance = (float) DB::table('daily_case_snapshots')
                ->where('entity_type', 'team')
                ->where('entity_id', $team->id)
                ->where('snapshot_date', '<', $date)
                ->orderByDesc('snapshot_date')
                ->value('amount') ?? $team->overturn;

            $balance = $previousBalance + $accumulated;

            $this->upsertSnapshot($date, 'team', $team->id, $team->name, round($balance, 2), [
                'overturn'                  => round($previousBalance, 2),
                'commission'                => $team->commission,
                'deposits'                  => round($deposits, 2),
                'withdrawals'               => round($withdrawals, 2),
                'team_commission'           => round($teamCommission, 2),
                'payments'                  => round($payments, 2),
                'paylira_expenses'          => round($teamExpenses, 2),
                'partner_capital_payments'  => round($teamPartnerPayments, 2),
                'intermediary_payments'     => round($teamIntermediaryPayments, 2),
                'transfer_in'               => round($transferIn, 2),
                'transfer_out'              => round($transferOut, 2),
                'team_syncs'                => round($teamSyncs, 2),
                'accumulated'               => round($accumulated, 2),
            ]);
        }

        $this->info("  Takım: {$teams->count()} kayıt");
    }

    private function snapshotPaylira(string $date): void
    {
        $merchants = DB::table('merchantUser')
            ->where('status', '1')
            ->select('id', 'name', 'commission', 'withdrawCommission', 'group_id')
            ->get();

        $groupNames = DB::table('merchant_groups')->where('status', 1)->pluck('name', 'id');

        // Aracı takım oranlarını hazırla
        $intermediaryTeamRates = DB::table('new_intermediary_team')
            ->join('new_intermediaries', 'new_intermediary_team.intermediary_id', '=', 'new_intermediaries.id')
            ->where('new_intermediary_team.status', 1)
            ->where('new_intermediaries.status', 1)
            ->select('new_intermediary_team.team_id', DB::raw('SUM(new_intermediary_team.commission_rate) as total_rate'))
            ->groupBy('new_intermediary_team.team_id')
            ->pluck('total_rate', 'team_id');

        $totalDailyNet = 0;
        $merchantBreakdown = [];

        foreach ($merchants as $m) {
            $deposits = (float) DB::table('invest')
                ->where('firm_id', $m->id)->where('type', 1)->where('status', 3)
                ->where('finalize_date', '>=', $this->dayStart)->where('finalize_date', '<', $this->dayEnd)->sum('amount');

            $withdrawals = (float) DB::table('invest')
                ->where('firm_id', $m->id)->where('type', 2)->where('status', 3)
                ->where('finalize_date', '>=', $this->dayStart)->where('finalize_date', '<', $this->dayEnd)->sum('amount');

            $depositComm = $deposits * $m->commission / 100;
            $withdrawComm = $withdrawals * $m->withdrawCommission / 100;

            // Ödeme komisyonları
            $deliveryComm = (float) DB::table('merchant_payments')
                ->where('merchant_id', $m->id)
                ->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)
                ->sum('delivery_commission_amount');

            // Kripto teslimat karı
            $deliveryProfit = (float) DB::table('merchant_payments')
                ->where('merchant_id', $m->id)
                ->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)
                ->sum('delivery_profit');

            $brut = $depositComm + $withdrawComm + $deliveryComm + $deliveryProfit;

            $teamComm = (float) DB::table('invest')
                ->join('teams', 'invest.team_id', '=', 'teams.id')
                ->where('invest.firm_id', $m->id)
                ->where('invest.type', 1)->where('invest.status', 3)
                ->where('invest.finalize_date', '>=', $this->dayStart)->where('invest.finalize_date', '<', $this->dayEnd)
                ->selectRaw('SUM(invest.amount * teams.commission / 100) as total')
                ->value('total') ?? 0;

            // Aracı komisyonu — hem takım hem merchant bazlı
            $intermediaryComm = 0;
            $merchantTeamDeposits = DB::table('invest')
                ->where('firm_id', $m->id)->where('type', 1)->where('status', 3)
                ->where('finalize_date', '>=', $this->dayStart)->where('finalize_date', '<', $this->dayEnd)
                ->select('team_id', DB::raw('SUM(amount) as total'))
                ->groupBy('team_id')
                ->pluck('total', 'team_id');

            foreach ($merchantTeamDeposits as $teamId => $teamDeposit) {
                if (isset($intermediaryTeamRates[$teamId])) {
                    $intermediaryComm += $teamDeposit * $intermediaryTeamRates[$teamId] / 100;
                }
            }

            // Merchant-bazlı aracı komisyonu
            $merchantInterRates = DB::table('new_intermediary_merchant')
                ->join('new_intermediaries', 'new_intermediary_merchant.intermediary_id', '=', 'new_intermediaries.id')
                ->where('new_intermediary_merchant.merchant_id', $m->id)
                ->where('new_intermediary_merchant.status', 1)
                ->where('new_intermediaries.status', 1)
                ->pluck('new_intermediary_merchant.commission_rate');
            foreach ($merchantInterRates as $rate) {
                $intermediaryComm += $deposits * (float) $rate / 100;
            }

            $merchantNet = $brut - $teamComm - $intermediaryComm;
            $totalDailyNet += $merchantNet;

            // Merchant bazlı paylira net — grup bazlı birleştir
            if ($deposits > 0 || $withdrawals > 0 || $deliveryComm > 0 || $deliveryProfit > 0) {
                $displayName = ($m->group_id && isset($groupNames[$m->group_id])) ? $groupNames[$m->group_id] : $m->name;

                // merchantBreakdown'da grup varsa birleştir
                $existing = array_search($displayName, array_column($merchantBreakdown, 'name'));
                $depositProfitNet = $depositComm - $teamComm - $intermediaryComm;
                if ($existing !== false) {
                    $merchantBreakdown[$existing]['net'] += round($merchantNet, 2);
                    $merchantBreakdown[$existing]['deposit_profit'] += round($depositProfitNet, 2);
                    $merchantBreakdown[$existing]['withdraw_profit'] += round($withdrawComm, 2);
                    $merchantBreakdown[$existing]['delivery_profit'] += round($deliveryComm + $deliveryProfit, 2);
                } else {
                    $merchantBreakdown[] = [
                        'name'            => $displayName,
                        'net'             => round($merchantNet, 2),
                        'deposit_profit'  => round($depositProfitNet, 2),
                        'withdraw_profit' => round($withdrawComm, 2),
                        'delivery_profit' => round($deliveryComm + $deliveryProfit, 2),
                    ];
                }
            }
        }

        // Sistem aracısı (type=3): tüm günlük sistem cirosu × oran. Merchant döngüsünden bağımsız tek seferlik hesap.
        $systemInterComm = 0;
        $systemInters = DB::table('new_intermediaries')
            ->where('status', 1)
            ->where('type', 3)
            ->get();
        if ($systemInters->isNotEmpty()) {
            $totalSystemDeposits = (float) DB::table('invest')
                ->where('type', 1)->where('status', 3)
                ->where('finalize_date', '>=', $this->dayStart)
                ->where('finalize_date', '<', $this->dayEnd)
                ->sum('amount');
            foreach ($systemInters as $si) {
                $systemInterComm += $totalSystemDeposits * (float) $si->commission_rate / 100;
            }
            $totalDailyNet -= $systemInterComm;
        }

        // Önceki günün birikimli bakiyesini al
        $previousBalance = (float) DB::table('daily_case_snapshots')
            ->where('entity_type', 'paylira')
            ->whereNull('entity_id')
            ->where('snapshot_date', '<', $date)
            ->orderByDesc('snapshot_date')
            ->value('amount') ?? 0;

        $dailyExpenses = (float) DB::table('paylira_expenses')
            ->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)
            ->sum('amount');

        // Partner ödemeleri (sermaye düşümleri) paylira karından düşülür
        $dailyPartnerPayments = (float) DB::table('paylira_partner_payments')
            ->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)
            ->sum('amount');

        // Partner sermaye girişleri paylira'ya eklenir
        $dailyPartnerCapitals = (float) DB::table('partner_capitals')
            ->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)
            ->sum('amount');

        $cumulativeNet = $previousBalance + $totalDailyNet - $dailyExpenses - $dailyPartnerPayments + $dailyPartnerCapitals;

        $this->upsertSnapshot($date, 'paylira', null, 'Paylira', round($cumulativeNet, 2), [
            'daily_net'             => round($totalDailyNet, 2),
            'expenses'              => round($dailyExpenses, 2),
            'partner_payments'      => round($dailyPartnerPayments, 2),
            'partner_capitals'      => round($dailyPartnerCapitals, 2),
            'previous_balance'      => round($previousBalance, 2),
            'system_inter_comm'     => round($systemInterComm, 2),
            'merchants'             => $merchantBreakdown,
        ]);

        $this->info("  Paylira günlük: " . number_format($totalDailyNet, 2) . " | Toplam: " . number_format($cumulativeNet, 2));
    }

    private function snapshotPartners(string $date): void
    {
        // Paylira günlük net'i al (bugünün snapshot'ından)
        $payliraSnap = DB::table('daily_case_snapshots')
            ->where('entity_type', 'paylira')
            ->whereNull('entity_id')
            ->where('snapshot_date', $date)
            ->first();

        if (! $payliraSnap) {
            $this->info("  Partner: Paylira snapshot bulunamadı, atlanıyor.");
            return;
        }

        $payliraDetails = json_decode($payliraSnap->details, true) ?? [];
        $dailyNet = $payliraDetails['daily_net'] ?? 0;

        $partners = DB::table('paylira_partners')->where('status', 1)->get();

        foreach ($partners as $partner) {
            $partnerDailyShare = $dailyNet * $partner->share_percent / 100;

            // Önceki günün birikimli bakiyesi
            $previousBalance = (float) DB::table('daily_case_snapshots')
                ->where('entity_type', 'partner')
                ->where('entity_id', $partner->id)
                ->where('snapshot_date', '<', $date)
                ->orderByDesc('snapshot_date')
                ->value('amount') ?? 0;

            // Günlük ödemeler
            $payments = (float) DB::table('paylira_partner_payments')
                ->where('partner_id', $partner->id)
                ->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)
                ->sum('amount');

            // Günlük sermaye eklemeleri
            $capitals = (float) DB::table('partner_capitals')
                ->where('partner_id', $partner->id)
                ->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)
                ->sum('amount');

            $expenses = (float) DB::table('paylira_expense_shares')
                ->join('paylira_expenses', 'paylira_expense_shares.expense_id', '=', 'paylira_expenses.id')
                ->where('paylira_expense_shares.partner_id', $partner->id)
                ->where('paylira_expenses.created_at', '>=', $this->dayStart)->where('paylira_expenses.created_at', '<', $this->dayEnd)
                ->sum('paylira_expense_shares.amount');

            // Partner transferleri
            $transferOut = (float) DB::table('partner_transfers')
                ->where('from_partner_id', $partner->id)
                ->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)
                ->sum('amount');
            $transferIn = (float) DB::table('partner_transfers')
                ->where('to_partner_id', $partner->id)
                ->where('created_at', '>=', $this->dayStart)->where('created_at', '<', $this->dayEnd)
                ->sum('amount');

            $caseBalance = $previousBalance + $partnerDailyShare + $capitals - $payments - $expenses - $transferOut + $transferIn;

            $this->upsertSnapshot($date, 'partner', $partner->id, $partner->name, round($caseBalance, 2), [
                'previous_balance' => round($previousBalance, 2),
                'daily_net'        => round($dailyNet, 2),
                'share_percent'    => $partner->share_percent,
                'daily_share'      => round($partnerDailyShare, 2),
                'capitals'         => round($capitals, 2),
                'expenses'         => round($expenses, 2),
                'payments'         => round($payments, 2),
                'transfer_in'      => round($transferIn, 2),
                'transfer_out'     => round($transferOut, 2),
            ]);
        }

        $this->info("  Partner: {$partners->count()} kayıt");
    }

    private function upsertSnapshot(string $date, string $type, ?int $entityId, string $name, float $amount, array $details): void
    {
        $existing = DB::table('daily_case_snapshots')
            ->where('snapshot_date', $date)
            ->where('entity_type', $type)
            ->where('entity_id', $entityId)
            ->first();

        $data = [
            'entity_name' => $name,
            'amount'      => $amount,
            'details'     => json_encode($details, JSON_UNESCAPED_UNICODE),
        ];

        if ($existing) {
            DB::table('daily_case_snapshots')->where('id', $existing->id)->update($data);
        } else {
            DB::table('daily_case_snapshots')->insert(array_merge($data, [
                'snapshot_date' => $date,
                'entity_type'   => $type,
                'entity_id'     => $entityId,
                'created_at'    => now(),
            ]));
        }
    }
}
