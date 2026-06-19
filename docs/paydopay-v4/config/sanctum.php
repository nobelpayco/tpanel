<?php

use Laravel\Sanctum\Sanctum;

return [
    /*
    |--------------------------------------------------------------------------
    | Stateful Domains
    |--------------------------------------------------------------------------
    */
    'stateful' => explode(',', (string) env('SANCTUM_STATEFUL_DOMAINS', sprintf(
        '%s%s',
        'localhost,localhost:3000,127.0.0.1,127.0.0.1:8000,::1',
        Sanctum::currentApplicationUrlWithPort(),
    ))),

    'guard' => ['web'],

    /*
    |--------------------------------------------------------------------------
    | Expiration Minutes
    |--------------------------------------------------------------------------
    | Access token süresi (dakika). 8 saat (480) — idle olsa da olmasa da
    | bu süre sonunda yeniden login gerekir.
    */
    'expiration' => (int) env('SANCTUM_TOKEN_EXPIRATION', 480),

    /*
    |--------------------------------------------------------------------------
    | Idle Timeout (Minutes)
    |--------------------------------------------------------------------------
    | Token son kullanımdan bu süre sonra otomatik invalidate edilir.
    */
    'idle_minutes' => (int) env('SANCTUM_IDLE_MINUTES', 30),

    // NOT: SANCTUM_TOKEN_PREFIX kullanılmıyor — default findToken() prefix'li id'yi
    // integer parse edemediği için 401'e sebep oluyor. Sanctum 4.x'te prefix
    // desteği için ayrı bir lookup mekanizması gerek; şimdilik boş bırakılır.
    'token_prefix' => env('SANCTUM_TOKEN_PREFIX', ''),

    'middleware' => [
        'authenticate_session' => Laravel\Sanctum\Http\Middleware\AuthenticateSession::class,
        'encrypt_cookies'      => Illuminate\Cookie\Middleware\EncryptCookies::class,
        'validate_csrf_token'  => Illuminate\Foundation\Http\Middleware\ValidateCsrfToken::class,
    ],
];
