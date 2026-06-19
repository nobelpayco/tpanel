<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Carbon;
use Illuminate\Support\Facades\DB;

class MerchantReportController extends Controller
{
    /**
     * Resolve date range from request, defaulting to current month.
     */
    private function dateRange(Request $request): array
    {
        return [
            Carbon::parse($request->get('date_from', now()->startOfMonth()->toDateString()))->startOfDay(),
            Carbon::parse($request->get('date_to', now()->toDateString()))->endOfDay(),
        ];
    }

    /**
     * Get filtered merchant IDs from request.
     * merchant_id param can be a group_id prefixed with 'g_' (e.g. 'g_1')
     */
    private function getFilteredMerchantIds(Request $request): ?array
    {
        $merchantParam = $request->get('merchant_id');
        if (!$merchantParam) return null;

        if (str_starts_with($merchantParam, 'g_')) {
            $groupId = (int) substr($merchantParam, 2);
            return DB::table('merchantUser')->where('group_id', $groupId)->pluck('id')->toArray();
        }

        return [(int) $merchantParam];
    }

    /**
     * Apply merchant / group filter to a query builder.
     */
    private function applyMerchantFilter($query, Request $request, string $column = 'firm_id')
    {
        $ids = $this->getFilteredMerchantIds($request);
        if ($ids !== null) {
            $query->whereIn($column, $ids);
        }
        return $query;
    }

    /**
     * Scope callback for ->when() chains — replaces merchant_id + group_id when clauses.
     */
    private function merchantScope(Request $request): ?\Closure
    {
        $ids = $this->getFilteredMerchantIds($request);
        if ($ids === null) return null;
        return fn ($q) => $q->whereIn('firm_id', $ids);
    }

    /**
     * Apply filtered merchant IDs to query if present.
     */
    private function filterQuery($query, ?array $ids, string $column = 'firm_id')
    {
        if ($ids) $query->whereIn($column, $ids);
        return $query;
    }

    /**
     * Get merchant display names keyed by firm_id.
     * Grouped merchants share the same display name (group name).
     */
    public function getMerchantDisplayMap(): array
    {
        $merchants = DB::table('merchantUser')
            ->where('status', '1')
            ->select('id', 'name', 'group_id')
            ->get();

        $groups = DB::table('merchant_groups')->where('status', 1)->pluck('name', 'id');

        $map = [];
        foreach ($merchants as $m) {
            if ($m->group_id && isset($groups[$m->group_id])) {
                $map[$m->id] = $groups[$m->group_id];
            } else {
                $map[$m->id] = $m->name;
            }
        }

        return $map;
    }

    /**
     * Get merchant filter options for frontend (group-aware).
     */
    public function filterOptions(): JsonResponse
    {
        // Pasifler dahil tüm merchant'lar — eski merchant raporu çekerken filtre listesinde de görünmeli
        $merchants = DB::table('merchantUser')
            ->select('id', 'name', 'group_id')
            ->orderBy('name')
            ->get();

        $groups = DB::table('merchant_groups')->where('status', 1)->get()->keyBy('id');

        $options = [];
        $processedGroupIds = [];

        foreach ($merchants as $m) {
            if ($m->group_id && isset($groups[$m->group_id])) {
                if (in_array($m->group_id, $processedGroupIds)) continue;
                $processedGroupIds[] = $m->group_id;
                $options[] = [
                    'id'   => 'g_' . $m->group_id,
                    'name' => $groups[$m->group_id]->name,
                ];
            } else {
                $options[] = [
                    'id'   => (string) $m->id,
                    'name' => $m->name,
                ];
            }
        }

        return response()->json($options);
    }

    /**
     * Get merchant names keyed by id (for labelling series).
     * Uses group-aware display names.
     */
    private function getMerchantNames(Request $request): array
    {
        $displayMap = $this->getMerchantDisplayMap();
        $merchantParam = $request->get('merchant_id');

        if ($merchantParam) {
            if (str_starts_with($merchantParam, 'g_')) {
                $groupId = (int) substr($merchantParam, 2);
                $ids = DB::table('merchantUser')->where('group_id', $groupId)->pluck('id')->toArray();
                return array_intersect_key($displayMap, array_flip($ids));
            } else {
                $id = (int) $merchantParam;
                return isset($displayMap[$id]) ? [$id => $displayMap[$id]] : [];
            }
        }

        return $displayMap;
    }

    // ─── 1. VOLUME PERFORMANCE ───────────────────────────────────────

    public function volumePerformance(Request $request): JsonResponse
    {
        [$from, $to] = $this->dateRange($request);
        $merchantNames = $this->getMerchantNames($request);
        $filteredIds = $this->getFilteredMerchantIds($request);

        // --- Daily deposit / withdrawal volume per merchant ---
        $dailyVolume = DB::table('invest')
            ->select(
                DB::raw("DATE(finalize_date) as date"),
                'firm_id',
                'type',
                DB::raw("SUM(amount) as total_amount"),
                DB::raw("COUNT(*) as tx_count")
            )
            ->where('status', '3')
            ->whereBetween('finalize_date', [$from, $to])
            ->groupBy('date', 'firm_id', 'type')
            ->orderBy('date');
        $this->filterQuery($dailyVolume, $filteredIds);
        $dailyVolume = $dailyVolume->get();

        $dates = $dailyVolume->pluck('date')->unique()->sort()->values()->toArray();

        // Build series per merchant per type
        $depositSeries = [];
        $withdrawalSeries = [];
        foreach ($dailyVolume as $row) {
            $label = $merchantNames[$row->firm_id] ?? "Merchant #{$row->firm_id}";
            if ($row->type === '1') {
                $depositSeries[$label][$row->date] = ($depositSeries[$label][$row->date] ?? 0) + (float) $row->total_amount;
            } else {
                $withdrawalSeries[$label][$row->date] = ($withdrawalSeries[$label][$row->date] ?? 0) + (float) $row->total_amount;
            }
        }

        $formatSeries = function (array $grouped, array $cats) {
            $series = [];
            foreach ($grouped as $name => $dateMap) {
                $data = [];
                foreach ($cats as $d) {
                    $data[] = $dateMap[$d] ?? 0;
                }
                $series[] = ['name' => $name, 'data' => $data];
            }
            return $series;
        };

        // --- Approval / rejection rates per day ---
        $statusCounts = DB::table('invest')
            ->select(
                DB::raw("DATE(created_at) as date"),
                'status',
                DB::raw("COUNT(*) as cnt")
            )
            ->whereIn('status', ['3', '4'])
            ->whereBetween('created_at', [$from, $to])
            ->when($filteredIds, fn($q) => $q->whereIn('firm_id', $filteredIds))
            ->groupBy('date', 'status')
            ->orderBy('date')
            ->get();

        $rateDates = $statusCounts->pluck('date')->unique()->sort()->values()->toArray();
        $approvedMap = [];
        $rejectedMap = [];
        foreach ($statusCounts as $row) {
            if ($row->status === '3') {
                $approvedMap[$row->date] = ($approvedMap[$row->date] ?? 0) + $row->cnt;
            } else {
                $rejectedMap[$row->date] = ($rejectedMap[$row->date] ?? 0) + $row->cnt;
            }
        }

        $approvalRates = [];
        $rejectionRates = [];
        foreach ($rateDates as $d) {
            $approved = $approvedMap[$d] ?? 0;
            $rejected = $rejectedMap[$d] ?? 0;
            $total = $approved + $rejected;
            $approvalRates[] = $total > 0 ? round($approved / $total * 100, 2) : 0;
            $rejectionRates[] = $total > 0 ? round($rejected / $total * 100, 2) : 0;
        }

        // --- Avg processing time (minutes) per day ---
        $avgProcessing = DB::table('invest')
            ->select(
                DB::raw("DATE(finalize_date) as date"),
                DB::raw("AVG(TIMESTAMPDIFF(SECOND, process_date, finalize_date)) as avg_seconds")
            )
            ->where('status', '3')
            ->whereNotNull('process_date')
            ->whereNotNull('finalize_date')
            ->whereBetween('finalize_date', [$from, $to])
            ->when($filteredIds, fn($q) => $q->whereIn('firm_id', $filteredIds))
            ->groupBy('date')
            ->orderBy('date')
            ->get();

        $procDates = $avgProcessing->pluck('date')->toArray();
        $procData = $avgProcessing->map(fn ($r) => round(($r->avg_seconds ?? 0) / 60, 2))->toArray();

        // --- Avg transaction amount trend per day ---
        $avgAmount = DB::table('invest')
            ->select(
                DB::raw("DATE(finalize_date) as date"),
                'type',
                DB::raw("AVG(amount) as avg_amount")
            )
            ->where('status', '3')
            ->whereBetween('finalize_date', [$from, $to])
            ->when($filteredIds, fn($q) => $q->whereIn('firm_id', $filteredIds))
            ->groupBy('date', 'type')
            ->orderBy('date')
            ->get();

        $avgDates = $avgAmount->pluck('date')->unique()->sort()->values()->toArray();
        $avgDepositMap = [];
        $avgWithdrawalMap = [];
        foreach ($avgAmount as $row) {
            if ($row->type === '1') {
                $avgDepositMap[$row->date] = round((float) $row->avg_amount, 2);
            } else {
                $avgWithdrawalMap[$row->date] = round((float) $row->avg_amount, 2);
            }
        }

        return response()->json([
            'daily_volume' => [
                'categories' => $dates,
                'deposit_series' => $formatSeries($depositSeries, $dates),
                'withdrawal_series' => $formatSeries($withdrawalSeries, $dates),
            ],
            'approval_rates' => [
                'categories' => $rateDates,
                'series' => [
                    ['name' => 'Onay Oranı (%)', 'data' => $approvalRates],
                    ['name' => 'Red Oranı (%)', 'data' => $rejectionRates],
                ],
            ],
            'avg_processing_time' => [
                'categories' => $procDates,
                'series' => [
                    ['name' => 'Ort. İşlem Süresi (dk)', 'data' => $procData],
                ],
            ],
            'avg_transaction_amount' => [
                'categories' => $avgDates,
                'series' => [
                    ['name' => 'Ort. Yatırım', 'data' => array_map(fn ($d) => $avgDepositMap[$d] ?? 0, $avgDates)],
                    ['name' => 'Ort. Çekim', 'data' => array_map(fn ($d) => $avgWithdrawalMap[$d] ?? 0, $avgDates)],
                ],
            ],
        ]);
    }

    // ─── 2. PLAYER ANALYSIS ──────────────────────────────────────────

    public function playerAnalysis(Request $request): JsonResponse
    {
        [$from, $to] = $this->dateRange($request);
        $filteredIds = $this->getFilteredMerchantIds($request);

        $baseQuery = fn () => $this->filterQuery(DB::table('invest')
            ->where('status', '3')
            ->whereBetween('finalize_date', [$from, $to]), $filteredIds);
        // --- Active player count per day ---
        $activePlayers = $baseQuery()
            ->select(
                DB::raw("DATE(finalize_date) as date"),
                DB::raw("COUNT(DISTINCT player_id) as player_count")
            )
            ->groupBy('date')
            ->orderBy('date')
            ->get();

        // --- New vs returning players per day ---
        // A "new" player is one whose first-ever approved transaction falls on that date.
        $firstTxDates = $baseQuery()
            ->select('player_id', DB::raw("MIN(DATE(finalize_date)) as first_date"))
            ->groupBy('player_id');

        $newPlayersPerDay = DB::query()
            ->fromSub($firstTxDates, 'ft')
            ->select('first_date as date', DB::raw("COUNT(*) as new_count"))
            ->whereBetween('first_date', [$from->toDateString(), $to->toDateString()])
            ->groupBy('first_date')
            ->orderBy('first_date')
            ->get()
            ->keyBy('date');

        $playerDates = $activePlayers->pluck('date')->toArray();
        $activeData = $activePlayers->pluck('player_count')->toArray();
        $newData = [];
        $returningData = [];
        foreach ($activePlayers as $row) {
            $newCount = (int) ($newPlayersPerDay[$row->date]->new_count ?? 0);
            $newData[] = $newCount;
            $returningData[] = max(0, $row->player_count - $newCount);
        }

        // --- Avg transactions per player ---
        $avgTxPerPlayer = $baseQuery()
            ->select(
                DB::raw("DATE(finalize_date) as date"),
                DB::raw("COUNT(*) / COUNT(DISTINCT player_id) as avg_tx")
            )
            ->groupBy('date')
            ->orderBy('date')
            ->get();

        // --- Top 20 players by transaction count ---
        $topPlayers = $baseQuery()
            ->select(
                'player_id',
                DB::raw("COUNT(*) as tx_count"),
                DB::raw("SUM(amount) as total_amount")
            )
            ->groupBy('player_id')
            ->orderByDesc('tx_count')
            ->limit(20)
            ->get();

        return response()->json([
            'active_player_trend' => [
                'categories' => $playerDates,
                'series' => [
                    ['name' => 'Aktif Oyuncu', 'data' => $activeData],
                ],
            ],
            'new_vs_returning' => [
                'categories' => $playerDates,
                'series' => [
                    ['name' => 'Yeni Oyuncu', 'data' => $newData],
                    ['name' => 'Dönen Oyuncu', 'data' => $returningData],
                ],
            ],
            'avg_tx_per_player' => [
                'categories' => $avgTxPerPlayer->pluck('date')->toArray(),
                'series' => [
                    ['name' => 'Ort. İşlem/Oyuncu', 'data' => $avgTxPerPlayer->map(fn ($r) => round((float) $r->avg_tx, 2))->toArray()],
                ],
            ],
            'top_players' => $topPlayers->map(fn ($r) => [
                'player_id' => $r->player_id,
                'tx_count' => (int) $r->tx_count,
                'total_amount' => (float) $r->total_amount,
            ])->toArray(),
        ]);
    }

    // ─── 3. AMOUNT ANALYSIS ─────────────────────────────────────────

    public function amountAnalysis(Request $request): JsonResponse
    {
        [$from, $to] = $this->dateRange($request);

        $filteredIds = $this->getFilteredMerchantIds($request);
        $baseWhere = function ($q) use ($from, $to, $filteredIds) {
            $q->where('status', '3')->whereBetween('finalize_date', [$from, $to]);
            if ($filteredIds) $q->whereIn('firm_id', $filteredIds);
            return $q;
        };

        // --- Amount distribution buckets ---
        $buckets = [
            '0-500' => [0, 500],
            '500-1K' => [500, 1000],
            '1K-2.5K' => [1000, 2500],
            '2.5K-5K' => [2500, 5000],
            '5K-10K' => [5000, 10000],
            '10K+' => [10000, null],
        ];

        $caseExpr = "CASE ";
        foreach ($buckets as $label => [$min, $max]) {
            if ($max === null) {
                $caseExpr .= "WHEN amount >= {$min} THEN '{$label}' ";
            } else {
                $caseExpr .= "WHEN amount >= {$min} AND amount < {$max} THEN '{$label}' ";
            }
        }
        $caseExpr .= "END";

        $distribution = $baseWhere(DB::table('invest'))
            ->select(
                'type',
                DB::raw("({$caseExpr}) as bucket"),
                DB::raw("COUNT(*) as cnt"),
                DB::raw("SUM(amount) as total_amount")
            )
            ->groupBy('type', 'bucket')
            ->get();

        $bucketLabels = array_keys($buckets);
        $depositDist = array_fill_keys($bucketLabels, 0);
        $withdrawalDist = array_fill_keys($bucketLabels, 0);
        $depositDistAmount = array_fill_keys($bucketLabels, 0);
        $withdrawalDistAmount = array_fill_keys($bucketLabels, 0);

        foreach ($distribution as $row) {
            if ($row->bucket === null) continue;
            if ($row->type === '1') {
                $depositDist[$row->bucket] = (int) $row->cnt;
                $depositDistAmount[$row->bucket] = (float) $row->total_amount;
            } else {
                $withdrawalDist[$row->bucket] = (int) $row->cnt;
                $withdrawalDistAmount[$row->bucket] = (float) $row->total_amount;
            }
        }

        // --- Hourly transaction density ---
        $hourly = $baseWhere(DB::table('invest'))
            ->select(
                DB::raw("HOUR(created_at) as hour"),
                'type',
                DB::raw("COUNT(*) as cnt")
            )
            ->groupBy('hour', 'type')
            ->orderBy('hour')
            ->get();

        $hours = range(0, 23);
        $hourLabels = array_map(fn ($h) => str_pad($h, 2, '0', STR_PAD_LEFT) . ':00', $hours);
        $hourlyDeposit = array_fill(0, 24, 0);
        $hourlyWithdrawal = array_fill(0, 24, 0);
        foreach ($hourly as $row) {
            if ($row->type === '1') {
                $hourlyDeposit[(int) $row->hour] = (int) $row->cnt;
            } else {
                $hourlyWithdrawal[(int) $row->hour] = (int) $row->cnt;
            }
        }

        // --- Daily min / max comparison ---
        $dailyMinMax = $baseWhere(DB::table('invest'))
            ->select(
                DB::raw("DATE(finalize_date) as date"),
                'type',
                DB::raw("MIN(amount) as min_amount"),
                DB::raw("MAX(amount) as max_amount")
            )
            ->groupBy('date', 'type')
            ->orderBy('date')
            ->get();

        $minMaxDates = $dailyMinMax->pluck('date')->unique()->sort()->values()->toArray();
        $depMinMap = [];
        $depMaxMap = [];
        $witMinMap = [];
        $witMaxMap = [];
        foreach ($dailyMinMax as $row) {
            if ($row->type === '1') {
                $depMinMap[$row->date] = (float) $row->min_amount;
                $depMaxMap[$row->date] = (float) $row->max_amount;
            } else {
                $witMinMap[$row->date] = (float) $row->min_amount;
                $witMaxMap[$row->date] = (float) $row->max_amount;
            }
        }

        return response()->json([
            'amount_distribution' => [
                'categories' => $bucketLabels,
                'series' => [
                    ['name' => 'Yatırım Adet', 'data' => array_values($depositDist)],
                    ['name' => 'Çekim Adet', 'data' => array_values($withdrawalDist)],
                ],
                'amount_series' => [
                    ['name' => 'Yatırım Tutar', 'data' => array_values($depositDistAmount)],
                    ['name' => 'Çekim Tutar', 'data' => array_values($withdrawalDistAmount)],
                ],
            ],
            'hourly_density' => [
                'categories' => $hourLabels,
                'series' => [
                    ['name' => 'Yatırım', 'data' => $hourlyDeposit],
                    ['name' => 'Çekim', 'data' => $hourlyWithdrawal],
                ],
            ],
            'daily_min_max' => [
                'categories' => $minMaxDates,
                'series' => [
                    ['name' => 'Yatırım Min', 'data' => array_map(fn ($d) => $depMinMap[$d] ?? 0, $minMaxDates)],
                    ['name' => 'Yatırım Max', 'data' => array_map(fn ($d) => $depMaxMap[$d] ?? 0, $minMaxDates)],
                    ['name' => 'Çekim Min', 'data' => array_map(fn ($d) => $witMinMap[$d] ?? 0, $minMaxDates)],
                    ['name' => 'Çekim Max', 'data' => array_map(fn ($d) => $witMaxMap[$d] ?? 0, $minMaxDates)],
                ],
            ],
        ]);
    }

    // ─── 4. FINANCIAL REPORT ────────────────────────────────────────

    public function financialReport(Request $request): JsonResponse
    {
        [$from, $to] = $this->dateRange($request);

        // Get merchants with commission info
        $merchantQuery = DB::table('merchantUser')->select('id', 'name', 'commission', 'withdrawCommission', 'group_id');

        $filteredIds = $this->getFilteredMerchantIds($request);
        if ($filteredIds) {
            $merchantQuery->whereIn('id', $filteredIds);
        }

        $merchants = $merchantQuery->get()->keyBy('id');
        $merchantIds = $merchants->pluck('id')->toArray();

        // --- Daily approved volumes per merchant ---
        $dailyQuery = DB::table('invest')
            ->select(
                DB::raw("DATE(finalize_date) as date"),
                'firm_id',
                'type',
                DB::raw("SUM(amount) as total_amount")
            )
            ->where('status', '3')
            ->whereBetween('finalize_date', [$from, $to]);

        if (count($merchantIds) > 0) {
            $dailyQuery->whereIn('firm_id', $merchantIds);
        }

        $daily = $dailyQuery
            ->groupBy('date', 'firm_id', 'type')
            ->orderBy('date')
            ->get();

        $dates = $daily->pluck('date')->unique()->sort()->values()->toArray();

        // Commission revenue per day per merchant (group-aware)
        $displayMap = $this->getMerchantDisplayMap();
        $commissionSeries = [];
        $netCaseSeries = [];

        foreach ($daily as $row) {
            $merchant = $merchants[$row->firm_id] ?? null;
            $label = $displayMap[$row->firm_id] ?? ($merchant->name ?? "Merchant #{$row->firm_id}");

            $commRate = 0;
            if ($merchant) {
                $commRate = $row->type === '1'
                    ? (float) ($merchant->commission ?? 0)
                    : (float) ($merchant->withdrawCommission ?? 0);
            }

            $commission = (float) $row->total_amount * ($commRate / 100);
            $commissionSeries[$label][$row->date] = ($commissionSeries[$label][$row->date] ?? 0) + $commission;

            // Net case: deposits add, withdrawals subtract (from merchant perspective)
            $sign = $row->type === '1' ? 1 : -1;
            $netCaseSeries[$label][$row->date] = ($netCaseSeries[$label][$row->date] ?? 0) + ($sign * (float) $row->total_amount);
        }

        $buildSeries = function (array $grouped, array $cats) {
            $series = [];
            foreach ($grouped as $name => $dateMap) {
                $data = [];
                foreach ($cats as $d) {
                    $data[] = round($dateMap[$d] ?? 0, 2);
                }
                $series[] = ['name' => $name, 'data' => $data];
            }
            return $series;
        };

        // --- Merchant comparison (side-by-side totals) ---
        $comparison = DB::table('invest')
            ->select(
                'firm_id',
                'type',
                DB::raw("SUM(amount) as total_amount"),
                DB::raw("COUNT(*) as tx_count")
            )
            ->where('status', '3')
            ->whereBetween('finalize_date', [$from, $to]);

        if (count($merchantIds) > 0) {
            $comparison->whereIn('firm_id', $merchantIds);
        }

        $comparison = $comparison
            ->groupBy('firm_id', 'type')
            ->get();

        $displayMap = $this->getMerchantDisplayMap();
        $comparisonData = [];
        foreach ($comparison as $row) {
            $label = $displayMap[$row->firm_id] ?? "Merchant #{$row->firm_id}";

            if (!isset($comparisonData[$label])) {
                $comparisonData[$label] = [
                    'merchant' => $label,
                    'deposit_amount' => 0,
                    'deposit_count' => 0,
                    'withdrawal_amount' => 0,
                    'withdrawal_count' => 0,
                ];
            }

            if ($row->type === '1') {
                $comparisonData[$label]['deposit_amount'] += (float) $row->total_amount;
                $comparisonData[$label]['deposit_count'] += (int) $row->tx_count;
            } else {
                $comparisonData[$label]['withdrawal_amount'] += (float) $row->total_amount;
                $comparisonData[$label]['withdrawal_count'] += (int) $row->tx_count;
            }
        }

        $comparisonLabels = array_keys($comparisonData);

        return response()->json([
            'commission_trend' => [
                'categories' => $dates,
                'series' => $buildSeries($commissionSeries, $dates),
            ],
            'net_case_trend' => [
                'categories' => $dates,
                'series' => $buildSeries($netCaseSeries, $dates),
            ],
            'merchant_comparison' => [
                'categories' => $comparisonLabels,
                'series' => [
                    ['name' => 'Yatırım', 'data' => array_map(fn ($l) => $comparisonData[$l]['deposit_amount'], $comparisonLabels)],
                    ['name' => 'Çekim', 'data' => array_map(fn ($l) => $comparisonData[$l]['withdrawal_amount'], $comparisonLabels)],
                ],
                'details' => array_values($comparisonData),
            ],
        ]);
    }

    // ─── 5. RISK REPORT ─────────────────────────────────────────────

    public function riskReport(Request $request): JsonResponse
    {
        [$from, $to] = $this->dateRange($request);

        $filteredIds = $this->getFilteredMerchantIds($request);

        // --- Low amount ratio per day (under 999 TL) ---
        $lowQuery = DB::table('invest')
            ->select(
                DB::raw("DATE(created_at) as date"),
                'type',
                DB::raw("COUNT(*) as total_cnt"),
                DB::raw("SUM(CASE WHEN amount < 999 THEN 1 ELSE 0 END) as low_cnt")
            )
            ->where('status', '3')
            ->whereBetween('created_at', [$from, $to]);
        if ($filteredIds) $lowQuery->whereIn('firm_id', $filteredIds);

        $dailyTotals = $lowQuery
            ->groupBy('date', 'type')
            ->orderBy('date')
            ->get();

        $lowDates = $dailyTotals->pluck('date')->unique()->sort()->values()->toArray();
        $lowDepositRatio = [];
        $lowWithdrawalRatio = [];
        $depTotalMap = [];
        $depLowMap = [];
        $witTotalMap = [];
        $witLowMap = [];

        foreach ($dailyTotals as $row) {
            if ($row->type === '1') {
                $depTotalMap[$row->date] = (int) $row->total_cnt;
                $depLowMap[$row->date] = (int) $row->low_cnt;
            } else {
                $witTotalMap[$row->date] = (int) $row->total_cnt;
                $witLowMap[$row->date] = (int) $row->low_cnt;
            }
        }

        foreach ($lowDates as $d) {
            $depTotal = $depTotalMap[$d] ?? 0;
            $depLow = $depLowMap[$d] ?? 0;
            $lowDepositRatio[] = $depTotal > 0 ? round($depLow / $depTotal * 100, 2) : 0;

            $witTotal = $witTotalMap[$d] ?? 0;
            $witLow = $witLowMap[$d] ?? 0;
            $lowWithdrawalRatio[] = $witTotal > 0 ? round($witLow / $witTotal * 100, 2) : 0;
        }

        // --- Rejected transaction trend per day ---
        $rejQuery = DB::table('invest')
            ->select(
                DB::raw("DATE(created_at) as date"),
                'type',
                DB::raw("COUNT(*) as cnt")
            )
            ->where('status', '4')
            ->whereBetween('created_at', [$from, $to]);
        if ($filteredIds) $rejQuery->whereIn('firm_id', $filteredIds);

        $rejectedTrend = $rejQuery
            ->groupBy('date', 'type')
            ->orderBy('date')
            ->get();

        $rejDates = $rejectedTrend->pluck('date')->unique()->sort()->values()->toArray();
        $rejDepositMap = [];
        $rejWithdrawalMap = [];
        foreach ($rejectedTrend as $row) {
            if ($row->type === '1') {
                $rejDepositMap[$row->date] = (int) $row->cnt;
            } else {
                $rejWithdrawalMap[$row->date] = (int) $row->cnt;
            }
        }

        // Calculate trend direction (increasing / decreasing)
        $rejDepositData = array_map(fn ($d) => $rejDepositMap[$d] ?? 0, $rejDates);
        $rejWithdrawalData = array_map(fn ($d) => $rejWithdrawalMap[$d] ?? 0, $rejDates);

        $trendDirection = function (array $data): string {
            if (count($data) < 2) return 'stable';
            $half = (int) ceil(count($data) / 2);
            $firstHalf = array_slice($data, 0, $half);
            $secondHalf = array_slice($data, $half);
            $avgFirst = count($firstHalf) > 0 ? array_sum($firstHalf) / count($firstHalf) : 0;
            $avgSecond = count($secondHalf) > 0 ? array_sum($secondHalf) / count($secondHalf) : 0;

            if ($avgSecond > $avgFirst * 1.1) return 'increasing';
            if ($avgSecond < $avgFirst * 0.9) return 'decreasing';
            return 'stable';
        };

        return response()->json([
            'low_amount_ratio' => [
                'categories' => $lowDates,
                'series' => [
                    ['name' => 'Yatırım Düşük Tutar Oranı (%)', 'data' => $lowDepositRatio],
                    ['name' => 'Çekim Düşük Tutar Oranı (%)', 'data' => $lowWithdrawalRatio],
                ],
            ],
            'rejected_trend' => [
                'categories' => $rejDates,
                'series' => [
                    ['name' => 'Reddedilen Yatırım', 'data' => $rejDepositData],
                    ['name' => 'Reddedilen Çekim', 'data' => $rejWithdrawalData],
                ],
                'deposit_trend_direction' => $trendDirection($rejDepositData),
                'withdrawal_trend_direction' => $trendDirection($rejWithdrawalData),
            ],
        ]);
    }
}
