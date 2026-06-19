<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Carbon;
use Illuminate\Support\Facades\DB;

class ConversionReportController extends Controller
{
    private function dateRange(Request $request): array
    {
        return [
            Carbon::parse($request->get('date_from', now()->startOfMonth()->toDateString()))->startOfDay(),
            Carbon::parse($request->get('date_to', now()->toDateString()))->endOfDay(),
        ];
    }

    private function scopeInvest($query, string $column = 'firm_id')
    {
        $user = auth()->user();
        if ($user?->hasMerchantScope()) {
            $query->whereIn($column, $user->merchant_ids);
        }
        return $query;
    }

    public function index(Request $request): JsonResponse
    {
        [$from, $to] = $this->dateRange($request);
        $type = $request->get('type', '1') === '2' ? '2' : '1';

        // 1) OVERALL
        $overall = $this->scopeInvest(DB::table('invest')
            ->where('type', $type)
            ->whereIn('status', ['3', '4'])
            ->whereBetween('created_at', [$from, $to]))
            ->selectRaw("SUM(status='3') as approved, SUM(status='4') as rejected")
            ->first();

        $approved = (int) ($overall->approved ?? 0);
        $rejected = (int) ($overall->rejected ?? 0);
        $total    = $approved + $rejected;
        $rate     = $total > 0 ? round($approved * 100 / $total, 2) : 0;

        // 1b) Önceki dönem
        $days     = $from->diffInDays($to) + 1;
        $prevTo   = $from->copy()->subSecond();
        $prevFrom = $prevTo->copy()->subDays($days)->addSecond();
        $prev = $this->scopeInvest(DB::table('invest')
            ->where('type', $type)
            ->whereIn('status', ['3', '4'])
            ->whereBetween('created_at', [$prevFrom, $prevTo]))
            ->selectRaw("SUM(status='3') as approved, SUM(status='4') as rejected")
            ->first();

        $prevApproved = (int) ($prev->approved ?? 0);
        $prevRejected = (int) ($prev->rejected ?? 0);
        $prevTotal    = $prevApproved + $prevRejected;
        $prevRate     = $prevTotal > 0 ? round($prevApproved * 100 / $prevTotal, 2) : 0;
        $deltaPp      = round($rate - $prevRate, 2);

        // 2) DAILY trend
        $daily = $this->scopeInvest(DB::table('invest')
            ->where('type', $type)
            ->whereIn('status', ['3', '4'])
            ->whereBetween('created_at', [$from, $to]))
            ->selectRaw("DATE(created_at) as date, SUM(status='3') as approved, SUM(status='4') as rejected")
            ->groupBy('date')
            ->orderBy('date')
            ->get();

        // Tüm günleri doldur (boş günler 0/0 → null oran)
        $dates = [];
        $cur = $from->copy()->startOfDay();
        $end = $to->copy()->startOfDay();
        while ($cur->lte($end)) {
            $dates[] = $cur->toDateString();
            $cur->addDay();
        }
        $dailyMap = [];
        foreach ($daily as $d) {
            $dailyMap[$d->date] = ['approved' => (int) $d->approved, 'rejected' => (int) $d->rejected];
        }
        $dailySeries = [];
        $dailyApproved = [];
        $dailyRejected = [];
        foreach ($dates as $d) {
            $a = $dailyMap[$d]['approved'] ?? 0;
            $r = $dailyMap[$d]['rejected'] ?? 0;
            $t = $a + $r;
            $dailySeries[]   = $t > 0 ? round($a * 100 / $t, 2) : null;
            $dailyApproved[] = $a;
            $dailyRejected[] = $r;
        }

        // 3) BY TEAM (merchant kullanıcısına gösterilmez)
        $byTeam = collect();
        if (! auth()->user()?->hasMerchantScope()) {
            $byTeamRaw = DB::table('invest')
                ->join('teams', 'invest.team_id', '=', 'teams.id')
                ->where('invest.type', $type)
                ->whereIn('invest.status', ['3', '4'])
                ->whereBetween('invest.created_at', [$from, $to])
                ->selectRaw("teams.id, teams.name, SUM(invest.status='3') as approved, SUM(invest.status='4') as rejected")
                ->groupBy('teams.id', 'teams.name')
                ->get();

            $byTeam = $byTeamRaw->map(function ($r) {
                $a = (int) $r->approved; $j = (int) $r->rejected; $t = $a + $j;
                return [
                    'id'       => $r->id,
                    'name'     => $r->name,
                    'approved' => $a,
                    'rejected' => $j,
                    'total'    => $t,
                    'rate'     => $t > 0 ? round($a * 100 / $t, 2) : 0,
                ];
            })->sortByDesc('total')->values();
        }

        // 4) BY MERCHANT (group-aware)
        $merchantNames = (new MerchantReportController)->getMerchantDisplayMap();
        $byMerchantRaw = $this->scopeInvest(DB::table('invest')
            ->where('type', $type)
            ->whereIn('status', ['3', '4'])
            ->whereBetween('created_at', [$from, $to]))
            ->selectRaw("firm_id, SUM(status='3') as approved, SUM(status='4') as rejected")
            ->groupBy('firm_id')
            ->get();

        // Aynı display name (grup) altında topla
        $merged = [];
        foreach ($byMerchantRaw as $r) {
            $name = $merchantNames[$r->firm_id] ?? "Merchant #{$r->firm_id}";
            if (! isset($merged[$name])) {
                $merged[$name] = ['name' => $name, 'approved' => 0, 'rejected' => 0];
            }
            $merged[$name]['approved'] += (int) $r->approved;
            $merged[$name]['rejected'] += (int) $r->rejected;
        }
        $byMerchant = collect(array_values($merged))->map(function ($m) {
            $t = $m['approved'] + $m['rejected'];
            $m['total'] = $t;
            $m['rate']  = $t > 0 ? round($m['approved'] * 100 / $t, 2) : 0;
            return $m;
        })->sortByDesc('total')->values();

        return response()->json([
            'overall' => [
                'approved'   => $approved,
                'rejected'   => $rejected,
                'total'      => $total,
                'rate'       => $rate,
                'prev_rate'  => $prevRate,
                'delta_pp'   => $deltaPp,
            ],
            'daily' => [
                'categories' => $dates,
                'rate'       => $dailySeries,
                'approved'   => $dailyApproved,
                'rejected'   => $dailyRejected,
            ],
            'by_team'     => $byTeam,
            'by_merchant' => $byMerchant,
        ]);
    }
}
