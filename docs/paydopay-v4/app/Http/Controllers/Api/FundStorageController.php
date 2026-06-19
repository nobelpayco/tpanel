<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Http;

class FundStorageController extends Controller
{
    public function index(Request $request): JsonResponse
    {
        $statusFilter = $request->get('status', '1');

        $query = DB::table('fund_storages');

        if ($statusFilter !== 'all') {
            $query->where('status', (int) $statusFilter);
        }

        $storages = $query->orderBy('type')->orderBy('name')->get()->map(function ($s) {
            $s->chain_balance = null;
            if ($s->type == 2 && $s->wallet_address) {
                $s->chain_balance = $this->getTronUsdtBalance($s->wallet_address);
            }
            return $s;
        });

        return response()->json([
            'storages'      => $storages,
            'total_balance' => $storages->sum('balance'),
        ]);
    }

    public function store(Request $request): JsonResponse
    {
        $request->validate([
            'name'           => 'required|string|max:255',
            'type'           => 'required|in:1,2,3,4',
            'balance'        => 'required|numeric',
            'wallet_address' => 'nullable|string|max:255',
        ]);

        $id = DB::table('fund_storages')->insertGetId([
            'name'           => $request->name,
            'type'           => $request->type,
            'wallet_address' => $request->type == 2 ? $request->wallet_address : null,
            'balance'        => $request->balance,
            'status'     => 1,
            'created_at' => now(),
            'updated_at' => now(),
        ]);

        return response()->json(['id' => $id, 'message' => 'Fon deposu oluşturuldu.']);
    }

    public function update(Request $request, int $id): JsonResponse
    {
        $request->validate([
            'name'           => 'sometimes|string|max:255',
            'type'           => 'sometimes|in:1,2,3,4',
            'balance'        => 'sometimes|numeric',
            'status'         => 'sometimes|in:0,1',
            'wallet_address' => 'nullable|string|max:255',
        ]);

        $data = $request->only(['name', 'type', 'balance', 'status', 'wallet_address']);
        // Sadece kripto (type=2) cüzdan adresi tutar; nakit (1) ve alacak (3) için null
        if (isset($data['type']) && $data['type'] != 2) {
            $data['wallet_address'] = null;
        }
        $data['updated_at'] = now();

        DB::table('fund_storages')->where('id', $id)->update($data);

        return response()->json(['message' => 'Güncellendi.']);
    }

    public function destroy(int $id): JsonResponse
    {
        DB::table('fund_storages')->where('id', $id)->update(['status' => 0, 'updated_at' => now()]);

        return response()->json(['message' => 'Devre dışı bırakıldı.']);
    }

    private function getTronUsdtBalance(string $address): ?float
    {
        try {
            $response = Http::timeout(5)->get("https://api.trongrid.io/v1/accounts/{$address}");

            if (! $response->ok()) return null;

            $trc20 = $response->json('data.0.trc20', []);
            $usdtContract = 'TR7NHqjeKQxGTCi8q8ZY4pL8otSzgjLj6t';

            foreach ($trc20 as $token) {
                if (isset($token[$usdtContract])) {
                    return round((float) bcdiv($token[$usdtContract], '1000000', 6), 2);
                }
            }

            return 0;
        } catch (\Exception) {
            return null;
        }
    }

    public function tronTxLookup(Request $request): JsonResponse
    {
        $request->validate(['tx_link' => 'required|string']);

        // Hash'i link'ten çıkar
        $link = $request->tx_link;
        if (preg_match('/([a-f0-9]{64})/i', $link, $matches)) {
            $hash = $matches[1];
        } else {
            return response()->json(['message' => 'Geçersiz işlem linki.'], 422);
        }

        try {
            $response = Http::timeout(10)->get("https://apilist.tronscanapi.com/api/transaction-info?hash={$hash}");
            if (! $response->ok()) {
                return response()->json(['message' => 'İşlem bilgisi alınamadı.'], 422);
            }

            $data = $response->json();
            $contractType = $data['contractType'] ?? null;

            // TRC20 (USDT) transfer
            if ($contractType == 31) {
                $triggerInfo = $data['trigger_info'] ?? [];
                $tokenAmount = $triggerInfo['parameter']['_value'] ?? null;
                $tokenInfo = $data['tokenTransferInfo'] ?? $data['trc20TransferInfo'] ?? null;

                if ($tokenAmount) {
                    $decimals = 6; // USDT decimals
                    $quantity = (float) bcdiv($tokenAmount, bcpow('10', (string) $decimals), $decimals);
                } elseif (isset($data['contractData']['amount'])) {
                    $quantity = (float) $data['contractData']['amount'] / 1000000;
                } else {
                    $quantity = null;
                }

                return response()->json(['quantity' => $quantity, 'type' => 'TRC20']);
            }

            // TRX transfer
            if ($contractType == 1) {
                $amount = ($data['contractData']['amount'] ?? 0) / 1000000;
                return response()->json(['quantity' => (float) $amount, 'type' => 'TRX']);
            }

            return response()->json(['message' => 'Desteklenmeyen işlem türü.'], 422);
        } catch (\Exception $e) {
            return response()->json(['message' => 'İşlem bilgisi alınamadı.'], 422);
        }
    }

    public function show(int $id, Request $request): JsonResponse
    {
        $storage = DB::table('fund_storages')->where('id', $id)->first();
        if (!$storage) return response()->json(['message' => 'Fon deposu bulunamadı.'], 404);

        $dateFrom = $request->get('date_from', now()->startOfMonth()->toDateString());
        $dateTo = $request->get('date_to', now()->toDateString());

        $movements = collect();

        // 1. Merchant ödemeleri (kripto, bu depodan düşüldü) - ÇIKIŞ
        $merchantPayments = DB::table('merchant_payments')
            ->leftJoin('merchantUser', 'merchant_payments.merchant_id', '=', 'merchantUser.id')
            ->leftJoin('users', 'merchant_payments.created_by', '=', 'users.id')
            ->where('merchant_payments.fund_storage_id', $id)
            ->whereDate('merchant_payments.created_at', '>=', $dateFrom)
            ->whereDate('merchant_payments.created_at', '<=', $dateTo)
            ->select(
                'merchant_payments.id',
                'merchant_payments.amount',
                'merchant_payments.delivery_profit',
                'merchant_payments.description',
                'merchant_payments.created_at',
                'merchantUser.name as merchant_name',
                'users.name as created_by_name'
            )
            ->get();

        foreach ($merchantPayments as $m) {
            $movements->push([
                'id'         => 'mp_' . $m->id,
                'direction'  => 'out',
                'source'     => 'Merchant Ödemesi',
                'target'     => $m->merchant_name,
                'amount'     => (float) $m->amount,
                'description'=> $m->description,
                'created_by' => $m->created_by_name,
                'created_at' => $m->created_at,
            ]);

            // Teslimat karı varsa ayrı bir GİRİŞ hareketi olarak ekle
            if ((float) $m->delivery_profit > 0) {
                $movements->push([
                    'id'         => 'mpdp_' . $m->id,
                    'direction'  => 'in',
                    'source'     => 'Teslimat Karı',
                    'target'     => $m->merchant_name,
                    'amount'     => (float) $m->delivery_profit,
                    'description'=> $m->description,
                    'created_by' => $m->created_by_name,
                    'created_at' => $m->created_at,
                ]);
            }
        }

        // 2. Aracı ödemeleri (kripto) - ÇIKIŞ
        $intermediaryPayments = DB::table('intermediary_payments')
            ->leftJoin('new_intermediaries', 'intermediary_payments.intermediary_id', '=', 'new_intermediaries.id')
            ->leftJoin('users', 'intermediary_payments.created_by', '=', 'users.id')
            ->where('intermediary_payments.fund_storage_id', $id)
            ->whereDate('intermediary_payments.created_at', '>=', $dateFrom)
            ->whereDate('intermediary_payments.created_at', '<=', $dateTo)
            ->select(
                'intermediary_payments.id',
                'intermediary_payments.amount',
                'intermediary_payments.description',
                'intermediary_payments.created_at',
                'new_intermediaries.name as inter_name',
                'users.name as created_by_name'
            )
            ->get();

        foreach ($intermediaryPayments as $m) {
            $movements->push([
                'id'         => 'ip_' . $m->id,
                'direction'  => 'out',
                'source'     => 'Aracı Ödemesi',
                'target'     => $m->inter_name,
                'amount'     => (float) $m->amount,
                'description'=> $m->description,
                'created_by' => $m->created_by_name,
                'created_at' => $m->created_at,
            ]);
        }

        // 3. Partner ödemeleri (kripto) - ÇIKIŞ
        $partnerPayments = DB::table('paylira_partner_payments')
            ->leftJoin('paylira_partners', 'paylira_partner_payments.partner_id', '=', 'paylira_partners.id')
            ->leftJoin('users', 'paylira_partner_payments.created_by', '=', 'users.id')
            ->where('paylira_partner_payments.fund_storage_id', $id)
            ->whereDate('paylira_partner_payments.created_at', '>=', $dateFrom)
            ->whereDate('paylira_partner_payments.created_at', '<=', $dateTo)
            ->select(
                'paylira_partner_payments.id',
                'paylira_partner_payments.amount',
                'paylira_partner_payments.description',
                'paylira_partner_payments.created_at',
                'paylira_partners.name as partner_name',
                'users.name as created_by_name'
            )
            ->get();

        foreach ($partnerPayments as $m) {
            $movements->push([
                'id'         => 'pp_' . $m->id,
                'direction'  => 'out',
                'source'     => 'Partner Ödemesi',
                'target'     => $m->partner_name,
                'amount'     => (float) $m->amount,
                'description'=> $m->description,
                'created_by' => $m->created_by_name,
                'created_at' => $m->created_at,
            ]);
        }

        // 4. Takım ödemeleri - GİRİŞ (takım ödemesi yapıldığında depoya gelir)
        $teamPayments = DB::table('team_payments')
            ->leftJoin('teams', 'team_payments.team_id', '=', 'teams.id')
            ->leftJoin('users', 'team_payments.created_by', '=', 'users.id')
            ->where('team_payments.fund_storage_id', $id)
            ->whereDate('team_payments.created_at', '>=', $dateFrom)
            ->whereDate('team_payments.created_at', '<=', $dateTo)
            ->select(
                'team_payments.id',
                'team_payments.amount',
                'team_payments.description',
                'team_payments.created_at',
                'teams.name as team_name',
                'users.name as created_by_name'
            )
            ->get();

        foreach ($teamPayments as $m) {
            $movements->push([
                'id'         => 'tp_' . $m->id,
                'direction'  => 'in',
                'source'     => 'Takım Ödemesi',
                'target'     => $m->team_name,
                'amount'     => (float) $m->amount,
                'description'=> $m->description,
                'created_by' => $m->created_by_name,
                'created_at' => $m->created_at,
            ]);
        }

        // 5. Partner sermaye ekleme - GİRİŞ
        $capitals = DB::table('partner_capitals')
            ->leftJoin('paylira_partners', 'partner_capitals.partner_id', '=', 'paylira_partners.id')
            ->leftJoin('users', 'partner_capitals.created_by', '=', 'users.id')
            ->where('partner_capitals.fund_storage_id', $id)
            ->whereDate('partner_capitals.created_at', '>=', $dateFrom)
            ->whereDate('partner_capitals.created_at', '<=', $dateTo)
            ->select(
                'partner_capitals.id',
                'partner_capitals.amount',
                'partner_capitals.description',
                'partner_capitals.created_at',
                'paylira_partners.name as partner_name',
                'users.name as created_by_name'
            )
            ->get();

        foreach ($capitals as $c) {
            $movements->push([
                'id'         => 'pc_' . $c->id,
                'direction'  => 'in',
                'source'     => 'Sermaye Ekleme',
                'target'     => $c->partner_name,
                'amount'     => (float) $c->amount,
                'description'=> $c->description,
                'created_by' => $c->created_by_name,
                'created_at' => $c->created_at,
            ]);
        }

        // 6. Fon transferleri (giden)
        $transfersOut = DB::table('fund_transfers')
            ->leftJoin('users', 'fund_transfers.created_by', '=', 'users.id')
            ->where('from_storage_id', $id)
            ->whereDate('fund_transfers.created_at', '>=', $dateFrom)
            ->whereDate('fund_transfers.created_at', '<=', $dateTo)
            ->select('fund_transfers.*', 'users.name as created_by_name')
            ->get();

        foreach ($transfersOut as $t) {
            $toName = DB::table('fund_storages')->where('id', $t->to_storage_id)->value('name');
            $movements->push([
                'id'         => 'fto_' . $t->id,
                'direction'  => 'out',
                'source'     => 'Transfer (Giden)',
                'target'     => $toName,
                'amount'     => (float) $t->amount,
                'description'=> $t->description . ($t->commission_amount > 0 ? ' (Komisyon: ₺' . number_format($t->commission_amount, 2, ',', '.') . ')' : ''),
                'created_by' => $t->created_by_name,
                'created_at' => $t->created_at,
            ]);
        }

        // 7. Fon transferleri (gelen)
        $transfersIn = DB::table('fund_transfers')
            ->leftJoin('users', 'fund_transfers.created_by', '=', 'users.id')
            ->where('to_storage_id', $id)
            ->whereDate('fund_transfers.created_at', '>=', $dateFrom)
            ->whereDate('fund_transfers.created_at', '<=', $dateTo)
            ->select('fund_transfers.*', 'users.name as created_by_name')
            ->get();

        foreach ($transfersIn as $t) {
            $fromName = DB::table('fund_storages')->where('id', $t->from_storage_id)->value('name');
            $movements->push([
                'id'         => 'fti_' . $t->id,
                'direction'  => 'in',
                'source'     => 'Transfer (Gelen)',
                'target'     => $fromName,
                'amount'     => (float) $t->received_amount,
                'description'=> $t->description,
                'created_by' => $t->created_by_name,
                'created_at' => $t->created_at,
            ]);
        }

        // 8. Senkron hareketleri - GİRİŞ
        $syncs = DB::table('fund_storage_syncs')
            ->leftJoin('users', 'fund_storage_syncs.created_by', '=', 'users.id')
            ->where('fund_storage_syncs.fund_storage_id', $id)
            ->whereDate('fund_storage_syncs.created_at', '>=', $dateFrom)
            ->whereDate('fund_storage_syncs.created_at', '<=', $dateTo)
            ->select('fund_storage_syncs.id', 'fund_storage_syncs.amount', 'fund_storage_syncs.description', 'fund_storage_syncs.created_at', 'users.name as created_by_name')
            ->get();

        foreach ($syncs as $s) {
            $movements->push([
                'id'         => 'sync_' . $s->id,
                'direction'  => (float) $s->amount >= 0 ? 'in' : 'out',
                'source'     => 'Senkron',
                'target'     => null,
                'amount'     => abs((float) $s->amount),
                'description'=> $s->description,
                'created_by' => $s->created_by_name,
                'created_at' => $s->created_at,
            ]);
        }

        // 9. Paylira masrafları (depodan düşülen) - ÇIKIŞ
        $expensesFromStorage = DB::table('paylira_expenses')
            ->leftJoin('users', 'paylira_expenses.created_by', '=', 'users.id')
            ->where('paylira_expenses.fund_storage_id', $id)
            ->whereDate('paylira_expenses.created_at', '>=', $dateFrom)
            ->whereDate('paylira_expenses.created_at', '<=', $dateTo)
            ->select('paylira_expenses.id', 'paylira_expenses.amount', 'paylira_expenses.description', 'paylira_expenses.created_at', 'users.name as created_by_name')
            ->get();

        foreach ($expensesFromStorage as $e) {
            $movements->push([
                'id'         => 'pex_' . $e->id,
                'direction'  => 'out',
                'source'     => 'Paylira Masraf',
                'target'     => null,
                'amount'     => (float) $e->amount,
                'description'=> $e->description,
                'created_by' => $e->created_by_name,
                'created_at' => $e->created_at,
            ]);
        }

        // Hareketler içinde EN ESKİ tarihi bul (varsa)
        $earliestMoveDate = $movements->min('created_at');
        $earliestMoveDay = $earliestMoveDate ? substr($earliestMoveDate, 0, 10) : null;

        // Anchor tarihi: ilk hareketten strictly önceki (yoksa dateFrom'dan strictly önceki)
        // Bu sayede initial_balance snapshot'ları (Mar 15 gibi) doğru baz alınır
        $anchorBefore = $earliestMoveDay ?: $dateFrom;

        $latestSnap = DB::table('daily_case_snapshots')
            ->where('entity_type', 'fund_storage')
            ->where('entity_id', $id)
            ->where('snapshot_date', '<', $anchorBefore)
            ->orderByDesc('snapshot_date')
            ->first();

        $startBalance = $latestSnap ? (float) $latestSnap->amount : 0;
        $snapDate = $latestSnap ? $latestSnap->snapshot_date : null;

        // Snapshot ile dateFrom arasındaki hareketleri başlangıca uygula
        $afterClause = function ($q) use ($snapDate) {
            if ($snapDate) $q->whereDate('created_at', '>', $snapDate);
        };

        $startBalance += (float) DB::table('merchant_payments')->where('fund_storage_id', $id)->whereDate('created_at', '<', $dateFrom)->where($afterClause)->sum('delivery_profit');
        $startBalance += (float) DB::table('team_payments')->where('fund_storage_id', $id)->whereDate('created_at', '<', $dateFrom)->where($afterClause)->sum('amount');
        $startBalance += (float) DB::table('partner_capitals')->where('fund_storage_id', $id)->whereDate('created_at', '<', $dateFrom)->where($afterClause)->sum('amount');
        $startBalance += (float) DB::table('fund_transfers')->where('to_storage_id', $id)->whereDate('created_at', '<', $dateFrom)->where($afterClause)->sum('received_amount');

        $startBalance -= (float) DB::table('merchant_payments')->where('fund_storage_id', $id)->whereDate('created_at', '<', $dateFrom)->where($afterClause)->sum('amount');
        $startBalance -= (float) DB::table('intermediary_payments')->where('fund_storage_id', $id)->whereDate('created_at', '<', $dateFrom)->where($afterClause)->sum('amount');
        $startBalance -= (float) DB::table('paylira_partner_payments')->where('fund_storage_id', $id)->whereDate('created_at', '<', $dateFrom)->where($afterClause)->sum('amount');
        $startBalance -= (float) DB::table('fund_transfers')->where('from_storage_id', $id)->whereDate('created_at', '<', $dateFrom)->where($afterClause)->sum('amount');
        $startBalance -= (float) DB::table('paylira_expenses')->where('fund_storage_id', $id)->whereDate('created_at', '<', $dateFrom)->where($afterClause)->sum('amount');
        $startBalance += (float) DB::table('fund_storage_syncs')->where('fund_storage_id', $id)->whereDate('created_at', '<', $dateFrom)->where($afterClause)->sum('amount');

        // Hareketleri eskiden yeniye sırala, before/after hesapla
        $asc = $movements->sortBy('created_at')->values();
        $running = $startBalance;
        $withBalance = $asc->map(function ($m) use (&$running) {
            $before = $running;
            $running += $m['direction'] === 'in' ? $m['amount'] : -$m['amount'];
            $m['balance_before'] = round($before, 2);
            $m['balance_after']  = round($running, 2);
            return $m;
        });

        $sorted = $withBalance->sortByDesc('created_at')->values();
        $totalIn = $sorted->where('direction', 'in')->sum('amount');
        $totalOut = $sorted->where('direction', 'out')->sum('amount');

        return response()->json([
            'storage'   => $storage,
            'movements' => $sorted,
            'summary'   => [
                'total_in'      => round($totalIn, 2),
                'total_out'     => round($totalOut, 2),
                'net'           => round($totalIn - $totalOut, 2),
                'start_balance' => round($startBalance, 2),
                'end_balance'   => round($running, 2),
            ],
        ]);
    }

    public function transfers(Request $request): JsonResponse
    {
        $dateFrom = $request->get('date_from');
        $dateTo = $request->get('date_to');

        $query = DB::table('fund_transfers')
            ->leftJoin('users', 'fund_transfers.created_by', '=', 'users.id')
            ->select('fund_transfers.*', 'users.name as created_by_name')
            ->orderByDesc('fund_transfers.created_at');

        if ($dateFrom && $dateTo) {
            $query->whereDate('fund_transfers.created_at', '>=', $dateFrom)
                  ->whereDate('fund_transfers.created_at', '<=', $dateTo);
        }

        $transfers = $query->limit(200)->get()->map(function ($t) {
            $t->from_name = DB::table('fund_storages')->where('id', $t->from_storage_id)->value('name');
            $t->to_name = DB::table('fund_storages')->where('id', $t->to_storage_id)->value('name');
            return $t;
        });

        return response()->json(['transfers' => $transfers]);
    }

    public function createTransfer(Request $request): JsonResponse
    {
        $request->validate([
            'from_storage_id' => 'required|integer|different:to_storage_id',
            'to_storage_id'   => 'required|integer',
            'amount'          => 'required|numeric|min:0.01',
            'commission_rate' => 'nullable|numeric|min:0|max:100',
            'description'     => 'nullable|string|max:500',
            'transfer_date'   => 'nullable|date',
        ]);

        $fromStorage = DB::table('fund_storages')->where('id', $request->from_storage_id)->first();
        $toStorage = DB::table('fund_storages')->where('id', $request->to_storage_id)->first();

        if (!$fromStorage || !$toStorage) {
            return response()->json(['message' => 'Fon deposu bulunamadı.'], 404);
        }

        if ((float) $fromStorage->balance < (float) $request->amount) {
            return response()->json(['message' => 'Kaynak depoda yeterli bakiye yok. Mevcut: ₺' . number_format($fromStorage->balance, 2, ',', '.')], 422);
        }

        // Komisyon hesabı (sadece nakit/alacak kaynaktan kripto iç kaynağa transferde)
        $commissionRate = (float) ($request->commission_rate ?? 0);
        $isExternalToInternal = ($fromStorage->type != 2 && $toStorage->type == 2);
        $commissionAmount = $isExternalToInternal ? round($request->amount * $commissionRate / 100, 2) : 0;
        $receivedAmount = round($request->amount - $commissionAmount, 2);

        DB::table('fund_transfers')->insert([
            'from_storage_id'   => $request->from_storage_id,
            'to_storage_id'     => $request->to_storage_id,
            'amount'            => $request->amount,
            'commission_rate'   => $commissionRate,
            'commission_amount' => $commissionAmount,
            'received_amount'   => $receivedAmount,
            'description'       => $request->description,
            'created_by'        => auth()->id(),
            'created_at'        => $request->transfer_date ? $request->transfer_date . ' ' . now()->format('H:i:s') : now(),
        ]);

        // Bakiyeleri güncelle
        DB::table('fund_storages')->where('id', $request->from_storage_id)->decrement('balance', $request->amount);
        DB::table('fund_storages')->where('id', $request->to_storage_id)->increment('balance', $receivedAmount);

        return response()->json(['message' => 'Transfer tamamlandı.']);
    }

    public function deleteTransfer(int $id): JsonResponse
    {
        $transfer = DB::table('fund_transfers')->where('id', $id)->first();
        if (!$transfer) {
            return response()->json(['message' => 'Transfer bulunamadı.'], 404);
        }

        $today = now()->toDateString();
        if (date('Y-m-d', strtotime($transfer->created_at)) < $today) {
            return response()->json(['message' => 'Sadece bugüne ait transferler silinebilir.'], 422);
        }

        // Bakiyeleri geri al
        DB::table('fund_storages')->where('id', $transfer->from_storage_id)->increment('balance', $transfer->amount);
        DB::table('fund_storages')->where('id', $transfer->to_storage_id)->decrement('balance', $transfer->received_amount);

        DB::table('fund_transfers')->where('id', $id)->delete();

        return response()->json(['message' => 'Transfer silindi.']);
    }

    public function addSync(Request $request): JsonResponse
    {
        // Sadece Super Admin (1) ve Sub Admin (4)
        if (! auth()->user()->isAdmin()) {
            return response()->json(['message' => 'Bu işlem için yetkiniz yok.'], 403);
        }

        $request->validate([
            'fund_storage_id' => 'required|integer',
            'amount'          => 'required|numeric',
            'description'     => 'nullable|string|max:1000',
            'sync_date'       => 'required|date',
        ]);

        $storage = DB::table('fund_storages')->where('id', $request->fund_storage_id)->first();
        if (! $storage) {
            return response()->json(['message' => 'Fon deposu bulunamadı.'], 404);
        }

        DB::table('fund_storage_syncs')->insert([
            'fund_storage_id' => $request->fund_storage_id,
            'amount'          => $request->amount,
            'description'     => $request->description,
            'created_by'      => auth()->id(),
            'created_at'      => $request->sync_date . ' ' . now()->format('H:i:s'),
        ]);

        DB::table('fund_storages')
            ->where('id', $request->fund_storage_id)
            ->increment('balance', $request->amount);

        return response()->json(['message' => 'Senkron eklendi.']);
    }

    public function deleteSync(int $id): JsonResponse
    {
        if (! auth()->user()->isAdmin()) {
            return response()->json(['message' => 'Bu işlem için yetkiniz yok.'], 403);
        }

        $sync = DB::table('fund_storage_syncs')->where('id', $id)->first();
        if (! $sync) {
            return response()->json(['message' => 'Senkron bulunamadı.'], 404);
        }

        DB::table('fund_storages')
            ->where('id', $sync->fund_storage_id)
            ->decrement('balance', $sync->amount);

        DB::table('fund_storage_syncs')->where('id', $id)->delete();

        return response()->json(['message' => 'Senkron silindi.']);
    }
}
