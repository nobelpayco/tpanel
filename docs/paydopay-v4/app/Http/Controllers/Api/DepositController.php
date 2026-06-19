<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use App\Support\TrustScore;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Http;
use Illuminate\Support\Facades\Storage;
use Symfony\Component\HttpFoundation\BinaryFileResponse;
use Symfony\Component\HttpFoundation\StreamedResponse;

class DepositController extends Controller
{
    public function pending(Request $request): JsonResponse
    {
        $user = auth()->user();

        $query = DB::table('invest')
            ->leftJoin('merchantUser', 'invest.firm_id', '=', 'merchantUser.id')
            ->leftJoin('teams', 'invest.team_id', '=', 'teams.id')
            ->leftJoin('bankAccounts', 'invest.bank_id', '=', 'bankAccounts.id')
            ->leftJoin('banks', 'bankAccounts.bank_id', '=', 'banks.id')
            ->leftJoin('users as agent', 'invest.agent_id', '=', 'agent.id')
            ->where('invest.type', 1)
            ->whereIn('invest.status', [1, 2])
            ->select(
                'invest.id', 'invest.status', 'invest.name', 'invest.amount', 'invest.original_amount', 'invest.amountChanged',
                'invest.order_id', 'invest.player_id', 'invest.playerID',
                'invest.created_at', 'invest.form_at', 'invest.process_date',
                'invest.agent_id', 'invest.firm_id', 'invest.team_id', 'invest.bank_id',
                'invest.iban', 'invest.u_id', 'invest.receipt_path',
                'merchantUser.name as merchant_name',
                'teams.name as team_name',
                'bankAccounts.account_holder', 'bankAccounts.account_iban', 'bankAccounts.account_code',
                'banks.name as bank_name', 'banks.logo as bank_logo',
                'agent.name as agent_name'
            );

        // Rol bazlı filtre
        if ($user->isTeamMember()) {
            $query->where('invest.team_id', $user->team_id);
        } elseif ($user->isMerchant()) {
            $merchantIds = $user->merchant_ids;
            if (count($merchantIds) > 1) {
                $query->whereIn('invest.firm_id', $merchantIds);
            } else {
                $query->where('invest.firm_id', $user->firm_id);
            }
        }

        // Filtreler
        if ($request->filled('merchant')) $query->where('invest.firm_id', $request->merchant);
        if ($request->filled('team')) $query->where('invest.team_id', $request->team);
        if ($request->filled('bank')) $query->where('invest.bank_id', $request->bank);
        if ($request->filled('name')) $query->where('invest.name', 'like', "%{$request->name}%");
        if ($request->filled('player_id')) $query->where('invest.player_id', $request->player_id);
        if ($request->filled('order_id')) $query->where('invest.order_id', $request->order_id);
        if ($request->filled('min_amount')) $query->where('invest.amount', '>=', $request->min_amount);
        if ($request->filled('max_amount')) $query->where('invest.amount', '<=', $request->max_amount);

        $deposits = $query->orderByDesc('invest.id')->limit(200)->get()->map(function ($d) use ($user) {
            [$trustRate, $trustCount] = TrustScore::calculate($d->player_id, $d->id);
            return [
                'id'              => $d->id,
                'status'          => (int) $d->status,
                'name'            => $d->name,
                'amount'          => (float) $d->amount,
                'original_amount' => $d->original_amount !== null ? (float) $d->original_amount : null,
                'amount_changed'  => (int) ($d->amountChanged ?? 0) === 1,
                'order_id'        => $d->order_id,
                'player_id'       => $d->player_id,
                'merchant_name'   => $user->isTeamMember() ? null : $d->merchant_name,
                'team_name'       => $user->hasMerchantScope() ? null : $d->team_name,
                'account_holder'  => $d->account_holder,
                'account_iban'    => $d->account_iban,
                'bank_name'       => $d->bank_name,
                'agent_name'      => $user->hasMerchantScope() ? null : $d->agent_name,
                'agent_id'        => $user->hasMerchantScope() ? null : $d->agent_id,
                'created_at'      => $d->created_at,
                'form_at'         => $d->form_at,
                'process_date'    => $d->process_date,
                'trust_rate'      => $trustRate,
                'trust_count'     => $trustCount,
                'has_receipt'     => ! empty($d->receipt_path),
            ];
        });

        return response()->json($deposits);
    }

    public function all(Request $request): JsonResponse
    {
        $user = auth()->user();

        $query = DB::table('invest')
            ->leftJoin('merchantUser', 'invest.firm_id', '=', 'merchantUser.id')
            ->leftJoin('teams', 'invest.team_id', '=', 'teams.id')
            ->leftJoin('bankAccounts', 'invest.bank_id', '=', 'bankAccounts.id')
            ->leftJoin('banks', 'bankAccounts.bank_id', '=', 'banks.id')
            ->leftJoin('users as agent', 'invest.agent_id', '=', 'agent.id')
            ->where('invest.type', 1)
            ->select(
                'invest.id', 'invest.status', 'invest.name', 'invest.amount', 'invest.original_amount', 'invest.amountChanged',
                'invest.order_id', 'invest.player_id', 'invest.u_id', 'invest.receipt_path',
                'invest.created_at', 'invest.finalize_date',
                'invest.firm_id', 'invest.team_id', 'invest.bank_id', 'invest.rejectType',
                'merchantUser.name as merchant_name',
                'teams.name as team_name',
                'bankAccounts.account_holder', 'bankAccounts.account_iban',
                'banks.name as bank_name', 'banks.logo as bank_logo',
                'agent.name as agent_name'
            );

        // Rol filtresi
        if ($user->isTeamMember()) {
            $query->where('invest.team_id', $user->team_id);
        } elseif ($user->isMerchant()) {
            $merchantIds = $user->merchant_ids;
            if (count($merchantIds) > 1) {
                $query->whereIn('invest.firm_id', $merchantIds);
            } else {
                $query->where('invest.firm_id', $user->firm_id);
            }
        }

        // Filtreler
        if ($request->boolean('converted_only')) $query->where('invest.isConverted', 1);
        if ($request->filled('id')) $query->where('invest.id', $request->id);
        if ($request->filled('status') && $request->status != 0) $query->where('invest.status', $request->status);
        if ($request->filled('merchant')) $query->where('invest.firm_id', $request->merchant);
        if ($request->filled('team')) $query->where('invest.team_id', $request->team);
        if ($request->filled('bank')) $query->where('invest.bank_id', $request->bank);
        if ($request->filled('name')) $query->where('invest.name', 'like', "%{$request->name}%");
        if ($request->filled('player_id')) $query->where('invest.player_id', $request->player_id);
        if ($request->filled('order_id')) $query->where('invest.order_id', $request->order_id);
        if ($request->filled('u_id')) $query->where('invest.u_id', $request->u_id);
        if ($request->filled('min_amount')) $query->where('invest.amount', '>=', $request->min_amount);
        if ($request->filled('max_amount')) $query->where('invest.amount', '<=', $request->max_amount);
        if ($request->filled('date_from')) $query->whereDate('invest.created_at', '>=', $request->date_from);
        if ($request->filled('date_to')) $query->whereDate('invest.created_at', '<=', $request->date_to);

        $perPage = (int) $request->get('per_page', 50);
        $page = (int) $request->get('page', 1);
        $total = (clone $query)->count();
        $totalAmount = (clone $query)->sum('invest.amount');

        $deposits = $query->orderByDesc('invest.id')
            ->offset(($page - 1) * $perPage)->limit($perPage)
            ->get()->map(function ($d) use ($user) {
                [$trustRate, $trustCount] = TrustScore::calculate($d->player_id, $d->id);
                return [
                    'id'              => $d->id,
                    'status'          => (int) $d->status,
                    'name'            => $d->name,
                    'amount'          => (float) $d->amount,
                    'original_amount' => $d->original_amount !== null ? (float) $d->original_amount : null,
                    'amount_changed'  => (int) ($d->amountChanged ?? 0) === 1,
                    'order_id'        => $d->order_id,
                    'player_id'       => $d->player_id,
                    'u_id'            => $d->u_id,
                    'merchant_name'   => $user->isTeamMember() ? null : $d->merchant_name,
                    'team_name'       => $user->hasMerchantScope() ? null : $d->team_name,
                    'account_holder'  => $d->account_holder,
                    'account_iban'    => $d->account_iban,
                    'bank_name'       => $d->bank_name,
                    'agent_name'      => $user->hasMerchantScope() ? null : $d->agent_name,
                    'reject_type'     => $d->rejectType,
                    'has_receipt'     => ! empty($d->receipt_path),
                    'created_at'      => $d->created_at,
                    'finalize_date'   => $d->finalize_date,
                    'trust_rate'      => $trustRate,
                    'trust_count'     => $trustCount,
                ];
            });

        return response()->json([
            'deposits'     => $deposits,
            'total'        => $total,
            'total_amount' => $totalAmount,
            'page'         => $page,
            'per_page'     => $perPage,
        ]);
    }

    /**
     * GET /api/deposits/{id}/receipt
     * Yetkili kullanıcılara (scope dahilinde) dekont dosyasını stream'ler.
     * Dosya private storage'da (storage/app/receipts) tutulur; direkt URL erişimi yok.
     */
    public function receipt(int $id)
    {
        $user = auth()->user();
        $d = DB::table('invest')->where('id', $id)->first();

        if (! $d || ! $d->receipt_path) {
            abort(404);
        }

        // Scope kontrolü
        if ($user->isTeamMember() && $d->team_id != $user->team_id) {
            abort(403);
        }
        if ($user->isMerchant() && ! in_array($d->firm_id, $user->merchant_ids ?: [$user->firm_id])) {
            abort(403);
        }

        if (! Storage::disk('local')->exists($d->receipt_path)) {
            abort(404);
        }

        $fullPath = Storage::disk('local')->path($d->receipt_path);
        $mime = mime_content_type($fullPath) ?: 'application/octet-stream';
        $allowed = ['image/jpeg', 'image/png', 'image/gif', 'image/webp', 'application/pdf'];
        if (! in_array($mime, $allowed, true)) {
            abort(415);
        }

        return response()->file($fullPath, [
            'Content-Type'        => $mime,
            'Content-Disposition' => 'inline; filename="receipt-' . $d->id . '"',
            'X-Content-Type-Options' => 'nosniff',
        ]);
    }

    public function detail(int $id): JsonResponse
    {
        $user = auth()->user();

        $d = DB::table('invest')
            ->leftJoin('merchantUser', 'invest.firm_id', '=', 'merchantUser.id')
            ->leftJoin('teams', 'invest.team_id', '=', 'teams.id')
            ->leftJoin('bankAccounts', 'invest.bank_id', '=', 'bankAccounts.id')
            ->leftJoin('banks', 'bankAccounts.bank_id', '=', 'banks.id')
            ->leftJoin('users as agent', 'invest.agent_id', '=', 'agent.id')
            ->where('invest.id', $id)
            ->select(
                'invest.*',
                'merchantUser.name as merchant_name',
                'teams.name as team_name',
                'bankAccounts.account_holder', 'bankAccounts.account_iban', 'bankAccounts.account_code',
                'banks.name as bank_name', 'banks.logo as bank_logo',
                'agent.name as agent_name'
            )->first();

        if (! $d) return response()->json(['message' => 'İşlem bulunamadı.'], 404);

        // Rol filtre
        if ($user->isTeamMember() && $d->team_id != $user->team_id) {
            return response()->json(['message' => 'Yetki yok.'], 403);
        }
        if ($user->isMerchant()) {
            $allowed = $user->merchant_ids;
            if (! in_array($d->firm_id, $allowed)) {
                return response()->json(['message' => 'Yetki yok.'], 403);
            }
        }

        // Üyenin son 10 işlemi (aynı player_id, aynı kapsam — admin tüm, takım kendi takımı, merchant kendi merchant'ı)
        $historyQuery = DB::table('invest')
            ->where('invest.player_id', $d->player_id)
            ->where('invest.id', '!=', $d->id)
            ->select('id', 'type', 'status', 'amount', 'name', 'created_at', 'finalize_date', 'rejectType');

        if ($user->isTeamMember()) {
            $historyQuery->where('invest.team_id', $user->team_id);
        } elseif ($user->isMerchant()) {
            $historyQuery->whereIn('invest.firm_id', $user->merchant_ids ?: [$user->firm_id]);
        }

        $history = $historyQuery->orderByDesc('id')->limit(10)->get();

        [$trustRate, $trustCount] = TrustScore::calculate($d->player_id, $d->id);

        return response()->json([
            'deposit'  => [
                'id'              => $d->id,
                'status'          => (int) $d->status,
                'name'            => $d->name,
                'amount'          => (float) $d->amount,
                'original_amount' => $d->original_amount !== null ? (float) $d->original_amount : null,
                'amount_changed'  => (int) ($d->amountChanged ?? 0) === 1,
                'order_id'        => $d->order_id,
                'player_id'       => $d->player_id,
                'playerID'        => $d->playerID,
                'merchant_name'   => $d->merchant_name,
                'team_name'       => $user->hasMerchantScope() ? null : $d->team_name,
                'account_holder'  => $d->account_holder,
                'account_iban'    => $d->account_iban,
                'bank_name'       => $d->bank_name,
                'bank_logo'       => $d->bank_logo,
                'agent_name'      => $user->hasMerchantScope() ? null : $d->agent_name,
                'created_at'      => $d->created_at,
                'form_at'         => $d->form_at,
                'process_date'    => $d->process_date,
                'iban'            => $d->iban,
                'added_type'      => $d->added_type,
                'trust_rate'      => $trustRate,
                'trust_count'     => $trustCount,
                'receipt_path'    => $d->receipt_path,
                'receipt_url'     => $d->receipt_path ? rtrim(config('app.url'), '/') . '/api/deposits/' . $d->id . '/receipt' : null,
            ],
            'history' => $history,
        ]);
    }

    public function approve(Request $request): JsonResponse
    {
        $request->validate([
            'id'     => 'required|integer',
            'amount' => 'nullable|numeric|min:0',
        ]);

        $user = auth()->user();

        if (! $user->canApproveTransactions()) {
            abort(403, __('auth.no_permission'));
        }

        $invest = DB::table('invest')->where('id', $request->id)->first();

        if (! $invest) {
            return response()->json(['message' => 'İşlem bulunamadı.'], 404);
        }

        if (! in_array((int) $invest->status, [1, 2], true)) {
            return response()->json(['message' => 'Bu işlem zaten sonuçlandırılmış.'], 422);
        }

        // Takım scope: admin dışındaki kullanıcılar sadece kendi takımlarına ait işlemi onaylayabilir
        if (! $user->isAdmin() && $invest->team_id != $user->team_id) {
            return response()->json(['message' => 'Bu işlemi onaylama yetkiniz yok.'], 403);
        }

        $updateData = [
            'status'        => 3,
            'agent_id'      => $user->id,
            'finalize_date' => now(),
        ];

        if ((int) $invest->status === 1) {
            $updateData['process_date'] = now();
        }

        // Tutar değişikliği
        if ($request->filled('amount') && $request->amount != $invest->amount) {
            $updateData['amount'] = $request->amount;
            $updateData['amountChanged'] = 1;
        }

        DB::table('invest')->where('id', $request->id)->update($updateData);

        DB::table('investLog')->insert([
            'investID'  => $request->id,
            'userID'    => $user->id,
            'ip'        => $request->ip(),
            'status'    => 3,
            'createdAt' => now(),
            'detail'    => 'İşlem onaylandı',
        ]);

        // Callback gönder — güncellenmiş invest'i tekrar oku, amount değişmişse callback yeni tutar ile gitsin
        $invest = DB::table('invest')->where('id', $request->id)->first();
        $this->sendCallback($invest, true);

        // Kasa max'a ulaştıysa anında pasife al + sistem chat'e bildir
        if ($invest->team_id) {
            app(\App\Services\MerchantBankService::class)->enforceMaxCase([(int) $invest->team_id]);
        }

        return response()->json(['message' => 'İşlem onaylandı.']);
    }

    public function reject(Request $request): JsonResponse
    {
        $request->validate([
            'id'          => 'required|integer',
            'reject_type' => 'required|in:1,2',
        ]);

        $user = auth()->user();

        if (! $user->canApproveTransactions()) {
            abort(403, __('auth.no_permission'));
        }

        $invest = DB::table('invest')->where('id', $request->id)->first();

        if (! $invest) {
            return response()->json(['message' => 'İşlem bulunamadı.'], 404);
        }

        if (! in_array((int) $invest->status, [1, 2], true)) {
            return response()->json(['message' => 'Bu işlem zaten sonuçlandırılmış.'], 422);
        }

        // Takım scope: admin dışındaki kullanıcılar sadece kendi takımlarına ait işlemi reddedebilir
        if (! $user->isAdmin() && $invest->team_id != $user->team_id) {
            return response()->json(['message' => 'Bu işlemi reddetme yetkiniz yok.'], 403);
        }

        // 10 dakika kuralı (admin hariç)
        if (! $user->isAdmin() && $invest->form_at) {
            $formTime = strtotime($invest->form_at);
            if ($formTime >= (time() - 600)) {
                return response()->json(['message' => 'Erken ret! İşlem en az 10 dakika beklemelidir.'], 422);
            }
        }

        $rejectMessages = [1 => 'Ödeme bulunamadı', 2 => 'Tekrarlanan talep'];

        DB::table('invest')->where('id', $request->id)->update([
            'status'        => 4,
            'rejectType'    => $request->reject_type,
            'agent_id'      => $user->id,
            'finalize_date' => now(),
        ]);

        DB::table('investLog')->insert([
            'investID'  => $request->id,
            'userID'    => $user->id,
            'ip'        => $request->ip(),
            'status'    => 4,
            'createdAt' => now(),
            'detail'    => $rejectMessages[$request->reject_type],
        ]);

        // Callback gönder
        $this->sendCallback($invest, false, $rejectMessages[$request->reject_type]);

        return response()->json(['message' => 'İşlem reddedildi.']);
    }

    public function filterMeta(): JsonResponse
    {
        $user = auth()->user();

        $merchants = [];
        if (! $user->isTeamMember()) {
            $merchants = DB::table('merchantUser')->where('status', '1')->select('id', 'name')->orderBy('name')->get();
        }

        $teams = [];
        if ($user->isAdmin()) {
            // Pasif takımlar dahil — sadece silinmiş olanlar (status=0) hariç.
            // Pasif takımlara ait eski çekimler hâlâ raporlanabiliyor; admin filtre için seçebilmeli.
            $teams = DB::table('teams')
                ->where('status', '!=', 0)
                ->select('id', 'name', 'status')
                ->orderBy('name')
                ->get();
        }

        $banks = DB::table('banks')->select('id', 'name')->orderBy('name')->get();

        return response()->json([
            'merchants' => $merchants,
            'teams'     => $teams,
            'banks'     => $banks,
        ]);
    }

    private function sendCallback($invest, bool $approved, string $detail = ''): void
    {
        app(\App\Services\CallbackService::class)->sendForInvest(
            $invest,
            $approved,
            $detail,
            auth()->id(),
        );
    }

    /**
     * POST /api/deposits/{id}/resend-callback — Super Admin only
     * Onaylanmış (3) veya reddedilmiş (4) bir invest için callback'i tekrar gönderir.
     */
    public function resendCallback(int $id): JsonResponse
    {
        if (! auth()->user()->isSuperAdmin()) {
            abort(403, 'Bu işlem için yetkiniz yok.');
        }

        $invest = DB::table('invest')->where('id', $id)->first();
        if (! $invest) {
            return response()->json(['message' => 'İşlem bulunamadı.'], 404);
        }
        if (! in_array((int) $invest->status, [3, 4], true)) {
            return response()->json(['message' => 'Yalnızca sonuçlanmış işlemler için tekrar gönderilebilir.'], 422);
        }

        $approved = (int) $invest->status === 3;
        $result = app(\App\Services\CallbackService::class)->sendForInvest(
            $invest,
            $approved,
            $approved ? '' : 'Manuel yeniden gönderim',
            auth()->id(),
            force: true,
            type: 'manual_resend',
        );

        return response()->json([
            'message' => $result['success'] ? 'Callback yeniden gönderildi.' : 'Callback gönderilemedi.',
            'result'  => $result,
        ], $result['success'] ? 200 : 502);
    }
}
