<?php

namespace App\Http\Controllers\Api\V1;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;

class WithdrawApiController extends Controller
{
    /**
     * POST /api/v1/withdraw
     * Body: { order_id, amount, player_id, name, iban, callback_url }
     */
    public function store(Request $request): JsonResponse
    {
        $merchant = $request->attributes->get('_merchant');

        $request->validate([
            'order_id'     => 'required|string|max:100',
            'amount'       => 'required|numeric|min:1',
            'player_id'    => 'required|string|max:100',
            'name'         => 'required|string|max:255',
            'iban'         => ['required', 'string', 'regex:/^TR\d{24}$/i'],
            'callback_url' => 'required|url|max:500',
        ]);

        $amount = (float) $request->amount;

        // Blacklist
        $blacklisted = DB::table('blacklist')->where('type', 1)->where('val', $request->player_id)->exists();
        if ($blacklisted) {
            return $this->error(403, 'Transaction not allowed (blacklist).');
        }

        // Duplicate order_id — sistem genelinde benzersiz olmalı (merchant farketmeksizin)
        $dup = DB::table('invest')
            ->where(function ($q) use ($request) {
                $q->where('order_id', $request->order_id)->orWhere('u_id', $request->order_id);
            })
            ->exists();
        if ($dup) {
            return $this->error(409, 'order_id already exists.');
        }

        $commission = (float) ($merchant->withdrawCommission ?? 0);
        $commissionAmount = round($amount * $commission / 100, 2);
        $uId = bin2hex(random_bytes(16));

        $investId = DB::table('invest')->insertGetId([
            'type'                       => '2',
            'status'                     => '0',
            'name'                       => $request->name,
            'amount'                     => $amount,
            'u_id'                       => $uId,
            'callbackUrl'                => $request->callback_url,
            'panel_commissin_amount'     => $commissionAmount,
            'payed_amount'               => $amount,
            'panel_commission_percent'   => (int) $commission,
            'iban'                       => strtoupper($request->iban),
            'api_id'                     => $merchant->id,
            'firm_id'                    => $merchant->id,
            'team_id'                    => null,
            'bank_id'                    => null,
            'player_id'                  => $request->player_id,
            'order_id'                   => $request->order_id,
            'added_type'                 => '1',
            'created_at'                 => now(),
            'ibanSeen'                   => 0,
            'callbackSended'             => 0,
            'isControled'                => 0,
            'isConverted'                => 0,
            'walletInvest'               => 0,
            'transaction_type'           => 1,
            'amountChanged'              => 0,
        ]);

        return response()->json([
            'code'    => 200,
            'status'  => true,
            'message' => 'Withdrawal request created.',
            'data'    => [
                'transaction_id' => $investId,
                'order_id'       => $request->order_id,
                'u_id'           => $uId,
                'amount'         => $amount,
                'status'         => 'pending',
            ],
        ]);
    }

    private function error(int $code, string $message): JsonResponse
    {
        return response()->json([
            'code'    => $code,
            'status'  => false,
            'message' => $message,
        ], $code);
    }
}
