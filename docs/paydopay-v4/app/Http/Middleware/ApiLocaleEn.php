<?php

namespace App\Http\Middleware;

use Closure;
use Illuminate\Http\Request;
use Symfony\Component\HttpFoundation\Response;

/**
 * Merchant API v1 + public pay endpoint'leri için locale'i İngilizce'ye zorla.
 * Validation hata mesajları ve manual response mesajları her zaman İngilizce döner.
 */
class ApiLocaleEn
{
    public function handle(Request $request, Closure $next): Response
    {
        app()->setLocale('en');
        return $next($request);
    }
}
