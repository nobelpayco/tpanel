<?php

namespace App\Http\Middleware;

use Closure;
use Illuminate\Http\Request;

class SetLocale
{
    public function handle(Request $request, Closure $next)
    {
        $locale = $request->header('Accept-Language', 'tr');

        if (in_array($locale, ['tr', 'en', 'ru', 'ur'])) {
            app()->setLocale($locale);
        }

        return $next($request);
    }
}
