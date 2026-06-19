<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Carbon;
use Illuminate\Support\Facades\DB;

class PlayerRiskController extends Controller
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
    private function applyMerchantFilter($query, ?array $merchantIds): void
    {
        if ($merchantIds !== null) {
            $query->whereIn('firm_id', $merchantIds);
        }
    }

    /**
     * Check blacklist status in bulk for a list of player IDs.
     */
    private function getBlacklistedPlayerIds(array $playerIds): array
    {
        if (empty($playerIds)) return [];

        return DB::table('blacklist')
            ->where('type', 1)
            ->whereIn('val', $playerIds)
            ->pluck('val')
            ->toArray();
    }

    /**
     * Şüpheli Oyuncu Tespiti — Suspicious Player Detection
     */
    public function suspiciousPlayers(Request $request): JsonResponse
    {
        [$dateFrom, $dateTo] = $this->dateRange($request);
        $merchantIds = $this->getFilteredMerchantIds($request);

        // Fetch all relevant transactions in the date range (deposits only)
        $query = DB::table('invest')
            ->where('type', '1')
            ->whereBetween('created_at', [$dateFrom, $dateTo]);

        $this->applyMerchantFilter($query, $merchantIds);

        $transactions = $query
            ->select('id', 'player_id', 'type', 'status', 'amount', 'created_at')
            ->get();

        // Group transactions by player
        $grouped = $transactions->groupBy('player_id');

        $flaggedPlayers = [];

        foreach ($grouped as $playerId => $txs) {
            $flags = [];
            $totalTx = $txs->count();
            $approved = $txs->where('status', '3')->count();
            $rejected = $txs->where('status', '4')->count();
            $totalAmount = $txs->sum('amount');
            $avgAmount = $totalTx > 0 ? round($totalAmount / $totalTx) : 0;

            $approvedTxs = $txs->where('status', '3');

            // a) Low amount flooding: 5+ approved transactions all under 999 TL, none above 1000 TL
            if ($approvedTxs->count() >= 5) {
                $maxAmount = $approvedTxs->max('amount');
                if ($maxAmount < 1000) {
                    $flags[] = 'low_amount';
                }
            }

            // b) Same amount pattern: 3+ transactions of the exact same amount (only under 1000 TL, exclude 1000+)
            $amountCounts = $txs->where('amount', '<', 1000)->groupBy('amount');
            foreach ($amountCounts as $amount => $group) {
                if ($group->count() >= 3) {
                    $flags[] = 'same_amount';
                    break;
                }
            }

            // c) Night activity: 50%+ of transactions between 00:00-06:00
            $nightCount = $txs->filter(function ($tx) {
                $hour = Carbon::parse($tx->created_at)->hour;
                return $hour >= 0 && $hour < 6;
            })->count();

            if ($totalTx > 0 && ($nightCount / $totalTx) >= 0.5) {
                $flags[] = 'night_activity';
            }

            // d) High rejection rate: 3+ rejected and 0 approved
            if ($rejected >= 3 && $approved === 0) {
                $flags[] = 'high_rejection';
            }

            // e) Rapid fire: 3+ transactions within 10 minutes of each other
            if ($totalTx >= 3) {
                $timestamps = $txs->pluck('created_at')
                    ->map(fn($ts) => Carbon::parse($ts)->timestamp)
                    ->sort()
                    ->values()
                    ->toArray();

                $hasRapidFire = false;
                for ($i = 0; $i <= count($timestamps) - 3; $i++) {
                    if (($timestamps[$i + 2] - $timestamps[$i]) <= 600) {
                        $hasRapidFire = true;
                        break;
                    }
                }

                if ($hasRapidFire) {
                    $flags[] = 'rapid_fire';
                }
            }

            if (!empty($flags)) {
                // Risk score calculation: base score per flag + severity multipliers
                $flagScores = [
                    'low_amount'     => 15,
                    'same_amount'    => 20,
                    'night_activity' => 15,
                    'high_rejection' => 25,
                    'rapid_fire'     => 25,
                ];

                $riskScore = 0;
                foreach ($flags as $flag) {
                    $riskScore += $flagScores[$flag] ?? 0;
                }
                $riskScore = min($riskScore, 100);

                $flaggedPlayers[] = [
                    'player_id'    => $playerId,
                    'risk_score'   => $riskScore,
                    'total_tx'     => $totalTx,
                    'approved'     => $approved,
                    'rejected'     => $rejected,
                    'total_amount' => $totalAmount,
                    'avg_amount'   => $avgAmount,
                    'flags'        => $flags,
                ];
            }
        }

        // Sort by risk score descending and limit to 200
        usort($flaggedPlayers, fn($a, $b) => $b['risk_score'] <=> $a['risk_score']);
        $flaggedPlayers = array_slice($flaggedPlayers, 0, 200);

        // Bulk blacklist check
        $playerIds = array_column($flaggedPlayers, 'player_id');
        $blacklisted = $this->getBlacklistedPlayerIds($playerIds);
        $blacklistedSet = array_flip($blacklisted);

        // Add blacklist status
        foreach ($flaggedPlayers as &$player) {
            $player['is_blacklisted'] = isset($blacklistedSet[$player['player_id']]);
        }
        unset($player);

        // Build summary
        $byFlag = [];
        foreach ($flaggedPlayers as $player) {
            foreach ($player['flags'] as $flag) {
                $byFlag[$flag] = ($byFlag[$flag] ?? 0) + 1;
            }
        }

        return response()->json([
            'players' => $flaggedPlayers,
            'summary' => [
                'total_risky' => count($flaggedPlayers),
                'by_flag'     => $byFlag,
            ],
        ]);
    }

    /**
     * Oyuncu Segmentasyonu — Player Segmentation
     */
    public function playerSegmentation(Request $request): JsonResponse
    {
        [$dateFrom, $dateTo] = $this->dateRange($request);
        $merchantIds = $this->getFilteredMerchantIds($request);

        // Get per-player aggregates within the date range
        $query = DB::table('invest')
            ->where('type', '1')
            ->whereBetween('created_at', [$dateFrom, $dateTo]);

        $this->applyMerchantFilter($query, $merchantIds);

        $playerStats = $query
            ->select('player_id')
            ->selectRaw("SUM(CASE WHEN status = '3' THEN 1 ELSE 0 END) as approved_count")
            ->selectRaw("SUM(CASE WHEN status = '4' THEN 1 ELSE 0 END) as rejected_count")
            ->selectRaw("COUNT(*) as total_count")
            ->selectRaw("SUM(CASE WHEN status = '3' THEN amount ELSE 0 END) as approved_amount")
            ->groupBy('player_id')
            ->get();

        // Find inactive players: had transactions before the date range but none in the range
        $activePlayerIds = $playerStats->pluck('player_id')->toArray();

        $inactiveQuery = DB::table('invest')
            ->where('type', '1')
            ->where('created_at', '<', $dateFrom);

        $this->applyMerchantFilter($inactiveQuery, $merchantIds);

        if (!empty($activePlayerIds)) {
            $inactiveQuery->whereNotIn('player_id', $activePlayerIds);
        }

        $inactivePlayers = $inactiveQuery
            ->select('player_id')
            ->selectRaw("SUM(CASE WHEN status = '3' THEN amount ELSE 0 END) as approved_amount")
            ->selectRaw("COUNT(*) as total_count")
            ->groupBy('player_id')
            ->limit(200)
            ->get();

        // Segment definitions
        $segments = [
            'VIP'      => ['count' => 0, 'total_amount' => 0, 'players' => []],
            'Active'   => ['count' => 0, 'total_amount' => 0, 'players' => []],
            'Normal'   => ['count' => 0, 'total_amount' => 0, 'players' => []],
            'New'      => ['count' => 0, 'total_amount' => 0, 'players' => []],
            'Risky'    => ['count' => 0, 'total_amount' => 0, 'players' => []],
            'Inactive' => ['count' => 0, 'total_amount' => 0, 'players' => []],
        ];

        foreach ($playerStats as $stat) {
            $approved = (int) $stat->approved_count;
            $rejected = (int) $stat->rejected_count;
            $total = (int) $stat->total_count;
            $amount = (float) $stat->approved_amount;

            // Determine segment (priority order: Risky check, then tier)
            $segment = 'New';

            if ($total >= 3 && $rejected > 0 && ($rejected / $total) > 0.5) {
                $segment = 'Risky';
            } elseif ($amount > 100000 || $approved >= 50) {
                $segment = 'VIP';
            } elseif (($approved >= 10 && $approved <= 49) || ($amount >= 10000 && $amount <= 100000)) {
                $segment = 'Active';
            } elseif ($approved >= 3 && $approved <= 9) {
                $segment = 'Normal';
            } elseif ($approved >= 1 && $approved <= 2) {
                $segment = 'New';
            }

            $segments[$segment]['count']++;
            $segments[$segment]['total_amount'] += $amount;

            // Keep top 10 players per segment (sorted by amount descending)
            if (count($segments[$segment]['players']) < 10) {
                $segments[$segment]['players'][] = [
                    'player_id'       => $stat->player_id,
                    'approved_count'  => $approved,
                    'rejected_count'  => $rejected,
                    'total_count'     => $total,
                    'approved_amount' => $amount,
                ];
            } else {
                // Check if this player has higher amount than the lowest in top 10
                $minIndex = 0;
                $minAmount = $segments[$segment]['players'][0]['approved_amount'];
                for ($i = 1; $i < 10; $i++) {
                    if ($segments[$segment]['players'][$i]['approved_amount'] < $minAmount) {
                        $minAmount = $segments[$segment]['players'][$i]['approved_amount'];
                        $minIndex = $i;
                    }
                }
                if ($amount > $minAmount) {
                    $segments[$segment]['players'][$minIndex] = [
                        'player_id'       => $stat->player_id,
                        'approved_count'  => $approved,
                        'rejected_count'  => $rejected,
                        'total_count'     => $total,
                        'approved_amount' => $amount,
                    ];
                }
            }
        }

        // Add inactive players
        foreach ($inactivePlayers as $stat) {
            $amount = (float) $stat->approved_amount;
            $segments['Inactive']['count']++;
            $segments['Inactive']['total_amount'] += $amount;

            if (count($segments['Inactive']['players']) < 10) {
                $segments['Inactive']['players'][] = [
                    'player_id'       => $stat->player_id,
                    'approved_count'  => 0,
                    'rejected_count'  => 0,
                    'total_count'     => (int) $stat->total_count,
                    'approved_amount' => $amount,
                ];
            }
        }

        // Sort top 10 players in each segment by amount descending
        foreach ($segments as &$seg) {
            usort($seg['players'], fn($a, $b) => $b['approved_amount'] <=> $a['approved_amount']);
        }
        unset($seg);

        // Bulk blacklist check for all displayed players
        $allPlayerIds = [];
        foreach ($segments as $seg) {
            foreach ($seg['players'] as $p) {
                $allPlayerIds[] = $p['player_id'];
            }
        }
        $blacklisted = $this->getBlacklistedPlayerIds($allPlayerIds);
        $blacklistedSet = array_flip($blacklisted);

        foreach ($segments as &$seg) {
            foreach ($seg['players'] as &$player) {
                $player['is_blacklisted'] = isset($blacklistedSet[$player['player_id']]);
            }
            unset($player);
        }
        unset($seg);

        // Build response
        $segmentList = [];
        $chartLabels = [];
        $chartCounts = [];
        $chartAmounts = [];

        foreach ($segments as $name => $data) {
            $segmentList[] = [
                'name'         => $name,
                'count'        => $data['count'],
                'total_amount' => $data['total_amount'],
                'players'      => $data['players'],
            ];
            $chartLabels[] = $name;
            $chartCounts[] = $data['count'];
            $chartAmounts[] = $data['total_amount'];
        }

        return response()->json([
            'segments' => $segmentList,
            'chart'    => [
                'labels'  => $chartLabels,
                'counts'  => $chartCounts,
                'amounts' => $chartAmounts,
            ],
        ]);
    }

    /**
     * Aynı player_id'ye sahip ama farklı ad/soyad kullanan oyuncular
     */
    public function multiNamePlayers(Request $request): JsonResponse
    {
        $dateFrom = $request->get('date_from', now()->startOfYear()->toDateString()) . ' 00:00:00';
        $dateTo = $request->get('date_to', now()->toDateString()) . ' 23:59:59';
        $merchantIds = $this->getFilteredMerchantIds($request);

        // Aynı player_id için 2+ farklı isim olan oyuncuları bul
        $query = DB::table('invest')
            ->where('type', '1')
            ->whereIn('status', ['3', '4'])
            ->whereNotNull('player_id')
            ->whereNotNull('name')
            ->where('name', '!=', '')
            ->whereBetween('created_at', [$dateFrom, $dateTo]);

        $this->applyMerchantFilter($query, $merchantIds);

        $results = $query
            ->select('player_id')
            ->selectRaw('COUNT(DISTINCT name) as name_count')
            ->selectRaw('COUNT(*) as total_count')
            ->selectRaw("SUM(CASE WHEN status = '3' THEN amount ELSE 0 END) as approved_amount")
            ->selectRaw("SUM(CASE WHEN status = '3' THEN 1 ELSE 0 END) as approved_count")
            ->selectRaw("SUM(CASE WHEN status = '4' THEN 1 ELSE 0 END) as rejected_count")
            ->groupBy('player_id')
            ->having('name_count', '>=', 2)
            ->orderByDesc('name_count')
            ->orderByDesc('total_count')
            ->limit(200)
            ->get();

        // Her oyuncu için farklı isimleri çek
        $playerIds = $results->pluck('player_id')->toArray();
        $namesByPlayer = [];

        if (!empty($playerIds)) {
            $nameQuery = DB::table('invest')
                ->where('type', '1')
                ->whereIn('player_id', $playerIds)
                ->whereIn('status', ['3', '4'])
                ->whereBetween('created_at', [$dateFrom, $dateTo])
                ->whereNotNull('name')
                ->where('name', '!=', '');

            $this->applyMerchantFilter($nameQuery, $merchantIds);

            $names = $nameQuery
                ->select('player_id', 'name')
                ->selectRaw('COUNT(*) as count')
                ->selectRaw('MAX(created_at) as last_used')
                ->groupBy('player_id', 'name')
                ->orderByDesc('count')
                ->get();

            foreach ($names as $n) {
                $namesByPlayer[$n->player_id][] = [
                    'name' => $n->name,
                    'count' => (int) $n->count,
                    'last_used' => $n->last_used,
                ];
            }
        }

        $blacklisted = $this->getBlacklistedPlayerIds($playerIds);
        $blacklistedSet = array_flip($blacklisted);

        $players = $results->map(function ($r) use ($namesByPlayer, $blacklistedSet) {
            return [
                'player_id'      => $r->player_id,
                'name_count'     => (int) $r->name_count,
                'total_count'    => (int) $r->total_count,
                'approved_count' => (int) $r->approved_count,
                'rejected_count' => (int) $r->rejected_count,
                'approved_amount' => round((float) $r->approved_amount, 2),
                'names'          => $namesByPlayer[$r->player_id] ?? [],
                'is_blacklisted' => isset($blacklistedSet[$r->player_id]),
            ];
        });

        return response()->json([
            'players' => $players,
            'total'   => $players->count(),
        ]);
    }
}
