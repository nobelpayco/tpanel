<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;

class InitialBalanceController extends Controller
{
    public function getEntities(): JsonResponse
    {
        // Merchant'ları grup bazlı birleştir
        $allMerchants = DB::table('merchantUser')
            ->select('id', 'name', 'group_id')
            ->orderBy('name')
            ->get();

        $groups = DB::table('merchant_groups')->get()->keyBy('id');
        $merchantItems = collect();
        $processedGroupIds = [];

        foreach ($allMerchants as $m) {
            if ($m->group_id && isset($groups[$m->group_id])) {
                if (in_array($m->group_id, $processedGroupIds)) continue;
                $processedGroupIds[] = $m->group_id;
                $groupMerchantIds = $allMerchants->where('group_id', $m->group_id)->pluck('id')->toArray();
                $merchantItems->push(['type' => 'merchant_group', 'id' => $m->group_id, 'merchant_ids' => $groupMerchantIds, 'name' => $groups[$m->group_id]->name, 'amount' => 0]);
            } else {
                $merchantItems->push(['type' => 'merchant', 'id' => $m->id, 'name' => $m->name, 'amount' => 0]);
            }
        }

        $merchants = $merchantItems;

        $teams = DB::table('teams')
            ->select('id', 'name')
            ->orderBy('name')
            ->get()
            ->map(fn($t) => ['type' => 'team', 'id' => $t->id, 'name' => $t->name, 'amount' => 0]);

        $intermediaries = DB::table('new_intermediaries')
            ->select('id', 'name')
            ->orderBy('name')
            ->get()
            ->map(fn($i) => ['type' => 'intermediary', 'id' => $i->id, 'name' => $i->name, 'amount' => 0]);

        $partners = DB::table('paylira_partners')
            ->select('id', 'name')
            ->orderBy('id')
            ->get()
            ->map(fn($p) => ['type' => 'partner', 'id' => $p->id, 'name' => $p->name, 'amount' => 0]);

        $paylira = [['type' => 'paylira', 'id' => null, 'name' => 'Paylira Net', 'amount' => 0]];

        return response()->json([
            'merchants'      => $merchants->values(),
            'teams'          => $teams->values(),
            'intermediaries' => $intermediaries->values(),
            'partners'       => $partners->values(),
            'paylira'        => $paylira,
        ]);
    }

    public function save(Request $request): JsonResponse
    {
        $request->validate([
            'date'     => 'required|date',
            'entities' => 'required|array',
            'entities.*.type'   => 'required|in:merchant,merchant_group,team,intermediary,partner,paylira',
            'entities.*.id'     => 'nullable|integer',
            'entities.*.name'   => 'required|string',
            'entities.*.amount' => 'required|numeric',
        ]);

        $date = $request->date;
        $userName = auth()->user()->name;

        // Seçilen tarihte mevcut snapshot varsa sil
        DB::table('daily_case_snapshots')->where('snapshot_date', $date)->delete();

        // Başlangıç bakiyelerini ayrı tabloya kaydet (sıfırlama için)
        DB::table('initial_balances')->where('snapshot_date', $date)->delete();

        foreach ($request->entities as $entity) {
            $row = [
                'snapshot_date' => $date,
                'entity_type'   => $entity['type'],
                'entity_id'     => $entity['id'],
                'entity_name'   => $entity['name'],
                'amount'        => $entity['amount'],
                'created_at'    => now(),
            ];

            // Snapshot'a kaydet
            DB::table('daily_case_snapshots')->insert(array_merge($row, [
                'details' => json_encode(['initial_balance' => true, 'set_by' => $userName]),
            ]));

            // Başlangıç bakiyelerine kaydet (yedek)
            DB::table('initial_balances')->insert(array_merge($row, [
                'set_by' => $userName,
            ]));
        }

        return response()->json(['message' => 'Başlangıç bakiyeleri kaydedildi.']);
    }

    public function reset(Request $request): JsonResponse
    {
        $request->validate(['date' => 'required|date']);

        $date = $request->date;

        // initial_balances'tan başlangıç bakiyelerini al
        $initials = DB::table('initial_balances')->where('snapshot_date', $date)->get();

        if ($initials->isEmpty()) {
            return response()->json(['message' => 'Bu tarih için başlangıç bakiyesi bulunamadı.'], 404);
        }

        // Tüm snapshot'ları sil (bu tarih ve sonrası)
        DB::table('daily_case_snapshots')->where('snapshot_date', '>=', $date)->delete();

        // Aracı bakiyelerini sıfırla
        DB::table('new_intermediaries')->update(['balance' => 0]);

        // Başlangıç bakiyelerini tekrar snapshot olarak yaz
        foreach ($initials as $init) {
            DB::table('daily_case_snapshots')->insert([
                'snapshot_date' => $init->snapshot_date,
                'entity_type'   => $init->entity_type,
                'entity_id'     => $init->entity_id,
                'entity_name'   => $init->entity_name,
                'amount'        => $init->amount,
                'details'       => json_encode(['initial_balance' => true, 'reset_by' => auth()->user()->name]),
                'created_at'    => now(),
            ]);
        }

        return response()->json(['message' => 'Snapshot\'lar sıfırlandı ve başlangıç bakiyeleri geri yüklendi.']);
    }
}
