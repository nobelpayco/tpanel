<?php

namespace App\Http\Controllers\Api\V1;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Support\Facades\DB;

/**
 * GET /api/v1/health
 * Public health endpoint — auth gerekmez.
 * Yanıt: { status, services{ api, database }, time, version }
 */
class HealthController extends Controller
{
    public function index(): JsonResponse
    {
        // DB ping
        $dbStatus = 'ok';
        $dbLatency = null;
        try {
            $t0 = microtime(true);
            DB::select('SELECT 1');
            $dbLatency = (int) round((microtime(true) - $t0) * 1000);
        } catch (\Throwable $e) {
            $dbStatus = 'error';
        }

        $overall = $dbStatus === 'ok' ? 'ok' : 'down';

        // Public response — sistem profili sızdırmamak için minimal payload
        $payload = [
            'status'   => $overall,
            'services' => [
                'api'      => ['status' => 'ok'],
                'database' => ['status' => $dbStatus],
            ],
            'time'     => now()->toIso8601String(),
            'version'  => config('app.version', 'v1'),
        ];

        $http = $overall === 'ok' ? 200 : 503;
        return response()->json($payload, $http);
    }
}
