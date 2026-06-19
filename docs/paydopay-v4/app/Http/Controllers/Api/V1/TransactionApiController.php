<?php

namespace App\Http\Controllers\Api\V1;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;

class TransactionApiController extends Controller
{
    private const STATUS_LABEL = [
        '0' => 'waiting',
        '1' => 'waiting',
        '2' => 'waiting',
        '3' => 'approved',
        '4' => 'rejected',
    ];

    /**
     * GET /api/v1/transaction/{order_id}
     *
     * Yanıt sözleşmesi (callback ile uyumlu):
     *   approved  → code=200, status=true,  message="Transaction approved"
     *   rejected  → code=201, status=false, message="Transaction rejected"
     *   waiting   → code=202, status=null,  message="Transaction is still being processed"
     *   not found → code=404, status=false
     */
    public function show(Request $request, string $orderId): JsonResponse
    {
        $merchant = $request->attributes->get('_merchant');

        $tx = DB::table('invest')
            ->where('firm_id', $merchant->id)
            ->where(function ($q) use ($orderId) {
                $q->where('order_id', $orderId)->orWhere('u_id', $orderId);
            })
            ->first();

        if (! $tx) {
            return response()->json([
                'code'    => 404,
                'status'  => false,
                'message' => 'Transaction not found.',
            ], 404);
        }

        $statusCode = (int) $tx->status;
        $statusLabel = self::STATUS_LABEL[(string) $statusCode] ?? 'unknown';

        // Üst seviye code / status / message — callback formatıyla aynı
        if ($statusLabel === 'approved') {
            $code = 200; $status = true; $message = 'Transaction approved';
        } elseif ($statusLabel === 'rejected') {
            $code = 201; $status = false; $message = 'Transaction rejected';
        } else {
            // 0, 1, 2 — henüz sonuçlanmadı
            $code = 202; $status = null; $message = 'Transaction is still being processed';
        }

        return response()->json([
            'code'    => $code,
            'status'  => $status,
            'message' => $message,
            'data'    => [
                'order_id'     => $tx->order_id,
                'u_id'         => $tx->u_id,
                'status'       => $statusLabel,
                'status_code'  => $statusCode,
                'type'         => $tx->type == 1 ? 'deposit' : 'withdraw',
                'amount'       => (float) $tx->amount,
                'name'         => $tx->name,
                'player_id'    => $tx->player_id,
                'created_at'   => $tx->created_at,
                'finalized_at' => $tx->finalize_date,
            ],
        ], $code === 202 ? 200 : $code);
    }
}
