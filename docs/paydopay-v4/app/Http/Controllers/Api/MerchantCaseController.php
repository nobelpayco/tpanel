<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;

class MerchantCaseController extends Controller
{
    private function denyMerchant(): void
    {
        if (auth()->user()?->isMerchant()) {
            abort(403, 'Bu sayfaya erişim yetkiniz yok.');
        }
    }

    public function index(): JsonResponse
    {
        $this->denyMerchant();
        $today = now()->toDateString();

        $allMerchants = DB::table('merchantUser')
            ->where('status', '1')
            ->select('id', 'name', 'caseNow', 'commission', 'withdrawCommission', 'group_id')
            ->get();

        $groups = DB::table('merchant_groups')->where('status', 1)->get()->keyBy('id');
        $merchantIds = $allMerchants->pluck('id')->toArray();

        // Toplu bugünkü veriler
        $todayDeposits = DB::table('invest')
            ->whereIn('firm_id', $merchantIds)->where('type', 1)->where('status', 3)
            ->whereDate('finalize_date', $today)
            ->select('firm_id', DB::raw('SUM(amount) as total'))
            ->groupBy('firm_id')->pluck('total', 'firm_id');

        $todayWithdrawals = DB::table('invest')
            ->whereIn('firm_id', $merchantIds)->where('type', 2)->where('status', 3)
            ->whereDate('finalize_date', $today)
            ->select('firm_id', DB::raw('SUM(amount) as total'))
            ->groupBy('firm_id')->pluck('total', 'firm_id');

        $todayPayments = DB::table('merchant_payments')
            ->whereIn('merchant_id', $merchantIds)->whereDate('created_at', $today)
            ->select('merchant_id', DB::raw('SUM(amount) as total'))
            ->groupBy('merchant_id')->pluck('total', 'merchant_id');

        $processedGroupIds = [];
        $merchantList = collect();

        foreach ($allMerchants as $m) {
            if ($m->group_id && isset($groups[$m->group_id])) {
                if (in_array($m->group_id, $processedGroupIds)) continue;
                $processedGroupIds[] = $m->group_id;
                $groupMerchants = $allMerchants->where('group_id', $m->group_id);
                $groupIds = $groupMerchants->pluck('id')->toArray();
                $displayName = $groups[$m->group_id]->name;
                $entityType = 'merchant_group';
                $entityId = $m->group_id;
                $totalCaseNow = $groupMerchants->sum('caseNow');
            } else {
                $groupMerchants = collect([$m]);
                $groupIds = [$m->id];
                $displayName = $m->name;
                $entityType = 'merchant';
                $entityId = $m->id;
                $totalCaseNow = $m->caseNow;
            }

            $lastSnap = (float) DB::table('daily_case_snapshots')
                ->where('entity_type', $entityType)
                ->where('entity_id', $entityId)
                ->where('snapshot_date', '<', $today)
                ->orderByDesc('snapshot_date')
                ->value('amount') ?? $totalCaseNow;

            $netDep = 0; $netWd = 0; $pay = 0;
            foreach ($groupMerchants as $gm) {
                $dep = (float) ($todayDeposits[$gm->id] ?? 0);
                $wd = (float) ($todayWithdrawals[$gm->id] ?? 0);
                $netDep += $dep - ($dep * $gm->commission / 100);
                $netWd += $wd + ($wd * $gm->withdrawCommission / 100);
                $pay += (float) ($todayPayments[$gm->id] ?? 0);
            }

            $merchantList->push([
                'id'         => $entityId,
                'name'       => $displayName,
                'group_id'   => $m->group_id,
                'group_name' => $m->group_id ? $displayName : null,
                'value'      => round($lastSnap + $netDep - $netWd - $pay, 2),
            ]);
        }

        return response()->json([
            'merchants'  => $merchantList,
            'total_case' => $merchantList->sum('value'),
        ]);
    }

    public function show(int $merchantId, \Illuminate\Http\Request $request): JsonResponse
    {
        $isGroup = $request->get('type') === 'group';

        // Merchant kullanıcı sadece kendi merchant'ını (veya grubunu) görebilir
        $user = auth()->user();
        if ($user?->isMerchant()) {
            $allowed = $isGroup
                ? (int) $user->merchant_group_id === $merchantId
                : in_array($merchantId, $user->merchant_ids);
            if (! $allowed) {
                abort(403, 'Bu sayfaya erişim yetkiniz yok.');
            }
        }

        $dateFrom = $request->get('date_from');
        $dateTo = $request->get('date_to');

        if ($isGroup) {
            $group = DB::table('merchant_groups')->where('id', $merchantId)->first();
            if (! $group) return response()->json(['message' => 'Grup bulunamadı.'], 404);

            $groupMerchants = DB::table('merchantUser')
                ->where('group_id', $merchantId)
                ->select('id', 'name', 'caseNow', 'commission', 'withdrawCommission', 'deliveryCommission')
                ->get();

            $merchantIds = $groupMerchants->pluck('id')->toArray();
            $entityType = 'merchant_group';
            $entityId = $merchantId;
            $displayName = $group->name;
            $totalCaseNow = $groupMerchants->sum('caseNow');
            $mainMerchant = $groupMerchants->first();
        } else {
            $merchant = DB::table('merchantUser')
                ->where('id', $merchantId)
                ->select('id', 'name', 'caseNow', 'commission', 'withdrawCommission', 'deliveryCommission')
                ->first();

            if (! $merchant) return response()->json(['message' => 'Merchant bulunamadı.'], 404);

            $groupMerchants = collect([$merchant]);
            $merchantIds = [$merchant->id];
            $entityType = 'merchant';
            $entityId = $merchant->id;
            $displayName = $merchant->name;
            $totalCaseNow = $merchant->caseNow;
            $mainMerchant = $merchant;
        }

        // Gün sonu kasaları (snapshot'lardan, bugün hariç — bugün anlık kasada)
        $today = now()->toDateString();
        $snapshotQuery = DB::table('daily_case_snapshots')
            ->where('entity_type', $entityType)
            ->where('entity_id', $entityId)
            ->where('snapshot_date', '<', $today)
            ->orderByDesc('snapshot_date');

        if ($dateFrom && $dateTo) {
            $snapshotQuery->where('snapshot_date', '>=', $dateFrom)->where('snapshot_date', '<=', $dateTo);
        } else {
            $snapshotQuery->limit(30);
        }

        $dailyCases = $snapshotQuery->get()
            ->map(function ($row) {
                $details = json_decode($row->details, true) ?? [];

                return [
                    'date'                       => $row->snapshot_date,
                    'amount'                     => $row->amount,
                    'previous_balance'           => $details['previous_balance'] ?? 0,
                    'deposits'                   => $details['deposits'] ?? 0,
                    'withdrawals'                => $details['withdrawals'] ?? 0,
                    'deposit_commission_amount'  => $details['deposit_commission_amount'] ?? 0,
                    'withdraw_commission_amount' => $details['withdraw_commission_amount'] ?? 0,
                    'daily_change'               => $details['daily_change'] ?? 0,
                    'payments'                   => $details['payments'] ?? 0,
                    'payment_commissions'        => $details['payment_commissions'] ?? 0,
                ];
            });

        // Anlık kasa = son snapshot gün sonu + bugünkü değişim
        $lastSnapshot = (float) DB::table('daily_case_snapshots')
            ->where('entity_type', $entityType)
            ->where('entity_id', $entityId)
            ->where('snapshot_date', '<', $today)
            ->orderByDesc('snapshot_date')
            ->value('amount') ?? $totalCaseNow;

        $todayDeposits = 0; $todayWithdrawals = 0; $todayPayments = 0;
        $netDeposit = 0; $netWithdraw = 0; $todayPaymentCommissions = 0;
        $todayDepositCommAmount = 0; $todayWithdrawCommAmount = 0;

        foreach ($groupMerchants as $gm) {
            $dep = (float) DB::table('invest')
                ->where('firm_id', $gm->id)->where('type', 1)->where('status', 3)
                ->whereDate('finalize_date', $today)->sum('amount');
            $wd = (float) DB::table('invest')
                ->where('firm_id', $gm->id)->where('type', 2)->where('status', 3)
                ->whereDate('finalize_date', $today)->sum('amount');
            $pay = (float) DB::table('merchant_payments')
                ->where('merchant_id', $gm->id)->whereDate('created_at', $today)->sum('amount');
            $payComm = (float) DB::table('merchant_payments')
                ->where('merchant_id', $gm->id)->whereDate('created_at', $today)->sum('delivery_commission_amount');

            $depComm = $dep * $gm->commission / 100;
            $wdComm = $wd * $gm->withdrawCommission / 100;

            $todayDeposits += $dep;
            $todayWithdrawals += $wd;
            $todayPayments += $pay;
            $todayPaymentCommissions += $payComm;
            $todayDepositCommAmount += $depComm;
            $todayWithdrawCommAmount += $wdComm;
            $netDeposit += $dep - $depComm;
            $netWithdraw += $wd + $wdComm;
        }

        $dailyChange = $netDeposit - $netWithdraw - $todayPayments;
        $currentCase = $lastSnapshot + $dailyChange;

        // Bugünkü canlı satırı tablonun başına ekle
        $todayRow = [
            'date'                       => $today,
            'amount'                     => round($currentCase, 2),
            'previous_balance'           => round($lastSnapshot, 2),
            'deposits'                   => round($todayDeposits, 2),
            'withdrawals'                => round($todayWithdrawals, 2),
            'deposit_commission_amount'  => round($todayDepositCommAmount, 2),
            'withdraw_commission_amount' => round($todayWithdrawCommAmount, 2),
            'daily_change'               => round($dailyChange, 2),
            'payments'                   => round($todayPayments, 2),
            'payment_commissions'        => round($todayPaymentCommissions, 2),
            'is_today'                   => true,
        ];

        $dailyCases->prepend($todayRow);

        // Grup ise merchant tabları için ayrı ayrı veri
        $tabs = [];
        if ($isGroup && $groupMerchants->count() > 1) {
            foreach ($groupMerchants as $gm) {
                $tabs[] = [
                    'id'                  => $gm->id,
                    'name'                => $gm->name,
                    'commission'          => $gm->commission,
                    'withdrawCommission'  => $gm->withdrawCommission,
                    'deliveryCommission'  => $gm->deliveryCommission,
                ];
            }
        }

        return response()->json([
            'merchant'     => [
                'id'                  => $entityId,
                'name'                => $displayName,
                'commission'          => $mainMerchant->commission,
                'withdrawCommission'  => $mainMerchant->withdrawCommission,
                'deliveryCommission'  => $mainMerchant->deliveryCommission,
            ],
            'is_group'     => $isGroup,
            'tabs'         => $tabs,
            'current_case' => round($currentCase, 2),
            'daily_cases'  => $dailyCases,
        ]);
    }

    public function payliraDailyNet(\Illuminate\Http\Request $request): JsonResponse
    {
        $this->denyMerchant();
        $dateFrom = $request->get('date_from');
        $dateTo = $request->get('date_to');
        $today = now()->toDateString();

        $query = DB::table('daily_case_snapshots')
            ->where('entity_type', 'paylira')
            ->whereNull('entity_id')
            ->where('snapshot_date', '<', $today)
            ->orderByDesc('snapshot_date');

        if ($dateFrom && $dateTo) {
            $query->where('snapshot_date', '>=', $dateFrom)->where('snapshot_date', '<=', $dateTo);
        } else {
            $query->limit(30);
        }

        $snapshots = $query->get();

        $result = $snapshots->map(function ($row) {
            $details = json_decode($row->details, true) ?? [];

            $expenses = (float) DB::table('paylira_expenses')
                ->whereDate('created_at', $row->snapshot_date)
                ->sum('amount');
            $partnerPayments = (float) DB::table('paylira_partner_payments')
                ->whereDate('created_at', $row->snapshot_date)
                ->sum('amount');

            $partnerCapitals = (float) DB::table('partner_capitals')
                ->whereDate('created_at', $row->snapshot_date)
                ->sum('amount');

            return [
                'date'              => $row->snapshot_date,
                'previous_balance'  => $details['previous_balance'] ?? 0,
                'merchants'         => $details['merchants'] ?? [],
                'daily_total'       => $details['daily_net'] ?? 0,
                'expenses'          => round($expenses, 2),
                'partner_payments'  => round($partnerPayments, 2),
                'partner_capitals'  => round($partnerCapitals, 2),
                'cumulative'        => $row->amount,
            ];
        });

        // Bugünkü canlı satır
        $showToday = !$dateFrom || ($dateFrom <= $today && $dateTo >= $today);
        if ($showToday) {
            $previousPaylira = (float) DB::table('daily_case_snapshots')
                ->where('entity_type', 'paylira')
                ->whereNull('entity_id')
                ->where('snapshot_date', '<', $today)
                ->orderByDesc('snapshot_date')
                ->value('amount') ?? 0;

            $merchants = DB::table('merchantUser')->where('status', '1')
                ->select('id', 'name', 'commission', 'withdrawCommission', 'group_id')->get();

            $groups = DB::table('merchant_groups')->where('status', 1)->pluck('name', 'id');
            $processedGroupIds = [];
            $merchantNets = [];
            $totalDailyNet = 0;

            foreach ($merchants as $m) {
                if ($m->group_id && isset($groups[$m->group_id])) {
                    if (in_array($m->group_id, $processedGroupIds)) continue;
                    $processedGroupIds[] = $m->group_id;
                    $groupMerchants = $merchants->where('group_id', $m->group_id);
                    $displayName = $groups[$m->group_id];
                } else {
                    $groupMerchants = collect([$m]);
                    $displayName = $m->name;
                }

                $net = 0;
                $depositProfit = 0;
                $withdrawProfit = 0;
                $deliveryProfitTotal = 0;
                foreach ($groupMerchants as $gm) {
                    $dep = (float) DB::table('invest')->where('firm_id', $gm->id)->where('type', 1)->where('status', 3)->whereDate('finalize_date', $today)->sum('amount');
                    $wd = (float) DB::table('invest')->where('firm_id', $gm->id)->where('type', 2)->where('status', 3)->whereDate('finalize_date', $today)->sum('amount');
                    $depComm = $dep * $gm->commission / 100;
                    $wdComm = $wd * $gm->withdrawCommission / 100;
                    $delComm = (float) DB::table('merchant_payments')->where('merchant_id', $gm->id)->whereDate('created_at', $today)->sum('delivery_commission_amount');
                    $delProfit = (float) DB::table('merchant_payments')->where('merchant_id', $gm->id)->whereDate('created_at', $today)->sum('delivery_profit');
                    $tc = (float) DB::table('invest')->join('teams', 'invest.team_id', '=', 'teams.id')->where('invest.firm_id', $gm->id)->where('invest.type', 1)->where('invest.status', 3)->whereDate('invest.finalize_date', $today)->selectRaw('SUM(invest.amount * teams.commission / 100) as total')->value('total') ?? 0;
                    $net += $depComm + $wdComm + $delComm + $delProfit - $tc;
                    $depositProfit += $depComm;
                    $withdrawProfit += $wdComm;
                    $deliveryProfitTotal += $delComm + $delProfit;
                }

                // Aracı komisyonları (hem merchant hem takım bazlı)
                $interComm = 0;
                $intermediaryTeamRates = DB::table('new_intermediary_team')
                    ->join('new_intermediaries', 'new_intermediary_team.intermediary_id', '=', 'new_intermediaries.id')
                    ->where('new_intermediary_team.status', 1)
                    ->where('new_intermediaries.status', 1)
                    ->select('new_intermediary_team.team_id', DB::raw('SUM(new_intermediary_team.commission_rate) as total_rate'))
                    ->groupBy('new_intermediary_team.team_id')
                    ->pluck('total_rate', 'team_id');

                foreach ($groupMerchants as $gm) {
                    $interRates = DB::table('new_intermediary_merchant')
                        ->join('new_intermediaries', 'new_intermediary_merchant.intermediary_id', '=', 'new_intermediaries.id')
                        ->where('new_intermediary_merchant.merchant_id', $gm->id)
                        ->where('new_intermediary_merchant.status', 1)
                        ->where('new_intermediaries.status', 1)
                        ->pluck('new_intermediary_merchant.commission_rate');
                    $dep = (float) DB::table('invest')->where('firm_id', $gm->id)->where('type', 1)->where('status', 3)->whereDate('finalize_date', $today)->sum('amount');
                    foreach ($interRates as $rate) {
                        $interComm += $dep * (float) $rate / 100;
                    }
                    $teamDeps = DB::table('invest')->where('firm_id', $gm->id)->where('type', 1)->where('status', 3)->whereDate('finalize_date', $today)
                        ->select('team_id', DB::raw('SUM(amount) as total'))->groupBy('team_id')->pluck('total', 'team_id');
                    foreach ($teamDeps as $teamId => $teamDep) {
                        if (isset($intermediaryTeamRates[$teamId])) {
                            $interComm += $teamDep * $intermediaryTeamRates[$teamId] / 100;
                        }
                    }
                }
                $net -= $interComm;

                // Yatırım karı net = depComm - teamComm - interComm (team & aracı sadece yatırımdan düşülüyor)
                $tcTotal = 0;
                foreach ($groupMerchants as $gm) {
                    $tcTotal += (float) DB::table('invest')->join('teams', 'invest.team_id', '=', 'teams.id')->where('invest.firm_id', $gm->id)->where('invest.type', 1)->where('invest.status', 3)->whereDate('invest.finalize_date', $today)->selectRaw('SUM(invest.amount * teams.commission / 100) as total')->value('total') ?? 0;
                }
                $depositProfitNet = $depositProfit - $tcTotal - $interComm;

                $merchantNets[] = [
                    'name'            => $displayName,
                    'net'             => round($net, 2),
                    'deposit_profit'  => round($depositProfitNet, 2),
                    'withdraw_profit' => round($withdrawProfit, 2),
                    'delivery_profit' => round($deliveryProfitTotal, 2),
                ];
                $totalDailyNet += $net;
            }

            $todayExpenses = (float) DB::table('paylira_expenses')
                ->whereDate('created_at', $today)
                ->sum('amount');

            $todayPartnerPayments = (float) DB::table('paylira_partner_payments')
                ->whereDate('created_at', $today)
                ->sum('amount');

            $todayPartnerCapitals = (float) DB::table('partner_capitals')
                ->whereDate('created_at', $today)
                ->sum('amount');

            $result->prepend([
                'date'              => $today,
                'previous_balance'  => round($previousPaylira, 2),
                'merchants'         => $merchantNets,
                'daily_total'       => round($totalDailyNet, 2),
                'expenses'          => round($todayExpenses, 2),
                'partner_payments'  => round($todayPartnerPayments, 2),
                'partner_capitals'  => round($todayPartnerCapitals, 2),
                'cumulative'        => round($previousPaylira + $totalDailyNet - $todayExpenses - $todayPartnerPayments + $todayPartnerCapitals, 2),
                'is_today'          => true,
            ]);
        }

        return response()->json($result);
    }

    public function payments(int $merchantId, Request $request): JsonResponse
    {
        $isGroup = $request->get('type') === 'group';

        // Merchant kullanıcı sadece kendi merchant'ının (veya grubunun) ödemelerini görebilir
        $user = auth()->user();
        if ($user?->isMerchant()) {
            $allowed = $isGroup
                ? (int) $user->merchant_group_id === $merchantId
                : in_array($merchantId, $user->merchant_ids);
            if (! $allowed) {
                abort(403, 'Bu sayfaya erişim yetkiniz yok.');
            }
        }

        $date = $request->get('date');
        $dateFrom = $request->get('date_from');
        $dateTo = $request->get('date_to');

        // Grup ise tüm merchant'ların ödemelerini getir
        if ($request->get('type') === 'group') {
            $groupMerchantIds = DB::table('merchantUser')
                ->where('group_id', $merchantId)
                ->pluck('id')->toArray();
            $query = DB::table('merchant_payments')
                ->whereIn('merchant_id', $groupMerchantIds)
                ->orderByDesc('created_at');
        } else {
            $query = DB::table('merchant_payments')
                ->where('merchant_id', $merchantId)
                ->orderByDesc('created_at');
        }

        if ($date) {
            $query->whereDate('created_at', $date);
        } elseif ($dateFrom && $dateTo) {
            $query->whereDate('created_at', '>=', $dateFrom)
                  ->whereDate('created_at', '<=', $dateTo);
        }

        $payments = $query->limit(200)->get()->map(function ($p) {
            $merchantName = DB::table('merchantUser')->where('id', $p->merchant_id)->value('name');

            return [
                'id'                        => $p->id,
                'merchant_id'               => $p->merchant_id,
                'merchant_name'             => $merchantName,
                'payment_type'              => $p->payment_type,
                'amount'                    => $p->amount,
                'delivery_commission_rate'   => $p->delivery_commission_rate,
                'delivery_commission_amount' => $p->delivery_commission_amount,
                'crypto_quantity'            => $p->crypto_quantity,
                'crypto_rate'               => $p->crypto_rate,
                'tx_link'                   => $p->tx_link,
                'description'               => $p->description,
                'created_at'                => $p->created_at,
            ];
        });

        $totalAmount = $payments->sum('amount');

        return response()->json([
            'payments' => $payments,
            'total'    => round($totalAmount, 2),
        ]);
    }

    public function addPayment(int $merchantId, Request $request): JsonResponse
    {
        $this->denyMerchant();
        $request->validate([
            'payment_type'    => 'required|in:1,2',
            'amount'          => 'required|numeric|not_in:0',
            'crypto_quantity' => 'nullable|numeric|min:0',
            'crypto_rate'     => 'nullable|numeric|min:0',
            'paid_amount'     => 'nullable|numeric|min:0',
            'tx_link'         => 'nullable|string|max:500',
            'fund_storage_id' => 'nullable|integer',
            'target_merchant_id' => 'nullable|integer',
            'description'     => 'nullable|string|max:1000',
            'payment_date'    => 'nullable|date',
            'is_group'        => 'nullable|boolean',
        ]);

        // Eğer grup ise, hedef merchant'ı belirle
        if ($request->is_group) {
            $group = DB::table('merchant_groups')->where('id', $merchantId)->first();
            if (! $group) {
                return response()->json(['message' => 'Grup bulunamadı.'], 404);
            }
            if ($request->target_merchant_id) {
                $merchantId = (int) $request->target_merchant_id;
            } else {
                $first = DB::table('merchantUser')->where('group_id', $group->id)->orderBy('id')->first();
                if (! $first) {
                    return response()->json(['message' => 'Grupta merchant bulunamadı.'], 404);
                }
                $merchantId = (int) $first->id;
            }
        }

        // Kripto ise fon deposu zorunlu
        if ($request->payment_type == 2 && !$request->fund_storage_id) {
            return response()->json(['message' => 'Kripto ödemede fon deposu seçimi zorunludur.'], 422);
        }

        // Fon deposu bakiye kontrolü (TL tutar düşülecek) — sadece pozitif tutarlar için
        if ($request->fund_storage_id) {
            $storage = DB::table('fund_storages')->where('id', $request->fund_storage_id)->first();
            if (!$storage) {
                return response()->json(['message' => 'Fon deposu bulunamadı.'], 404);
            }
            if ((float) $request->amount > 0 && (float) $storage->balance < (float) $request->amount) {
                return response()->json(['message' => 'Depoda yeterli bakiye yok. Mevcut bakiye: ₺' . number_format($storage->balance, 2, ',', '.') ], 422);
            }
        }

        $merchant = DB::table('merchantUser')->where('id', $merchantId)->first();
        if (! $merchant) {
            return response()->json(['message' => 'Merchant bulunamadı.'], 404);
        }

        $deliveryRate = (float) $merchant->deliveryCommission;
        // Negatif tutarlı ödemeler (iade / geri dönen çekim) komisyon hesabına dahil edilmez
        $deliveryAmount = $request->amount > 0 ? round($request->amount * $deliveryRate / 100, 2) : 0;

        // Kripto ödemede teslimat karı = TL Tutar × merchant.deliveryCommission%
        // Kriptoda delivery_commission_amount=0 olur, çift sayım önlenir
        $paidAmount = null;
        $deliveryProfit = null;
        if ($request->payment_type == 2) {
            $deliveryProfit = $request->amount > 0 ? round((float) $request->amount * $deliveryRate / 100, 2) : 0;
            $paidAmount = round((float) $request->amount - $deliveryProfit, 2);
            $deliveryAmount = 0;
        }

        DB::table('merchant_payments')->insert([
            'merchant_id'               => $merchantId,
            'payment_type'              => $request->payment_type,
            'amount'                    => $request->amount,
            'paid_amount'               => $paidAmount,
            'delivery_profit'           => $deliveryProfit,
            'delivery_commission_rate'   => $deliveryRate,
            'delivery_commission_amount' => $deliveryAmount,
            'crypto_quantity'            => $request->payment_type == 2 ? $request->crypto_quantity : null,
            'crypto_rate'               => $request->payment_type == 2 ? $request->crypto_rate : null,
            'tx_link'                   => $request->payment_type == 2 ? $request->tx_link : null,
            'fund_storage_id'           => $request->payment_type == 2 ? $request->fund_storage_id : null,
            'description'               => $request->description,
            'created_by'                => auth()->id(),
            'created_at'                => $request->payment_date ? $request->payment_date . ' ' . now()->format('H:i:s') : now(),
        ]);

        DB::table('merchantUser')
            ->where('id', $merchantId)
            ->decrement('caseNow', $request->amount);

        // Kripto ise fon deposundan TL tutar düş, sonra teslimat karı kar olarak ekle
        if ($request->payment_type == 2 && $request->fund_storage_id) {
            DB::table('fund_storages')
                ->where('id', $request->fund_storage_id)
                ->decrement('balance', $request->amount);

            if ($deliveryProfit > 0) {
                DB::table('fund_storages')
                    ->where('id', $request->fund_storage_id)
                    ->increment('balance', $deliveryProfit);
            } elseif ($deliveryProfit < 0) {
                DB::table('fund_storages')
                    ->where('id', $request->fund_storage_id)
                    ->decrement('balance', abs($deliveryProfit));
            }
        }

        return response()->json(['message' => 'Ödeme eklendi.']);
    }

    public function deletePayment(int $merchantId, int $paymentId): JsonResponse
    {
        $this->denyMerchant();
        $payment = DB::table('merchant_payments')
            ->where('id', $paymentId)
            ->where('merchant_id', $merchantId)
            ->first();

        if (! $payment) {
            return response()->json(['message' => 'Ödeme bulunamadı.'], 404);
        }

        $today = now()->toDateString();
        $paymentDate = date('Y-m-d', strtotime($payment->created_at));

        // Geçmiş günlerdeki ödemeler silinemez
        if ($paymentDate < $today) {
            return response()->json(['message' => 'Sadece bugüne ait ödemeler silinebilir.'], 422);
        }

        // caseNow'a geri ekle
        DB::table('merchantUser')
            ->where('id', $merchantId)
            ->increment('caseNow', $payment->amount);

        // Kripto ise fon deposuna geri ekle (TL tutar geri, teslimat karını geri al)
        if ($payment->fund_storage_id) {
            $deliveryProfit = (float) ($payment->delivery_profit ?? 0);

            DB::table('fund_storages')
                ->where('id', $payment->fund_storage_id)
                ->increment('balance', $payment->amount);

            if ($deliveryProfit > 0) {
                DB::table('fund_storages')
                    ->where('id', $payment->fund_storage_id)
                    ->decrement('balance', $deliveryProfit);
            } elseif ($deliveryProfit < 0) {
                DB::table('fund_storages')
                    ->where('id', $payment->fund_storage_id)
                    ->increment('balance', abs($deliveryProfit));
            }
        }

        // Ödemeyi sil
        DB::table('merchant_payments')->where('id', $paymentId)->delete();

        return response()->json(['message' => 'Ödeme silindi.']);
    }
}
