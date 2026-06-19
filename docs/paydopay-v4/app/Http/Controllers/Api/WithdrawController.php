<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use App\Services\TelegramService;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Http;
use Illuminate\Support\Facades\Storage;
use Illuminate\Support\Str;

class WithdrawController extends Controller
{
    public function pending(Request $request): JsonResponse
    {
        $user = auth()->user();

        $query = DB::table('invest')
            ->leftJoin('merchantUser', 'invest.firm_id', '=', 'merchantUser.id')
            ->leftJoin('teams', 'invest.team_id', '=', 'teams.id')
            ->leftJoin('users as agent', 'invest.agent_id', '=', 'agent.id')
            // Türkiye IBAN: pos 5-9 = 5 haneli banka kodu (ilki "0" padding), banks.code 4 hane → pos 6-9 al
            ->leftJoin('banks', 'banks.code', '=', DB::raw("SUBSTRING(REPLACE(invest.iban, ' ', ''), 6, 4)"))
            ->where('invest.type', '2')
            ->whereIn('invest.status', ['0', '1', '2'])
            ->select(
                'invest.id', 'invest.status', 'invest.name', 'invest.amount',
                'invest.order_id', 'invest.player_id', 'invest.iban',
                'invest.created_at', 'invest.form_at', 'invest.process_date',
                'invest.agent_id', 'invest.firm_id', 'invest.team_id',
                'invest.u_id',
                'merchantUser.name as merchant_name',
                'teams.name as team_name',
                'agent.name as agent_name',
                'banks.name as bank_name',
                DB::raw('(SELECT COUNT(*) FROM invest_receipts WHERE invest_receipts.invest_id = invest.id) AS receipt_count')
            );

        // Rol bazlı filtre
        // Team member: status 0 herkese açık, status 1-2 sadece kendi takımı
        if ($user->isTeamMember()) {
            $query->where(function ($q) use ($user) {
                $q->where('invest.status', '0') // Havuzdaki herkes görür
                  ->orWhere(function ($q2) use ($user) {
                      // team_id atanmamış işlemler de havuzda
                      $q2->whereIn('invest.status', ['1', '2'])
                          ->whereNull('invest.team_id');
                  })
                  ->orWhere(function ($q3) use ($user) {
                      // team_id atanmış ise sadece kendi takımı
                      $q3->whereIn('invest.status', ['1', '2'])
                          ->where('invest.team_id', $user->team_id);
                  });
            });
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
        if ($request->filled('name')) $query->where('invest.name', 'like', "%{$request->name}%");
        if ($request->filled('player_id')) $query->where('invest.player_id', $request->player_id);
        if ($request->filled('order_id')) $query->where('invest.order_id', $request->order_id);
        if ($request->filled('min_amount')) $query->where('invest.amount', '>=', $request->min_amount);
        if ($request->filled('max_amount')) $query->where('invest.amount', '<=', $request->max_amount);

        $withdrawals = $query->orderByDesc('invest.id')->limit(200)->get()->map(function ($d) use ($user) {
            return [
                'id'            => $d->id,
                'status'        => (int) $d->status,
                'name'          => $d->name,
                'amount'        => (float) $d->amount,
                'order_id'      => $d->order_id,
                'player_id'     => $d->player_id,
                'iban'          => $d->iban,
                'bank_name'     => $d->bank_name,
                'merchant_name' => $user->isTeamMember() ? null : $d->merchant_name,
                'team_name'     => $user->hasMerchantScope() ? null : $d->team_name,
                'agent_name'    => $user->hasMerchantScope() ? null : $d->agent_name,
                'agent_id'      => $user->hasMerchantScope() ? null : $d->agent_id,
                'receipt_count' => (int) $d->receipt_count,
                'created_at'    => $d->created_at,
                'form_at'       => $d->form_at,
                'process_date'  => $d->process_date,
            ];
        });

        return response()->json($withdrawals);
    }

    public function all(Request $request): JsonResponse
    {
        $user = auth()->user();

        $query = DB::table('invest')
            ->leftJoin('merchantUser', 'invest.firm_id', '=', 'merchantUser.id')
            ->leftJoin('teams', 'invest.team_id', '=', 'teams.id')
            ->leftJoin('users as agent', 'invest.agent_id', '=', 'agent.id')
            ->leftJoin('banks', 'banks.code', '=', DB::raw("SUBSTRING(REPLACE(invest.iban, ' ', ''), 6, 4)"))
            ->where('invest.type', '2')
            ->select(
                'invest.id', 'invest.status', 'invest.name', 'invest.amount',
                'invest.order_id', 'invest.player_id', 'invest.iban', 'invest.u_id',
                'invest.created_at', 'invest.finalize_date', 'invest.rejectType',
                'invest.firm_id', 'invest.team_id',
                'merchantUser.name as merchant_name',
                'teams.name as team_name',
                'agent.name as agent_name',
                'banks.name as bank_name',
                'teams.telegram_missing_receipt_enabled_at',
                DB::raw('(SELECT COUNT(*) FROM invest_receipts WHERE invest_receipts.invest_id = invest.id) AS receipt_count')
            );

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

        if ($request->filled('id')) $query->where('invest.id', $request->id);
        if ($request->filled('status') && $request->status != 0) $query->where('invest.status', $request->status);
        if ($request->filled('merchant')) $query->where('invest.firm_id', $request->merchant);
        if ($request->filled('team')) $query->where('invest.team_id', $request->team);
        if ($request->filled('name')) $query->where('invest.name', 'like', "%{$request->name}%");
        if ($request->filled('player_id')) $query->where('invest.player_id', $request->player_id);
        if ($request->filled('order_id')) $query->where('invest.order_id', $request->order_id);
        if ($request->filled('u_id')) $query->where('invest.u_id', $request->u_id);
        if ($request->filled('min_amount')) $query->where('invest.amount', '>=', $request->min_amount);
        if ($request->filled('max_amount')) $query->where('invest.amount', '<=', $request->max_amount);
        if ($request->filled('date_from')) $query->whereDate('invest.created_at', '>=', $request->date_from);
        if ($request->filled('date_to')) $query->whereDate('invest.created_at', '<=', $request->date_to);

        // Dekont yüklenmeyen onaylı çekimler (sarı satır kriteri ile birebir)
        if ($request->boolean('missing_receipt')) {
            $query->where('invest.status', 3)
                ->whereRaw('(SELECT COUNT(*) FROM invest_receipts WHERE invest_receipts.invest_id = invest.id) = 0')
                ->whereNotNull('teams.telegram_missing_receipt_enabled_at')
                ->whereNotNull('invest.finalize_date')
                ->whereColumn('invest.finalize_date', '>=', 'teams.telegram_missing_receipt_enabled_at');
        }

        $perPage = (int) $request->get('per_page', 50);
        $page = (int) $request->get('page', 1);
        $total = (clone $query)->count();
        $totalAmount = (clone $query)->sum('invest.amount');

        $withdrawals = $query->orderByDesc('invest.id')
            ->offset(($page - 1) * $perPage)->limit($perPage)
            ->get()->map(function ($d) use ($user) {
                return [
                    'id'            => $d->id,
                    'status'        => (int) $d->status,
                    'name'          => $d->name,
                    'amount'        => (float) $d->amount,
                    'order_id'      => $d->order_id,
                    'player_id'     => $d->player_id,
                    'iban'          => $d->iban,
                    'bank_name'     => $d->bank_name,
                    'u_id'          => $d->u_id,
                    'merchant_name' => $user->isTeamMember() ? null : $d->merchant_name,
                    'team_name'     => $user->hasMerchantScope() ? null : $d->team_name,
                    'agent_name'    => $user->hasMerchantScope() ? null : $d->agent_name,
                    'reject_type'   => $d->rejectType,
                    'receipt_count' => (int) $d->receipt_count,
                    'receipt_warning' => (
                        (int) $d->status === 3
                        && (int) $d->receipt_count === 0
                        && $d->telegram_missing_receipt_enabled_at !== null
                        && $d->finalize_date !== null
                        && $d->finalize_date >= $d->telegram_missing_receipt_enabled_at
                    ),
                    'created_at'    => $d->created_at,
                    'finalize_date' => $d->finalize_date,
                ];
            });

        return response()->json([
            'withdrawals'  => $withdrawals,
            'total'        => $total,
            'total_amount' => $totalAmount,
            'page'         => $page,
            'per_page'     => $perPage,
        ]);
    }

    public function detail(int $id): JsonResponse
    {
        $user = auth()->user();

        $d = DB::table('invest')
            ->leftJoin('merchantUser', 'invest.firm_id', '=', 'merchantUser.id')
            ->leftJoin('teams', 'invest.team_id', '=', 'teams.id')
            ->leftJoin('users as agent', 'invest.agent_id', '=', 'agent.id')
            ->where('invest.id', $id)->where('invest.type', '2')
            ->select(
                'invest.*',
                'merchantUser.name as merchant_name',
                'teams.name as team_name',
                'agent.name as agent_name'
            )->first();

        if (! $d) return response()->json(['message' => 'İşlem bulunamadı.'], 404);

        if ($user->isTeamMember() && $d->team_id != $user->team_id) {
            return response()->json(['message' => 'Yetki yok.'], 403);
        }
        if ($user->isMerchant() && ! in_array($d->firm_id, $user->merchant_ids ?: [$user->firm_id])) {
            return response()->json(['message' => 'Yetki yok.'], 403);
        }

        // Üyenin son 10 çekimi (aynı player_id, aynı kapsam)
        $historyQuery = DB::table('invest')
            ->where('player_id', $d->player_id)
            ->where('id', '!=', $d->id)
            ->where('type', 2)
            ->select('id', 'type', 'status', 'amount', 'name', 'created_at', 'finalize_date', 'rejectType');

        if ($user->isTeamMember()) {
            $historyQuery->where('team_id', $user->team_id);
        } elseif ($user->isMerchant()) {
            $historyQuery->whereIn('firm_id', $user->merchant_ids ?: [$user->firm_id]);
        }

        $history = $historyQuery->orderByDesc('id')->limit(10)->get();

        return response()->json([
            'withdraw' => [
                'id'            => $d->id,
                'status'        => (int) $d->status,
                'name'          => $d->name,
                'amount'        => (float) $d->amount,
                'order_id'      => $d->order_id,
                'player_id'     => $d->player_id,
                'iban'          => $d->iban,
                'merchant_name' => $d->merchant_name,
                'team_name'     => $user->hasMerchantScope() ? null : $d->team_name,
                'agent_name'    => $user->hasMerchantScope() ? null : $d->agent_name,
                'created_at'    => $d->created_at,
                'process_date'  => $d->process_date,
                'finalize_date' => $d->finalize_date,
            ],
            'history'  => $history,
        ]);
    }

    public function take(Request $request): JsonResponse
    {
        $request->validate(['id' => 'required|integer']);
        $user = auth()->user();

        if (! $user->canApproveTransactions()) {
            abort(403, __('auth.no_permission'));
        }

        $invest = DB::table('invest')->where('id', $request->id)->first();

        if (! $invest || ! in_array($invest->status, ['0', '1']) || $invest->agent_id) {
            return response()->json(['message' => 'Bu işlem alınamaz.'], 422);
        }

        // Scope kontrolü:
        // - Havuzda (status=0, team yok): admin VEYA takım üyesi üstlenebilir (kendi takımına atar)
        // - Beklemede (status=1, takıma atanmış): yalnızca aynı takımın üyesi üstlenebilir
        if (! $user->isAdmin()) {
            $isPool = (string) $invest->status === '0' || empty($invest->team_id);
            if (! $isPool && $invest->team_id != $user->team_id) {
                return response()->json(['message' => 'Bu çekim sizin takımınıza atanmamış.'], 403);
            }
            // Pool'dan üstlenme: takım üyesi kendi takımına atar
            if ($isPool && empty($user->team_id)) {
                return response()->json(['message' => 'Havuzdan üstlenmek için takıma atanmış olmalısınız.'], 403);
            }
        }

        // Admin status=0 çekimi üstlenirse: kendi takımı yoksa olduğu gibi bırak
        $newTeamId = $invest->team_id ?: $user->team_id;

        DB::table('invest')->where('id', $request->id)->update([
            'status'       => 2,
            'agent_id'     => $user->id,
            'team_id'      => $newTeamId,
            'process_date' => now(),
        ]);

        DB::table('investLog')->insert([
            'investID'  => $request->id,
            'userID'    => $user->id,
            'ip'        => $request->ip(),
            'status'    => 2,
            'createdAt' => now(),
            'detail'    => 'Çekim üstlenildi',
        ]);

        return response()->json(['message' => 'İşlem üstlenildi.']);
    }

    public function release(Request $request): JsonResponse
    {
        $request->validate(['id' => 'required|integer']);
        $user = auth()->user();

        if (! $user->canApproveTransactions()) {
            abort(403, __('auth.no_permission'));
        }

        $invest = DB::table('invest')->where('id', $request->id)->first();

        if (! $invest || $invest->status != 2) {
            return response()->json(['message' => 'Bu işlem bırakılamaz.'], 422);
        }

        if (! $user->isAdmin() && $invest->team_id != $user->team_id) {
            return response()->json(['message' => 'Bu işlemi yalnızca işlemin atandığı takımdaki kullanıcılar bırakabilir.'], 403);
        }

        // Bırakma: status '0' (havuz, ENUM string), agent_id ve team_id null
        DB::table('invest')->where('id', $request->id)->update([
            'status'       => '0',
            'agent_id'     => null,
            'team_id'      => null,
            'process_date' => null,
        ]);

        DB::table('investLog')->insert([
            'investID'  => $request->id,
            'userID'    => $user->id,
            'ip'        => $request->ip(),
            'status'    => 0,
            'createdAt' => now(),
            'detail'    => 'Çekim bırakıldı (havuza döndü)',
        ]);

        return response()->json(['message' => 'İşlem bırakıldı.']);
    }

    public function approve(Request $request): JsonResponse
    {
        $request->validate(['id' => 'required|integer']);
        $user = auth()->user();

        if (! $user->canApproveTransactions()) {
            abort(403, __('auth.no_permission'));
        }

        $invest = DB::table('invest')->where('id', $request->id)->first();

        if (! $invest) {
            return response()->json(['message' => 'İşlem bulunamadı.'], 404);
        }

        if (! $user->isAdmin() && ($invest->status != 2 || $invest->team_id != $user->team_id)) {
            return response()->json(['message' => 'Bu işlemi onaylama yetkiniz yok.'], 403);
        }

        $hasReceipt = DB::table('invest_receipts')->where('invest_id', $request->id)->exists();
        if (! $hasReceipt) {
            return response()->json(['message' => 'Onay için en az bir dekont yüklemeniz gerekiyor.'], 422);
        }

        DB::table('invest')->where('id', $request->id)->update([
            'status'        => 3,
            'agent_id'      => $user->id,
            'finalize_date' => now(),
        ]);

        DB::table('investLog')->insert([
            'investID'  => $request->id,
            'userID'    => $user->id,
            'ip'        => $request->ip(),
            'status'    => 3,
            'createdAt' => now(),
            'detail'    => 'Çekim onaylandı',
        ]);

        // Callback
        $this->sendCallback($invest, true);

        // Kasa max'a ulaştıysa anında pasife al + sistem chat'e bildir
        if ($invest->team_id) {
            app(\App\Services\MerchantBankService::class)->enforceMaxCase([(int) $invest->team_id]);
        }

        return response()->json(['message' => 'Çekim onaylandı.']);
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

        if (! $user->isAdmin() && ($invest->status != 2 || $invest->team_id != $user->team_id)) {
            return response()->json(['message' => 'Bu işlemi reddetme yetkiniz yok.'], 403);
        }

        $rejectMessages = [1 => 'Çekim reddedildi', 2 => 'Tekrarlanan talep'];

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

        $this->sendCallback($invest, false, $rejectMessages[$request->reject_type]);

        return response()->json(['message' => 'Çekim reddedildi.']);
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
     * POST /api/withdrawals/{id}/resend-callback — Super Admin only
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

    public function bulkAssign(Request $request): JsonResponse
    {
        $request->validate([
            'ids'     => 'required|array|min:1',
            'ids.*'   => 'integer',
            'team_id' => 'required|integer',
        ]);

        $user = auth()->user();

        // Toplu atama admin'lerce yapılabilir (Super Admin + Sub Admin)
        if (! $user->isAdmin()) {
            abort(403, 'Toplu atama için admin yetkisi gerekir.');
        }

        $team = DB::table('teams')->where('id', $request->team_id)->where('status', 1)->first();
        if (! $team) {
            return response()->json(['message' => 'Takım bulunamadı veya aktif değil.'], 404);
        }

        // Takımın ilk aktif kullanıcısı (team agent / team admin)
        $firstUser = DB::table('users')
            ->where('team_id', $team->id)
            ->whereIn('user_type', [2, 5])
            ->where('status', '1')
            ->orderBy('id')
            ->first();

        if (! $firstUser) {
            return response()->json(['message' => 'Bu takımda atanabilecek aktif kullanıcı yok.'], 422);
        }

        // Sadece bekleyen (havuzda veya beklemede) ve çekim olan kayıtları ata
        $eligible = DB::table('invest')
            ->whereIn('id', $request->ids)
            ->where('type', '2')
            ->whereIn('status', ['0', '1'])
            ->pluck('id')
            ->all();

        if (empty($eligible)) {
            return response()->json(['message' => 'Atanabilecek uygun çekim bulunamadı.'], 422);
        }

        DB::table('invest')->whereIn('id', $eligible)->update([
            'status'       => 2,
            'team_id'      => $team->id,
            'agent_id'     => $firstUser->id,
            'process_date' => now(),
        ]);

        $logRows = array_map(fn ($id) => [
            'investID'  => $id,
            'userID'    => $user->id,
            'ip'        => $request->ip(),
            'status'    => 2,
            'createdAt' => now(),
            'detail'    => "Toplu atama → takım: {$team->name}",
        ], $eligible);
        DB::table('investLog')->insert($logRows);

        // Telegram bildirimi — takım çekim grubu + flag ikisi de açıksa
        if (
            (int) ($team->telegram_enabled ?? 0) === 1
            && ! empty($team->telegram_withdraw_chat_id)
            && (int) ($team->telegram_withdraw_assigned_enabled ?? 0) === 1
        ) {
            $assigned = DB::table('invest')->whereIn('id', $eligible)
                ->select('id', 'order_id', 'amount', 'name', 'player_id')
                ->get();

            $count = $assigned->count();
            $total = $assigned->sum('amount');
            $atan  = $user->name ?: ($user->username ?? '-');

            $fmtAmt = fn ($v) => '₺' . number_format((float) $v, 0, ',', '.');

            if ($count === 1) {
                $w = $assigned->first();
                $msg = "📥 *ÇEKİM ATANDI* — `#" . TelegramService::escape($w->order_id ?: (string) $w->id) . "`\n"
                     . "*Üye:* " . TelegramService::escape($w->name ?: '-') . "\n"
                     . "*Player ID:* " . TelegramService::escape($w->player_id ?: '-') . "\n"
                     . "*Tutar:* " . TelegramService::escape($fmtAmt($w->amount)) . "\n"
                     . "*Atayan:* " . TelegramService::escape($atan);
            } else {
                $lines = $assigned->map(fn ($w) =>
                    "`#" . TelegramService::escape($w->order_id ?: (string) $w->id) . "` · "
                    . TelegramService::escape($fmtAmt($w->amount))
                )->implode("\n");

                $msg = "📥 *{$count} ÇEKİM ATANDI*\n"
                     . "*Toplam:* " . TelegramService::escape($fmtAmt($total)) . "\n"
                     . "*Atayan:* " . TelegramService::escape($atan) . "\n\n"
                     . $lines;
            }

            if (TelegramService::send($team->telegram_withdraw_chat_id, $msg)) {
                $now = now();
                DB::table('telegram_notifications')->insertOrIgnore(
                    $assigned->map(fn ($w) => [
                        'invest_id' => $w->id,
                        'type'      => 'withdraw_assigned',
                        'sent_at'   => $now,
                    ])->all()
                );
            }
        }

        return response()->json([
            'message'  => count($eligible) . ' çekim ' . $team->name . ' takımına atandı.',
            'assigned' => count($eligible),
            'team'     => ['id' => $team->id, 'name' => $team->name],
            'agent'    => ['id' => $firstUser->id, 'name' => $firstUser->name],
        ]);
    }

    public function receipts(int $id): JsonResponse
    {
        $user = auth()->user();

        if (! $user->canApproveTransactions()) {
            abort(403, __('auth.no_permission'));
        }

        $invest = DB::table('invest')->where('id', $id)->where('type', '2')->first();
        if (! $invest) {
            return response()->json(['message' => 'İşlem bulunamadı.'], 404);
        }

        if ($user->isTeamMember() && $invest->team_id != $user->team_id) {
            \Illuminate\Support\Facades\Log::warning('Receipt 403', [
                'user_id'   => $user->id, 'user_name' => $user->name,
                'user_type' => $user->user_type, 'user_team_id' => $user->team_id,
                'invest_id' => $invest->id, 'invest_team_id' => $invest->team_id,
            ]);
            abort(403, __('auth.no_permission'));
        }

        $rows = DB::table('invest_receipts')
            ->leftJoin('users', 'invest_receipts.uploaded_by', '=', 'users.id')
            ->leftJoin('users as mverifier', 'invest_receipts.manual_verified_by', '=', 'mverifier.id')
            ->where('invest_receipts.invest_id', $id)
            ->orderByDesc('invest_receipts.id')
            ->select(
                'invest_receipts.id', 'invest_receipts.file_path', 'invest_receipts.original_name',
                'invest_receipts.mime_type', 'invest_receipts.file_size', 'invest_receipts.uploaded_at',
                'invest_receipts.verification_status', 'invest_receipts.verification_score',
                'invest_receipts.verification_data', 'invest_receipts.verification_notes',
                'invest_receipts.metadata_flags',
                'invest_receipts.verified_at', 'invest_receipts.manual_verified_by',
                'users.name as uploaded_by_name',
                'mverifier.name as manual_verifier_name'
            )
            ->get()
            ->map(fn ($r) => [
                'id'               => $r->id,
                'original_name'    => $r->original_name,
                'mime_type'        => $r->mime_type,
                'file_size'        => (int) $r->file_size,
                'is_image'         => str_starts_with((string) $r->mime_type, 'image/'),
                'is_pdf'           => $r->mime_type === 'application/pdf',
                'uploaded_at'      => $r->uploaded_at,
                'uploaded_by_name' => $r->uploaded_by_name,
                'url'              => url("/api/withdrawals/{$id}/receipts/{$r->id}"),
                'verification_status' => $r->verification_status,
                'verification_score'  => $r->verification_score !== null ? (int) $r->verification_score : null,
                'verification_data'   => $r->verification_data ? json_decode($r->verification_data, true) : null,
                'verification_notes'  => $r->verification_notes,
                'metadata_flags'      => $r->metadata_flags ? json_decode($r->metadata_flags, true) : null,
                'verified_at'         => $r->verified_at,
                'manual_verifier_name'=> $r->manual_verifier_name,
            ]);

        return response()->json(['receipts' => $rows]);
    }

    public function uploadReceipt(int $id, Request $request): JsonResponse
    {
        $user = auth()->user();

        if (! $user->canApproveTransactions()) {
            abort(403, __('auth.no_permission'));
        }

        $invest = DB::table('invest')->where('id', $id)->where('type', '2')->first();
        if (! $invest) {
            return response()->json(['message' => 'İşlem bulunamadı.'], 404);
        }

        if ($user->isTeamMember() && $invest->team_id != $user->team_id) {
            abort(403, __('auth.no_permission'));
        }

        // İki mod: multipart (file) veya base64 JSON (WAF bypass). WAF multipart upload'ı blokluyor.
        $originalName = null;
        $mimeType = null;
        $fileSize = 0;
        $ext = null;
        $binary = null;

        if ($request->hasFile('file')) {
            $request->validate([
                'file' => 'required|file|max:10240|mimes:pdf,jpg,jpeg,png,webp',
            ]);
            $f = $request->file('file');
            $binary = file_get_contents($f->getRealPath());
            $originalName = $f->getClientOriginalName() ?: ('receipt.' . $f->extension());
            $mimeType = $f->getMimeType();
            $fileSize = $f->getSize();
            $ext = strtolower($f->getClientOriginalExtension() ?: $f->extension());
        } else {
            $request->validate([
                'file_base64'   => 'required|string',
                'file_name'     => 'required|string|max:255',
                'mime_type'     => 'required|string|in:application/pdf,image/jpeg,image/png,image/webp',
            ], [
                'mime_type.in' => 'Sadece PDF, JPG, PNG veya WEBP yükleyebilirsiniz.',
            ]);

            $raw = $request->input('file_base64');
            // data URI prefix'ini ("data:application/pdf;base64,") temizle
            if (str_contains($raw, ',')) $raw = substr($raw, strpos($raw, ',') + 1);
            $binary = base64_decode($raw, true);
            if ($binary === false) {
                return response()->json(['message' => 'Geçersiz base64 dosya verisi.'], 422);
            }
            $fileSize = strlen($binary);
            if ($fileSize > 10 * 1024 * 1024) {
                return response()->json(['message' => 'Dosya boyutu 10 MB\'ı aşamaz.'], 422);
            }
            $mimeType = $request->input('mime_type');
            $originalName = $request->input('file_name');
            $extMap = ['application/pdf' => 'pdf', 'image/jpeg' => 'jpg', 'image/png' => 'png', 'image/webp' => 'webp'];
            $ext = $extMap[$mimeType] ?? 'bin';
        }

        $name = Str::uuid()->toString() . '.' . $ext;
        $path = "receipts/withdrawals/{$id}/{$name}";
        \Illuminate\Support\Facades\Storage::disk('public')->put($path, $binary);
        $fileHash = hash('sha256', $binary);
        $perceptualHash = \App\Services\PerceptualHashService::dHash($binary, $mimeType);

        $receiptId = DB::table('invest_receipts')->insertGetId([
            'invest_id'       => $id,
            'file_path'       => $path,
            'original_name'   => $originalName,
            'mime_type'       => $mimeType,
            'file_size'       => $fileSize,
            'file_hash'       => $fileHash,
            'perceptual_hash' => $perceptualHash,
            'uploaded_by'     => $user->id,
            'uploaded_at'     => now(),
        ]);

        // AI doğrulama job'ı queue'ya bırak
        \App\Jobs\VerifyReceiptJob::dispatch($receiptId);

        DB::table('investLog')->insert([
            'investID'  => $id,
            'userID'    => $user->id,
            'ip'        => $request->ip(),
            'status'    => $invest->status,
            'createdAt' => now(),
            'detail'    => 'Dekont yüklendi (' . $originalName . ')',
        ]);

        return response()->json([
            'message' => 'Dekont yüklendi.',
            'id'      => $receiptId,
            'url'     => url("/api/withdrawals/{$id}/receipts/{$receiptId}"),
        ], 201);
    }

    public function downloadReceipt(int $id, int $rid)
    {
        $user = auth()->user();

        if (! $user->canApproveTransactions()) {
            abort(403, __('auth.no_permission'));
        }

        $invest = DB::table('invest')->where('id', $id)->where('type', '2')->first();
        if (! $invest) {
            abort(404, 'İşlem bulunamadı.');
        }

        if ($user->isTeamMember() && $invest->team_id != $user->team_id) {
            \Illuminate\Support\Facades\Log::warning('Receipt 403', [
                'user_id'   => $user->id, 'user_name' => $user->name,
                'user_type' => $user->user_type, 'user_team_id' => $user->team_id,
                'invest_id' => $invest->id, 'invest_team_id' => $invest->team_id,
            ]);
            abort(403, __('auth.no_permission'));
        }

        $receipt = DB::table('invest_receipts')->where('id', $rid)->where('invest_id', $id)->first();
        if (! $receipt) {
            abort(404, 'Dekont bulunamadı.');
        }

        if (! Storage::disk('public')->exists($receipt->file_path)) {
            abort(404, 'Dosya bulunamadı.');
        }

        return response()->file(Storage::disk('public')->path($receipt->file_path), [
            'Content-Type'        => $receipt->mime_type ?? 'application/octet-stream',
            'Content-Disposition' => 'inline; filename="' . ($receipt->original_name ?? basename($receipt->file_path)) . '"',
        ]);
    }

    /**
     * Dekont doğrulama sayfası listesi — sadece super/sub admin.
     * AI analizi tamamlanmış (status != pending) dekontu olan çekimleri döner.
     * Her çekim için en son uploaded olan dekont alınır + score sayfada satır rengi belirler.
     */
    public function receiptReview(Request $request): JsonResponse
    {
        $user = auth()->user();
        if (! $user->isAdmin()) {
            abort(403, __('auth.no_permission'));
        }

        // Filters
        $statusFilter = $request->get('status'); // 'verified', 'suspicious', 'rejected', null=all
        $page = max(1, (int) $request->get('page', 1));
        $perPage = (int) $request->get('per_page', 50);

        // Her invest için en yüksek id'li (en son yüklenmiş) dekont
        $latestReceiptSub = DB::table('invest_receipts as ir2')
            ->select(DB::raw('MAX(id) as latest_id'))
            ->whereColumn('ir2.invest_id', 'invest_receipts.invest_id');

        $query = DB::table('invest_receipts')
            ->join('invest', 'invest.id', '=', 'invest_receipts.invest_id')
            ->leftJoin('teams', 'invest.team_id', '=', 'teams.id')
            ->leftJoin('merchantUser', 'invest.firm_id', '=', 'merchantUser.id')
            ->leftJoin('users as agent', 'invest.agent_id', '=', 'agent.id')
            ->leftJoin('users as mverifier', 'invest_receipts.manual_verified_by', '=', 'mverifier.id')
            ->where('invest.type', '2')
            ->where('invest_receipts.verification_status', '!=', 'pending')
            ->whereRaw('invest_receipts.id = (' . $latestReceiptSub->toSql() . ')', $latestReceiptSub->getBindings());

        if ($statusFilter && in_array($statusFilter, ['verified', 'suspicious', 'rejected'], true)) {
            // 'verified' filtre hem otomatik hem manuel doğrulanmışları kapsar
            if ($statusFilter === 'verified') {
                $query->whereIn('invest_receipts.verification_status', ['verified', 'manually_verified']);
            } else {
                $query->where('invest_receipts.verification_status', $statusFilter);
            }
        }

        $total = (clone $query)->count();

        $rows = $query
            ->select(
                'invest.id as invest_id', 'invest.order_id', 'invest.amount', 'invest.name as recipient',
                'invest.iban', 'invest.status as invest_status', 'invest.finalize_date',
                'invest_receipts.id as receipt_id', 'invest_receipts.verification_status',
                'invest_receipts.verification_score', 'invest_receipts.verification_data',
                'invest_receipts.verification_notes', 'invest_receipts.verified_at',
                'invest_receipts.manual_verified_by', 'mverifier.name as manual_verifier_name',
                'teams.name as team_name', 'merchantUser.name as merchant_name',
                'agent.name as agent_name'
            )
            ->orderByDesc('invest_receipts.verified_at')
            ->offset(($page - 1) * $perPage)
            ->limit($perPage)
            ->get()
            ->map(fn ($r) => [
                'invest_id'    => (int) $r->invest_id,
                'order_id'     => $r->order_id,
                'amount'       => (float) $r->amount,
                'recipient'    => $r->recipient,
                'iban'         => $r->iban,
                'invest_status'=> (int) $r->invest_status,
                'finalize_date'=> $r->finalize_date,
                'team_name'    => $r->team_name,
                'merchant_name'=> $r->merchant_name,
                'agent_name'   => $r->agent_name,
                'receipt_id'   => (int) $r->receipt_id,
                'verification_status' => $r->verification_status,
                'verification_score'  => $r->verification_score !== null ? (int) $r->verification_score : null,
                'verification_notes'  => $r->verification_notes,
                'verified_at'  => $r->verified_at,
                'verification_data' => $r->verification_data ? json_decode($r->verification_data, true) : null,
                'manual_verifier_name' => $r->manual_verifier_name,
            ]);

        // Özet sayım
        $counts = DB::table('invest_receipts')
            ->join('invest', 'invest.id', '=', 'invest_receipts.invest_id')
            ->where('invest.type', '2')
            ->whereRaw('invest_receipts.id = (' . $latestReceiptSub->toSql() . ')', $latestReceiptSub->getBindings())
            ->select('invest_receipts.verification_status', DB::raw('COUNT(*) AS c'))
            ->groupBy('invest_receipts.verification_status')
            ->pluck('c', 'verification_status');

        return response()->json([
            'items'    => $rows,
            'total'    => $total,
            'page'     => $page,
            'per_page' => $perPage,
            'counts'   => [
                'pending'    => (int) ($counts['pending'] ?? 0),
                // verified = otomatik + manuel doğrulananlar
                'verified'   => (int) ($counts['verified'] ?? 0) + (int) ($counts['manually_verified'] ?? 0),
                'manually_verified' => (int) ($counts['manually_verified'] ?? 0),
                'suspicious' => (int) ($counts['suspicious'] ?? 0),
                'rejected'   => (int) ($counts['rejected'] ?? 0),
            ],
        ]);
    }

    /**
     * Admin/Sub admin tarafından manuel doğrulama — şüpheli/reddedilen dekontu "manually_verified" yap.
     */
    public function manualVerifyReceipt(int $id, int $rid): JsonResponse
    {
        $user = auth()->user();
        if (! $user->isAdmin()) {
            abort(403, __('auth.no_permission'));
        }

        $invest = DB::table('invest')->where('id', $id)->where('type', '2')->first();
        if (! $invest) {
            return response()->json(['message' => 'İşlem bulunamadı.'], 404);
        }

        $receipt = DB::table('invest_receipts')->where('id', $rid)->where('invest_id', $id)->first();
        if (! $receipt) {
            return response()->json(['message' => 'Dekont bulunamadı.'], 404);
        }

        if ($receipt->verification_status === 'manually_verified') {
            return response()->json(['message' => 'Bu dekont zaten manuel doğrulanmış.'], 422);
        }

        $existingNotes = (string) ($receipt->verification_notes ?? '');
        $stamp = now()->format('d.m.Y H:i');
        $manualNote = "Manuel doğrulandı: {$user->name} · {$stamp}";
        $newNotes = $existingNotes
            ? $existingNotes . "\n" . $manualNote
            : $manualNote;

        DB::table('invest_receipts')->where('id', $rid)->update([
            'verification_status' => 'manually_verified',
            'verification_score'  => 100,
            'verification_notes'  => $newNotes,
            'manual_verified_by'  => $user->id,
            'verified_at'         => now(),
        ]);

        DB::table('investLog')->insert([
            'investID'  => $id,
            'userID'    => $user->id,
            'ip'        => request()->ip(),
            'status'    => $invest->status,
            'createdAt' => now(),
            'detail'    => "Dekont manuel doğrulandı (receipt #{$rid})",
        ]);

        return response()->json(['message' => 'Manuel doğrulandı.']);
    }

    public function notifyMissingReceipts(Request $request): JsonResponse
    {
        $user = auth()->user();
        if (! $user->canApproveTransactions()) {
            abort(403, __('auth.no_permission'));
        }

        $request->validate([
            'team_id' => 'required|integer',
        ]);

        $team = DB::table('teams')->where('id', $request->team_id)->first();
        if (! $team) {
            return response()->json(['message' => 'Takım bulunamadı.'], 404);
        }
        if (empty($team->telegram_withdraw_chat_id)) {
            return response()->json(['message' => 'Takımın çekim chat ID\'si tanımlı değil.'], 422);
        }
        if (empty($team->telegram_missing_receipt_enabled_at)) {
            return response()->json(['message' => 'Bu takımda "dekont yüklenmeyen" bildirimi açık değil.'], 422);
        }

        // Sarı satır kriteri: status=3, receipt_count=0, finalize_date >= telegram_missing_receipt_enabled_at
        $missing = DB::table('invest')
            ->where('invest.team_id', $team->id)
            ->where('invest.type', '2')
            ->where('invest.status', 3)
            ->where('invest.finalize_date', '>=', $team->telegram_missing_receipt_enabled_at)
            ->whereNotNull('invest.finalize_date')
            ->whereRaw('(SELECT COUNT(*) FROM invest_receipts WHERE invest_receipts.invest_id = invest.id) = 0')
            ->orderBy('invest.finalize_date')
            ->get(['invest.id', 'invest.order_id', 'invest.finalize_date']);

        if ($missing->isEmpty()) {
            return response()->json(['message' => 'Bu takımda dekont yüklenmeyen çekim yok.', 'count' => 0], 422);
        }

        $now = now();
        $lines = [];
        foreach ($missing as $w) {
            $finalizedAt = \Carbon\Carbon::parse($w->finalize_date);
            $totalMinutes = max(0, (int) $finalizedAt->diffInMinutes($now));
            $days = intdiv($totalMinutes, 1440);
            $hours = intdiv($totalMinutes % 1440, 60);
            $minutes = $totalMinutes % 60;
            $parts = [];
            if ($days > 0) $parts[] = $days . ' gün';
            if ($hours > 0) $parts[] = $hours . ' saat';
            if ($minutes > 0 || empty($parts)) $parts[] = $minutes . ' dakika';
            $elapsed = implode(' ', $parts);
            $orderCode = $w->order_id ?: ('#' . $w->id);
            $lines[] = TelegramService::escape($orderCode) . ' \\- ' . TelegramService::escape($elapsed) . ' dekont yüklenmedi\\.';
        }

        // MarkdownV2 — kalın yazı için *bold*
        $message = implode("\n", $lines)
                 . "\n\n"
                 . '*' . TelegramService::escape('Lütfen en kısa sürede dekontları yükleyin!') . '*';

        $ok = TelegramService::send($team->telegram_withdraw_chat_id, $message, 'MarkdownV2');

        if (! $ok) {
            return response()->json(['message' => 'Telegram bildirimi gönderilemedi.', 'count' => count($lines)], 500);
        }

        return response()->json([
            'message' => 'Bildirim gönderildi.',
            'count'   => count($lines),
            'chat_id' => $team->telegram_withdraw_chat_id,
        ]);
    }

    public function flagFakeReceipt(int $id, int $rid, Request $request): JsonResponse
    {
        $user = auth()->user();
        if (! $user->isAdmin()) {
            abort(403, __('auth.no_permission'));
        }

        $request->validate([
            'reason' => 'nullable|string|max:500',
        ]);

        $invest = DB::table('invest')->where('id', $id)->where('type', '2')->first();
        if (! $invest) {
            return response()->json(['message' => 'İşlem bulunamadı.'], 404);
        }

        $receipt = DB::table('invest_receipts')->where('id', $rid)->where('invest_id', $id)->first();
        if (! $receipt) {
            return response()->json(['message' => 'Dekont bulunamadı.'], 404);
        }

        // Şablon DB'sine ekle (perceptual_hash zorunlu — yoksa exact file_hash ile yedek)
        $templateId = null;
        if ($receipt->perceptual_hash || $receipt->file_hash) {
            $templateId = DB::table('fake_receipt_templates')->insertGetId([
                'receipt_id'      => $rid,
                'invest_id'       => $id,
                'perceptual_hash' => $receipt->perceptual_hash ?: '',
                'file_hash'       => $receipt->file_hash,
                'reason'          => $request->input('reason'),
                'reported_by'     => $user->id,
                'reported_at'     => now(),
            ]);
        }

        // Receipt'i reject olarak işaretle, notes'a not ekle
        $stamp = now()->format('d.m.Y H:i');
        $note = "🚫 Sahte olarak işaretlendi: {$user->name} · {$stamp}"
              . ($request->input('reason') ? " · Sebep: " . $request->input('reason') : '');
        $newNotes = $receipt->verification_notes
            ? $receipt->verification_notes . "\n" . $note
            : $note;

        DB::table('invest_receipts')->where('id', $rid)->update([
            'verification_status' => 'rejected',
            'verification_score'  => 0,
            'verification_notes'  => $newNotes,
            'verified_at'         => now(),
        ]);

        DB::table('investLog')->insert([
            'investID'  => $id,
            'userID'    => $user->id,
            'ip'        => $request->ip(),
            'status'    => $invest->status,
            'createdAt' => now(),
            'detail'    => "Dekont sahte olarak işaretlendi (receipt #{$rid}" . ($templateId ? ", template #{$templateId}" : '') . ")",
        ]);

        return response()->json([
            'message'     => 'Sahte olarak işaretlendi.',
            'template_id' => $templateId,
        ]);
    }

    public function verifyReceipt(int $id, int $rid): JsonResponse
    {
        $user = auth()->user();
        if (! $user->canApproveTransactions()) {
            abort(403, __('auth.no_permission'));
        }

        $invest = DB::table('invest')->where('id', $id)->where('type', '2')->first();
        if (! $invest) {
            return response()->json(['message' => 'İşlem bulunamadı.'], 404);
        }
        if ($user->isTeamMember() && $invest->team_id != $user->team_id) {
            abort(403, __('auth.no_permission'));
        }

        $receipt = DB::table('invest_receipts')->where('id', $rid)->where('invest_id', $id)->first();
        if (! $receipt) {
            return response()->json(['message' => 'Dekont bulunamadı.'], 404);
        }

        // Status'u pending'e çek, sonuçlar temizlensin, job tekrar tetiklensin
        DB::table('invest_receipts')->where('id', $rid)->update([
            'verification_status' => 'pending',
            'verification_score'  => null,
            'verification_data'   => null,
            'verification_notes'  => null,
            'verified_at'         => null,
        ]);

        \App\Jobs\VerifyReceiptJob::dispatch($rid);

        return response()->json(['message' => 'Yeniden doğrulama başlatıldı. Sonuç birkaç dakika içinde gelecek.']);
    }
}
