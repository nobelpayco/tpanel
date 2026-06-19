<?php

namespace App\Http\Middleware;

use Closure;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;

/**
 * Sanctum token'ın son kullanımını takip eder; idle süresi aşılırsa token
 * silinir ve 401 döner. Frontend idle logout'una bir server-side güvence.
 *
 * Default: 30 dakika. .env'de SANCTUM_IDLE_MINUTES ile ayarlanabilir.
 */
class EnforceIdleTimeout
{
    public function handle(Request $request, Closure $next)
    {
        $token = $request->user()?->currentAccessToken();
        if (! $token) {
            return $next($request);
        }

        $idleMinutes = (int) config('sanctum.idle_minutes', 30);
        if ($idleMinutes <= 0) {
            return $next($request);
        }

        $lastUsed = $token->last_used_at;
        if ($lastUsed && $lastUsed->lt(now()->subMinutes($idleMinutes))) {
            // Idle aşıldı — tokeni sil
            DB::table('personal_access_tokens')->where('id', $token->id)->delete();

            return response()->json([
                'message' => __('auth.idle_timeout'),
                'code'    => 'idle_timeout',
            ], 401);
        }

        return $next($request);
    }
}
