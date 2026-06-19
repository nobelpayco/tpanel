<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;

class IntermediaryController extends Controller
{
    public function index(Request $request): JsonResponse
    {
        $statusFilter = $request->get('status', '1'); // varsayılan aktif

        $query = DB::table('new_intermediaries');

        if ($statusFilter !== 'all') {
            $query->where('status', (int) $statusFilter);
        }

        $intermediaries = $query->orderBy('type')
            ->orderBy('name')
            ->get()
            ->map(function ($item) {
                $item->merchants = DB::table('new_intermediary_merchant')
                    ->join('merchantUser', 'new_intermediary_merchant.merchant_id', '=', 'merchantUser.id')
                    ->where('new_intermediary_merchant.intermediary_id', $item->id)
                    ->select('merchantUser.id', 'merchantUser.name', 'new_intermediary_merchant.commission_rate', 'new_intermediary_merchant.status', 'new_intermediary_merchant.id as pivot_id')
                    ->get();

                $item->teams = DB::table('new_intermediary_team')
                    ->join('teams', 'new_intermediary_team.team_id', '=', 'teams.id')
                    ->where('new_intermediary_team.intermediary_id', $item->id)
                    ->select('teams.id', 'teams.name', 'new_intermediary_team.commission_rate', 'new_intermediary_team.status', 'new_intermediary_team.id as pivot_id')
                    ->get();

                return $item;
            });

        $merchants = DB::table('merchantUser')
            ->where('status', '1')
            ->select('id', 'name')
            ->orderBy('name')
            ->get();

        $teams = DB::table('teams')
            ->where('status', '!=', 0)
            ->select('id', 'name')
            ->orderBy('name')
            ->get();

        return response()->json([
            'intermediaries' => $intermediaries,
            'merchants'      => $merchants,
            'teams'          => $teams,
        ]);
    }

    public function store(Request $request): JsonResponse
    {
        $request->validate([
            'name' => 'required|string|max:255',
            'type' => 'required|in:1,2,3',
            'commission_rate' => 'nullable|numeric|min:0|max:100',
        ]);

        $id = DB::table('new_intermediaries')->insertGetId([
            'name'            => $request->name,
            'type'            => $request->type,
            'commission_rate' => (int) $request->type === 3 ? (float) $request->input('commission_rate', 0) : 0,
            'status'          => 1,
            'created_at'      => now(),
        ]);

        return response()->json(['id' => $id, 'message' => 'Aracı oluşturuldu.']);
    }

    public function update(Request $request, int $id): JsonResponse
    {
        $request->validate([
            'name'            => 'sometimes|string|max:255',
            'type'            => 'sometimes|in:1,2,3',
            'status'          => 'sometimes|in:0,1',
            'commission_rate' => 'sometimes|numeric|min:0|max:100',
        ]);

        DB::table('new_intermediaries')->where('id', $id)->update($request->only(['name', 'type', 'status', 'commission_rate']));

        return response()->json(['message' => 'Aracı güncellendi.']);
    }

    public function destroy(int $id): JsonResponse
    {
        DB::table('new_intermediary_merchant')->where('intermediary_id', $id)->update(['status' => 0]);
        DB::table('new_intermediary_team')->where('intermediary_id', $id)->update(['status' => 0]);
        DB::table('new_intermediaries')->where('id', $id)->update(['status' => 0]);

        return response()->json(['message' => 'Aracı devre dışı bırakıldı.']);
    }

    // Merchant bağla
    public function attachMerchant(Request $request): JsonResponse
    {
        $request->validate([
            'intermediary_id' => 'required|integer',
            'merchant_id'     => 'required|integer',
            'commission_rate' => 'required|numeric|min:0|max:100',
        ]);

        $exists = DB::table('new_intermediary_merchant')
            ->where('intermediary_id', $request->intermediary_id)
            ->where('merchant_id', $request->merchant_id)
            ->first();

        if ($exists) {
            return response()->json(['message' => 'Bu merchant zaten bağlı.'], 422);
        }

        DB::table('new_intermediary_merchant')->insert([
            'intermediary_id' => $request->intermediary_id,
            'merchant_id'     => $request->merchant_id,
            'commission_rate' => $request->commission_rate,
            'status'          => 1,
            'created_at'      => now(),
        ]);

        return response()->json(['message' => 'Merchant bağlandı.']);
    }

    public function detachMerchant(int $pivotId): JsonResponse
    {
        DB::table('new_intermediary_merchant')->where('id', $pivotId)->update(['status' => 0]);

        return response()->json(['message' => 'Bağlantı devre dışı bırakıldı.']);
    }

    public function updateMerchantRate(Request $request, int $pivotId): JsonResponse
    {
        $request->validate([
            'commission_rate' => 'required|numeric|min:0|max:100',
            'status'          => 'sometimes|in:0,1',
        ]);

        DB::table('new_intermediary_merchant')
            ->where('id', $pivotId)
            ->update($request->only(['commission_rate', 'status']));

        return response()->json(['message' => 'Güncellendi.']);
    }

    // Takım bağla
    public function attachTeam(Request $request): JsonResponse
    {
        $request->validate([
            'intermediary_id' => 'required|integer',
            'team_id'         => 'required|integer',
            'commission_rate' => 'required|numeric|min:0|max:100',
        ]);

        $exists = DB::table('new_intermediary_team')
            ->where('intermediary_id', $request->intermediary_id)
            ->where('team_id', $request->team_id)
            ->first();

        if ($exists) {
            return response()->json(['message' => 'Bu takım zaten bağlı.'], 422);
        }

        DB::table('new_intermediary_team')->insert([
            'intermediary_id' => $request->intermediary_id,
            'team_id'         => $request->team_id,
            'commission_rate' => $request->commission_rate,
            'status'          => 1,
            'created_at'      => now(),
        ]);

        return response()->json(['message' => 'Takım bağlandı.']);
    }

    public function detachTeam(int $pivotId): JsonResponse
    {
        DB::table('new_intermediary_team')->where('id', $pivotId)->update(['status' => 0]);

        return response()->json(['message' => 'Bağlantı devre dışı bırakıldı.']);
    }

    public function updateTeamRate(Request $request, int $pivotId): JsonResponse
    {
        $request->validate([
            'commission_rate' => 'required|numeric|min:0|max:100',
            'status'          => 'sometimes|in:0,1',
        ]);

        DB::table('new_intermediary_team')
            ->where('id', $pivotId)
            ->update($request->only(['commission_rate', 'status']));

        return response()->json(['message' => 'Güncellendi.']);
    }
}
