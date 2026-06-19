<?php

namespace App\Support;

use Illuminate\Support\Facades\DB;

class TrustScore
{
    /**
     * Bayesian-weighted trust score (0-100) based on last 10 deposits.
     * Returns [rate, count] — null rate if no history.
     */
    public static function calculate($playerId, $beforeId = null): array
    {
        if (! $playerId) return [null, 0];

        $q = DB::table('invest')
            ->where('player_id', $playerId)
            ->where('type', 1)
            ->whereIn('status', ['3', '4']);

        if ($beforeId) $q->where('id', '<', $beforeId);

        $lastTen = $q->orderByDesc('id')->limit(10)->pluck('status');

        if ($lastTen->count() === 0) return [null, 0];

        $approved = $lastTen->filter(fn($s) => $s == '3')->count();
        $count = $lastTen->count();
        $rawRate = $approved / $count;
        $weight = min($count / 10, 1);
        $rate = (int) round((0.75 * (1 - $weight) + $rawRate * $weight) * 100);

        return [$rate, $count];
    }
}
