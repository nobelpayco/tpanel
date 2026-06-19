<?php

namespace App\Http\Controllers\Api\V1;

use App\Http\Controllers\Controller;
use App\Services\CallbackService;
use App\Services\MerchantBankService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;

/**
 * Oyuncunun ödeme sayfası için public endpoint'ler.
 * Auth: u_id md5 (tahmin edilemez); apiKey/HMAC YOK.
 */
class PublicPaymentController extends Controller
{
    public function __construct(
        private MerchantBankService $bankService,
        private CallbackService $callbacks,
    ) {}

    /**
     * GET /api/v1/pay/{u_id}
     * Frontend sayfası için durumu + (gerekirse) IBAN listesini döner.
     */
    public function show(string $uId): JsonResponse
    {
        $tx = DB::table('invest')->where('u_id', $uId)->first();
        if (! $tx) {
            return $this->error(404, __('messages.request_not_found'));
        }

        // Link expiry — system_settings'ten oku, switch açıksa süre dolmuş bekleyen invest'i otomatik reddet
        $expiryEnabled = (string) DB::table('system_settings')->where('key', 'pay_link_expiry_enabled')->value('value') === '1';
        $expiryMinutes = (int) DB::table('system_settings')->where('key', 'pay_link_expiry_minutes')->value('value') ?: 15;
        $isPending = in_array((string) $tx->status, ['0', '1', '2'], true);
        if ($expiryEnabled && $expiryMinutes > 0 && $isPending) {
            $createdAt = $tx->created_at ? \Carbon\Carbon::parse($tx->created_at) : null;
            if ($createdAt && $createdAt->lt(now()->subMinutes($expiryMinutes))) {
                DB::table('invest')->where('u_id', $uId)->update([
                    'status'        => '4',
                    'finalize_date' => now(),
                ]);
                $tx->status = '4';
                $this->callbacks->sendExpire($tx);
                return response()->json([
                    'code'    => 410,
                    'status'  => false,
                    'message' => 'Ödeme bulunmadı',
                    'expired' => true,
                ], 410);
            }
        }

        // Henüz IBAN seçilmedi ve hala beklemede → otomatik ilk uygun bankayı ata
        if ((int) $tx->ibanSeen === 0 && in_array((string) $tx->status, ['0', '1'], true)) {
            $picked = $this->bankService->pickOne((float) $tx->amount, (int) $tx->firm_id);
            if ($picked) {
                DB::table('invest')->where('u_id', $uId)->update([
                    'bank_id'  => $picked->id,
                    'team_id'  => $picked->team_id,
                    'status'   => '1',
                    'ibanSeen' => 1,
                    'form_at'  => now(),
                ]);
                $tx = DB::table('invest')->where('u_id', $uId)->first();
            } else {
                MerchantBankService::alertNoIbanAvailable(
                    merchantId: (int) $tx->firm_id,
                    amount: (float) $tx->amount,
                    investId: (int) $tx->id,
                    playerId: $tx->player_id,
                    orderId: $tx->order_id,
                    playerName: $tx->name,
                );
            }
        }

        $statusLabel = [
            '0' => 'pending', '1' => 'pending', '2' => 'processing',
            '3' => 'approved', '4' => 'rejected',
        ][(string) $tx->status] ?? 'unknown';

        $data = [
            'u_id'         => $tx->u_id,
            'order_id'     => $tx->order_id,
            'amount'       => (float) $tx->amount,
            'name'         => $tx->name,
            'status'       => $statusLabel,
            'iban_seen'    => (int) $tx->ibanSeen,
            'bank'         => null,
            'has_receipt'  => ! empty($tx->receipt_path),
            'success_url'  => $tx->callbackOkUrl,
            'fail_url'     => $tx->callbackFailUrl,
            'expires_at'   => ($expiryEnabled && $expiryMinutes > 0 && $tx->created_at)
                ? \Carbon\Carbon::parse($tx->created_at)->addMinutes($expiryMinutes)->toIso8601String()
                : null,
        ];

        if ($tx->bank_id) {
            $bank = DB::table('bankAccounts')
                ->join('banks', 'bankAccounts.bank_id', '=', 'banks.id')
                ->where('bankAccounts.id', $tx->bank_id)
                ->select(
                    'bankAccounts.id',
                    'bankAccounts.account_holder',
                    'bankAccounts.account_iban',
                    'banks.name as bank_name',
                )
                ->first();
            $data['bank'] = $bank ? [
                'id'             => $bank->id,
                'account_holder' => $bank->account_holder,
                'account_iban'   => $bank->account_iban,
                'bank_name'      => $bank->bank_name,
            ] : null;
        }

        return response()->json(['code' => 200, 'status' => true, 'data' => $data]);
    }

    /**
     * POST /api/v1/pay/{u_id}/receipt
     * Oyuncu dekont yükler — image/pdf, max 5MB.
     */
    public function uploadReceipt(string $uId, Request $request): JsonResponse
    {
        $tx = DB::table('invest')->where('u_id', $uId)->first();
        if (! $tx) return $this->error(404, __('messages.request_not_found'));
        if (! in_array((string) $tx->status, ['0', '1', '2'], true)) {
            return $this->error(409, __('messages.receipt_upload_blocked'));
        }

        $allowedMimes = [
            'image/jpeg'      => 'jpg',
            'image/png'       => 'png',
            'image/gif'       => 'gif',
            'image/webp'      => 'webp',
            'application/pdf' => 'pdf',
        ];

        $request->validate([
            'receipt' => [
                'required',
                'file',
                'max:10240', // 10 MB
                function ($attribute, $value, $fail) use ($allowedMimes) {
                    // 1) Symfony MIME detection (magic bytes okur, uzantıya değil dosya içeriğine bakar)
                    $mime = $value->getMimeType();
                    if (! array_key_exists($mime, $allowedMimes)) {
                        $fail(__('messages.receipt_only_image_pdf'));
                        return;
                    }
                    // 2) Ek bir kontrol: finfo ile de aynı sonucu doğrula
                    $finfo = finfo_open(FILEINFO_MIME_TYPE);
                    $actual = finfo_file($finfo, $value->getPathname());
                    finfo_close($finfo);
                    if (! array_key_exists($actual, $allowedMimes)) {
                        $fail(__('messages.receipt_only_image_pdf'));
                    }
                },
            ],
        ], [
            'receipt.max' => __('messages.receipt_max_size'),
        ]);

        $file = $request->file('receipt');
        // Uzantı MIME map'inden gelir, kullanıcı input'undan değil — path traversal + executable upload imkansız
        $ext = $allowedMimes[$file->getMimeType()] ?? 'bin';
        // Dosya adı tahmin edilemez (128-bit random)
        $filename = bin2hex(random_bytes(16)) . '.' . $ext;

        // PRIVATE storage — storage/app/receipts altında, public symlink'i yok
        $file->storeAs('receipts', $filename, 'local');

        // DB'de sadece dosya adı saklanır; serve etmek için signed route kullanılır
        DB::table('invest')->where('u_id', $uId)->update([
            'receipt_path' => 'receipts/' . $filename,
        ]);

        return response()->json([
            'code'    => 200,
            'status'  => true,
            'message' => __('messages.receipt_uploaded'),
        ]);
    }



    /**
     * POST /api/v1/pay/{u_id}/select-bank
     * Body: { bank_id, amount? }
     */
    public function selectBank(Request $request, string $uId): JsonResponse
    {
        $request->validate([
            'bank_id' => 'required|integer',
            'amount'  => 'nullable|numeric|min:1',
        ]);

        $tx = DB::table('invest')->where('u_id', $uId)->first();
        if (! $tx) {
            return $this->error(404, __('messages.request_not_found'));
        }

        if ((int) $tx->ibanSeen === 1) {
            return $this->error(409, __('messages.bank_already_selected'));
        }

        $finalAmount = $request->filled('amount') ? (float) $request->amount : (float) $tx->amount;

        $bank = $this->bankService->validate((int) $request->bank_id, $finalAmount, (int) $tx->firm_id);
        if (! $bank) {
            return $this->error(422, __('messages.invalid_bank'));
        }

        DB::table('invest')->where('u_id', $uId)->update([
            'bank_id'   => $bank->id,
            'team_id'   => $bank->team_id,
            'status'    => '1',
            'ibanSeen'  => 1,
            'amount'    => $finalAmount,
            'form_at'   => now(),
        ]);

        return response()->json([
            'code'   => 200,
            'status' => true,
            'data'   => [
                'bank' => [
                    'id'             => $bank->id,
                    'account_holder' => $bank->account_holder,
                    'account_iban'   => $bank->account_iban,
                    'bank_name'      => $bank->bank_name,
                ],
                'amount' => $finalAmount,
            ],
        ]);
    }

    /**
     * POST /api/v1/pay/{u_id}/paid
     * Oyuncu "ödeme yaptım" der → durum 1 → 2 (işlemde)
     */
    public function markPaid(string $uId): JsonResponse
    {
        $tx = DB::table('invest')->where('u_id', $uId)->first();
        if (! $tx) return $this->error(404, __('messages.request_not_found'));
        if ((string) $tx->status !== '1') {
            return $this->error(409, __('messages.action_not_allowed'));
        }
        DB::table('invest')->where('u_id', $uId)->update([
            'status'       => '2',
            'process_date' => now(),
        ]);
        return response()->json(['code' => 200, 'status' => true, 'message' => __('messages.mark_paid_success')]);
    }

    /**
     * POST /api/v1/pay/{u_id}/cancel
     * Oyuncu işlemi iptal eder → durum 0/1 → 4 (red, rejectType=5 kullanıcı iptali)
     */
    public function cancel(string $uId): JsonResponse
    {
        $tx = DB::table('invest')->where('u_id', $uId)->first();
        if (! $tx) return $this->error(404, __('messages.request_not_found'));
        if (! in_array((string) $tx->status, ['0', '1'], true)) {
            return $this->error(409, __('messages.action_not_allowed'));
        }
        DB::table('invest')->where('u_id', $uId)->update([
            'status'        => '4',
            'rejectType'    => 5,
            'finalize_date' => now(),
        ]);
        return response()->json(['code' => 200, 'status' => true, 'message' => __('messages.cancel_success')]);
    }

    private function error(int $code, string $message): JsonResponse
    {
        return response()->json(['code' => $code, 'status' => false, 'message' => $message], $code);
    }
}
