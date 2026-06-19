<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Carbon;
use Illuminate\Support\Facades\DB;

class TeamReportController extends Controller
{
    private function dateRange(Request $request): array
    {
        return [
            Carbon::parse($request->get('date_from', now()->startOfMonth()->toDateString()))->startOfDay(),
            Carbon::parse($request->get('date_to', now()->toDateString()))->endOfDay(),
        ];
    }

    private function getFilteredTeamIds(Request $request): ?array
    {
        $param = $request->get('team_ids');
        if (! $param) return null;
        if (is_string($param)) $param = explode(',', $param);
        $ids = array_values(array_filter(array_map('intval', (array) $param)));
        return $ids ?: null;
    }

    public function filterOptions(): JsonResponse
    {
        $teams = DB::table('teams')
            ->where('status', '!=', 0)
            ->orderBy('name')
            ->select('id', 'name')
            ->get();

        return response()->json($teams);
    }

    /**
     * Comparison overview: KPIs per team for given date range.
     */
    public function overview(Request $request): JsonResponse
    {
        [$from, $to] = $this->dateRange($request);
        $teamIds = $this->getFilteredTeamIds($request);

        $teamsQ = DB::table('teams')->where('status', '!=', 0);
        if ($teamIds) $teamsQ->whereIn('id', $teamIds);
        $teams = $teamsQ->orderBy('name')->get(['id', 'name']);

        // Aggregate counts + amounts grouped by team_id, type, status
        $aggQ = DB::table('invest')
            ->select(
                'team_id',
                'type',
                'status',
                DB::raw('COUNT(*) as cnt'),
                DB::raw('SUM(amount) as total_amount'),
                DB::raw('AVG(CASE WHEN process_date IS NOT NULL AND finalize_date IS NOT NULL THEN TIMESTAMPDIFF(SECOND, process_date, finalize_date) END) as avg_seconds')
            )
            ->whereIn('status', ['3', '4'])
            ->whereBetween('created_at', [$from, $to])
            ->groupBy('team_id', 'type', 'status');
        if ($teamIds) $aggQ->whereIn('team_id', $teamIds);
        $agg = $aggQ->get();

        // Build per-team map
        $rows = [];
        foreach ($teams as $t) {
            $rows[$t->id] = [
                'team_id'             => $t->id,
                'team_name'           => $t->name,
                'deposit_approved'    => 0,
                'deposit_rejected'    => 0,
                'deposit_volume'      => 0.0,
                'deposit_avg_seconds' => null,
                'withdraw_approved'   => 0,
                'withdraw_rejected'   => 0,
                'withdraw_volume'     => 0.0,
                'withdraw_avg_seconds'=> null,
            ];
        }

        $sumSec = []; $cntSec = [];
        foreach ($agg as $r) {
            if (! isset($rows[$r->team_id])) continue;
            $isDeposit = $r->type === '1';
            $isApproved = $r->status === '3';
            $key = $isDeposit ? 'deposit' : 'withdraw';

            if ($isApproved) {
                $rows[$r->team_id][$key.'_approved'] = (int) $r->cnt;
                $rows[$r->team_id][$key.'_volume']   = (float) $r->total_amount;
                if ($r->avg_seconds !== null) {
                    $rows[$r->team_id][$key.'_avg_seconds'] = round((float) $r->avg_seconds, 1);
                }
            } else {
                $rows[$r->team_id][$key.'_rejected'] = (int) $r->cnt;
            }
        }

        // Compute success rates
        foreach ($rows as &$row) {
            $dTotal = $row['deposit_approved'] + $row['deposit_rejected'];
            $wTotal = $row['withdraw_approved'] + $row['withdraw_rejected'];
            $row['deposit_success_rate']  = $dTotal > 0 ? round($row['deposit_approved'] * 100 / $dTotal, 2) : 0;
            $row['withdraw_success_rate'] = $wTotal > 0 ? round($row['withdraw_approved'] * 100 / $wTotal, 2) : 0;
            $row['net_volume'] = round($row['deposit_volume'] - $row['withdraw_volume'], 2);
        }
        unset($row);

        return response()->json(['rows' => array_values($rows)]);
    }

    /**
     * Daily trend charts: success rate, avg approval time, volume per team.
     */
    public function trends(Request $request): JsonResponse
    {
        [$from, $to] = $this->dateRange($request);
        $teamIds = $this->getFilteredTeamIds($request);

        $teamsQ = DB::table('teams')->where('status', '!=', 0);
        if ($teamIds) $teamsQ->whereIn('id', $teamIds);
        $teams = $teamsQ->orderBy('name')->get(['id', 'name'])->keyBy('id');

        $daily = DB::table('invest')
            ->select(
                'team_id',
                DB::raw('DATE(created_at) as date'),
                'status',
                'type',
                DB::raw('COUNT(*) as cnt'),
                DB::raw('SUM(amount) as total_amount'),
                DB::raw('AVG(CASE WHEN process_date IS NOT NULL AND finalize_date IS NOT NULL THEN TIMESTAMPDIFF(SECOND, process_date, finalize_date) END) as avg_seconds')
            )
            ->whereIn('status', ['3', '4'])
            ->whereBetween('created_at', [$from, $to])
            ->when($teamIds, fn($q) => $q->whereIn('team_id', $teamIds))
            ->groupBy('team_id', 'date', 'status', 'type')
            ->orderBy('date')
            ->get();

        // Build date list
        $dates = [];
        $cur = $from->copy()->startOfDay();
        $endDay = $to->copy()->startOfDay();
        while ($cur->lte($endDay)) {
            $dates[] = $cur->toDateString();
            $cur->addDay();
        }

        // Per-team daily aggregates
        $byTeam = [];
        foreach ($teams as $t) {
            $byTeam[$t->id] = [
                'name' => $t->name,
                'days' => array_fill_keys($dates, [
                    'd_app' => 0, 'd_rej' => 0, 'd_vol' => 0.0,
                    'w_app' => 0, 'w_rej' => 0, 'w_vol' => 0.0,
                    'sec_sum' => 0.0, 'sec_cnt' => 0,
                ]),
            ];
        }

        foreach ($daily as $r) {
            if (! isset($byTeam[$r->team_id])) continue;
            if (! isset($byTeam[$r->team_id]['days'][$r->date])) continue;
            $d = &$byTeam[$r->team_id]['days'][$r->date];
            $isDeposit = $r->type === '1';
            $isApproved = $r->status === '3';
            $prefix = $isDeposit ? 'd' : 'w';

            if ($isApproved) {
                $d[$prefix.'_app'] += (int) $r->cnt;
                $d[$prefix.'_vol'] += (float) $r->total_amount;
                if ($r->avg_seconds !== null && $r->cnt > 0) {
                    $d['sec_sum'] += (float) $r->avg_seconds * (int) $r->cnt;
                    $d['sec_cnt'] += (int) $r->cnt;
                }
            } else {
                $d[$prefix.'_rej'] += (int) $r->cnt;
            }
            unset($d);
        }

        // Build series
        $successSeries = [];
        $avgTimeSeries = [];
        $volumeSeries  = [];
        foreach ($byTeam as $tid => $info) {
            $successData = [];
            $timeData    = [];
            $volData     = [];
            foreach ($dates as $d) {
                $row = $info['days'][$d];
                $total = $row['d_app'] + $row['d_rej'] + $row['w_app'] + $row['w_rej'];
                $approved = $row['d_app'] + $row['w_app'];
                $successData[] = $total > 0 ? round($approved * 100 / $total, 2) : null;
                $timeData[]    = $row['sec_cnt'] > 0 ? round($row['sec_sum'] / $row['sec_cnt'] / 60, 2) : null;
                $volData[]     = round($row['d_vol'], 2);
            }
            $successSeries[] = ['name' => $info['name'], 'data' => $successData];
            $avgTimeSeries[] = ['name' => $info['name'], 'data' => $timeData];
            $volumeSeries[]  = ['name' => $info['name'], 'data' => $volData];
        }

        return response()->json([
            'categories'      => $dates,
            'success_rate'    => $successSeries,
            'avg_time_min'    => $avgTimeSeries,
            'deposit_volume'  => $volumeSeries,
        ]);
    }

    /**
     * Hourly density per team (heat-style data) — when do approvals happen.
     */
    public function hourly(Request $request): JsonResponse
    {
        [$from, $to] = $this->dateRange($request);
        $teamIds = $this->getFilteredTeamIds($request);

        $rows = DB::table('invest')
            ->select(
                'team_id',
                DB::raw('HOUR(finalize_date) as hour'),
                DB::raw('COUNT(*) as cnt')
            )
            ->where('status', '3')
            ->whereNotNull('finalize_date')
            ->whereBetween('finalize_date', [$from, $to])
            ->when($teamIds, fn($q) => $q->whereIn('team_id', $teamIds))
            ->groupBy('team_id', 'hour')
            ->get();

        $teamsQ = DB::table('teams')->where('status', '!=', 0);
        if ($teamIds) $teamsQ->whereIn('id', $teamIds);
        $teams = $teamsQ->orderBy('name')->get(['id', 'name']);

        $hours = range(0, 23);
        $series = [];
        foreach ($teams as $t) {
            $data = array_fill_keys($hours, 0);
            foreach ($rows as $r) {
                if ((int) $r->team_id === (int) $t->id) {
                    $data[(int) $r->hour] = (int) $r->cnt;
                }
            }
            $series[] = ['name' => $t->name, 'data' => array_values($data)];
        }

        return response()->json([
            'categories' => array_map(fn ($h) => sprintf('%02d:00', $h), $hours),
            'series'     => $series,
        ]);
    }
}
