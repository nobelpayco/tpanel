<?php

namespace App\Services;

use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Http;
use Illuminate\Support\Facades\Log;

/**
 * Merchant'lara callback POST gönderir; her gönderimi api_callback_logs'a yazar.
 *
 * Tipler:
 *   success         — ödeme onaylandı (admin onayı veya invest status=3 olunca)
 *   fail            — ödeme başarısız (admin red, expire, vs.)
 *   expire          — link süresi doldu (auto-reject)
 *   manual_resend   — Super Admin tarafından el ile tekrar gönderim
 */
class CallbackService
{
    public function send(
        string $url,
        array $payload,
        string $type,
        ?int $investId = null,
        ?int $merchantId = null,
        ?int $triggeredBy = null,
    ): array {
        $startedAt = microtime(true);
        $status = null;
        $body = null;
        $error = null;

        if (! $url) {
            $error = 'callback URL boş';
        } else {
            try {
                $response = Http::timeout(10)->asJson()->post($url, $payload);
                $status = $response->status();
                $body = substr($response->body(), 0, 4000);
            } catch (\Throwable $e) {
                $error = substr($e->getMessage(), 0, 500);
                Log::warning('Callback send exception', [
                    'url' => $url,
                    'invest_id' => $investId,
                    'error' => $e->getMessage(),
                ]);
            }
        }

        $duration = (int) ((microtime(true) - $startedAt) * 1000);

        DB::table('api_callback_logs')->insert([
            'invest_id'       => $investId,
            'merchant_id'     => $merchantId,
            'direction'       => 'out',
            'type'            => $type,
            'url'             => substr($url, 0, 500),
            'request_payload' => json_encode($payload, JSON_UNESCAPED_UNICODE),
            'response_status' => $status,
            'response_body'   => $body,
            'duration_ms'     => $duration,
            'error'           => $error,
            'triggered_by'    => $triggeredBy,
            'created_at'      => now(),
        ]);

        return [
            'success' => $status !== null && $status >= 200 && $status < 300,
            'status'  => $status,
            'body'    => $body,
            'error'   => $error,
        ];
    }

    /**
     * Bir invest için "ödeme bulunmadı" / expire callback'i.
     * callbackFailUrl varsa kullanılır.
     */
    public function sendExpire(object $invest, ?int $triggeredBy = null): array
    {
        $payload = [
            'order_id' => $invest->order_id,
            'uID'      => $invest->u_id,
            'status'   => false,
            'message'  => 'Ödeme bulunmadı',
        ];

        return $this->send(
            url: $invest->callbackUrl ?? $invest->callbackFailUrl ?? '',
            payload: $payload,
            type: 'expire',
            investId: (int) $invest->id,
            merchantId: (int) $invest->firm_id,
            triggeredBy: $triggeredBy,
        );
    }

    /**
     * Invest için onay/red callback'i — DepositController/WithdrawController'daki
     * eski private sendCallback mantığının service karşılığı (hash + form/json + log).
     *
     * $force=true: callbackSended flag'i göz ardı edilir (manuel resend).
     * Başarılı gönderim sonrası invest.callbackSended=1 yapılır.
     */
    public function sendForInvest(
        object $invest,
        bool $approved,
        string $detail = '',
        ?int $triggeredBy = null,
        bool $force = false,
        string $type = null,
    ): array {
        if (! $force && (int) ($invest->callbackSended ?? 0) === 1) {
            return ['success' => false, 'status' => null, 'body' => null, 'error' => 'already sent'];
        }
        if (empty($invest->callbackUrl)) {
            return ['success' => false, 'status' => null, 'body' => null, 'error' => 'callbackUrl empty'];
        }

        $merchant = DB::table('merchantUser')->where('id', $invest->firm_id)->first();
        if (! $merchant) {
            return ['success' => false, 'status' => null, 'body' => null, 'error' => 'merchant not found'];
        }

        $hash = hash('sha256', $merchant->apiKey . '|' . $invest->order_id . '|' . ($approved ? 'true' : 'false'));

        $payload = $approved ? [
            'code'       => 200,
            'status'     => true,
            'uID'        => $invest->order_id,
            'saleID'     => $invest->u_id,
            'amount'     => (float) $invest->amount,
            'senderName' => $invest->name,
            'hash'       => $hash,
            'message'    => 'Ödeme onaylandı - Transaction approved',
        ] : [
            'code'       => 201,
            'status'     => false,
            'message'    => $detail ?: 'Ödeme reddedildi',
            'detail'     => $detail,
            'uID'        => $invest->order_id,
            'saleID'     => $invest->u_id,
            'amount'     => (float) $invest->amount,
            'senderName' => $invest->name,
            'hash'       => $hash,
        ];

        $useJson = (int) ($merchant->new_api ?? 0) === 1;
        $startedAt = microtime(true);
        $status = null;
        $body = null;
        $error = null;

        try {
            $resp = $useJson
                ? Http::timeout(10)->asJson()->post($invest->callbackUrl, $payload)
                : Http::timeout(10)->asForm()->post($invest->callbackUrl, $payload);
            $status = $resp->status();
            $body = substr($resp->body(), 0, 4000);
        } catch (\Throwable $e) {
            $error = substr($e->getMessage(), 0, 500);
            Log::warning('Callback send exception', ['url' => $invest->callbackUrl, 'invest_id' => $invest->id, 'error' => $e->getMessage()]);
        }

        $duration = (int) ((microtime(true) - $startedAt) * 1000);
        $isSuccess = $status !== null && $status >= 200 && $status < 300;

        DB::table('api_callback_logs')->insert([
            'invest_id'       => (int) $invest->id,
            'merchant_id'     => (int) $invest->firm_id,
            'direction'       => 'out',
            'type'            => $type ?: ($approved ? 'success' : 'fail'),
            'url'             => substr($invest->callbackUrl, 0, 500),
            'request_payload' => json_encode($payload, JSON_UNESCAPED_UNICODE),
            'response_status' => $status,
            'response_body'   => $body,
            'duration_ms'     => $duration,
            'error'           => $error,
            'triggered_by'    => $triggeredBy,
            'created_at'      => now(),
        ]);

        if ($isSuccess) {
            DB::table('invest')->where('id', $invest->id)->update(['callbackSended' => 1]);
        }

        return ['success' => $isSuccess, 'status' => $status, 'body' => $body, 'error' => $error];
    }
}
