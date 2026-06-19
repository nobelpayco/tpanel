<?php

namespace App\Http\Middleware;

use Closure;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Symfony\Component\HttpFoundation\Response;

/**
 * Merchant API v1 auth — Stripe-style HMAC.
 *
 * Header'lar:
 *   X-Api-Key:   <merchantUser.apiKey>
 *   X-Timestamp: <unix-seconds>
 *   X-Signature: hex( hmac_sha256(apiSecret, signed_payload) )
 *
 * signed_payload = method + "\n" + path + "\n" + timestamp + "\n" + sha256(request_body)
 *
 * Doğrulama başarılı olursa $request->attributes['_merchant'] set edilir.
 */
class MerchantApiAuth
{
    private const TIMESTAMP_DRIFT_SECONDS = 300;

    public function handle(Request $request, Closure $next): Response
    {
        $startedAt = microtime(true);
        $apiKey   = $request->header('X-Api-Key', '');
        $timestamp = $request->header('X-Timestamp', '');
        $signature = $request->header('X-Signature', '');

        if ($apiKey === '' || $timestamp === '' || $signature === '') {
            return $this->fail($request, 401, 'Missing authentication headers.', null, $startedAt);
        }

        $merchant = DB::table('merchantUser')->where('apiKey', $apiKey)->first();
        if (! $merchant) {
            return $this->fail($request, 401, 'Invalid apiKey.', null, $startedAt);
        }

        if ((string) $merchant->status !== '1') {
            return $this->fail($request, 403, 'Account is not active.', $merchant->id, $startedAt);
        }

        if (! $merchant->apiSecret) {
            return $this->fail($request, 500, 'apiSecret is not configured.', $merchant->id, $startedAt);
        }

        $now = time();
        $ts  = (int) $timestamp;
        if (abs($now - $ts) > self::TIMESTAMP_DRIFT_SECONDS) {
            return $this->fail($request, 401, 'Timestamp drift exceeded.', $merchant->id, $startedAt);
        }

        $body = $request->getContent();
        $bodyHash = hash('sha256', $body);
        $signedPayload = $request->getMethod() . "\n"
            . $request->getPathInfo() . "\n"
            . $timestamp . "\n"
            . $bodyHash;

        $expected = hash_hmac('sha256', $signedPayload, $merchant->apiSecret);
        if (! hash_equals($expected, $signature)) {
            return $this->fail($request, 401, 'İmza hatalı.', $merchant->id, $startedAt);
        }

        $request->attributes->set('_merchant', $merchant);

        $response = $next($request);

        // Controller içinde validation/iş kuralı vb. ile 4xx/5xx dönmüş olabilir → status'a göre tip ayarla
        $code = $response->getStatusCode();
        $logStatus = ($code >= 200 && $code < 300) ? 'success' : 'error';

        $this->logRequest(
            $merchant->id,
            $request,
            $code,
            $logStatus,
            null,
            $startedAt,
            (string) $response->getContent(),
        );

        return $response;
    }

    private function fail(Request $request, int $status, string $message, ?int $merchantId, float $startedAt): Response
    {
        $bodyJson = json_encode(['code' => $status, 'status' => false, 'message' => $message], JSON_UNESCAPED_UNICODE);
        $this->logRequest($merchantId, $request, $status, 'error', $message, $startedAt, $bodyJson);

        return response()->json([
            'code'    => $status,
            'status'  => false,
            'message' => $message,
        ], $status);
    }

    private function logRequest(?int $merchantId, Request $request, int $httpCode, string $status, ?string $message, float $startedAt, ?string $responseBody = null): void
    {
        $duration = (int) ((microtime(true) - $startedAt) * 1000);
        $url = $request->getMethod() . ' ' . $request->getPathInfo();

        // Yeni birleşik log tablosu — UI'daki "API & Callback Logları" buradan okur
        try {
            DB::table('api_callback_logs')->insert([
                'invest_id'       => null,
                'merchant_id'     => $merchantId,
                'direction'       => 'in',
                'type'            => $status === 'success' ? 'inbound_api' : 'inbound_api_error',
                'url'             => substr($url, 0, 500),
                'request_payload' => substr((string) $request->getContent(), 0, 4000),
                'response_status' => $httpCode,
                'response_body'   => $responseBody !== null ? substr($responseBody, 0, 4000) : null,
                'duration_ms'     => $duration,
                'error'           => $message ? substr($message, 0, 500) : null,
                'triggered_by'    => null,
                'created_at'      => now(),
            ]);
        } catch (\Throwable) {
            // tablo yoksa sessizce geç
        }

        // Geriye uyumluluk — eski apiRequestLog tablosu da yazılmaya devam etsin
        try {
            DB::table('apiRequestLog')->insert([
                'merchantId'  => $merchantId,
                'requestIp'   => $request->ip(),
                'requestData' => substr((string) $request->getContent(), 0, 4000),
                'status'      => $status,
                'message'     => $message
                    ? substr($message, 0, 250)
                    : ($url . ' → ' . $httpCode),
                'created_at'  => now(),
            ]);
        } catch (\Throwable) {
            // log tablosu yoksa veya farklı şemada ise sessizce devam et
        }
    }
}
