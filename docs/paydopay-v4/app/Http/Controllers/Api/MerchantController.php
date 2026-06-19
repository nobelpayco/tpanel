<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;

class MerchantController extends Controller
{
    public function index(Request $request): JsonResponse
    {
        $statusFilter = $request->get('status', '1');

        $query = DB::table('merchantUser')
            ->leftJoin('merchant_groups', 'merchantUser.group_id', '=', 'merchant_groups.id')
            ->select(
                'merchantUser.*',
                'merchant_groups.name as group_name'
            );

        if ($statusFilter !== 'all') {
            $query->where('merchantUser.status', $statusFilter);
        }

        $merchants = $query->orderBy('merchantUser.name')->get();

        return response()->json($merchants);
    }

    public function store(Request $request): JsonResponse
    {
        if (! $request->user()->canManageMerchants()) {
            abort(403, __('auth.no_permission'));
        }

        $request->validate([
            'name'                => 'required|string|max:255',
            'email'               => 'nullable|string|max:255',
            'commission'          => 'required|numeric|min:0|max:100',
            'withdrawCommission'  => 'required|numeric|min:0|max:100',
            'deliveryCommission'  => 'required|numeric|min:0|max:100',
            'depositLimit'        => 'nullable|numeric|min:0',
            'minDeposit'          => 'nullable|numeric|min:0',
            'maxDeposit'          => 'nullable|numeric|min:0',
            'group_id'            => 'nullable|integer',
            'approved_ip'         => 'nullable|string',
        ]);

        $apiKey = Str::random(48);

        $id = DB::table('merchantUser')->insertGetId([
            'name'                => $request->name,
            'email'               => $request->email,
            'password'            => '',
            'apiKey'              => $apiKey,
            'commission'          => $request->commission,
            'withdrawCommission'  => $request->withdrawCommission,
            'deliveryCommission'  => $request->deliveryCommission,
            'depositLimit'        => $request->depositLimit ?? 0,
            'minDeposit'          => $request->minDeposit ?? 0,
            'maxDeposit'          => $request->maxDeposit ?? 0,
            'group_id'            => $request->group_id,
            'approved_ip'         => $request->approved_ip,
            'status'              => '1',
            'caseNow'             => 0,
            'useWallet'           => 0,
            'created_At'          => now(),
        ]);

        return response()->json(['id' => $id, 'api_key' => $apiKey, 'message' => 'Merchant oluşturuldu.']);
    }

    public function update(Request $request, int $id): JsonResponse
    {
        if (! $request->user()->canManageMerchants()) {
            abort(403, __('auth.no_permission'));
        }

        $request->validate([
            'name'                => 'sometimes|string|max:255',
            'email'               => 'nullable|string|max:255',
            'commission'          => 'sometimes|numeric|min:0|max:100',
            'withdrawCommission'  => 'sometimes|numeric|min:0|max:100',
            'deliveryCommission'  => 'sometimes|numeric|min:0|max:100',
            'depositLimit'        => 'nullable|numeric|min:0',
            'minDeposit'          => 'nullable|numeric|min:0',
            'maxDeposit'          => 'nullable|numeric|min:0',
            'group_id'            => 'nullable|integer',
            'approved_ip'         => 'nullable|string',
            'status'              => 'sometimes|in:0,1',
            'new_api'             => 'sometimes|in:0,1',
        ]);

        DB::table('merchantUser')->where('id', $id)->update(
            $request->only([
                'name', 'email', 'commission', 'withdrawCommission', 'deliveryCommission',
                'depositLimit', 'minDeposit', 'maxDeposit', 'group_id', 'approved_ip', 'status', 'new_api',
            ])
        );

        return response()->json(['message' => 'Merchant güncellendi.']);
    }

    public function destroy(Request $request, int $id): JsonResponse
    {
        if (! $request->user()->canManageMerchants()) {
            abort(403, __('auth.no_permission'));
        }

        DB::table('merchantUser')->where('id', $id)->update(['status' => '0']);
        return response()->json(['message' => 'Merchant devre dışı bırakıldı.']);
    }

    /**
     * Merchant API anahtarlarını gör veya yeniden üret.
     * GET  /merchants/{id}/credentials       → apiKey + apiSecret (admin görüntüleme)
     * POST /merchants/{id}/rotate-secret     → yeni 64-char apiSecret üretir, döner (sadece bir kez)
     * POST /merchants/{id}/rotate-key        → yeni 48-char apiKey + 64-char apiSecret üretir
     */
    public function showCredentials(int $id): JsonResponse
    {
        $m = DB::table('merchantUser')->where('id', $id)->first(['id', 'name', 'apiKey', 'apiSecret']);
        if (! $m) return response()->json(['message' => 'Merchant bulunamadı.'], 404);

        return response()->json([
            'id'          => $m->id,
            'name'        => $m->name,
            'api_key'     => $m->apiKey,
            'has_secret'  => ! empty($m->apiSecret),
        ]);
    }

    public function rotateSecret(int $id): JsonResponse
    {
        $m = DB::table('merchantUser')->where('id', $id)->first();
        if (! $m) return response()->json(['message' => 'Merchant bulunamadı.'], 404);

        $secret = \Illuminate\Support\Str::random(64);
        DB::table('merchantUser')->where('id', $id)->update(['apiSecret' => $secret]);

        return response()->json([
            'message'    => 'Yeni API Secret üretildi. Bu değer bir daha gösterilmez; merchant ile paylaşın.',
            'api_key'    => $m->apiKey,
            'api_secret' => $secret,
        ]);
    }

    public function rotateKey(int $id): JsonResponse
    {
        $m = DB::table('merchantUser')->where('id', $id)->first();
        if (! $m) return response()->json(['message' => 'Merchant bulunamadı.'], 404);

        $key    = \Illuminate\Support\Str::random(48);
        $secret = \Illuminate\Support\Str::random(64);
        DB::table('merchantUser')->where('id', $id)->update(['apiKey' => $key, 'apiSecret' => $secret]);

        return response()->json([
            'message'    => 'Yeni API Key + Secret üretildi. Eski apiKey artık geçersiz.',
            'api_key'    => $key,
            'api_secret' => $secret,
        ]);
    }

    // --- Gruplar ---

    public function groups(): JsonResponse
    {
        $groups = DB::table('merchant_groups')
            ->where('status', 1)
            ->orderBy('name')
            ->get()
            ->map(function ($g) {
                $g->merchants = DB::table('merchantUser')
                    ->where('group_id', $g->id)
                    ->select('id', 'name', 'status')
                    ->get();
                return $g;
            });

        $ungrouped = DB::table('merchantUser')
            ->whereNull('group_id')
            ->where('status', '1')
            ->select('id', 'name')
            ->get();

        return response()->json([
            'groups'    => $groups,
            'ungrouped' => $ungrouped,
        ]);
    }

    public function storeGroup(Request $request): JsonResponse
    {
        $request->validate(['name' => 'required|string|max:255']);

        $id = DB::table('merchant_groups')->insertGetId([
            'name'       => $request->name,
            'status'     => 1,
            'created_at' => now(),
        ]);

        return response()->json(['id' => $id, 'message' => 'Grup oluşturuldu.']);
    }

    public function updateGroup(Request $request, int $id): JsonResponse
    {
        $request->validate([
            'name'   => 'sometimes|string|max:255',
            'status' => 'sometimes|in:0,1',
        ]);

        DB::table('merchant_groups')->where('id', $id)->update($request->only(['name', 'status']));
        return response()->json(['message' => 'Grup güncellendi.']);
    }

    public function destroyGroup(int $id): JsonResponse
    {
        DB::table('merchantUser')->where('group_id', $id)->update(['group_id' => null]);
        DB::table('merchant_groups')->where('id', $id)->update(['status' => 0]);
        return response()->json(['message' => 'Grup devre dışı bırakıldı.']);
    }

    public function assignToGroup(Request $request): JsonResponse
    {
        $request->validate([
            'merchant_id' => 'required|integer',
            'group_id'    => 'nullable|integer',
        ]);

        DB::table('merchantUser')
            ->where('id', $request->merchant_id)
            ->update(['group_id' => $request->group_id]);

        return response()->json(['message' => 'Merchant gruba atandı.']);
    }
}
