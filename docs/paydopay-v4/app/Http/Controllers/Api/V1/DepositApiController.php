<?php

namespace App\Http\Controllers\Api\V1;

use App\Http\Controllers\Controller;
use App\Services\MerchantBankService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;

class DepositApiController extends Controller
{
    /**
     * POST /api/v1/deposit
     * Body: { order_id, amount, player_id, name, callback_url, successRedirectUrl, failRedirectUrl }
     */
    public function store(Request $request): JsonResponse
    {
        $merchant = $request->attributes->get('_merchant');

        $request->validate([
            'order_id'           => 'required|string|max:100',
            'amount'             => 'required|numeric|min:1',
            'player_id'          => 'required|string|max:100',
            'name'               => 'required|string|max:255',
            'callback_url'       => 'required|url|max:500',
            'successRedirectUrl' => 'required|url|max:500',
            'failRedirectUrl'    => 'required|url|max:500',
        ]);

        $amount = (float) $request->amount;

        // Min/Max deposit kontrolü
        if ($amount < (float) $merchant->minDeposit) {
            return $this->error(422, 'Minimum deposit amount: ' . $merchant->minDeposit . ' TL');
        }
        if ($merchant->maxDeposit && $amount > (float) $merchant->maxDeposit) {
            return $this->error(422, 'Maximum deposit amount: ' . $merchant->maxDeposit . ' TL');
        }

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

        // Pending order
        $hasPending = DB::table('invest')
            ->where('player_id', $request->player_id)
            ->whereIn('status', ['1', '2'])
            ->where('ibanSeen', 1)
            ->where('created_at', '>=', now()->subMinutes(10))
            ->exists();
        if ($hasPending) {
            return $this->error(409, 'You have a pending order. Please complete it or try again in 10 minutes.');
        }

        $commission = (float) $merchant->commission;
        $commissionAmount = round($amount * $commission / 100, 2);
        $uId = bin2hex(random_bytes(16));

        $investId = DB::table('invest')->insertGetId([
            'type'                       => '1',
            'status'                     => '0',
            'name'                       => $request->name,
            'amount'                     => $amount,
            'original_amount'            => $amount,
            'u_id'                       => $uId,
            'callbackUrl'                => $request->callback_url,
            'callbackOkUrl'              => $request->successRedirectUrl,
            'callbackFailUrl'            => $request->failRedirectUrl,
            'panel_commissin_amount'     => $commissionAmount,
            'payed_amount'               => $amount,
            'panel_commission_percent'   => (int) $commission,
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

        // Hosted payment order yaratıldığı anda eligible IBAN yoksa PayRoute'a uyarı at
        // (Oyuncu pay sayfasını hiç açmasa bile yöneticiler haberdar olsun)
        if (app(MerchantBankService::class)->pickOne($amount, (int) $merchant->id) === null) {
            MerchantBankService::alertNoIbanAvailable(
                merchantId: (int) $merchant->id,
                amount: $amount,
                investId: $investId,
                playerId: $request->player_id,
                orderId: $request->order_id,
                playerName: $request->name,
            );
        }

        return response()->json([
            'code'    => 200,
            'status'  => true,
            'message' => 'Deposit request created.',
            'data'    => [
                'transaction_id' => $investId,
                'order_id'       => $request->order_id,
                'u_id'           => $uId,
                'amount'         => $amount,
                'pay_url'        => rtrim(config('app.url'), '/') . '/pay/' . $uId,
            ],
        ]);
    }

    /**
     * POST /api/v1/deposit/direct (H2H)
     * Body: { order_id, amount, player_id, name, callback_url }
     * Yanıtta doğrudan ilk uygun IBAN döner; pay_url yok, redirect URL'leri istenmez.
     */
    public function storeDirect(Request $request, MerchantBankService $banks): JsonResponse
    {
        $merchant = $request->attributes->get('_merchant');

        $request->validate([
            'order_id'     => 'required|string|max:100',
            'amount'       => 'required|numeric|min:1',
            'player_id'    => 'required|string|max:100',
            'name'         => 'required|string|max:255',
            'callback_url' => 'required|url|max:500',
        ]);

        $amount = (float) $request->amount;

        // Min/Max deposit kontrolü
        if ($amount < (float) $merchant->minDeposit) {
            return $this->error(422, 'Minimum deposit amount: ' . $merchant->minDeposit . ' TL');
        }
        if ($merchant->maxDeposit && $amount > (float) $merchant->maxDeposit) {
            return $this->error(422, 'Maximum deposit amount: ' . $merchant->maxDeposit . ' TL');
        }

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

        // Pending order
        $hasPending = DB::table('invest')
            ->where('player_id', $request->player_id)
            ->whereIn('status', ['1', '2'])
            ->where('ibanSeen', 1)
            ->where('created_at', '>=', now()->subMinutes(10))
            ->exists();
        if ($hasPending) {
            return $this->error(409, 'You have a pending order. Please complete it or try again in 10 minutes.');
        }

        // İlk uygun IBAN'ı seç
        $bank = $banks->pickOne($amount, (int) $merchant->id);
        if (! $bank) {
            MerchantBankService::alertNoIbanAvailable(
                merchantId: (int) $merchant->id,
                amount: $amount,
                playerId: $request->player_id,
                orderId: $request->order_id,
                playerName: $request->name,
            );
            return $this->error(503, 'No available bank account for this amount.');
        }

        $commission = (float) $merchant->commission;
        $commissionAmount = round($amount * $commission / 100, 2);
        $uId = bin2hex(random_bytes(16));

        $investId = DB::table('invest')->insertGetId([
            'type'                       => '1',
            'status'                     => '1',
            'name'                       => $request->name,
            'amount'                     => $amount,
            'original_amount'            => $amount,
            'u_id'                       => $uId,
            'callbackUrl'                => $request->callback_url,
            'panel_commissin_amount'     => $commissionAmount,
            'payed_amount'               => $amount,
            'panel_commission_percent'   => (int) $commission,
            'api_id'                     => $merchant->id,
            'firm_id'                    => $merchant->id,
            'team_id'                    => $bank->team_id,
            'bank_id'                    => $bank->id,
            'player_id'                  => $request->player_id,
            'order_id'                   => $request->order_id,
            'added_type'                 => '1',
            'created_at'                 => now(),
            'form_at'                    => now(),
            'ibanSeen'                   => 1,
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
            'message' => 'Deposit request created (H2H).',
            'data'    => [
                'transaction_id' => $investId,
                'order_id'       => $request->order_id,
                'u_id'           => $uId,
                'amount'         => $amount,
                'bank' => [
                    'id'             => $bank->id,
                    'account_holder' => $bank->account_holder,
                    'account_iban'   => $bank->account_iban,
                    'bank_name'      => $bank->bank_name,
                ],
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
