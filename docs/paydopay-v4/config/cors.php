<?php

/*
|--------------------------------------------------------------------------
| Cross-Origin Resource Sharing (CORS)
|--------------------------------------------------------------------------
| API v1 ve panel endpoint'leri için CORS politikası. Bearer token kullanılıyor;
| credentials cookie kullanmıyoruz, bu nedenle `supports_credentials` false.
*/

return [
    'paths' => ['api/*', 'sanctum/csrf-cookie'],

    'allowed_methods' => ['GET', 'POST', 'PUT', 'PATCH', 'DELETE', 'OPTIONS'],

    'allowed_origins' => array_filter(array_map('trim', explode(',', (string) env('CORS_ALLOWED_ORIGINS', '*')))),

    'allowed_origins_patterns' => [],

    'allowed_headers' => [
        'Accept',
        'Authorization',
        'Content-Type',
        'X-Requested-With',
        'X-Api-Key',
        'X-Timestamp',
        'X-Signature',
    ],

    'exposed_headers' => [],

    'max_age' => 3600,

    'supports_credentials' => false,
];
