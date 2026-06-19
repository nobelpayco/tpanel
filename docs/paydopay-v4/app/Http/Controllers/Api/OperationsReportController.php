<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Carbon;
use Illuminate\Support\Facades\DB;

class OperationsReportController extends Controller
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
     * Apply merchant filter to a query builder.
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
     * Report 15 – İşlem Kuyruk Analizi
     */
    public function queueAnalysis(Request $request): JsonResponse
    {
        [$dateFrom, $dateTo] = $this->dateRange($request);

        // ── 1. Current queue status (real-time, not filtered by date) ──
        $statusQuery = DB::table('invest')
            ->whereIn('status', ['0', '1', '2'])
            ->select(
                'status',
                'type',
                DB::raw('COUNT(*) as count'),
                DB::raw('COALESCE(SUM(amount), 0) as total_amount')
            )
            ->groupBy('status', 'type');
        $this->applyMerchantFilter($statusQuery, $request);
        $currentQueue = $statusQuery->get();

        $statusLabels = ['0' => 'Beklemede', '1' => 'İşlemde', '2' => 'İşleniyor'];
        $typeLabels   = ['1' => 'Yatırım', '2' => 'Çekim'];

        $queueStatus = $currentQueue->map(fn ($row) => [
            'status'       => $row->status,
            'status_label' => $statusLabels[$row->status] ?? $row->status,
            'type'         => $row->type,
            'type_label'   => $typeLabels[$row->type] ?? $row->type,
            'count'        => (int) $row->count,
            'total_amount' => (float) $row->total_amount,
        ]);

        // ── 2. Age distribution of pending transactions (real-time) ──
        $ageBuckets = [
            ['label' => '0-5 dk',     'min' => 0,    'max' => 300],
            ['label' => '5-10 dk',    'min' => 300,  'max' => 600],
            ['label' => '10-30 dk',   'min' => 600,  'max' => 1800],
            ['label' => '30 dk-1 sa', 'min' => 1800, 'max' => 3600],
            ['label' => '1 sa+',      'min' => 3600, 'max' => null],
        ];

        $ageDistribution = [];
        foreach ($ageBuckets as $bucket) {
            $q = DB::table('invest')
                ->whereIn('status', ['0', '1', '2'])
                ->where('created_at', '<=', now()->subSeconds($bucket['min']));

            if ($bucket['max'] !== null) {
                $q->where('created_at', '>', now()->subSeconds($bucket['max']));
            }

            $this->applyMerchantFilter($q, $request);

            $row = $q->selectRaw('COUNT(*) as count, COALESCE(SUM(amount), 0) as total_amount')->first();

            $ageDistribution[] = [
                'label'        => $bucket['label'],
                'count'        => (int) $row->count,
                'total_amount' => (float) $row->total_amount,
            ];
        }

        // ── 3. Average wait time by team (real-time) ──
        $avgWaitQuery = DB::table('invest')
            ->whereIn('invest.status', ['0', '1', '2'])
            ->leftJoin('teams', 'invest.team_id', '=', 'teams.id')
            ->select(
                'invest.team_id',
                'teams.name as team_name',
                DB::raw('COUNT(*) as count'),
                DB::raw('AVG(TIMESTAMPDIFF(SECOND, invest.created_at, NOW())) as avg_wait_seconds')
            )
            ->groupBy('invest.team_id', 'teams.name');
        $this->applyMerchantFilter($avgWaitQuery, $request, 'invest.firm_id');
        $avgWaitByTeam = $avgWaitQuery->get()->map(fn ($row) => [
            'team_id'          => $row->team_id,
            'team_name'        => $row->team_name ?? 'Atanmamış',
            'count'            => (int) $row->count,
            'avg_wait_seconds' => round((float) $row->avg_wait_seconds),
            'avg_wait_label'   => $this->formatDuration((float) $row->avg_wait_seconds),
        ]);

        // ── 4. Oldest pending transactions (top 20, real-time) ──
        $oldestQuery = DB::table('invest')
            ->whereIn('invest.status', ['0', '1', '2'])
            ->leftJoin('teams', 'invest.team_id', '=', 'teams.id')
            ->select(
                'invest.id',
                'invest.type',
                'invest.status',
                'invest.amount',
                'invest.name',
                'invest.created_at',
                'invest.firm_id',
                'invest.team_id',
                'teams.name as team_name',
                DB::raw('TIMESTAMPDIFF(SECOND, invest.created_at, NOW()) as wait_seconds')
            )
            ->orderBy('invest.created_at', 'asc')
            ->limit(20);
        $this->applyMerchantFilter($oldestQuery, $request, 'invest.firm_id');
        $oldestPending = $oldestQuery->get()->map(fn ($row) => [
            'id'           => $row->id,
            'type'         => $row->type,
            'type_label'   => $typeLabels[$row->type] ?? $row->type,
            'status'       => $row->status,
            'status_label' => $statusLabels[$row->status] ?? $row->status,
            'amount'       => (float) $row->amount,
            'name'         => $row->name,
            'firm_id'      => $row->firm_id,
            'team_id'      => $row->team_id,
            'team_name'    => $row->team_name ?? 'Atanmamış',
            'created_at'   => $row->created_at,
            'wait_seconds' => (int) $row->wait_seconds,
            'wait_label'   => $this->formatDuration((int) $row->wait_seconds),
        ]);

        // ── 5. Queue trend – hourly for today (date-filtered) ──
        $todayStart = now()->startOfDay();
        $todayEnd   = now()->endOfDay();

        $enteringQuery = DB::table('invest')
            ->whereBetween('created_at', [$todayStart, $todayEnd])
            ->select(
                DB::raw('HOUR(created_at) as hour'),
                DB::raw('COUNT(*) as count')
            )
            ->groupBy(DB::raw('HOUR(created_at)'));
        $this->applyMerchantFilter($enteringQuery, $request);
        $entering = $enteringQuery->pluck('count', 'hour');

        $processedQuery = DB::table('invest')
            ->whereIn('status', ['3', '4'])
            ->whereBetween('finalize_date', [$todayStart, $todayEnd])
            ->select(
                DB::raw('HOUR(finalize_date) as hour'),
                DB::raw('COUNT(*) as count')
            )
            ->groupBy(DB::raw('HOUR(finalize_date)'));
        $this->applyMerchantFilter($processedQuery, $request);
        $processed = $processedQuery->pluck('count', 'hour');

        $queueTrend = [];
        for ($h = 0; $h <= 23; $h++) {
            $queueTrend[] = [
                'hour'      => $h,
                'entering'  => (int) ($entering[$h] ?? 0),
                'processed' => (int) ($processed[$h] ?? 0),
            ];
        }

        // ── ApexCharts series ──
        $trendSeries = [
            [
                'name' => 'Gelen',
                'data' => array_column($queueTrend, 'entering'),
            ],
            [
                'name' => 'İşlenen',
                'data' => array_column($queueTrend, 'processed'),
            ],
        ];

        $ageChartSeries = [
            [
                'name' => 'İşlem Sayısı',
                'data' => array_column($ageDistribution, 'count'),
            ],
        ];

        return response()->json([
            'queue_status'     => $queueStatus,
            'age_distribution' => $ageDistribution,
            'avg_wait_by_team' => $avgWaitByTeam,
            'oldest_pending'   => $oldestPending,
            'queue_trend'      => $queueTrend,
            'charts'           => [
                'trend' => [
                    'series'     => $trendSeries,
                    'categories' => array_map(fn ($h) => sprintf('%02d:00', $h), range(0, 23)),
                ],
                'age_distribution' => [
                    'series'     => $ageChartSeries,
                    'categories' => array_column($ageDistribution, 'label'),
                ],
            ],
        ]);
    }

    /**
     * Report 16 – Pik Saat Analizi
     */
    public function peakHourAnalysis(Request $request): JsonResponse
    {
        [$dateFrom, $dateTo] = $this->dateRange($request);

        $dayNamesTr = [
            1 => 'Pzt',
            2 => 'Sal',
            3 => 'Çar',
            4 => 'Per',
            5 => 'Cum',
            6 => 'Cmt',
            7 => 'Paz',
        ];

        // ── Heatmap data: day-of-week x hour ──
        $heatmapQuery = DB::table('invest')
            ->whereBetween('created_at', [$dateFrom, $dateTo])
            ->whereIn('status', ['3', '4'])
            ->select(
                DB::raw('DAYOFWEEK(created_at) as dow'),
                DB::raw('HOUR(created_at) as hour'),
                DB::raw('COUNT(*) as count')
            )
            ->groupBy(DB::raw('DAYOFWEEK(created_at)'), DB::raw('HOUR(created_at)'));
        $this->applyMerchantFilter($heatmapQuery, $request);
        $rawHeatmap = $heatmapQuery->get();

        // MySQL DAYOFWEEK: 1=Sunday..7=Saturday → map to ISO: 1=Mon..7=Sun
        $mysqlToIso = [2 => 1, 3 => 2, 4 => 3, 5 => 4, 6 => 5, 7 => 6, 1 => 7];

        // Build matrix
        $matrix = [];
        foreach (range(1, 7) as $iso) {
            foreach (range(0, 23) as $h) {
                $matrix[$iso][$h] = 0;
            }
        }
        foreach ($rawHeatmap as $row) {
            $iso = $mysqlToIso[$row->dow] ?? null;
            if ($iso !== null) {
                $matrix[$iso][$row->hour] = (int) $row->count;
            }
        }

        // ApexCharts heatmap series (each series = one day row)
        $heatmapSeries = [];
        foreach ($dayNamesTr as $iso => $name) {
            $data = [];
            foreach (range(0, 23) as $h) {
                $data[] = [
                    'x'     => sprintf('%02d:00', $h),
                    'y'     => $matrix[$iso][$h],
                ];
            }
            $heatmapSeries[] = [
                'name' => $name,
                'data' => $data,
            ];
        }

        // ── Hourly totals (bar chart) ──
        $hourlyTotals = [];
        for ($h = 0; $h <= 23; $h++) {
            $total = 0;
            foreach (range(1, 7) as $iso) {
                $total += $matrix[$iso][$h];
            }
            $hourlyTotals[] = $total;
        }

        // ── Day of week totals (bar chart) ──
        $dayTotals = [];
        foreach ($dayNamesTr as $iso => $name) {
            $dayTotals[] = [
                'day'   => $name,
                'total' => array_sum($matrix[$iso]),
            ];
        }

        // ── Peak hour identification ──
        $peakHour  = 0;
        $peakCount = 0;
        foreach ($hourlyTotals as $h => $total) {
            if ($total > $peakCount) {
                $peakHour  = $h;
                $peakCount = $total;
            }
        }

        $peakDay      = '';
        $peakDayCount = 0;
        foreach ($dayTotals as $dt) {
            if ($dt['total'] > $peakDayCount) {
                $peakDay      = $dt['day'];
                $peakDayCount = $dt['total'];
            }
        }

        // ── Deposits vs Withdrawals hourly ──
        $typeHourlyQuery = DB::table('invest')
            ->whereBetween('created_at', [$dateFrom, $dateTo])
            ->whereIn('status', ['3', '4'])
            ->select(
                'type',
                DB::raw('HOUR(created_at) as hour'),
                DB::raw('COUNT(*) as count')
            )
            ->groupBy('type', DB::raw('HOUR(created_at)'));
        $this->applyMerchantFilter($typeHourlyQuery, $request);
        $typeHourlyRaw = $typeHourlyQuery->get();

        $depositHourly    = array_fill(0, 24, 0);
        $withdrawalHourly = array_fill(0, 24, 0);
        foreach ($typeHourlyRaw as $row) {
            if ($row->type === '1') {
                $depositHourly[$row->hour] = (int) $row->count;
            } elseif ($row->type === '2') {
                $withdrawalHourly[$row->hour] = (int) $row->count;
            }
        }

        return response()->json([
            'heatmap'    => $heatmapSeries,
            'peak'       => [
                'hour'       => sprintf('%02d:00', $peakHour),
                'hour_count' => $peakCount,
                'day'        => $peakDay,
                'day_count'  => $peakDayCount,
            ],
            'day_totals' => $dayTotals,
            'charts'     => [
                'heatmap' => [
                    'series' => $heatmapSeries,
                ],
                'hourly_totals' => [
                    'series'     => [['name' => 'İşlem Sayısı', 'data' => $hourlyTotals]],
                    'categories' => array_map(fn ($h) => sprintf('%02d:00', $h), range(0, 23)),
                ],
                'day_totals' => [
                    'series'     => [['name' => 'İşlem Sayısı', 'data' => array_column($dayTotals, 'total')]],
                    'categories' => array_column($dayTotals, 'day'),
                ],
                'type_comparison' => [
                    'series' => [
                        ['name' => 'Yatırım',  'data' => array_values($depositHourly)],
                        ['name' => 'Çekim', 'data' => array_values($withdrawalHourly)],
                    ],
                    'categories' => array_map(fn ($h) => sprintf('%02d:00', $h), range(0, 23)),
                ],
            ],
        ]);
    }

    /**
     * Report 17 – SLA Raporu
     */
    public function slaReport(Request $request): JsonResponse
    {
        [$dateFrom, $dateTo] = $this->dateRange($request);

        $slaThreshold = 600; // 10 minutes in seconds
        $outlierMax   = 7200; // 2 hours

        // Base conditions for all SLA queries
        $baseConditions = function ($q) use ($dateFrom, $dateTo, $outlierMax, $request) {
            $q->where('status', '3')
              ->whereNotNull('finalize_date')
              ->whereBetween('created_at', [$dateFrom, $dateTo])
              ->whereRaw('TIMESTAMPDIFF(SECOND, created_at, finalize_date) <= ?', [$outlierMax])
              ->whereRaw('TIMESTAMPDIFF(SECOND, created_at, finalize_date) >= 0');
            $this->applyMerchantFilter($q, $request);
        };

        // ── 1. Overall SLA compliance ──
        $overallQuery = DB::table('invest');
        $baseConditions($overallQuery);
        $overall = $overallQuery->selectRaw(
            'COUNT(*) as total,
             SUM(CASE WHEN TIMESTAMPDIFF(SECOND, created_at, finalize_date) <= ? THEN 1 ELSE 0 END) as within_sla',
            [$slaThreshold]
        )->first();

        $overallTotal     = (int) $overall->total;
        $overallWithinSla = (int) $overall->within_sla;
        $overallRate      = $overallTotal > 0 ? round(($overallWithinSla / $overallTotal) * 100, 2) : 0;

        // ── 2. SLA compliance by team ──
        $teamQuery = DB::table('invest')
            ->leftJoin('teams', 'invest.team_id', '=', 'teams.id');
        $baseConditions($teamQuery);
        $byTeam = $teamQuery->select(
            'invest.team_id',
            'teams.name as team_name',
            DB::raw('COUNT(*) as total'),
            DB::raw('SUM(CASE WHEN TIMESTAMPDIFF(SECOND, invest.created_at, invest.finalize_date) <= ' . $slaThreshold . ' THEN 1 ELSE 0 END) as within_sla'),
            DB::raw('AVG(TIMESTAMPDIFF(SECOND, invest.created_at, invest.finalize_date)) as avg_duration')
        )
            ->groupBy('invest.team_id', 'teams.name')
            ->orderByDesc(DB::raw('COUNT(*)'))
            ->get()
            ->map(fn ($row) => [
                'team_id'      => $row->team_id,
                'team_name'    => $row->team_name ?? 'Atanmamış',
                'total'        => (int) $row->total,
                'within_sla'   => (int) $row->within_sla,
                'breached'     => (int) $row->total - (int) $row->within_sla,
                'rate'         => $row->total > 0 ? round(($row->within_sla / $row->total) * 100, 2) : 0,
                'avg_duration' => round((float) $row->avg_duration),
                'avg_label'    => $this->formatDuration((float) $row->avg_duration),
            ]);

        // ── 3. SLA compliance by hour of day ──
        $hourQuery = DB::table('invest');
        $baseConditions($hourQuery);
        $byHourRaw = $hourQuery->select(
            DB::raw('HOUR(created_at) as hour'),
            DB::raw('COUNT(*) as total'),
            DB::raw('SUM(CASE WHEN TIMESTAMPDIFF(SECOND, created_at, finalize_date) <= ' . $slaThreshold . ' THEN 1 ELSE 0 END) as within_sla')
        )
            ->groupBy(DB::raw('HOUR(created_at)'))
            ->get()
            ->keyBy('hour');

        $byHour = [];
        for ($h = 0; $h <= 23; $h++) {
            $row   = $byHourRaw[$h] ?? null;
            $total = $row ? (int) $row->total : 0;
            $ws    = $row ? (int) $row->within_sla : 0;
            $byHour[] = [
                'hour'       => $h,
                'total'      => $total,
                'within_sla' => $ws,
                'rate'       => $total > 0 ? round(($ws / $total) * 100, 2) : 0,
            ];
        }

        // ── 4. SLA compliance trend (daily) ──
        $dailyQuery = DB::table('invest');
        $baseConditions($dailyQuery);
        $dailyTrend = $dailyQuery->select(
            DB::raw('DATE(created_at) as date'),
            DB::raw('COUNT(*) as total'),
            DB::raw('SUM(CASE WHEN TIMESTAMPDIFF(SECOND, created_at, finalize_date) <= ' . $slaThreshold . ' THEN 1 ELSE 0 END) as within_sla')
        )
            ->groupBy(DB::raw('DATE(created_at)'))
            ->orderBy(DB::raw('DATE(created_at)'))
            ->get()
            ->map(fn ($row) => [
                'date'       => $row->date,
                'total'      => (int) $row->total,
                'within_sla' => (int) $row->within_sla,
                'rate'       => $row->total > 0 ? round(($row->within_sla / $row->total) * 100, 2) : 0,
            ]);

        // ── 5. Worst performing agents ──
        $agentQuery = DB::table('invest')
            ->leftJoin('users', 'invest.agent_id', '=', 'users.id');
        $baseConditions($agentQuery);
        $worstAgents = $agentQuery
            ->whereNotNull('invest.agent_id')
            ->select(
                'invest.agent_id',
                'users.name as agent_name',
                DB::raw('COUNT(*) as total'),
                DB::raw('SUM(CASE WHEN TIMESTAMPDIFF(SECOND, invest.created_at, invest.finalize_date) > ' . $slaThreshold . ' THEN 1 ELSE 0 END) as breach_count'),
                DB::raw('AVG(TIMESTAMPDIFF(SECOND, invest.created_at, invest.finalize_date)) as avg_duration')
            )
            ->groupBy('invest.agent_id', 'users.name')
            ->having('breach_count', '>', 0)
            ->orderByDesc('breach_count')
            ->limit(20)
            ->get()
            ->map(fn ($row) => [
                'agent_id'     => $row->agent_id,
                'agent_name'   => $row->agent_name ?? 'Bilinmiyor',
                'total'        => (int) $row->total,
                'breach_count' => (int) $row->breach_count,
                'avg_duration' => round((float) $row->avg_duration),
                'avg_label'    => $this->formatDuration((float) $row->avg_duration),
                'breach_rate'  => $row->total > 0 ? round(($row->breach_count / $row->total) * 100, 2) : 0,
            ]);

        // ── 6. SLA breach details by time range ──
        $breachRanges = [
            ['label' => '10-15 dk',   'min' => 600,  'max' => 900],
            ['label' => '15-30 dk',   'min' => 900,  'max' => 1800],
            ['label' => '30 dk-1 sa', 'min' => 1800, 'max' => 3600],
            ['label' => '1 sa+',      'min' => 3600, 'max' => $outlierMax],
        ];

        $breachCases = [];
        foreach ($breachRanges as $range) {
            $bq = DB::table('invest');
            $baseConditions($bq);
            $count = $bq->whereRaw('TIMESTAMPDIFF(SECOND, created_at, finalize_date) > ?', [$range['min']])
                        ->whereRaw('TIMESTAMPDIFF(SECOND, created_at, finalize_date) <= ?', [$range['max']])
                        ->count();

            $breachCases[] = [
                'label' => $range['label'],
                'count' => $count,
            ];
        }

        // ── Charts ──
        return response()->json([
            'overall' => [
                'total'      => $overallTotal,
                'within_sla' => $overallWithinSla,
                'breached'   => $overallTotal - $overallWithinSla,
                'rate'       => $overallRate,
            ],
            'by_team'        => $byTeam,
            'by_hour'        => $byHour,
            'daily_trend'    => $dailyTrend,
            'worst_agents'   => $worstAgents,
            'breach_details' => $breachCases,
            'charts'         => [
                'hourly_sla' => [
                    'series'     => [['name' => 'SLA %', 'data' => array_column($byHour, 'rate')]],
                    'categories' => array_map(fn ($h) => sprintf('%02d:00', $h), range(0, 23)),
                ],
                'daily_trend' => [
                    'series'     => [['name' => 'SLA %', 'data' => $dailyTrend->pluck('rate')->values()->all()]],
                    'categories' => $dailyTrend->pluck('date')->values()->all(),
                ],
                'breach_distribution' => [
                    'series'     => [array_column($breachCases, 'count')],
                    'labels'     => array_column($breachCases, 'label'),
                ],
            ],
        ]);
    }

    /**
     * Format seconds into a human-readable Turkish duration label.
     */
    private function formatDuration(float $seconds): string
    {
        $seconds = (int) round($seconds);
        if ($seconds < 60) {
            return $seconds . ' sn';
        }
        if ($seconds < 3600) {
            $min = intdiv($seconds, 60);
            $sec = $seconds % 60;
            return $min . ' dk ' . $sec . ' sn';
        }

        $hours = intdiv($seconds, 3600);
        $min   = intdiv($seconds % 3600, 60);
        return $hours . ' sa ' . $min . ' dk';
    }
}
