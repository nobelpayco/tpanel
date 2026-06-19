<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;

class TeamController extends Controller
{
    public function index(Request $request): JsonResponse
    {
        $statusFilter = $request->get('status', '1');
        $search = $request->get('search');

        $query = DB::table('teams');

        // 'every' = silinmiş (status=0) dahil tüm takımlar (ör. partner ödeme düşümünde eski takımlar)
        // 'all'   = silinmiş hariç tüm takımlar (status != 0)
        if ($statusFilter === 'every') {
            // filtre yok — hepsi
        } elseif ($statusFilter !== 'all') {
            $query->where('status', (int) $statusFilter);
        } else {
            $query->where('status', '!=', 0);
        }

        if ($search) {
            $query->where('name', 'like', "%{$search}%");
        }

        $teams = $query->orderBy('name')->get();

        return response()->json($teams);
    }

    public function show(int $id): JsonResponse
    {
        $team = DB::table('teams')->where('id', $id)->first();

        if (! $team) {
            return response()->json(['message' => 'Takım bulunamadı.'], 404);
        }

        return response()->json($team);
    }

    public function store(Request $request): JsonResponse
    {
        if (! $request->user()->canManageTeams()) {
            abort(403, __('auth.no_permission'));
        }

        $request->validate([
            'name'             => 'required|string|max:255',
            'status'           => 'required|in:0,1,2,3',
            'min_invest'       => 'required|numeric|min:0',
            'max_invest'       => 'required|numeric|min:0',
            'wait_limit'       => 'required|numeric|min:0',
            'commission'       => 'required|numeric|min:0',
            'maxCase'              => 'required|numeric',
            'allow_duplicate_iban' => 'sometimes|in:0,1',
            'block_when_full'      => 'sometimes|in:0,1',
            'telegram_enabled'     => 'sometimes|in:0,1',
            'telegram_chat_id'     => 'nullable|string|max:50',
            'telegram_withdraw_chat_id'        => 'nullable|string|max:50',
            'telegram_reconciliation_chat_id'  => 'nullable|string|max:50',
            'telegram_credit_low_enabled'      => 'sometimes|in:0,1',
            'telegram_credit_low_threshold'    => 'nullable|numeric|min:0',
            'telegram_pending_invest_enabled'  => 'sometimes|in:0,1',
            'telegram_missing_receipt_enabled' => 'sometimes|in:0,1',
            'telegram_withdraw_assigned_enabled' => 'sometimes|in:0,1',
            'telegram_cash_report_enabled'     => 'sometimes|in:0,1',
        ]);

        // İsim kontrolü
        $exists = DB::table('teams')->where('name', $request->name)->first();
        if ($exists) {
            return response()->json(['message' => 'Bu takım adı zaten mevcut.'], 422);
        }

        $id = DB::table('teams')->insertGetId([
            'name'                 => $request->name,
            'status'               => $request->status,
            'min_invest'           => $request->min_invest,
            'max_invest'           => $request->max_invest,
            'wait_limit'           => $request->wait_limit,
            'commission'           => $request->commission,
            'maxCase'              => $request->maxCase,
            'allow_duplicate_iban' => (int) $request->get('allow_duplicate_iban', 0),
            'block_when_full'      => (int) $request->get('block_when_full', 1),
            'account_perm'         => 0,
            'allowed_customers'    => '',
            'overturn'             => 0,
            'withdraw'             => 0,
            'telegram_enabled'     => (int) $request->get('telegram_enabled', 0),
            'telegram_chat_id'     => $request->telegram_chat_id,
            'telegram_withdraw_chat_id'        => $request->telegram_withdraw_chat_id,
            'telegram_reconciliation_chat_id'  => $request->telegram_reconciliation_chat_id,
            'telegram_credit_low_enabled'      => (int) $request->get('telegram_credit_low_enabled', 0),
            'telegram_credit_low_threshold'    => $request->telegram_credit_low_threshold,
            'telegram_credit_low_state'        => (int) $request->get('telegram_credit_low_enabled', 0) === 1 ? 1 : 0,
            'telegram_credit_low_enabled_at'   => (int) $request->get('telegram_credit_low_enabled', 0) === 1 ? now() : null,
            'telegram_pending_invest_enabled'  => (int) $request->get('telegram_pending_invest_enabled', 0),
            'telegram_pending_invest_enabled_at' => (int) $request->get('telegram_pending_invest_enabled', 0) === 1 ? now() : null,
            'telegram_missing_receipt_enabled' => (int) $request->get('telegram_missing_receipt_enabled', 0),
            'telegram_missing_receipt_enabled_at' => (int) $request->get('telegram_missing_receipt_enabled', 0) === 1 ? now() : null,
            'telegram_withdraw_assigned_enabled' => (int) $request->get('telegram_withdraw_assigned_enabled', 0),
            'telegram_withdraw_assigned_enabled_at' => (int) $request->get('telegram_withdraw_assigned_enabled', 0) === 1 ? now() : null,
            'telegram_cash_report_enabled'    => (int) $request->get('telegram_cash_report_enabled', 0),
            'created_at'           => now(),
        ]);

        return response()->json(['id' => $id, 'message' => 'Takım oluşturuldu.']);
    }

    public function update(Request $request, int $id): JsonResponse
    {
        if (! $request->user()->canManageTeams()) {
            abort(403, __('auth.no_permission'));
        }

        $request->validate([
            'name'             => 'sometimes|string|max:255',
            'status'           => 'sometimes|in:0,1,2,3',
            'min_invest'       => 'sometimes|nullable|numeric|min:0',
            'max_invest'       => 'sometimes|nullable|numeric|min:0',
            'wait_limit'       => 'sometimes|nullable|numeric|min:0',
            'commission'       => 'sometimes|nullable|numeric|min:0',
            'maxCase'              => 'sometimes|nullable|numeric',
            'allow_duplicate_iban' => 'sometimes|in:0,1',
            'block_when_full'      => 'sometimes|in:0,1',
            'overturn'             => 'sometimes|nullable|numeric',
            'withdraw'             => 'sometimes|nullable|numeric',
            'telegram_enabled'     => 'sometimes|in:0,1',
            'telegram_chat_id'     => 'nullable|string|max:50',
            'telegram_withdraw_chat_id'        => 'nullable|string|max:50',
            'telegram_reconciliation_chat_id'  => 'nullable|string|max:50',
            'telegram_credit_low_enabled'      => 'sometimes|in:0,1',
            'telegram_credit_low_threshold'    => 'nullable|numeric|min:0',
            'telegram_pending_invest_enabled'  => 'sometimes|in:0,1',
            'telegram_missing_receipt_enabled' => 'sometimes|in:0,1',
            'telegram_withdraw_assigned_enabled' => 'sometimes|in:0,1',
            'telegram_cash_report_enabled'     => 'sometimes|in:0,1',
        ]);

        $data = $request->only([
            'name', 'status', 'min_invest', 'max_invest',
            'wait_limit', 'commission', 'maxCase', 'allow_duplicate_iban', 'block_when_full',
            'overturn', 'withdraw',
            'telegram_enabled', 'telegram_chat_id',
            'telegram_withdraw_chat_id', 'telegram_reconciliation_chat_id',
            'telegram_credit_low_enabled', 'telegram_credit_low_threshold',
            'telegram_pending_invest_enabled', 'telegram_missing_receipt_enabled',
            'telegram_withdraw_assigned_enabled',
            'telegram_cash_report_enabled',
        ]);

        // Threshold değişirse veya switch açılıp kapansa state'i sıfırla
        // (state=1 başla → ilk cron'da current<threshold ise state=0'a düşer; current>=threshold zaten low, mesaj atılmaz)
        if (array_key_exists('telegram_credit_low_threshold', $data) || array_key_exists('telegram_credit_low_enabled', $data)) {
            $data['telegram_credit_low_state'] = 1;
        }

        // Switch'ler 0→1 olduğunda enabled_at = now() — sadece bu andan sonraki invest'ler için bildirim
        $currentTeam = DB::table('teams')->where('id', $id)->first();
        if ($currentTeam) {
            $switchTimestampMap = [
                'telegram_credit_low_enabled'      => 'telegram_credit_low_enabled_at',
                'telegram_pending_invest_enabled'  => 'telegram_pending_invest_enabled_at',
                'telegram_missing_receipt_enabled' => 'telegram_missing_receipt_enabled_at',
                'telegram_withdraw_assigned_enabled' => 'telegram_withdraw_assigned_enabled_at',
            ];
            foreach ($switchTimestampMap as $switchField => $tsField) {
                if (array_key_exists($switchField, $data)
                    && (int) $data[$switchField] === 1
                    && (int) ($currentTeam->{$switchField} ?? 0) === 0) {
                    $data[$tsField] = now();
                }
            }
        }

        // Numeric alanlardan null/boş olanları filtrele (telegram_chat_id null kalabilir)
        foreach (['min_invest', 'max_invest', 'wait_limit', 'commission', 'maxCase', 'overturn', 'withdraw'] as $numField) {
            if (array_key_exists($numField, $data) && ($data[$numField] === null || $data[$numField] === '')) {
                unset($data[$numField]);
            }
        }

        DB::table('teams')->where('id', $id)->update($data);

        return response()->json(['message' => 'Takım güncellendi.']);
    }

    public function destroy(Request $request, int $id): JsonResponse
    {
        if (! $request->user()->canManageTeams()) {
            abort(403, __('auth.no_permission'));
        }

        DB::table('teams')->where('id', $id)->update(['status' => 0]);
        return response()->json(['message' => 'Takım devre dışı bırakıldı.']);
    }
}
