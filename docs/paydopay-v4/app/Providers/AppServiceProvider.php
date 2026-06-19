<?php

namespace App\Providers;

use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Log;
use Illuminate\Support\ServiceProvider;

class AppServiceProvider extends ServiceProvider
{
    public function register(): void
    {
        //
    }

    public function boot(): void
    {
        // Slow query logger — SLOW_QUERY_MS ms üzerindeki sorguları slow-query.log'a yazar
        $threshold = (int) env('SLOW_QUERY_MS', 500);
        if ($threshold > 0) {
            DB::listen(function ($query) use ($threshold) {
                if ($query->time < $threshold) return;
                $sql = $query->sql;
                foreach ($query->bindings as $b) {
                    $val = is_numeric($b) ? (string) $b : "'" . addslashes((string) $b) . "'";
                    $sql = preg_replace('/\?/', $val, $sql, 1);
                }
                Log::channel('slow_query')->warning(sprintf('[%dms] %s', round($query->time), $sql), [
                    'connection' => $query->connectionName,
                    'context'    => app()->runningInConsole()
                        ? 'cli'
                        : (request()?->fullUrl() ?? 'web'),
                ]);
            });
        }
    }
}
