<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use App\Models\User;
use App\Services\Auth\TwoFactorService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\Auth;

class AuthController extends Controller
{
    public function login(Request $request): JsonResponse
    {
        $request->validate([
            'username' => 'required|string',
            'password' => 'required|string',
        ]);

        $user = User::where('username', $request->username)
            ->where('status', '1')
            ->first();

        // Constant-time comparison: user yoksa dummy hash ile kontrol et — timing leak kapanır
        $dummyHash = '00000000000000000000000000000000'; // 32-char dummy md5
        $storedHash = $user?->password ?? $dummyHash;
        $passwordOk = hash_equals($storedHash, md5($request->password));

        if (! $user || ! $passwordOk) {
            return response()->json([
                'message' => __('auth.invalid_credentials'),
            ], 401);
        }

        if ($user->isBlocked()) {
            return response()->json([
                'message' => __('auth.account_blocked'),
            ], 403);
        }

        // 2FA kontrolü
        if ($user->hasOtpEnabled()) {
            $tempToken = encrypt($user->id . '|' . now()->addMinutes(5)->timestamp);

            $response = [
                'two_factor' => true,
                'temp_token' => $tempToken,
                'message'    => __('auth.two_factor_required'),
            ];

            if (! $user->otp_code) {
                $twoFactor = app(TwoFactorService::class);
                $secret = $twoFactor->generateSecret();
                $user->update(['otp_code' => $secret]);

                // Sadece QR (SVG base64) gönder — plaintext secret asla response'a girmez.
                // Kullanıcı manuel girmek isterse QR'daki otpauth URI'sini Authenticator app'inden gösterir.
                $response['setup_required'] = true;
                $response['qr_code'] = base64_encode($twoFactor->getQrCodeSvg($user, $secret));
            }

            return response()->json($response);
        }

        $user->update(['last_login' => now()]);
        $token = $user->createToken('auth-token')->plainTextToken;

        return response()->json([
            'token' => $token,
            'user'  => $this->userPayload($user),
        ]);
    }

    public function verifyTwoFactor(Request $request, TwoFactorService $twoFactor): JsonResponse
    {
        $request->validate([
            'temp_token' => 'required|string',
            'code'       => 'required|string|size:6',
        ]);

        try {
            $decrypted = decrypt($request->temp_token);
            [$userId, $expiresAt] = explode('|', $decrypted);

            if (now()->timestamp > (int) $expiresAt) {
                return response()->json(['message' => __('auth.session_expired')], 401);
            }
        } catch (\Exception) {
            return response()->json(['message' => __('auth.invalid_token')], 401);
        }

        $user = User::findOrFail($userId);

        if (! $twoFactor->verify($user, $request->code)) {
            return response()->json(['message' => __('auth.invalid_code')], 401);
        }

        $user->update(['last_login' => now()]);
        $token = $user->createToken('auth-token')->plainTextToken;

        return response()->json([
            'token' => $token,
            'user'  => $this->userPayload($user),
        ]);
    }

    public function me(Request $request): JsonResponse
    {
        return response()->json(['user' => $this->userPayload($request->user())]);
    }

    public function logout(Request $request): JsonResponse
    {
        $request->user()->currentAccessToken()->delete();
        return response()->json(['message' => __('auth.logged_out')]);
    }

    public function changePassword(Request $request): JsonResponse
    {
        $request->validate([
            'current_password' => 'required|string',
            'new_password'     => ['required', 'string', 'min:6', 'regex:/^(?=.*[A-Za-z])(?=.*\d).+$/', 'different:current_password'],
        ], [
            'new_password.min'       => __('auth.pw_min'),
            'new_password.regex'     => __('auth.pw_regex'),
            'new_password.different' => __('auth.pw_different'),
        ]);

        $user = $request->user();

        if (md5($request->current_password) !== $user->password) {
            return response()->json(['message' => __('auth.current_password_wrong')], 422);
        }

        \Illuminate\Support\Facades\DB::table('users')
            ->where('id', $user->id)
            ->update(['password' => md5($request->new_password)]);

        // Şifre değişiminde TÜM tokenleri iptal et — mevcut oturum dahil. Kullanıcı yeniden login olacak.
        \Illuminate\Support\Facades\DB::table('personal_access_tokens')
            ->where('tokenable_id', $user->id)
            ->delete();

        return response()->json([
            'message'         => __('auth.password_updated_reauth'),
            'reauth_required' => true,
        ]);
    }

    private function userPayload(User $user): array
    {
        return [
            'id'         => $user->id,
            'name'       => $user->name,
            'username'   => $user->username,
            'user_type'  => $user->user_type,
            'role_label' => $user->role_label,
            'team_id'    => $user->team_id,
            'firm_id'    => $user->firm_id,
            'permissions' => [
                'manage_users'        => $user->canManageUsers(),
                'manage_teams'        => $user->canManageTeams(),
                'manage_merchants'    => $user->canManageMerchants(),
                'manage_bank_accounts'=> $user->canManageBankAccounts(),
                'approve_transactions'=> $user->canApproveTransactions(),
                'block_users'         => $user->canBlockUsers(),
                'system_settings'     => $user->canAccessSystemSettings(),
                'financial_reports'   => $user->canViewFinancialReports(),
                'performance_reports' => $user->canViewPerformanceReports(),
            ],
        ];
    }
}
