<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use App\Models\Invest;
use App\Models\User;
use App\Services\MerchantBankService;
use App\Support\TrustScore;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Carbon;
use Illuminate\Support\Facades\DB;

class DashboardController extends Controller
{
    private function scopeQuery($query)
    {
        $user = auth()->user();

        if ($user->hasTeamScope()) {
            $query->where('team_id', $user->team_id);
        } elseif ($user->hasMerchantScope()) {
            $merchantIds = $user->merchant_ids;
            if (count($merchantIds) > 1) {
                $query->whereIn('firm_id', $merchantIds);
            } else {
                $query->where('firm_id', $user->firm_id);
            }
        }

        return $query;
    }

    public function widget(Request $request): JsonResponse
    {
        $today = now()->toDateString();

        // Toplam yatırım / çekim (bugün)
        $totalDeposits = (float) DB::table('invest')
            ->where('type', 1)->where('status', 3)
            ->whereDate('finalize_date', $today)
            ->sum('amount');
        $totalWithdrawals = (float) DB::table('invest')
            ->where('type', 2)->where('status', 3)
            ->whereDate('finalize_date', $today)
            ->sum('amount');

        // Takım kasaları — ortak helper (snapshot + tüm hareketler + team_syncs dahil)
        $teams = DB::table('teams')->select('id', 'name')->get();
        $cashes = app(MerchantBankService::class)->currentCashForTeams($teams->pluck('id')->all());
        $teamRows = [];
        $totalTeamCase = 0;
        foreach ($teams as $t) {
            $cc = (float) ($cashes[(int) $t->id] ?? 0);
            if ($cc != 0) {
                $teamRows[] = ['name' => $t->name, 'case' => round($cc, 2)];
                $totalTeamCase += $cc;
            }
        }

        // Widget JSON (max 12 satır)
        $rows = [];
        $rows[] = ['key' => 'DEPOSIT / WITHDRAW', 'color' => 'main'];
        $rows[] = ['key' => 'Deposit', 'value' => number_format($totalDeposits, 0, ',', '.'), 'color' => 'success'];
        $rows[] = ['key' => 'Withdraw', 'value' => number_format($totalWithdrawals, 0, ',', '.'), 'color' => 'danger'];
        $rows[] = ['key' => ''];

        $showTeams = ['M2', 'M18'];
        $filteredRows = array_filter($teamRows, fn($r) => in_array($r['name'], $showTeams));
        $filteredTotal = array_sum(array_column($filteredRows, 'case'));

        $rows[] = ['key' => 'TEAMS', 'value' => number_format($filteredTotal, 0, ',', '.'), 'color' => 'main'];
        foreach ($filteredRows as $t) {
            $color = $t['case'] >= 0 ? 'success' : 'danger';
            $rows[] = ['key' => $t['name'], 'value' => number_format($t['case'], 0, ',', '.'), 'color' => $color];
        }

        $rows[] = ['key' => ''];
        $rows[] = ['key' => 'LAST UPDATE', 'value' => now()->format('H:i'), 'color' => 'muted'];

        return response()->json($rows);
    }

    public function stats(Request $request): JsonResponse
    {
        $dateFrom = $request->get('date_from', now()->toDateString());
        $dateTo = $request->get('date_to', now()->toDateString());
        $dateFromStart = $dateFrom . ' 00:00:00';
        $dateToEnd = $dateTo . ' 23:59:59';

        // Kullanılabilir IBAN istatistikleri: limitlere uygun olan (üyelere gösterilen) IBAN'lar
        // Effective limit: hem bankAccounts hem teams limit'i birlikte uygulanır.
        $eligible = app(MerchantBankService::class)->eligibleIbans();
        $availableMin = $eligible->isNotEmpty()
            ? (float) $eligible->min(fn ($a) => max((float) $a->min_invest, (float) $a->team_min))
            : 0;
        $availableMax = $eligible->isNotEmpty()
            ? (float) $eligible->max(fn ($a) => min((float) $a->max_invest, (float) $a->team_max))
            : 0;

        return response()->json([
            'total_deposits'             => $this->scopeQuery(Invest::where('type', '1')->where('status', '3')->where('finalize_date', '>=', $dateFromStart)->where('finalize_date', '<=', $dateToEnd))->sum('amount'),
            'total_withdrawals'          => $this->scopeQuery(Invest::where('type', '2')->where('status', '3')->where('finalize_date', '>=', $dateFromStart)->where('finalize_date', '<=', $dateToEnd))->sum('amount'),
            'pending_deposits'           => $this->scopeQuery(Invest::where('type', '1')->whereIn('status', ['1', '2']))->count(),
            'pending_withdrawals'        => $this->scopeQuery(Invest::where('type', '2')->whereIn('status', ['0', '1', '2']))->count(),
            'pending_withdrawals_amount' => $this->scopeQuery(Invest::where('type', '2')->whereIn('status', ['0', '1', '2']))->sum('amount'),
            'available_ibans_count'      => $eligible->count(),
            'available_ibans_min'        => round($availableMin, 2),
            'available_ibans_max'        => round($availableMax, 2),
        ]);
    }

    public function merchantCases(Request $request): JsonResponse
    {
        $user = auth()->user();
        $dateFrom = $request->get('date_from', now()->toDateString());
        $dateTo = $request->get('date_to', now()->toDateString());

        // Team member ise kendi takım kasasını gösterir
        if ($user->hasTeamScope()) {
            return $this->teamCase($user, $dateFrom, $dateTo);
        }

        $allMerchants = DB::table('merchantUser')->where('status', '1')
            ->select('id', 'name', 'caseNow', 'commission', 'withdrawCommission', 'group_id')->get();

        if ($user->hasMerchantScope()) {
            $merchantIds = $user->merchant_ids;
            $allMerchants = $allMerchants->whereIn('id', $merchantIds);
        }

        $groups = DB::table('merchant_groups')->where('status', 1)->get()->keyBy('id');
        $processedGroupIds = [];
        $cases = collect();

        foreach ($allMerchants as $m) {
            if ($m->group_id && isset($groups[$m->group_id])) {
                if (in_array($m->group_id, $processedGroupIds)) continue;
                $processedGroupIds[] = $m->group_id;
                $groupMerchants = $allMerchants->where('group_id', $m->group_id);
                $displayName = $groups[$m->group_id]->name;
                $entityType = 'merchant_group';
                $entityId = $m->group_id;
            } else {
                $groupMerchants = collect([$m]);
                $displayName = $m->name;
                $entityType = 'merchant';
                $entityId = $m->id;
            }

            // dateTo snapshot'ı varsa onu kullan, yoksa canlı hesapla
            $snapshot = DB::table('daily_case_snapshots')
                ->where('entity_type', $entityType)
                ->where('entity_id', $entityId)
                ->where('snapshot_date', $dateTo)
                ->first();

            // Ortalama onay süresi (grup bazlı)
            $groupIds = $groupMerchants->pluck('id')->toArray();
            $avgApprovalSec = (int) DB::table('invest')
                ->whereIn('firm_id', $groupIds)
                ->where('type', 1)->where('status', 3)
                ->whereDate('finalize_date', '>=', $dateFrom)
                ->whereDate('finalize_date', '<=', $dateTo)
                ->whereRaw('TIMESTAMPDIFF(SECOND, created_at, finalize_date) <= 7200')
                ->avg(DB::raw('TIMESTAMPDIFF(SECOND, created_at, finalize_date)')) ?? 0;

            if ($snapshot && $dateTo < now()->toDateString()) {
                // Geçmiş gün: snapshot'tan devir ve detayları al
                $details = json_decode($snapshot->details, true) ?? [];
                $cases->push([
                    'name'            => $displayName,
                    'case_now'        => round($details['previous_balance'] ?? 0, 2),
                    'deposits'        => round($details['deposits'] ?? 0, 2),
                    'commission'      => $groupMerchants->first()->commission,
                    'withdrawals'     => round($details['withdrawals'] ?? 0, 2),
                    'net_case'        => round((float) $snapshot->amount, 2),
                    'avg_approval_sec'=> $avgApprovalSec,
                ]);
            } else {
                // Bugün veya snapshot yok: canlı hesapla
                $lastSnapshot = (float) DB::table('daily_case_snapshots')
                    ->where('entity_type', $entityType)
                    ->where('entity_id', $entityId)
                    ->where('snapshot_date', '<', $dateTo)
                    ->orderByDesc('snapshot_date')
                    ->value('amount') ?? $groupMerchants->sum('caseNow');

                $deposits = 0; $withdrawals = 0; $netDeposit = 0; $netWithdraw = 0; $payments = 0;

                foreach ($groupMerchants as $gm) {
                    $dep = (float) DB::table('invest')
                        ->where('firm_id', $gm->id)->where('type', 1)->where('status', 3)
                        ->whereDate('finalize_date', '>=', $dateFrom)->whereDate('finalize_date', '<=', $dateTo)->sum('amount');
                    $wd = (float) DB::table('invest')
                        ->where('firm_id', $gm->id)->where('type', 2)->where('status', 3)
                        ->whereDate('finalize_date', '>=', $dateFrom)->whereDate('finalize_date', '<=', $dateTo)->sum('amount');

                    $deposits += $dep;
                    $withdrawals += $wd;
                    $netDeposit += $dep - ($dep * $gm->commission / 100);
                    $netWithdraw += $wd + ($wd * $gm->withdrawCommission / 100);
                    $payments += (float) DB::table('merchant_payments')
                        ->where('merchant_id', $gm->id)->whereDate('created_at', '>=', $dateFrom)->whereDate('created_at', '<=', $dateTo)->sum('amount');
                }

                $currentCase = $lastSnapshot + $netDeposit - $netWithdraw - $payments;

                $cases->push([
                    'name'            => $displayName,
                    'case_now'        => round($lastSnapshot, 2),
                    'deposits'        => round($deposits, 2),
                    'commission'      => $groupMerchants->first()->commission,
                    'withdrawals'     => round($withdrawals, 2),
                    'net_case'        => round($currentCase, 2),
                    'avg_approval_sec'=> $avgApprovalSec,
                ]);
            }
        }

        return response()->json([
            'items'      => $cases,
            'total_case' => round($cases->sum('net_case'), 2),
            'type'       => 'merchant',
        ]);
    }

    private function teamCase($user, $dateFrom, $dateTo): JsonResponse
    {
        $team = DB::table('teams')->where('id', $user->team_id)->first();
        if (! $team) {
            return response()->json(['items' => [], 'total_case' => 0, 'type' => 'team']);
        }

        $deposits = (float) DB::table('invest')
            ->where('team_id', $team->id)->where('type', 1)->where('status', 3)
            ->whereDate('finalize_date', '>=', $dateFrom)->whereDate('finalize_date', '<=', $dateTo)->sum('amount');

        $withdrawals = (float) DB::table('invest')
            ->where('team_id', $team->id)->where('type', 2)->where('status', 3)
            ->whereDate('finalize_date', '>=', $dateFrom)->whereDate('finalize_date', '<=', $dateTo)->sum('amount');

        $netDeposit = $deposits - ($deposits * $team->commission / 100);
        $currentCase = $team->overturn + $netDeposit - $withdrawals;

        $item = [
            'name'       => $team->name,
            'case_now'   => round($team->overturn, 2),
            'deposits'   => round($deposits, 2),
            'commission' => $team->commission,
            'withdrawals'=> round($withdrawals, 2),
            'net_case'   => round($currentCase, 2),
        ];

        return response()->json([
            'items'      => [$item],
            'total_case' => round($currentCase, 2),
            'type'       => 'team',
        ]);
    }

    public function yearlyVolume(Request $request): JsonResponse
    {
        $user = auth()->user();
        $dateFrom = $request->get('date_from', now()->toDateString());
        $dateTo = $request->get('date_to', now()->toDateString());

        // Tarih aralığı verilmişse o aralığı göster, yoksa son 30 gün
        $isSingleDay = ($dateFrom === $dateTo);
        $startDate = $isSingleDay ? now()->subDays(29)->toDateString() : $dateFrom;
        $endDate = $isSingleDay ? $dateTo : $dateTo;

        $baseDeposits = Invest::where('type', 1)->where('status', 3)
            ->whereDate('finalize_date', '>=', $startDate)->whereDate('finalize_date', '<=', $endDate);
        $baseWithdrawals = Invest::where('type', 2)->where('status', 3)
            ->whereDate('finalize_date', '>=', $startDate)->whereDate('finalize_date', '<=', $endDate);

        $deposits = $this->scopeQuery($baseDeposits)
            ->select(DB::raw('DATE(finalize_date) as day'), DB::raw('SUM(amount) as total'))
            ->groupBy('day')
            ->pluck('total', 'day')
            ->toArray();

        $withdrawals = $this->scopeQuery($baseWithdrawals)
            ->select(DB::raw('DATE(finalize_date) as day'), DB::raw('SUM(amount) as total'))
            ->groupBy('day')
            ->pluck('total', 'day')
            ->toArray();

        $days = [];
        $depositData = [];
        $withdrawalData = [];

        $start = Carbon::parse($startDate);
        $end = Carbon::parse($endDate);
        $diffDays = $start->diffInDays($end);

        for ($i = 0; $i <= $diffDays; $i++) {
            $date = $start->copy()->addDays($i);
            $key = $date->toDateString();
            $days[] = $date->format('d M');
            $depositData[] = round($deposits[$key] ?? 0);
            $withdrawalData[] = round($withdrawals[$key] ?? 0);
        }

        return response()->json([
            'days'        => $days,
            'deposits'    => $depositData,
            'withdrawals' => $withdrawalData,
        ]);
    }

    public function recentTransactions(Request $request): JsonResponse
    {
        $user = auth()->user();
        $isTeamMember = $user->hasTeamScope();
        $dateFrom = $request->get('date_from', now()->toDateString());
        $dateTo = $request->get('date_to', now()->toDateString());

        $query = Invest::with(['team:id,name', 'bankAccount.bank:id,name']);

        // Team/Agent merchant adını göremez
        if (!$isTeamMember) {
            $query->with(['merchant:id,name']);
        }

        $this->scopeQuery($query);

        $query->whereDate('created_at', '>=', $dateFrom)->whereDate('created_at', '<=', $dateTo);

        $transactions = $query->orderByDesc('id')
            ->limit(20)
            ->get()
            ->map(function ($tx) use ($isTeamMember) {
                $duration = null;
                if ($tx->finalize_date && $tx->created_at) {
                    $diff = $tx->created_at->diffInSeconds(Carbon::parse($tx->finalize_date));
                    if ($diff <= 7200) {
                        $duration = $diff;
                    }
                }

                // Oyuncunun son 10 yatırım işlemine göre güven oranı (sadece yatırım)
                [$trustRate, $trustCount] = (int) $tx->type === 1
                    ? TrustScore::calculate($tx->player_id, $tx->id)
                    : [null, 0];

                $row = [
                    'id'         => $tx->id,
                    'type'       => (int) $tx->type,
                    'status'     => (int) $tx->status,
                    'name'       => $tx->name,
                    'player_id'  => $tx->player_id,
                    'amount'     => $tx->amount,
                    'team'       => $tx->team->name ?? '-',
                    'bank'       => $tx->bankAccount->bank->name ?? '-',
                    'trust_rate'  => $trustRate,
                    'trust_count' => $trustCount,
                    'duration'   => $duration,
                    'created_at_raw' => $tx->created_at?->toISOString(),
                    'date'           => $tx->created_at?->format('H:i d.m.Y') ?? '-',
                ];

                if (!$isTeamMember) {
                    $row['merchant'] = $tx->merchant->name ?? '-';
                }

                return $row;
            });

        return response()->json($transactions);
    }

    public function teamPerformance(Request $request): JsonResponse
    {
        $user = auth()->user();

        if ($user->hasMerchantScope()) {
            abort(403, 'Bu içeriği görüntüleme yetkiniz yok.');
        }

        $dateFrom = $request->get('date_from', now()->toDateString());
        $dateTo = $request->get('date_to', now()->toDateString());

        $query = DB::table('invest')
            ->join('teams', 'invest.team_id', '=', 'teams.id')
            ->where('invest.type', 1)
            ->whereDate('invest.finalize_date', '>=', $dateFrom)
            ->whereDate('invest.finalize_date', '<=', $dateTo)
            ->whereIn('invest.status', [3, 4]);

        // Team scope — sadece kendi takımı
        if ($user->hasTeamScope()) {
            $query->where('invest.team_id', $user->team_id);
        } elseif ($user->hasMerchantScope()) {
            $query->where('invest.firm_id', $user->firm_id);
        }

        $teamRows = $query->select(
                'teams.id',
                'teams.name',
                'teams.maxCase',
                DB::raw('SUM(CASE WHEN invest.status = 3 THEN 1 ELSE 0 END) as approved'),
                DB::raw('SUM(CASE WHEN invest.status = 4 THEN 1 ELSE 0 END) as rejected'),
                DB::raw('SUM(CASE WHEN invest.status = 3 THEN invest.amount ELSE 0 END) as total_amount'),
                DB::raw('SUM(CASE WHEN invest.status = 3 AND TIMESTAMPDIFF(SECOND, invest.created_at, invest.finalize_date) <= 7200 THEN TIMESTAMPDIFF(SECOND, invest.created_at, invest.finalize_date) ELSE 0 END) as approved_total_seconds'),
                DB::raw('SUM(CASE WHEN invest.status = 3 AND TIMESTAMPDIFF(SECOND, invest.created_at, invest.finalize_date) <= 7200 THEN 1 ELSE 0 END) as approved_time_count'),
                DB::raw('SUM(CASE WHEN invest.status = 4 AND TIMESTAMPDIFF(SECOND, invest.created_at, invest.finalize_date) <= 7200 THEN TIMESTAMPDIFF(SECOND, invest.created_at, invest.finalize_date) ELSE 0 END) as rejected_total_seconds'),
                DB::raw('SUM(CASE WHEN invest.status = 4 AND TIMESTAMPDIFF(SECOND, invest.created_at, invest.finalize_date) <= 7200 THEN 1 ELSE 0 END) as rejected_time_count'),
                DB::raw('SUM(CASE WHEN invest.process_date IS NOT NULL AND TIMESTAMPDIFF(SECOND, invest.created_at, invest.process_date) <= 7200 THEN TIMESTAMPDIFF(SECOND, invest.created_at, invest.process_date) ELSE 0 END) as process_total_seconds'),
                DB::raw('SUM(CASE WHEN invest.process_date IS NOT NULL AND TIMESTAMPDIFF(SECOND, invest.created_at, invest.process_date) <= 7200 THEN 1 ELSE 0 END) as process_time_count'),
            )
            ->groupBy('teams.id', 'teams.name', 'teams.maxCase')
            ->orderByDesc('total_amount')
            ->get();

        $teamIds = $teamRows->pluck('id')->all();
        // Tek call ile tüm takımların anlık kasası (team_syncs dahil — ortak helper)
        $cashes = app(MerchantBankService::class)->currentCashForTeams($teamIds);
        // Son çekim — batch query
        $lastWithdraws = $teamIds ? DB::table('invest')
            ->whereIn('team_id', $teamIds)->where('type', 2)->where('status', 3)
            ->groupBy('team_id')->select('team_id', DB::raw('MAX(finalize_date) AS last'))
            ->pluck('last', 'team_id') : collect();

        $teams = $teamRows->map(function ($team) use ($cashes, $lastWithdraws) {
            $total = $team->approved + $team->rejected;
            $rate = $total > 0 ? round(($team->approved / $total) * 100, 1) : 0;
            $avgApprovedSec = $team->approved_time_count > 0 ? round($team->approved_total_seconds / $team->approved_time_count) : 0;
            $avgRejectedSec = $team->rejected_time_count > 0 ? round($team->rejected_total_seconds / $team->rejected_time_count) : 0;
            $avgProcessSec  = $team->process_time_count > 0 ? round($team->process_total_seconds / $team->process_time_count) : 0;

            return [
                'id'               => $team->id,
                'name'             => $team->name,
                'approved'         => $team->approved,
                'rejected'         => $team->rejected,
                'rate'             => $rate,
                'total'            => $team->total_amount,
                'avg_approved_sec' => $avgApprovedSec,
                'avg_rejected_sec' => $avgRejectedSec,
                'avg_process_sec'  => $avgProcessSec,
                'current_case'     => round((float) ($cashes[(int) $team->id] ?? 0), 2),
                'max_case'         => (float) ($team->maxCase ?? 0),
                'last_withdraw'    => $lastWithdraws[$team->id] ?? null,
            ];
        });

        return response()->json($teams);
    }

    public function teamDetail(int $teamId, Request $request): JsonResponse
    {
        if (auth()->user()?->hasMerchantScope()) {
            abort(403, 'Bu içeriği görüntüleme yetkiniz yok.');
        }

        $dateFrom = $request->get('date_from', now()->toDateString());
        $dateTo = $request->get('date_to', now()->toDateString());

        $team = DB::table('teams')->where('id', $teamId)->first();
        if (! $team) return response()->json(['message' => 'Takım bulunamadı.'], 404);

        // Özet istatistikler
        $summary = DB::table('invest')
            ->where('team_id', $teamId)
            ->where('type', '1')
            ->whereIn('status', ['3', '4'])
            ->whereDate('finalize_date', '>=', $dateFrom)
            ->whereDate('finalize_date', '<=', $dateTo)
            ->selectRaw("
                SUM(CASE WHEN status = '3' THEN 1 ELSE 0 END) as approved,
                SUM(CASE WHEN status = '4' THEN 1 ELSE 0 END) as rejected,
                SUM(CASE WHEN status = '3' THEN amount ELSE 0 END) as approved_amount,
                SUM(CASE WHEN status = '4' THEN amount ELSE 0 END) as rejected_amount,
                AVG(CASE WHEN status = '3' THEN amount END) as avg_amount,
                AVG(CASE WHEN status = '3' AND finalize_date IS NOT NULL AND TIMESTAMPDIFF(SECOND, created_at, finalize_date) <= 7200 THEN TIMESTAMPDIFF(SECOND, created_at, finalize_date) END) as avg_approve_time,
                AVG(CASE WHEN status = '4' AND finalize_date IS NOT NULL AND TIMESTAMPDIFF(SECOND, created_at, finalize_date) <= 7200 THEN TIMESTAMPDIFF(SECOND, created_at, finalize_date) END) as avg_reject_time,
                AVG(CASE WHEN process_date IS NOT NULL AND TIMESTAMPDIFF(SECOND, created_at, process_date) <= 7200 THEN TIMESTAMPDIFF(SECOND, created_at, process_date) END) as avg_process_time
            ")
            ->first();

        $total = ($summary->approved ?? 0) + ($summary->rejected ?? 0);
        $approvalRate = $total > 0 ? round($summary->approved / $total * 100, 1) : 0;

        // Agent bazlı performans
        $agents = DB::table('invest')
            ->join('users', 'invest.agent_id', '=', 'users.id')
            ->where('invest.team_id', $teamId)
            ->where('invest.type', '1')
            ->whereIn('invest.status', ['3', '4'])
            ->whereDate('invest.finalize_date', '>=', $dateFrom)
            ->whereDate('invest.finalize_date', '<=', $dateTo)
            ->select(
                'users.id', 'users.name',
                DB::raw("SUM(CASE WHEN invest.status = '3' THEN 1 ELSE 0 END) as approved"),
                DB::raw("SUM(CASE WHEN invest.status = '4' THEN 1 ELSE 0 END) as rejected"),
                DB::raw("SUM(CASE WHEN invest.status = '3' THEN invest.amount ELSE 0 END) as total_amount"),
                DB::raw("AVG(CASE WHEN invest.status = '3' AND invest.finalize_date IS NOT NULL AND TIMESTAMPDIFF(SECOND, invest.created_at, invest.finalize_date) <= 7200 THEN TIMESTAMPDIFF(SECOND, invest.created_at, invest.finalize_date) END) as avg_time")
            )
            ->groupBy('users.id', 'users.name')
            ->orderByDesc('total_amount')
            ->get()
            ->map(function ($a) {
                $total = $a->approved + $a->rejected;
                return [
                    'name'     => $a->name,
                    'approved' => (int) $a->approved,
                    'rejected' => (int) $a->rejected,
                    'rate'     => $total > 0 ? round($a->approved / $total * 100, 1) : 0,
                    'total'    => round((float) $a->total_amount, 2),
                    'avg_time' => $a->avg_time ? round($a->avg_time) : null,
                ];
            });

        // Saatlik dağılım
        $hourly = DB::table('invest')
            ->where('team_id', $teamId)
            ->where('type', '1')
            ->where('status', '3')
            ->whereDate('finalize_date', '>=', $dateFrom)
            ->whereDate('finalize_date', '<=', $dateTo)
            ->selectRaw("HOUR(created_at) as h, COUNT(*) as cnt, SUM(amount) as total")
            ->groupBy('h')
            ->orderBy('h')
            ->pluck('cnt', 'h');

        $hourlyLabels = [];
        $hourlyCounts = [];
        for ($i = 0; $i < 24; $i++) {
            $hourlyLabels[] = str_pad($i, 2, '0', STR_PAD_LEFT) . ':00';
            $hourlyCounts[] = (int) ($hourly[$i] ?? 0);
        }

        // Tutar dağılımı
        $amountDist = DB::table('invest')
            ->where('team_id', $teamId)
            ->where('type', '1')
            ->where('status', '3')
            ->whereDate('finalize_date', '>=', $dateFrom)
            ->whereDate('finalize_date', '<=', $dateTo)
            ->selectRaw("
                CASE
                    WHEN amount <= 500 THEN '0-500'
                    WHEN amount <= 1000 THEN '500-1K'
                    WHEN amount <= 2500 THEN '1K-2.5K'
                    WHEN amount <= 5000 THEN '2.5K-5K'
                    WHEN amount <= 10000 THEN '5K-10K'
                    ELSE '10K+'
                END as range_label,
                COUNT(*) as cnt
            ")
            ->groupBy('range_label')
            ->orderByRaw("FIELD(range_label, '0-500','500-1K','1K-2.5K','2.5K-5K','5K-10K','10K+')")
            ->get();

        // Son 20 işlem
        $recentTx = DB::table('invest')
            ->leftJoin('users as agent', 'invest.agent_id', '=', 'agent.id')
            ->where('invest.team_id', $teamId)
            ->where('invest.type', '1')
            ->whereIn('invest.status', ['3', '4'])
            ->whereDate('invest.finalize_date', '>=', $dateFrom)
            ->whereDate('invest.finalize_date', '<=', $dateTo)
            ->select('invest.id', 'invest.status', 'invest.amount', 'invest.name', 'invest.player_id', 'invest.created_at', 'invest.finalize_date', 'agent.name as agent_name')
            ->orderByDesc('invest.id')
            ->limit(20)
            ->get()
            ->map(function ($tx) {
                $duration = null;
                if ($tx->finalize_date && $tx->created_at) {
                    $diff = strtotime($tx->finalize_date) - strtotime($tx->created_at);
                    if ($diff >= 0 && $diff <= 7200) $duration = $diff;
                }
                return [
                    'id'         => $tx->id,
                    'status'     => (int) $tx->status,
                    'amount'     => (float) $tx->amount,
                    'name'       => $tx->name,
                    'player_id'  => $tx->player_id,
                    'agent_name' => $tx->agent_name,
                    'duration'   => $duration,
                    'date'       => $tx->created_at,
                ];
            });

        return response()->json([
            'team' => ['id' => $team->id, 'name' => $team->name, 'commission' => $team->commission],
            'summary' => [
                'approved'         => (int) ($summary->approved ?? 0),
                'rejected'         => (int) ($summary->rejected ?? 0),
                'approval_rate'    => $approvalRate,
                'approved_amount'  => round((float) ($summary->approved_amount ?? 0), 2),
                'rejected_amount'  => round((float) ($summary->rejected_amount ?? 0), 2),
                'avg_amount'       => round((float) ($summary->avg_amount ?? 0), 2),
                'avg_approve_time' => $summary->avg_approve_time ? round($summary->avg_approve_time) : null,
                'avg_reject_time'  => $summary->avg_reject_time ? round($summary->avg_reject_time) : null,
                'avg_process_time' => $summary->avg_process_time ? round($summary->avg_process_time) : null,
            ],
            'agents'        => $agents,
            'hourly'        => ['labels' => $hourlyLabels, 'counts' => $hourlyCounts],
            'amount_dist'   => $amountDist,
            'recent'        => $recentTx,
        ]);
    }

    public function playerTransactions(string $playerId, Request $request): JsonResponse
    {
        $page = max(1, (int) $request->get('page', 1));
        $perPage = 10;

        $total = $this->scopeQuery(
            DB::table('invest')->where('player_id', $playerId)->whereIn('status', ['3', '4'])
        )->count();

        $items = $this->scopeQuery(
            DB::table('invest')->where('player_id', $playerId)->whereIn('status', ['3', '4'])
        )
            ->orderByDesc('id')
            ->limit($perPage)
            ->offset(($page - 1) * $perPage)
            ->get()
            ->map(function ($tx) {
                return [
                    'id'       => $tx->id,
                    'type'     => (int) $tx->type,
                    'status'   => (int) $tx->status,
                    'amount'   => (float) $tx->amount,
                    'name'     => $tx->name,
                    'date'     => $tx->created_at,
                    'duration' => $tx->finalize_date
                        ? max(0, min(7200, (strtotime($tx->finalize_date) - strtotime($tx->created_at))))
                        : null,
                ];
            });

        return response()->json([
            'items'     => $items,
            'total'     => $total,
            'page'      => $page,
            'per_page'  => $perPage,
            'last_page' => (int) ceil($total / $perPage),
        ]);
    }

    public function playerStats(string $playerId): JsonResponse
    {
        // Genel istatistikler (yatırım + çekim)
        $allTx = $this->scopeQuery(
            DB::table('invest')->where('player_id', $playerId)->whereIn('status', ['3', '4'])
        )
            ->selectRaw("
                COUNT(*) as total,
                SUM(CASE WHEN status = '3' THEN 1 ELSE 0 END) as approved,
                SUM(CASE WHEN status = '4' THEN 1 ELSE 0 END) as rejected,
                SUM(CASE WHEN status = '3' THEN amount ELSE 0 END) as approved_amount,
                SUM(CASE WHEN status = '4' THEN amount ELSE 0 END) as rejected_amount,
                AVG(CASE WHEN status = '3' THEN amount END) as avg_approved_amount,
                MIN(CASE WHEN status = '3' THEN amount END) as min_amount,
                MAX(CASE WHEN status = '3' THEN amount END) as max_amount,
                MIN(created_at) as first_tx,
                MAX(created_at) as last_tx,
                AVG(CASE WHEN status = '3' AND finalize_date IS NOT NULL AND TIMESTAMPDIFF(SECOND, created_at, finalize_date) <= 7200 THEN TIMESTAMPDIFF(SECOND, created_at, finalize_date) END) as avg_approve_time
            ")
            ->first();

        $approvalRate = $allTx->total > 0 ? round(($allTx->approved / $allTx->total) * 100) : 0;

        // Son 30 gün günlük dağılım (grafik için)
        $dailyStats = $this->scopeQuery(
            DB::table('invest')->where('player_id', $playerId)->whereIn('status', ['3', '4'])
        )
            ->whereDate('created_at', '>=', now()->subDays(30)->toDateString())
            ->selectRaw("
                DATE(created_at) as day,
                SUM(CASE WHEN status = '3' THEN 1 ELSE 0 END) as approved,
                SUM(CASE WHEN status = '4' THEN 1 ELSE 0 END) as rejected,
                SUM(CASE WHEN status = '3' THEN amount ELSE 0 END) as amount
            ")
            ->groupBy('day')
            ->orderBy('day')
            ->get();

        $days = [];
        $approvedData = [];
        $rejectedData = [];
        $amountData = [];

        foreach ($dailyStats as $d) {
            $days[] = Carbon::parse($d->day)->format('d M');
            $approvedData[] = (int) $d->approved;
            $rejectedData[] = (int) $d->rejected;
            $amountData[] = round((float) $d->amount);
        }

        // Tutar dağılımı (grafik için)
        $amountRanges = $this->scopeQuery(
            DB::table('invest')->where('player_id', $playerId)->where('status', '3')
        )
            ->selectRaw("
                CASE
                    WHEN amount <= 500 THEN '0-500'
                    WHEN amount <= 1000 THEN '500-1K'
                    WHEN amount <= 2500 THEN '1K-2.5K'
                    WHEN amount <= 5000 THEN '2.5K-5K'
                    WHEN amount <= 10000 THEN '5K-10K'
                    ELSE '10K+'
                END as range_label,
                COUNT(*) as cnt
            ")
            ->groupBy('range_label')
            ->orderByRaw("FIELD(range_label, '0-500','500-1K','1K-2.5K','2.5K-5K','5K-10K','10K+')")
            ->get();

        // Son 10 işlem
        $recentTx = $this->scopeQuery(
            DB::table('invest')->where('player_id', $playerId)->where('type', '1')->whereIn('status', ['3', '4'])
        )
            ->orderByDesc('id')
            ->limit(10)
            ->get()
            ->map(function ($tx) {
                return [
                    'id'       => $tx->id,
                    'status'   => (int) $tx->status,
                    'amount'   => (float) $tx->amount,
                    'name'     => $tx->name,
                    'date'     => $tx->created_at,
                    'duration' => $tx->finalize_date
                        ? max(0, min(7200, (strtotime($tx->finalize_date) - strtotime($tx->created_at))))
                        : null,
                ];
            });

        // Blacklist kontrolü
        $isBlacklisted = DB::table('blacklist')
            ->where('type', 1)
            ->where('val', $playerId)
            ->exists();

        return response()->json([
            'player_id'       => $playerId,
            'is_blacklisted'  => $isBlacklisted,
            'summary'         => [
                'total'              => (int) $allTx->total,
                'approved'           => (int) $allTx->approved,
                'rejected'           => (int) $allTx->rejected,
                'approval_rate'      => $approvalRate,
                'approved_amount'    => round((float) $allTx->approved_amount, 2),
                'rejected_amount'    => round((float) $allTx->rejected_amount, 2),
                'avg_amount'         => round((float) $allTx->avg_approved_amount, 2),
                'min_amount'         => round((float) $allTx->min_amount, 2),
                'max_amount'         => round((float) $allTx->max_amount, 2),
                'avg_approve_time'   => $allTx->avg_approve_time ? round($allTx->avg_approve_time) : null,
                'first_tx'           => $allTx->first_tx,
                'last_tx'            => $allTx->last_tx,
            ],
            'daily_chart'     => [
                'days'     => $days,
                'approved' => $approvedData,
                'rejected' => $rejectedData,
                'amounts'  => $amountData,
            ],
            'amount_ranges'   => $amountRanges,
            'recent'          => $recentTx,
        ]);
    }
}
