<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;

class IntermediaryCaseController extends Controller
{
    public function index(): JsonResponse
    {
        $today = now()->toDateString();

        $intermediaries = DB::table('new_intermediaries')
            ->where('status', 1)
            ->select('id', 'name', 'type', 'balance')
            ->get()
            ->map(function ($i) use ($today) {
                $lastSnap = (float) DB::table('daily_case_snapshots')
                    ->where('entity_type', 'intermediary')
                    ->where('entity_id', $i->id)
                    ->where('snapshot_date', '<', $today)
                    ->orderByDesc('snapshot_date')
                    ->value('amount') ?? 0;

                // Bugünkü günlük komisyon
                $daily = $this->calcDailyCommission($i->id, $today);

                $todayPayments = (float) DB::table('intermediary_payments')
                    ->where('intermediary_id', $i->id)
                    ->whereDate('created_at', $today)
                    ->sum('amount');

                return [
                    'id'             => $i->id,
                    'name'           => $i->name,
                    'type'           => $i->type,
                    'current_case'   => round($lastSnap + $daily - $todayPayments, 2),
                ];
            });

        return response()->json($intermediaries);
    }

    public function show(int $id, \Illuminate\Http\Request $request): JsonResponse
    {
        $today = now()->toDateString();
        $dateFrom = $request->get('date_from');
        $dateTo = $request->get('date_to');

        $inter = DB::table('new_intermediaries')->where('id', $id)->first();
        if (! $inter) {
            return response()->json(['message' => 'Aracı bulunamadı.'], 404);
        }

        // Gün sonu kasaları (bugün hariç)
        $snapshotQuery = DB::table('daily_case_snapshots')
            ->where('entity_type', 'intermediary')
            ->where('entity_id', $id)
            ->where('snapshot_date', '<', $today)
            ->orderByDesc('snapshot_date');

        if ($dateFrom && $dateTo) {
            $snapshotQuery->where('snapshot_date', '>=', $dateFrom)->where('snapshot_date', '<=', $dateTo);
        } else {
            $snapshotQuery->limit(30);
        }

        $dailyCases = $snapshotQuery->get()
            ->map(function ($row) use ($id) {
                $details = json_decode($row->details, true) ?? [];

                $payments = (float) DB::table('intermediary_payments')
                    ->where('intermediary_id', $id)
                    ->whereDate('created_at', $row->snapshot_date)
                    ->sum('amount');

                return [
                    'date'               => $row->snapshot_date,
                    'amount'             => $row->amount,
                    'previous_balance'   => $details['previous_balance'] ?? 0,
                    'daily_commission'   => $details['daily_commission'] ?? 0,
                    'payments'           => round($payments, 2),
                ];
            });

        // Bugünkü canlı satır
        $lastSnap = (float) DB::table('daily_case_snapshots')
            ->where('entity_type', 'intermediary')
            ->where('entity_id', $id)
            ->where('snapshot_date', '<', $today)
            ->orderByDesc('snapshot_date')
            ->value('amount') ?? 0;

        $todayCommission = $this->calcDailyCommission($id, $today);

        $todayPayments = (float) DB::table('intermediary_payments')
            ->where('intermediary_id', $id)
            ->whereDate('created_at', $today)
            ->sum('amount');

        $currentCase = $lastSnap + $todayCommission - $todayPayments;

        $todayRow = [
            'date'             => $today,
            'amount'           => round($currentCase, 2),
            'previous_balance' => round($lastSnap, 2),
            'daily_commission' => round($todayCommission, 2),
            'payments'         => round($todayPayments, 2),
            'is_today'         => true,
        ];

        $dailyCases->prepend($todayRow);

        $totalPayments = (float) DB::table('intermediary_payments')
            ->where('intermediary_id', $id)
            ->sum('amount');

        // Mahsup için TÜM takımlar (silinmiş dahil) — yalnız bağlı olanlar değil
        $teams = DB::table('teams')
            ->select('id', 'name', 'status')
            ->orderBy('name')
            ->get();

        return response()->json([
            'intermediary'   => $inter,
            'current_case'   => round($currentCase, 2),
            'total_payments' => round($totalPayments, 2),
            'daily_cases'    => $dailyCases,
            'teams'          => $teams,
        ]);
    }

    public function payments(int $id, Request $request): JsonResponse
    {
        $date = $request->get('date');
        $dateFrom = $request->get('date_from');
        $dateTo = $request->get('date_to');

        $query = DB::table('intermediary_payments')
            ->where('intermediary_id', $id)
            ->orderByDesc('created_at');

        if ($date) {
            $query->whereDate('created_at', $date);
        } elseif ($dateFrom && $dateTo) {
            $query->whereDate('created_at', '>=', $dateFrom)
                  ->whereDate('created_at', '<=', $dateTo);
        }

        $payments = $query->limit(200)->get()->map(function ($p) {
            $teamName = null;
            if ($p->team_id) {
                $teamName = DB::table('teams')->where('id', $p->team_id)->value('name');
            }

            return [
                'id'              => $p->id,
                'payment_type'    => $p->payment_type,
                'amount'          => $p->amount,
                'crypto_quantity' => $p->crypto_quantity,
                'crypto_rate'     => $p->crypto_rate,
                'tx_link'         => $p->tx_link,
                'team_id'         => $p->team_id,
                'team_name'       => $teamName,
                'description'     => $p->description,
                'created_at'      => $p->created_at,
            ];
        });

        return response()->json([
            'payments' => $payments,
            'total'    => round($payments->sum('amount'), 2),
        ]);
    }

    public function addPayment(int $id, Request $request): JsonResponse
    {
        $request->validate([
            'payment_type'    => 'required|in:1,2,3',
            'amount'          => 'required|numeric|min:0.01',
            'crypto_quantity' => 'nullable|numeric|min:0',
            'crypto_rate'     => 'nullable|numeric|min:0',
            'tx_link'         => 'nullable|string|max:500',
            'fund_storage_id' => 'nullable|integer',
            'team_id'         => 'nullable|integer',
            'description'     => 'nullable|string|max:1000',
            'payment_date'    => 'nullable|date',
        ]);

        // Kripto ise fon deposu zorunlu
        if ($request->payment_type == 2 && !$request->fund_storage_id) {
            return response()->json(['message' => 'Kripto ödemede fon deposu seçimi zorunludur.'], 422);
        }

        // Fon deposu bakiye kontrolü
        if ($request->fund_storage_id) {
            $storage = DB::table('fund_storages')->where('id', $request->fund_storage_id)->first();
            if (!$storage) {
                return response()->json(['message' => 'Fon deposu bulunamadı.'], 404);
            }
            if ((float) $storage->balance < (float) $request->amount) {
                return response()->json(['message' => 'Depoda yeterli bakiye yok. Mevcut bakiye: ₺' . number_format($storage->balance, 2, ',', '.')], 422);
            }
        }

        $inter = DB::table('new_intermediaries')->where('id', $id)->first();
        if (! $inter) {
            return response()->json(['message' => 'Aracı bulunamadı.'], 404);
        }

        DB::table('intermediary_payments')->insert([
            'intermediary_id' => $id,
            'payment_type'    => $request->payment_type,
            'amount'          => $request->amount,
            'crypto_quantity' => $request->payment_type == 2 ? $request->crypto_quantity : null,
            'crypto_rate'     => $request->payment_type == 2 ? $request->crypto_rate : null,
            'tx_link'         => $request->payment_type == 2 ? $request->tx_link : null,
            'fund_storage_id' => $request->payment_type == 2 ? $request->fund_storage_id : null,
            'team_id'         => $request->payment_type == 3 ? $request->team_id : null,
            'description'     => $request->description,
            'created_by'      => auth()->id(),
            'created_at'      => $request->payment_date ? $request->payment_date . ' ' . now()->format('H:i:s') : now(),
        ]);

        // Aracı balance'dan düş
        DB::table('new_intermediaries')->where('id', $id)->decrement('balance', $request->amount);

        // Kripto ise fon deposundan düş
        if ($request->payment_type == 2 && $request->fund_storage_id) {
            DB::table('fund_storages')
                ->where('id', $request->fund_storage_id)
                ->decrement('balance', $request->amount);
        }

        // Grup alacak mahsubu ise takımın overturn'ünden de düş
        if ($request->payment_type == 3 && $request->team_id) {
            DB::table('teams')->where('id', $request->team_id)->decrement('overturn', $request->amount);
        }

        return response()->json(['message' => 'Ödeme eklendi.']);
    }

    public function deletePayment(int $id, int $paymentId): JsonResponse
    {
        $payment = DB::table('intermediary_payments')
            ->where('id', $paymentId)
            ->where('intermediary_id', $id)
            ->first();

        if (! $payment) {
            return response()->json(['message' => 'Ödeme bulunamadı.'], 404);
        }

        $today = now()->toDateString();
        $paymentDate = date('Y-m-d', strtotime($payment->created_at));

        if ($paymentDate < $today) {
            return response()->json(['message' => 'Sadece bugüne ait ödemeler silinebilir.'], 422);
        }

        // Balance'a geri ekle
        DB::table('new_intermediaries')->where('id', $id)->increment('balance', $payment->amount);

        // Kripto ise fon deposuna geri ekle
        if ($payment->fund_storage_id) {
            DB::table('fund_storages')
                ->where('id', $payment->fund_storage_id)
                ->increment('balance', $payment->amount);
        }

        // Mahsup ise takıma geri ekle
        if ($payment->payment_type == 3 && $payment->team_id) {
            DB::table('teams')->where('id', $payment->team_id)->increment('overturn', $payment->amount);
        }

        DB::table('intermediary_payments')->where('id', $paymentId)->delete();

        return response()->json(['message' => 'Ödeme silindi.']);
    }

    private function calcDailyCommission(int $intermediaryId, string $date): float
    {
        $total = 0;

        $teamRates = DB::table('new_intermediary_team')
            ->where('intermediary_id', $intermediaryId)
            ->where('status', 1)
            ->get();

        foreach ($teamRates as $tr) {
            $dep = (float) DB::table('invest')
                ->where('team_id', $tr->team_id)->where('type', 1)->where('status', 3)
                ->whereDate('finalize_date', $date)->sum('amount');
            $total += $dep * $tr->commission_rate / 100;
        }

        $merchantRates = DB::table('new_intermediary_merchant')
            ->where('intermediary_id', $intermediaryId)
            ->where('status', 1)
            ->get();

        foreach ($merchantRates as $mr) {
            $dep = (float) DB::table('invest')
                ->where('firm_id', $mr->merchant_id)->where('type', 1)->where('status', 3)
                ->whereDate('finalize_date', $date)->sum('amount');
            $total += $dep * $mr->commission_rate / 100;
        }

        return $total;
    }
}
