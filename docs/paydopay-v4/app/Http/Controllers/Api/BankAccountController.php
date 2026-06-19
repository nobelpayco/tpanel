<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;

class BankAccountController extends Controller
{
    public function index(Request $request): JsonResponse
    {
        $user = auth()->user();
        $statusFilter = $request->get('status', '1');
        $bankFilter = $request->get('bank_id');
        $teamFilter = $request->get('team_id');
        $search = $request->get('search');

        $query = DB::table('bankAccounts')
            ->join('banks', 'bankAccounts.bank_id', '=', 'banks.id')
            ->join('teams', 'bankAccounts.team_id', '=', 'teams.id')
            ->select(
                'bankAccounts.*',
                'banks.name as bank_name',
                'banks.code as bank_code',
                'banks.logo as bank_logo',
                'teams.name as team_name'
            );

        // Rol bazlı filtre
        if ($user->isTeamMember()) {
            $query->where('bankAccounts.team_id', $user->team_id);
        }

        if ($statusFilter !== 'all') {
            $query->where('bankAccounts.status', (int) $statusFilter);
        } else {
            $query->where('bankAccounts.status', '!=', 0);
        }

        if ($bankFilter) {
            $query->where('bankAccounts.bank_id', $bankFilter);
        }

        if ($teamFilter) {
            $query->where('bankAccounts.team_id', $teamFilter);
        }

        if ($search) {
            $query->where(function ($q) use ($search) {
                $q->where('bankAccounts.account_holder', 'like', "%{$search}%")
                    ->orWhere('bankAccounts.account_iban', 'like', "%{$search}%")
                    ->orWhere('bankAccounts.account_code', 'like', "%{$search}%");
            });
        }

        $accounts = $query
            ->orderBy('bankAccounts.sort_order')
            ->orderByDesc('bankAccounts.id')
            ->get();

        // Her hesap için bugünkü kullanılan adet ve toplam tutar (limit yönetimi UI'da görünsün diye)
        $today = now()->toDateString();
        $ids = collect($accounts)->pluck('id')->toArray();
        $usage = empty($ids) ? collect() : DB::table('invest')
            ->whereIn('bank_id', $ids)
            ->whereIn('status', ['1', '2', '3'])
            ->whereDate('created_at', $today)
            ->selectRaw('bank_id, COUNT(*) as cnt, COALESCE(SUM(amount), 0) as total_amt')
            ->groupBy('bank_id')
            ->get()
            ->keyBy('bank_id');

        foreach ($accounts as $a) {
            $u = $usage[$a->id] ?? null;
            $a->daily_count_used  = (int) ($u->cnt ?? 0);
            $a->max_amount_used   = (float) ($u->total_amt ?? 0);
        }

        return response()->json($accounts);
    }

    public function banks(): JsonResponse
    {
        $banks = DB::table('banks')->orderBy('name')->get();
        return response()->json($banks);
    }

    public function teams(): JsonResponse
    {
        $teams = DB::table('teams')->orderBy('name')->get();
        return response()->json($teams);
    }

    public function show(int $id): JsonResponse
    {
        $user = auth()->user();

        $account = DB::table('bankAccounts')
            ->join('banks', 'bankAccounts.bank_id', '=', 'banks.id')
            ->join('teams', 'bankAccounts.team_id', '=', 'teams.id')
            ->where('bankAccounts.id', $id)
            ->select('bankAccounts.*', 'banks.name as bank_name', 'teams.name as team_name')
            ->first();

        if (! $account) {
            return response()->json(['message' => 'Hesap bulunamadı.'], 404);
        }

        if ($user->isTeamMember() && $account->team_id != $user->team_id) {
            return response()->json(['message' => 'Yetkiniz yok.'], 403);
        }

        return response()->json($account);
    }

    public function identifyBank(Request $request): JsonResponse
    {
        $request->validate(['iban' => 'required|string|min:26']);

        $iban = str_replace(' ', '', $request->iban);
        // TR IBAN: pos 5-9 (1-indexed) 5 haneli banka kodu — ilk hane "0" padding'i, banks.code 4 hane
        $bankCode = substr($iban, 5, 4);

        $bank = DB::table('banks')->where('code', $bankCode)->first();

        if (! $bank) {
            return response()->json(['message' => 'Banka bulunamadı.'], 404);
        }

        return response()->json(['bank' => $bank]);
    }

    public function store(Request $request): JsonResponse
    {
        $authUser = $request->user();
        if (! $authUser->canManageBankAccounts()) {
            abort(403, __('auth.no_permission'));
        }

        // Takım üyesi (team admin/agent) için takım otomatik olarak kendi takımı
        $isTeamMember = $authUser->isTeamMember();
        if ($isTeamMember) {
            $request->merge(['team_id' => (int) $authUser->team_id]);
        }

        $request->validate([
            'status'            => 'required|in:0,1,2,3',
            'account_code'      => 'required|string|min:5',
            'account_holder'    => 'required|string|max:255',
            'account_iban'      => 'required|string|min:26',
            'bank_id'           => 'required|integer',
            'min_invest'        => 'required|numeric|min:0',
            'max_invest'        => 'required|numeric|min:0',
            'max_per_invest'    => 'required|numeric|min:0',
            'max_amount'        => 'required|numeric|min:0',
            'team_id'           => 'required|integer',
            'walletID'          => 'nullable|integer',
            'daily_count_limit' => 'nullable|integer|min:0',
        ]);

        $iban = str_replace(' ', '', $request->account_iban);

        // IBAN duplicate kontrol — hedef takımın allow_duplicate_iban ayarına göre
        $targetTeam = DB::table('teams')->where('id', $request->team_id)->first();
        $allowDup = $targetTeam && (int) $targetTeam->allow_duplicate_iban === 1;

        if (! $allowDup) {
            $exists = DB::table('bankAccounts')
                ->where('account_iban', $iban)
                ->where('status', '!=', 0)
                ->first();

            if ($exists) {
                return response()->json(['message' => 'Bu IBAN zaten sistemde kayıtlı. Aynı IBAN ile birden fazla hesap eklenmesine takım ayarından izin verilmiş olmalı.'], 422);
            }
        }

        // Yeni hesap en sona eklenir (sort_order = max + 1)
        $nextOrder = (int) DB::table('bankAccounts')->max('sort_order') + 1;

        $id = DB::table('bankAccounts')->insertGetId([
            'bank_id'           => $request->bank_id,
            'account_holder'    => $request->account_holder,
            'account_iban'      => $iban,
            'account_code'      => $request->account_code,
            'min_invest'        => $request->min_invest,
            'max_invest'        => $request->max_invest,
            'max_per_invest'    => $request->max_per_invest,
            'max_amount'        => $request->max_amount,
            'status'            => $request->status,
            'team_id'           => $request->team_id,
            'walletID'          => $request->walletID,
            'daily_count_limit' => (int) ($request->daily_count_limit ?? 0),
            'sort_order'        => $nextOrder,
            'created_at'        => now(),
        ]);

        return response()->json(['id' => $id, 'message' => 'Banka hesabı oluşturuldu.']);
    }

    public function update(Request $request, int $id): JsonResponse
    {
        $authUser = $request->user();
        if (! $authUser->canManageBankAccounts()) {
            abort(403, __('auth.no_permission'));
        }

        // Takım üyesi başka takıma taşıyamaz — gönderirse kendi takımı ile değiştir
        if ($authUser->isTeamMember()) {
            $request->merge(['team_id' => (int) $authUser->team_id]);
        }

        $request->validate([
            'status'            => 'sometimes|in:0,1,2,3',
            'account_code'      => 'sometimes|string|max:50',
            'account_holder'    => 'sometimes|string|max:255',
            'account_iban'      => 'sometimes|string|min:20',
            'bank_id'           => 'sometimes|integer',
            'min_invest'        => 'sometimes|nullable|numeric|min:0',
            'max_invest'        => 'sometimes|nullable|numeric|min:0',
            'max_per_invest'    => 'sometimes|nullable|numeric|min:0',
            'max_amount'        => 'sometimes|nullable|numeric|min:0',
            'team_id'           => 'sometimes|integer',
            'walletID'          => 'nullable|integer',
            'daily_count_limit' => 'nullable|integer|min:0',
        ]);

        $data = $request->only([
            'status', 'account_code', 'account_holder', 'bank_id',
            'min_invest', 'max_invest', 'max_per_invest', 'max_amount',
            'team_id', 'walletID', 'daily_count_limit',
        ]);

        if ($request->has('account_iban')) {
            $data['account_iban'] = str_replace(' ', '', $request->account_iban);
        }

        // IBAN veya takım değişiyorsa: hedef takımın allow_duplicate_iban=0 ise sistem genelinde duplicate kontrol
        $current = DB::table('bankAccounts')->where('id', $id)->first();
        $newIban = $data['account_iban'] ?? ($current->account_iban ?? null);
        $newTeamId = $data['team_id'] ?? ($current->team_id ?? null);

        if ($newIban && $newTeamId) {
            $targetTeam = DB::table('teams')->where('id', $newTeamId)->first();
            $allowDup = $targetTeam && (int) $targetTeam->allow_duplicate_iban === 1;

            if (! $allowDup) {
                $exists = DB::table('bankAccounts')
                    ->where('account_iban', $newIban)
                    ->where('status', '!=', 0)
                    ->where('id', '!=', $id)
                    ->first();

                if ($exists) {
                    return response()->json(['message' => 'Bu IBAN zaten sistemde kayıtlı. Aynı IBAN ile birden fazla hesap eklenmesine takım ayarından izin verilmiş olmalı.'], 422);
                }
            }
        }

        DB::table('bankAccounts')->where('id', $id)->update($data);

        return response()->json(['message' => 'Hesap güncellendi.']);
    }

    public function destroy(Request $request, int $id): JsonResponse
    {
        if (! $request->user()->canManageBankAccounts()) {
            abort(403, __('auth.no_permission'));
        }

        DB::table('bankAccounts')->where('id', $id)->update(['status' => 0]);
        return response()->json(['message' => 'Hesap devre dışı bırakıldı.']);
    }

    /**
     * Sürükle-bırak sonrası tüm sıralamayı kaydet.
     * Body: { ids: [3, 7, 1, ...] } → sort_order = index + 1
     */
    public function reorder(Request $request): JsonResponse
    {
        if (! $request->user()->isAdmin()) {
            abort(403, __('auth.no_permission'));
        }

        $request->validate([
            'ids'   => 'required|array',
            'ids.*' => 'integer',
        ]);

        DB::transaction(function () use ($request) {
            foreach ($request->ids as $idx => $id) {
                DB::table('bankAccounts')->where('id', (int) $id)->update(['sort_order' => $idx + 1]);
            }
        });

        return response()->json(['message' => 'Sıralama güncellendi.']);
    }

    /**
     * Tek hesabı belirli pozisyona taşı; mevcut o pozisyon ve sonrası +1 kaydırılır.
     * Body: { position: int }
     */
    public function setSortOrder(int $id, Request $request): JsonResponse
    {
        if (! $request->user()->isAdmin()) {
            abort(403, __('auth.no_permission'));
        }

        $request->validate([
            'position' => 'required|integer|min:1',
        ]);

        $target = DB::table('bankAccounts')->where('id', $id)->first();
        if (! $target) {
            return response()->json(['message' => 'Hesap bulunamadı.'], 404);
        }

        $newPos = (int) $request->position;

        DB::transaction(function () use ($id, $newPos) {
            // 1) Hedefi geçici çok büyük değere taşı (çakışmasın)
            DB::table('bankAccounts')->where('id', $id)->update(['sort_order' => 999999]);

            // 2) sort_order >= newPos olan tüm kayıtları +1 kaydır
            DB::table('bankAccounts')
                ->where('sort_order', '>=', $newPos)
                ->where('id', '!=', $id)
                ->increment('sort_order');

            // 3) Hedefi istenen pozisyona yerleştir
            DB::table('bankAccounts')->where('id', $id)->update(['sort_order' => $newPos]);

            // 4) Boşlukları normalize et (sort_order'ı kompakt sırala)
            $all = DB::table('bankAccounts')->orderBy('sort_order')->orderBy('id')->pluck('id');
            foreach ($all as $idx => $rowId) {
                DB::table('bankAccounts')->where('id', $rowId)->update(['sort_order' => $idx + 1]);
            }
        });

        return response()->json(['message' => 'Öneri sırası güncellendi.']);
    }
}
