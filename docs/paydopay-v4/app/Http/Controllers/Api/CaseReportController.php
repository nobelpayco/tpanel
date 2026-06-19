<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Http;

class CaseReportController extends Controller
{
    private function denyMerchant(): void
    {
        if (auth()->user()?->isMerchant()) {
            abort(403, 'Bu sayfaya erişim yetkiniz yok.');
        }
    }

    public function summary(Request $request): JsonResponse
    {
        $this->denyMerchant();
        $request->validate([
            'date_from' => 'nullable|date',
            'date_to'   => 'nullable|date',
        ]);

        $dateFrom = $request->date_from ?? now()->toDateString();
        $dateTo = $request->date_to ?? now()->toDateString();
        $today = now()->toDateString();
        $isPast = $dateTo < $today;

        // Date range helpers (index kullanabilen formatta)
        $todayStart = $today . ' 00:00:00';
        $todayEnd   = date('Y-m-d 00:00:00', strtotime($today . ' +1 day'));
        $rangeStart = $dateFrom . ' 00:00:00';
        $rangeEnd   = date('Y-m-d 00:00:00', strtotime($dateTo . ' +1 day'));

        // === Merchant kasaları ===
        $allMerchants = DB::table('merchantUser')
            ->where('status', '1')
            ->select('id', 'name', 'caseNow', 'commission', 'withdrawCommission', 'group_id')
            ->get();

        $groups = DB::table('merchant_groups')->where('status', 1)->get()->keyBy('id');
        $merchantIds = $allMerchants->pluck('id')->toArray();

        // Toplu snapshot'lar
        $allSnapshots = DB::table('daily_case_snapshots')
            ->where('snapshot_date', $dateTo)
            ->whereIn('entity_type', ['merchant', 'merchant_group', 'intermediary', 'team', 'paylira'])
            ->get()
            ->groupBy(fn($s) => $s->entity_type . ':' . $s->entity_id);

        // Bugün ise: toplu canlı veriler
        if (! $isPast) {
            $todayDeposits = DB::table('invest')
                ->whereIn('firm_id', $merchantIds)->where('type', 1)->where('status', 3)
                ->where('finalize_date', '>=', $todayStart)->where('finalize_date', '<', $todayEnd)
                ->select('firm_id', DB::raw('SUM(amount) as total'))
                ->groupBy('firm_id')->pluck('total', 'firm_id');

            $todayWithdrawals = DB::table('invest')
                ->whereIn('firm_id', $merchantIds)->where('type', 2)->where('status', 3)
                ->where('finalize_date', '>=', $todayStart)->where('finalize_date', '<', $todayEnd)
                ->select('firm_id', DB::raw('SUM(amount) as total'))
                ->groupBy('firm_id')->pluck('total', 'firm_id');

            $todayPayments = DB::table('merchant_payments')
                ->whereIn('merchant_id', $merchantIds)->where('created_at', '>=', $todayStart)->where('created_at', '<', $todayEnd)
                ->select('merchant_id', DB::raw('SUM(amount) as total'))
                ->groupBy('merchant_id')->pluck('total', 'merchant_id');

            // Toplu son snapshot'lar (devir için)
            $lastSnapshots = DB::table('daily_case_snapshots')
                ->whereIn('entity_type', ['merchant', 'merchant_group'])
                ->where('snapshot_date', '<', $today)
                ->orderByDesc('snapshot_date')
                ->get()
                ->groupBy(fn($s) => $s->entity_type . ':' . $s->entity_id)
                ->map(fn($group) => $group->first());
        }

        $processedGroupIds = [];
        $merchantCases = collect();

        foreach ($allMerchants as $m) {
            if ($m->group_id && isset($groups[$m->group_id])) {
                if (in_array($m->group_id, $processedGroupIds)) continue;
                $processedGroupIds[] = $m->group_id;
                $groupMerchants = $allMerchants->where('group_id', $m->group_id);
                $displayName = $groups[$m->group_id]->name;
                $entityType = 'merchant_group';
                $entityId = $m->group_id;
                $totalCaseNow = $groupMerchants->sum('caseNow');
            } else {
                $groupMerchants = collect([$m]);
                $displayName = $m->name;
                $entityType = 'merchant';
                $entityId = $m->id;
                $totalCaseNow = $m->caseNow;
            }

            $snapKey = $entityType . ':' . $entityId;
            $snapshot = ($allSnapshots[$snapKey] ?? collect())->first();

            if ($snapshot && $isPast) {
                $caseValue = (float) $snapshot->amount;
            } else {
                $lastSnapKey = $entityType . ':' . $entityId;
                $lastSnap = isset($lastSnapshots[$lastSnapKey])
                    ? (float) $lastSnapshots[$lastSnapKey]->amount
                    : $totalCaseNow;

                $netDeposit = 0; $netWithdraw = 0; $pay = 0;
                foreach ($groupMerchants as $gm) {
                    $dep = (float) ($todayDeposits[$gm->id] ?? 0);
                    $wd = (float) ($todayWithdrawals[$gm->id] ?? 0);
                    $netDeposit += $dep - ($dep * $gm->commission / 100);
                    $netWithdraw += $wd + ($wd * $gm->withdrawCommission / 100);
                    $pay += (float) ($todayPayments[$gm->id] ?? 0);
                }

                $caseValue = $lastSnap + $netDeposit - $netWithdraw - $pay;
            }

            $merchantCases->push([
                'id'    => $entityId,
                'name'  => $displayName,
                'value' => round($caseValue, 2),
            ]);
        }

        $merchantCases = $merchantCases->filter(fn($m) => $m['value'] != 0)->values();

        // === Aracı komisyonları ===
        $intermediaries = DB::table('new_intermediaries')
            ->where('status', 1)
            ->select('id', 'name', 'type', 'balance', 'commission_rate')
            ->get();

        $interIds = $intermediaries->pluck('id')->toArray();

        if (! $isPast && count($interIds) > 0) {
            // Toplu: aracı merchant oranları
            $interMerchantRates = DB::table('new_intermediary_merchant')
                ->whereIn('intermediary_id', $interIds)
                ->where('status', 1)
                ->select('intermediary_id', 'merchant_id', 'commission_rate')
                ->get()
                ->groupBy('intermediary_id');

            // Toplu: aracı takım oranları
            $interTeamRates = DB::table('new_intermediary_team')
                ->whereIn('intermediary_id', $interIds)
                ->where('status', 1)
                ->select('intermediary_id', 'team_id', 'commission_rate')
                ->get()
                ->groupBy('intermediary_id');

            // Toplu: bugünkü yatırım toplamları (merchant bazlı)
            $todayDepositsByMerchant = $todayDeposits ?? collect();

            // Toplu: bugünkü yatırım toplamları (takım bazlı)
            $teamIds = DB::table('teams')->pluck('id')->toArray();
            $todayDepositsByTeam = DB::table('invest')
                ->whereIn('team_id', $teamIds)->where('type', 1)->where('status', 3)
                ->where('finalize_date', '>=', $todayStart)->where('finalize_date', '<', $todayEnd)
                ->select('team_id', DB::raw('SUM(amount) as total'))
                ->groupBy('team_id')->pluck('total', 'team_id');

            // Toplu: aracı ödemeleri
            $interPaymentsToday = DB::table('intermediary_payments')
                ->whereIn('intermediary_id', $interIds)
                ->where('created_at', '>=', $todayStart)->where('created_at', '<', $todayEnd)
                ->select('intermediary_id', DB::raw('SUM(amount) as total'))
                ->groupBy('intermediary_id')->pluck('total', 'intermediary_id');

            // Toplu: son snapshot devir
            $interLastSnapshots = DB::table('daily_case_snapshots')
                ->where('entity_type', 'intermediary')
                ->whereIn('entity_id', $interIds)
                ->where('snapshot_date', '<', $today)
                ->orderByDesc('snapshot_date')
                ->get()
                ->groupBy('entity_id')
                ->map(fn($group) => $group->first());
        }

        $intermediaryCases = $intermediaries->map(function ($inter) use ($isPast, $allSnapshots, &$interMerchantRates, &$interTeamRates, &$todayDepositsByMerchant, &$todayDepositsByTeam, &$interPaymentsToday, &$interLastSnapshots, $today, $todayStart, $todayEnd) {
            $snapKey = 'intermediary:' . $inter->id;
            $snapshot = ($allSnapshots[$snapKey] ?? collect())->first();

            if ($snapshot && $isPast) {
                $totalBalance = (float) $snapshot->amount;
                $details = json_decode($snapshot->details, true) ?? [];
                $dailyCommission = $details['daily_commission'] ?? 0;
            } else {
                $lastSnap = isset($interLastSnapshots[$inter->id])
                    ? (float) $interLastSnapshots[$inter->id]->amount
                    : 0;

                $dailyCommission = 0;

                if ((int) $inter->type === 3) {
                    // Sistem aracısı: tüm sistem cirosu × oran
                    $totalSystemDeposits = (float) $todayDepositsByMerchant->sum();
                    $dailyCommission = $totalSystemDeposits * (float) $inter->commission_rate / 100;
                } else {
                    $merchantRates = $interMerchantRates[$inter->id] ?? collect();
                    foreach ($merchantRates as $mr) {
                        $dep = (float) ($todayDepositsByMerchant[$mr->merchant_id] ?? 0);
                        $dailyCommission += $dep * $mr->commission_rate / 100;
                    }

                    $teamRates = $interTeamRates[$inter->id] ?? collect();
                    foreach ($teamRates as $tr) {
                        $dep = (float) ($todayDepositsByTeam[$tr->team_id] ?? 0);
                        $dailyCommission += $dep * $tr->commission_rate / 100;
                    }
                }

                $todayPay = (float) ($interPaymentsToday[$inter->id] ?? 0);
                $totalBalance = $lastSnap + $dailyCommission - $todayPay;
            }

            return [
                'name'             => $inter->name,
                'type'             => $inter->type,
                'daily_commission' => round($dailyCommission ?? 0, 2),
                'value'            => round($totalBalance, 2),
            ];
        })->filter(fn($i) => $i['value'] != 0)->values();

        // === Takım kasaları ===
        $teams = DB::table('teams')
            ->select('id', 'name', 'overturn', 'commission')
            ->get();

        $teamIdsAll = $teams->pluck('id')->toArray();

        if (! $isPast && count($teamIdsAll) > 0) {
            $teamDepositSums = DB::table('invest')
                ->whereIn('team_id', $teamIdsAll)->where('type', 1)->where('status', 3)
                ->where('finalize_date', '>=', $todayStart)->where('finalize_date', '<', $todayEnd)
                ->select('team_id', DB::raw('SUM(amount) as total'))
                ->groupBy('team_id')->pluck('total', 'team_id');

            $teamWithdrawSums = DB::table('invest')
                ->whereIn('team_id', $teamIdsAll)->where('type', 2)->where('status', 3)
                ->where('finalize_date', '>=', $todayStart)->where('finalize_date', '<', $todayEnd)
                ->select('team_id', DB::raw('SUM(amount) as total'))
                ->groupBy('team_id')->pluck('total', 'team_id');

            $teamLastSnapshots = DB::table('daily_case_snapshots')
                ->where('entity_type', 'team')
                ->whereIn('entity_id', $teamIdsAll)
                ->where('snapshot_date', '<', $today)
                ->orderByDesc('snapshot_date')
                ->get()
                ->groupBy('entity_id')
                ->map(fn($group) => $group->first());
        }

        $teamBalances = $teams->map(function ($team) use ($isPast, $allSnapshots, &$teamDepositSums, &$teamWithdrawSums, &$teamLastSnapshots, $today, $todayStart, $todayEnd) {
            $snapKey = 'team:' . $team->id;
            $snapshot = ($allSnapshots[$snapKey] ?? collect())->first();

            if ($snapshot && $isPast) {
                $balance = (float) $snapshot->amount;
            } else {
                $lastSnap = isset($teamLastSnapshots[$team->id])
                    ? (float) $teamLastSnapshots[$team->id]->amount
                    : (float) $team->overturn;

                $deposits = (float) ($teamDepositSums[$team->id] ?? 0);
                $withdrawals = (float) ($teamWithdrawSums[$team->id] ?? 0);
                $teamCommission = $deposits * $team->commission / 100;
                $payments = (float) DB::table('team_payments')
                    ->where('team_id', $team->id)->where('created_at', '>=', $todayStart)->where('created_at', '<', $todayEnd)->sum('amount');
                $expenses = (float) DB::table('paylira_expenses')
                    ->where('team_id', $team->id)->where('created_at', '>=', $todayStart)->where('created_at', '<', $todayEnd)->sum('amount');
                $partnerPay = (float) DB::table('paylira_partner_payments')
                    ->where('team_id', $team->id)->where('payment_type', '3')
                    ->where('created_at', '>=', $todayStart)->where('created_at', '<', $todayEnd)->sum('amount');
                $interPay = (float) DB::table('intermediary_payments')
                    ->where('team_id', $team->id)->where('payment_type', '3')
                    ->where('created_at', '>=', $todayStart)->where('created_at', '<', $todayEnd)->sum('amount');
                $transferOut = (float) DB::table('team_transfers')
                    ->where('from_team_id', $team->id)->where('created_at', '>=', $todayStart)->where('created_at', '<', $todayEnd)->sum('amount');
                $transferIn = (float) DB::table('team_transfers')
                    ->where('to_team_id', $team->id)->where('created_at', '>=', $todayStart)->where('created_at', '<', $todayEnd)->sum('amount');
                $syncs = (float) DB::table('team_syncs')
                    ->where('team_id', $team->id)->where('created_at', '>=', $todayStart)->where('created_at', '<', $todayEnd)->sum('amount');
                $balance = $lastSnap + $deposits - $teamCommission - $withdrawals
                    - $payments - $expenses - $partnerPay - $interPay - $transferOut + $transferIn - $syncs;
            }

            return [
                'name'  => $team->name,
                'value' => round($balance, 2),
            ];
        })->filter(fn($t) => $t['value'] != 0)->values();

        // === Paylira net ===
        $totalDepositCommission = 0;
        $totalWithdrawCommission = 0;
        $totalDeliveryCommission = 0;
        $totalTeamCommission = 0;
        $totalIntermediaryCommission = $intermediaryCases->sum('daily_commission');

        if ($isPast) {
            // Geçmiş: toplu sorgu ile merchant bazlı hesapla
            $depositSums = DB::table('invest')
                ->whereIn('firm_id', $merchantIds)->where('type', 1)->where('status', 3)
                ->where('finalize_date', '>=', $rangeStart)->where('finalize_date', '<', $rangeEnd)
                ->select('firm_id', DB::raw('SUM(amount) as total'))
                ->groupBy('firm_id')->pluck('total', 'firm_id');

            $withdrawSums = DB::table('invest')
                ->whereIn('firm_id', $merchantIds)->where('type', 2)->where('status', 3)
                ->where('finalize_date', '>=', $rangeStart)->where('finalize_date', '<', $rangeEnd)
                ->select('firm_id', DB::raw('SUM(amount) as total'))
                ->groupBy('firm_id')->pluck('total', 'firm_id');

            $deliverySums = DB::table('merchant_payments')
                ->whereIn('merchant_id', $merchantIds)
                ->where('created_at', '>=', $rangeStart)->where('created_at', '<', $rangeEnd)
                ->select('merchant_id', DB::raw('SUM(delivery_commission_amount) as total'))
                ->groupBy('merchant_id')->pluck('total', 'merchant_id');

            $teamCommSums = DB::table('invest')
                ->join('teams', 'invest.team_id', '=', 'teams.id')
                ->whereIn('invest.firm_id', $merchantIds)
                ->where('invest.type', 1)->where('invest.status', 3)
                ->where('invest.finalize_date', '>=', $rangeStart)->where('invest.finalize_date', '<', $rangeEnd)
                ->select('invest.firm_id', DB::raw('SUM(invest.amount * teams.commission / 100) as total'))
                ->groupBy('invest.firm_id')->pluck('total', 'invest.firm_id');

            foreach ($allMerchants as $m) {
                $dep = (float) ($depositSums[$m->id] ?? 0);
                $wd = (float) ($withdrawSums[$m->id] ?? 0);
                $totalDepositCommission += $dep * $m->commission / 100;
                $totalWithdrawCommission += $wd * $m->withdrawCommission / 100;
                $totalDeliveryCommission += (float) ($deliverySums[$m->id] ?? 0);
                $totalTeamCommission += (float) ($teamCommSums[$m->id] ?? 0);
            }
        } else {
            // Bugün: zaten toplanan verilerden hesapla
            foreach ($allMerchants as $m) {
                $dep = (float) ($todayDeposits[$m->id] ?? 0);
                $wd = (float) ($todayWithdrawals[$m->id] ?? 0);
                $totalDepositCommission += $dep * $m->commission / 100;
                $totalWithdrawCommission += $wd * $m->withdrawCommission / 100;
            }

            $totalDeliveryCommission = (float) DB::table('merchant_payments')
                ->whereIn('merchant_id', $merchantIds)
                ->where('created_at', '>=', $todayStart)->where('created_at', '<', $todayEnd)
                ->sum('delivery_commission_amount');

            $totalTeamCommission = (float) DB::table('invest')
                ->join('teams', 'invest.team_id', '=', 'teams.id')
                ->whereIn('invest.firm_id', $merchantIds)
                ->where('invest.type', 1)->where('invest.status', 3)
                ->where('invest.finalize_date', '>=', $todayStart)->where('invest.finalize_date', '<', $todayEnd)
                ->selectRaw('SUM(invest.amount * teams.commission / 100) as total')
                ->value('total') ?? 0;
        }

        $totalPayliraBrut = $totalDepositCommission + $totalWithdrawCommission + $totalDeliveryCommission;

        // Paylira net — geçmiş: snapshot, bugün: devir + canlı
        $payliraSnapKey = 'paylira:';
        $payliraSnapshot = ($allSnapshots[$payliraSnapKey] ?? collect())->first();

        if ($payliraSnapshot && $isPast) {
            $payliraNet = (float) $payliraSnapshot->amount;
        } else {
            $previousPaylira = (float) DB::table('daily_case_snapshots')
                ->where('entity_type', 'paylira')
                ->whereNull('entity_id')
                ->where('snapshot_date', '<', $today)
                ->orderByDesc('snapshot_date')
                ->value('amount') ?? 0;

            $todayExpenses = (float) DB::table('paylira_expenses')
                ->where('created_at', '>=', $todayStart)->where('created_at', '<', $todayEnd)->sum('amount');
            $todayPartnerPayments = (float) DB::table('paylira_partner_payments')
                ->where('created_at', '>=', $todayStart)->where('created_at', '<', $todayEnd)->sum('amount');
            $todayPartnerCapitals = (float) DB::table('partner_capitals')
                ->where('created_at', '>=', $todayStart)->where('created_at', '<', $todayEnd)->sum('amount');
            $todayNet = $totalPayliraBrut - $totalTeamCommission - $totalIntermediaryCommission - $todayExpenses - $todayPartnerPayments + $todayPartnerCapitals;
            $payliraNet = $previousPaylira + $todayNet;
        }

        // Fon depoları — geçmiş için doğrudan snapshot, bugün için son snapshot + sonraki hareketler
        $fundStorageSnaps = $isPast
            ? DB::table('daily_case_snapshots')
                ->where('entity_type', 'fund_storage')
                ->where('snapshot_date', $dateTo)
                ->get()
                ->keyBy('entity_id')
            : collect();

        $fundStorages = DB::table('fund_storages')
            ->where('status', 1)
            ->select('id', 'name', 'type', 'balance', 'wallet_address')
            ->orderBy('type')
            ->orderBy('name')
            ->get()
            ->map(function ($fs) use ($isPast, $fundStorageSnaps, $today, $todayStart, $todayEnd) {
                if ($isPast) {
                    $fs->balance = isset($fundStorageSnaps[$fs->id])
                        ? (float) $fundStorageSnaps[$fs->id]->amount
                        : 0;
                } else {
                    // Anlık bakiye: fund_storages.balance kolonu zaten her hareketle real-time güncellenir
                    $fs->balance = round((float) $fs->balance, 2);
                }
                $fs->chain_balance = null;
                if ($fs->type == 2 && $fs->wallet_address && ! $isPast) {
                    $fs->chain_balance = $this->getTronUsdtBalance($fs->wallet_address);
                }
                return $fs;
            });

        // Nakit kaynaklar (type 1,2) kasaya dahil; alacak/verecek (3,4) kasa dışı ayrı gösterilir
        $cashStorages = $fundStorages->filter(fn ($fs) => in_array((int) $fs->type, [1, 2], true))->values();
        $receivablePayables = $fundStorages->filter(fn ($fs) => in_array((int) $fs->type, [3, 4], true))->values();

        return response()->json([
            'merchant_cases'           => $merchantCases,
            'total_merchant_case'      => $merchantCases->sum('value'),
            'intermediary_cases'       => $intermediaryCases,
            'total_intermediary'       => $intermediaryCases->sum('value'),
            'paylira_net'              => round($payliraNet, 2),
            'team_balances'            => $teamBalances,
            'total_team_balance'       => $teamBalances->sum('value'),
            'fund_storages'            => $cashStorages,
            'total_fund_storage'       => $cashStorages->sum('balance'),
            'receivable_payables'      => $receivablePayables,
            'total_receivable_payable' => $receivablePayables->sum('balance'),
        ]);
    }

    public function index(Request $request): JsonResponse
    {
        $this->denyMerchant();
        $request->validate([
            'date_from' => 'nullable|date',
            'date_to'   => 'nullable|date',
        ]);

        $dateFrom = $request->date_from ?? now()->toDateString();
        $dateTo = $request->date_to ?? now()->toDateString();
        $rangeStart = $dateFrom . ' 00:00:00';
        $rangeEnd   = date('Y-m-d 00:00:00', strtotime($dateTo . ' +1 day'));

        $merchants = DB::table('merchantUser')
            ->where('status', '1')
            ->select('id', 'name', 'commission', 'withdrawCommission')
            ->get();

        $merchantIds = $merchants->pluck('id')->toArray();

        // Toplu sorgular
        $depositSums = DB::table('invest')
            ->whereIn('firm_id', $merchantIds)->where('type', 1)->where('status', 3)
            ->where('finalize_date', '>=', $rangeStart)->where('finalize_date', '<', $rangeEnd)
            ->select('firm_id', DB::raw('SUM(amount) as total'))
            ->groupBy('firm_id')->pluck('total', 'firm_id');

        $withdrawSums = DB::table('invest')
            ->whereIn('firm_id', $merchantIds)->where('type', 2)->where('status', 3)
            ->where('finalize_date', '>=', $rangeStart)->where('finalize_date', '<', $rangeEnd)
            ->select('firm_id', DB::raw('SUM(amount) as total'))
            ->groupBy('firm_id')->pluck('total', 'firm_id');

        $teamCommSums = DB::table('invest')
            ->join('teams', 'invest.team_id', '=', 'teams.id')
            ->whereIn('invest.firm_id', $merchantIds)
            ->where('invest.type', 1)->where('invest.status', 3)
            ->where('invest.finalize_date', '>=', $rangeStart)->where('invest.finalize_date', '<', $rangeEnd)
            ->select('invest.firm_id', DB::raw('SUM(invest.amount * teams.commission / 100) as total'))
            ->groupBy('invest.firm_id')->pluck('total', 'invest.firm_id');

        $report = $merchants->map(function ($merchant) use ($depositSums, $withdrawSums, $teamCommSums) {
            $totalDeposit = (float) ($depositSums[$merchant->id] ?? 0);
            $totalWithdraw = (float) ($withdrawSums[$merchant->id] ?? 0);

            $depositCommission = $totalDeposit * $merchant->commission / 100;
            $withdrawCommission = $totalWithdraw * $merchant->withdrawCommission / 100;
            $payliraBrut = $depositCommission + $withdrawCommission;

            $teamCommission = (float) ($teamCommSums[$merchant->id] ?? 0);

            $payliraIntermediary = $this->calcIntermediaryCommission($merchant->id, 1, $totalDeposit);
            $merchantIntermediary = $this->calcIntermediaryCommission($merchant->id, 2, $totalDeposit);
            $payliraNet = $payliraBrut - $teamCommission - $payliraIntermediary - $merchantIntermediary;

            return [
                'merchant'               => $merchant->name,
                'total_deposit'          => round($totalDeposit, 2),
                'total_withdraw'         => round($totalWithdraw, 2),
                'paylira_commission_rate' => $merchant->commission,
                'withdraw_commission_rate'=> $merchant->withdrawCommission,
                'paylira_brut'           => round($payliraBrut, 2),
                'team_commission'        => round($teamCommission, 2),
                'paylira_intermediary'   => round($payliraIntermediary, 2),
                'merchant_intermediary'  => round($merchantIntermediary, 2),
                'paylira_net'            => round($payliraNet, 2),
            ];
        });

        $totals = [
            'total_deposit'          => $report->sum('total_deposit'),
            'paylira_brut'           => $report->sum('paylira_brut'),
            'team_commission'        => $report->sum('team_commission'),
            'paylira_intermediary'   => $report->sum('paylira_intermediary'),
            'merchant_intermediary'  => $report->sum('merchant_intermediary'),
            'paylira_net'            => $report->sum('paylira_net'),
        ];

        return response()->json([
            'report' => $report,
            'totals' => $totals,
        ]);
    }

    private function calcIntermediaryCommission(int $merchantId, int $type, float $totalDeposit): float
    {
        $rate = (float) DB::table('new_intermediary_merchant')
            ->join('new_intermediaries', 'new_intermediary_merchant.intermediary_id', '=', 'new_intermediaries.id')
            ->where('new_intermediary_merchant.merchant_id', $merchantId)
            ->where('new_intermediary_merchant.status', 1)
            ->where('new_intermediaries.type', $type)
            ->where('new_intermediaries.status', 1)
            ->sum('new_intermediary_merchant.commission_rate');

        return $totalDeposit * $rate / 100;
    }

    private function getTronUsdtBalance(string $address): ?float
    {
        try {
            $response = Http::timeout(5)->get("https://api.trongrid.io/v1/accounts/{$address}");
            if (! $response->ok()) return null;

            $trc20 = $response->json('data.0.trc20', []);
            $usdtContract = 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t';

            foreach ($trc20 as $token) {
                if (isset($token[$usdtContract])) {
                    return round((float) bcdiv($token[$usdtContract], '1000000', 6), 2);
                }
            }
            return 0;
        } catch (\Exception) {
            return null;
        }
    }
}
