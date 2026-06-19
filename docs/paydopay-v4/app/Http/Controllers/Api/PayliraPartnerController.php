<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;

class PayliraPartnerController extends Controller
{
    public function index(): JsonResponse
    {
        $today = now()->toDateString();

        // Paylira bugünkü net — bir kere hesapla
        $payliraLastSnap = (float) DB::table('daily_case_snapshots')
            ->where('entity_type', 'paylira')
            ->whereNull('entity_id')
            ->where('snapshot_date', '<', $today)
            ->orderByDesc('snapshot_date')
            ->value('amount') ?? 0;

        $payliraCurrent = $this->getPayliraCurrentNet($today, $payliraLastSnap);
        $todayNet = $payliraCurrent - $payliraLastSnap;

        $partners = DB::table('paylira_partners')->where('status', 1)->orderByDesc('id')->get()->map(function ($p) use ($today, $todayNet) {
            $lastSnap = (float) DB::table('daily_case_snapshots')
                ->where('entity_type', 'partner')
                ->where('entity_id', $p->id)
                ->where('snapshot_date', '<', $today)
                ->orderByDesc('snapshot_date')
                ->value('amount') ?? 0;

            $todayShare = $todayNet * $p->share_percent / 100;

            $todayPayments = (float) DB::table('paylira_partner_payments')
                ->where('partner_id', $p->id)
                ->whereDate('created_at', $today)
                ->sum('amount');

            $todayCapitals = (float) DB::table('partner_capitals')
                ->where('partner_id', $p->id)
                ->whereDate('created_at', $today)
                ->sum('amount');

            $todayExpenses = (float) DB::table('paylira_expense_shares')
                ->join('paylira_expenses', 'paylira_expense_shares.expense_id', '=', 'paylira_expenses.id')
                ->where('paylira_expense_shares.partner_id', $p->id)
                ->whereDate('paylira_expenses.created_at', $today)
                ->sum('paylira_expense_shares.amount');

            return [
                'id'            => $p->id,
                'name'          => $p->name,
                'share_percent' => $p->share_percent,
                'current_case'  => round($lastSnap + $todayShare + $todayCapitals - $todayPayments - $todayExpenses, 2),
            ];
        });

        return response()->json($partners);
    }

    public function show(int $id, \Illuminate\Http\Request $request): JsonResponse
    {
        $today = now()->toDateString();
        $dateFrom = $request->get('date_from');
        $dateTo = $request->get('date_to');
        $partner = DB::table('paylira_partners')->where('id', $id)->first();

        if (! $partner) {
            return response()->json(['message' => 'Ortak bulunamadı.'], 404);
        }

        // Gün sonu kasaları (bugün hariç)
        $snapshotQuery = DB::table('daily_case_snapshots')
            ->where('entity_type', 'partner')
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
                $payments = (float) DB::table('paylira_partner_payments')
                    ->where('partner_id', $id)
                    ->whereDate('created_at', $row->snapshot_date)
                    ->sum('amount');

                $capitals = (float) DB::table('partner_capitals')
                    ->where('partner_id', $id)
                    ->whereDate('created_at', $row->snapshot_date)
                    ->sum('amount');

                $expenses = (float) DB::table('paylira_expense_shares')
                    ->join('paylira_expenses', 'paylira_expense_shares.expense_id', '=', 'paylira_expenses.id')
                    ->where('paylira_expense_shares.partner_id', $id)
                    ->whereDate('paylira_expenses.created_at', $row->snapshot_date)
                    ->sum('paylira_expense_shares.amount');

                return [
                    'date'             => $row->snapshot_date,
                    'amount'           => $row->amount,
                    'previous_balance' => $details['previous_balance'] ?? 0,
                    'daily_share'      => $details['daily_share'] ?? 0,
                    'capitals'         => round($capitals, 2),
                    'expenses'         => round($expenses, 2),
                    'payments'         => round($payments, 2),
                ];
            });

        // Bugünkü canlı satır
        $lastSnap = (float) DB::table('daily_case_snapshots')
            ->where('entity_type', 'partner')
            ->where('entity_id', $id)
            ->where('snapshot_date', '<', $today)
            ->orderByDesc('snapshot_date')
            ->value('amount') ?? 0;

        $payliraLastSnap = (float) DB::table('daily_case_snapshots')
            ->where('entity_type', 'paylira')
            ->whereNull('entity_id')
            ->where('snapshot_date', '<', $today)
            ->orderByDesc('snapshot_date')
            ->value('amount') ?? 0;

        $payliraCurrent = $this->getPayliraCurrentNet($today, $payliraLastSnap);
        $todayNet = $payliraCurrent - $payliraLastSnap;
        $todayShare = $todayNet * $partner->share_percent / 100;

        $todayPayments = (float) DB::table('paylira_partner_payments')
            ->where('partner_id', $id)
            ->whereDate('created_at', $today)
            ->sum('amount');

        $todayCapitals = (float) DB::table('partner_capitals')
            ->where('partner_id', $id)
            ->whereDate('created_at', $today)
            ->sum('amount');

        $todayExpenses = (float) DB::table('paylira_expense_shares')
            ->join('paylira_expenses', 'paylira_expense_shares.expense_id', '=', 'paylira_expenses.id')
            ->where('paylira_expense_shares.partner_id', $id)
            ->whereDate('paylira_expenses.created_at', $today)
            ->sum('paylira_expense_shares.amount');

        $currentCase = $lastSnap + $todayShare + $todayCapitals - $todayPayments - $todayExpenses;

        $todayRow = [
            'date'             => $today,
            'amount'           => round($currentCase, 2),
            'previous_balance' => round($lastSnap, 2),
            'daily_share'      => round($todayShare, 2),
            'capitals'         => round($todayCapitals, 2),
            'expenses'         => round($todayExpenses, 2),
            'payments'         => round($todayPayments, 2),
            'is_today'         => true,
        ];

        $dailyCases->prepend($todayRow);

        return response()->json([
            'partner'      => $partner,
            'current_case' => round($currentCase, 2),
            'daily_cases'  => $dailyCases,
        ]);
    }

    public function payments(int $id, Request $request): JsonResponse
    {
        $date = $request->get('date');
        $dateFrom = $request->get('date_from');
        $dateTo = $request->get('date_to');

        // Normal ödemeler
        $paymentsQuery = DB::table('paylira_partner_payments')
            ->leftJoin('users', 'paylira_partner_payments.created_by', '=', 'users.id')
            ->where('partner_id', $id)
            ->select('paylira_partner_payments.*', 'users.name as created_by_name')
            ->orderByDesc('paylira_partner_payments.created_at');

        if ($date) {
            $paymentsQuery->whereDate('paylira_partner_payments.created_at', $date);
        } elseif ($dateFrom && $dateTo) {
            $paymentsQuery->whereDate('paylira_partner_payments.created_at', '>=', $dateFrom)
                          ->whereDate('paylira_partner_payments.created_at', '<=', $dateTo);
        }

        $payments = $paymentsQuery->limit(200)->get()->map(function ($p) {
            $sourceName = null;
            $sourceType = null;
            if ($p->payment_type == '2' && $p->fund_storage_id) {
                $sourceName = DB::table('fund_storages')->where('id', $p->fund_storage_id)->value('name');
                $sourceType = 'fund_storage';
            } elseif ($p->payment_type == '3' && $p->team_id) {
                $sourceName = DB::table('teams')->where('id', $p->team_id)->value('name');
                $sourceType = 'team';
            }
            return [
                'id'              => $p->id,
                'is_expense'      => false,
                'is_capital'      => (bool) ($p->is_capital ?? false),
                'payment_type'    => (int) $p->payment_type,
                'amount'          => (float) $p->amount,
                'crypto_quantity' => $p->crypto_quantity,
                'crypto_rate'     => $p->crypto_rate,
                'tx_link'         => $p->tx_link,
                'source_type'     => $sourceType,
                'source_name'     => $sourceName,
                'created_by_name' => $p->created_by_name,
                'description'     => $p->description,
                'created_at'      => $p->created_at,
            ];
        });

        // Masraf payları
        $expensesQuery = DB::table('paylira_expense_shares')
            ->join('paylira_expenses', 'paylira_expense_shares.expense_id', '=', 'paylira_expenses.id')
            ->leftJoin('teams', 'paylira_expenses.team_id', '=', 'teams.id')
            ->leftJoin('users', 'paylira_expenses.created_by', '=', 'users.id')
            ->where('paylira_expense_shares.partner_id', $id)
            ->select(
                'paylira_expense_shares.id',
                'paylira_expense_shares.amount',
                'paylira_expenses.description',
                'paylira_expenses.created_at',
                'paylira_expenses.id as expense_id',
                'teams.name as team_name',
                'users.name as created_by_name'
            )
            ->orderByDesc('paylira_expenses.created_at');

        if ($date) {
            $expensesQuery->whereDate('paylira_expenses.created_at', $date);
        } elseif ($dateFrom && $dateTo) {
            $expensesQuery->whereDate('paylira_expenses.created_at', '>=', $dateFrom)
                          ->whereDate('paylira_expenses.created_at', '<=', $dateTo);
        }

        $expenses = $expensesQuery->limit(200)->get()->map(function ($e) {
            return [
                'id'           => 'exp_' . $e->id,
                'is_expense'   => true,
                'source_type'  => $e->team_name ? 'team' : null,
                'source_name'  => $e->team_name,
                'payment_type' => 0,
                'amount'         => (float) $e->amount,
                'created_by_name' => $e->created_by_name,
                'description'    => $e->description,
                'created_at'     => $e->created_at,
            ];
        });

        // Partner transferleri (giden)
        $transferOutQuery = DB::table('partner_transfers')
            ->leftJoin('paylira_partners', 'partner_transfers.to_partner_id', '=', 'paylira_partners.id')
            ->leftJoin('users', 'partner_transfers.created_by', '=', 'users.id')
            ->where('partner_transfers.from_partner_id', $id)
            ->select('partner_transfers.*', 'paylira_partners.name as to_partner_name', 'users.name as created_by_name')
            ->orderByDesc('partner_transfers.created_at');
        if ($date) $transferOutQuery->whereDate('partner_transfers.created_at', $date);
        elseif ($dateFrom && $dateTo) $transferOutQuery->whereDate('partner_transfers.created_at', '>=', $dateFrom)->whereDate('partner_transfers.created_at', '<=', $dateTo);

        $transfersOut = $transferOutQuery->limit(200)->get()->map(function ($t) {
            return [
                'id'              => 'pto_' . $t->id,
                'is_expense'      => false,
                'is_capital'      => false,
                'source'          => 'partner_transfer_out',
                'payment_type'    => 0,
                'amount'          => (float) $t->amount,
                'to_partner_name' => $t->to_partner_name,
                'created_by_name' => $t->created_by_name,
                'description'     => $t->description,
                'created_at'      => $t->created_at,
            ];
        });

        // Partner transferleri (gelen)
        $transferInQuery = DB::table('partner_transfers')
            ->leftJoin('paylira_partners', 'partner_transfers.from_partner_id', '=', 'paylira_partners.id')
            ->leftJoin('users', 'partner_transfers.created_by', '=', 'users.id')
            ->where('partner_transfers.to_partner_id', $id)
            ->select('partner_transfers.*', 'paylira_partners.name as from_partner_name', 'users.name as created_by_name')
            ->orderByDesc('partner_transfers.created_at');
        if ($date) $transferInQuery->whereDate('partner_transfers.created_at', $date);
        elseif ($dateFrom && $dateTo) $transferInQuery->whereDate('partner_transfers.created_at', '>=', $dateFrom)->whereDate('partner_transfers.created_at', '<=', $dateTo);

        $transfersIn = $transferInQuery->limit(200)->get()->map(function ($t) {
            return [
                'id'                => 'pti_' . $t->id,
                'is_expense'        => false,
                'is_capital'        => false,
                'source'            => 'partner_transfer_in',
                'payment_type'      => 0,
                'amount'            => (float) $t->amount,
                'from_partner_name' => $t->from_partner_name,
                'created_by_name'   => $t->created_by_name,
                'description'       => $t->description,
                'created_at'        => $t->created_at,
            ];
        });

        // Sermaye girişleri
        $capitalsQuery = DB::table('partner_capitals')
            ->leftJoin('users', 'partner_capitals.created_by', '=', 'users.id')
            ->where('partner_capitals.partner_id', $id)
            ->select('partner_capitals.*', 'users.name as created_by_name')
            ->orderByDesc('partner_capitals.created_at');
        if ($date) $capitalsQuery->whereDate('partner_capitals.created_at', $date);
        elseif ($dateFrom && $dateTo) $capitalsQuery->whereDate('partner_capitals.created_at', '>=', $dateFrom)->whereDate('partner_capitals.created_at', '<=', $dateTo);

        $capitalsItems = $capitalsQuery->limit(200)->get()->map(function ($c) {
            return [
                'id'              => 'cap_' . $c->id,
                'is_expense'      => false,
                'is_capital'      => false,
                'source'          => 'capital',
                'payment_type'    => (int) $c->payment_type,
                'amount'          => (float) $c->amount,
                'crypto_quantity' => $c->crypto_quantity,
                'crypto_rate'     => $c->crypto_rate,
                'tx_link'         => $c->tx_link,
                'fund_storage_name' => $c->fund_storage_id
                    ? DB::table('fund_storages')->where('id', $c->fund_storage_id)->value('name')
                    : null,
                'created_by_name' => $c->created_by_name,
                'description'     => $c->description,
                'created_at'      => $c->created_at,
            ];
        });

        // Birleştir ve tarihe göre sırala
        $combined = $payments->concat($expenses)->concat($transfersOut)->concat($transfersIn)->concat($capitalsItems)->sortByDesc('created_at')->values();

        return response()->json([
            'payments' => $combined,
            'total'    => round($combined->sum('amount'), 2),
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
            'is_capital'      => 'nullable|boolean',
        ]);

        // Kripto ise fon deposu zorunlu
        if ($request->payment_type == 2 && !$request->fund_storage_id) {
            return response()->json(['message' => 'Kripto ödemede fon deposu seçimi zorunludur.'], 422);
        }

        // Takım ise takım seçimi zorunlu
        if ($request->payment_type == 3 && !$request->team_id) {
            return response()->json(['message' => 'Takım ödemede takım seçimi zorunludur.'], 422);
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

        DB::table('paylira_partner_payments')->insert([
            'partner_id'      => $id,
            'payment_type'    => $request->payment_type,
            'amount'          => $request->amount,
            'crypto_quantity' => $request->payment_type == 2 ? $request->crypto_quantity : null,
            'crypto_rate'     => $request->payment_type == 2 ? $request->crypto_rate : null,
            'tx_link'         => $request->payment_type == 2 ? $request->tx_link : null,
            'fund_storage_id' => $request->payment_type == 2 ? $request->fund_storage_id : null,
            'team_id'         => $request->payment_type == 3 ? $request->team_id : null,
            'is_capital'      => $request->boolean('is_capital') ? 1 : 0,
            'description'     => $request->description,
            'created_by'      => auth()->id(),
            'created_at'      => $request->payment_date ? $request->payment_date . ' ' . now()->format('H:i:s') : now(),
        ]);

        // Kripto ise fon deposundan düş
        if ($request->payment_type == 2 && $request->fund_storage_id) {
            DB::table('fund_storages')
                ->where('id', $request->fund_storage_id)
                ->decrement('balance', $request->amount);
        }

        // Takım ise takımdan düş
        if ($request->payment_type == 3 && $request->team_id) {
            DB::table('teams')
                ->where('id', $request->team_id)
                ->decrement('overturn', $request->amount);
        }

        return response()->json(['message' => 'Ödeme eklendi.']);
    }

    public function deletePayment(int $id, int $paymentId): JsonResponse
    {
        $payment = DB::table('paylira_partner_payments')
            ->where('id', $paymentId)->where('partner_id', $id)->first();

        if (! $payment) return response()->json(['message' => 'Ödeme bulunamadı.'], 404);

        $today = now()->toDateString();
        if (date('Y-m-d', strtotime($payment->created_at)) < $today) {
            return response()->json(['message' => 'Sadece bugüne ait ödemeler silinebilir.'], 422);
        }

        if ($payment->fund_storage_id) {
            DB::table('fund_storages')
                ->where('id', $payment->fund_storage_id)
                ->increment('balance', $payment->amount);
        }

        if ($payment->team_id) {
            DB::table('teams')
                ->where('id', $payment->team_id)
                ->increment('overturn', $payment->amount);
        }

        DB::table('paylira_partner_payments')->where('id', $paymentId)->delete();
        return response()->json(['message' => 'Ödeme silindi.']);
    }

    public function capitals(int $id, Request $request): JsonResponse
    {
        $dateFrom = $request->get('date_from');
        $dateTo = $request->get('date_to');

        $query = DB::table('partner_capitals')
            ->where('partner_id', $id)
            ->orderByDesc('created_at');

        if ($dateFrom && $dateTo) {
            $query->whereDate('created_at', '>=', $dateFrom)
                  ->whereDate('created_at', '<=', $dateTo);
        }

        $capitals = $query->limit(200)->get()->map(function ($c) {
            $c->fund_storage_name = $c->fund_storage_id
                ? DB::table('fund_storages')->where('id', $c->fund_storage_id)->value('name')
                : null;
            $c->partner_name = DB::table('paylira_partners')->where('id', $c->partner_id)->value('name');
            return $c;
        });

        return response()->json([
            'capitals' => $capitals,
            'total'    => round($capitals->sum('amount'), 2),
        ]);
    }

    public function addCapital(int $id, Request $request): JsonResponse
    {
        $request->validate([
            'payment_type'    => 'required|in:1,2',
            'amount'          => 'required|numeric|min:0.01',
            'crypto_quantity' => 'nullable|numeric|min:0',
            'crypto_rate'     => 'nullable|numeric|min:0',
            'tx_link'         => 'nullable|string|max:500',
            'fund_storage_id' => 'required|integer',
            'description'     => 'nullable|string|max:1000',
            'payment_date'    => 'nullable|date',
        ]);

        DB::table('partner_capitals')->insert([
            'partner_id'      => $id,
            'payment_type'    => $request->payment_type,
            'amount'          => $request->amount,
            'crypto_quantity' => $request->payment_type == 2 ? $request->crypto_quantity : null,
            'crypto_rate'     => $request->payment_type == 2 ? $request->crypto_rate : null,
            'tx_link'         => $request->payment_type == 2 ? $request->tx_link : null,
            'fund_storage_id' => $request->fund_storage_id,
            'description'     => $request->description,
            'created_by'      => auth()->id(),
            'created_at'      => $request->payment_date ? $request->payment_date . ' ' . now()->format('H:i:s') : now(),
        ]);

        // Fon deposuna ekle
        DB::table('fund_storages')
            ->where('id', $request->fund_storage_id)
            ->increment('balance', $request->amount);

        return response()->json(['message' => 'Sermaye eklendi.']);
    }

    public function deleteCapital(int $id, int $capitalId): JsonResponse
    {
        $capital = DB::table('partner_capitals')
            ->where('id', $capitalId)->where('partner_id', $id)->first();

        if (! $capital) return response()->json(['message' => 'Sermaye bulunamadı.'], 404);

        $today = now()->toDateString();
        if (date('Y-m-d', strtotime($capital->created_at)) < $today) {
            return response()->json(['message' => 'Sadece bugüne ait sermayeler silinebilir.'], 422);
        }

        // Fon deposundan düş
        if ($capital->fund_storage_id) {
            DB::table('fund_storages')
                ->where('id', $capital->fund_storage_id)
                ->decrement('balance', $capital->amount);
        }

        DB::table('partner_capitals')->where('id', $capitalId)->delete();
        return response()->json(['message' => 'Sermaye silindi.']);
    }

    // === Masraf Düşüm ===

    public function allPartnerPayments(Request $request): JsonResponse
    {
        $dateFrom = $request->get('date_from');
        $dateTo = $request->get('date_to');

        $query = DB::table('paylira_partner_payments')
            ->leftJoin('paylira_partners', 'paylira_partner_payments.partner_id', '=', 'paylira_partners.id')
            ->leftJoin('fund_storages', 'paylira_partner_payments.fund_storage_id', '=', 'fund_storages.id')
            ->leftJoin('teams', 'paylira_partner_payments.team_id', '=', 'teams.id')
            ->leftJoin('users', 'paylira_partner_payments.created_by', '=', 'users.id')
            ->orderByDesc('paylira_partner_payments.created_at');

        if ($dateFrom && $dateTo) {
            $query->whereDate('paylira_partner_payments.created_at', '>=', $dateFrom)
                  ->whereDate('paylira_partner_payments.created_at', '<=', $dateTo);
        }

        $rows = $query->select(
                'paylira_partner_payments.*',
                'paylira_partners.name as partner_name',
                'fund_storages.name as fund_storage_name',
                'teams.name as team_name',
                'users.name as created_by_name'
            )->limit(500)->get()->map(function ($p) {
                return [
                    'id'               => $p->id,
                    'partner_name'     => $p->partner_name,
                    'payment_type'     => (int) $p->payment_type,
                    'amount'           => (float) $p->amount,
                    'fund_storage_name'=> $p->fund_storage_name,
                    'team_name'        => $p->team_name,
                    'description'      => $p->description,
                    'created_by_name'  => $p->created_by_name,
                    'created_at'       => $p->created_at,
                ];
            });

        return response()->json(['payments' => $rows, 'total' => round($rows->sum('amount'), 2)]);
    }

    public function expenses(Request $request): JsonResponse
    {
        $dateFrom = $request->get('date_from');
        $dateTo = $request->get('date_to');

        $query = DB::table('paylira_expenses')
            ->orderByDesc('created_at');

        if ($dateFrom && $dateTo) {
            $query->whereDate('created_at', '>=', $dateFrom)
                  ->whereDate('created_at', '<=', $dateTo);
        }

        $expenses = $query->limit(200)->get()->map(function ($e) {
            $shares = DB::table('paylira_expense_shares')
                ->join('paylira_partners', 'paylira_expense_shares.partner_id', '=', 'paylira_partners.id')
                ->where('expense_id', $e->id)
                ->select('paylira_expense_shares.*', 'paylira_partners.name as partner_name')
                ->get();

            return [
                'id'          => $e->id,
                'amount'      => (float) $e->amount,
                'description' => $e->description,
                'created_at'  => $e->created_at,
                'shares'      => $shares,
            ];
        });

        return response()->json([
            'expenses' => $expenses,
            'total'    => round($expenses->sum('amount'), 2),
        ]);
    }

    public function addExpense(Request $request): JsonResponse
    {
        $request->validate([
            'amount'          => 'required|numeric|min:0.01',
            'description'     => 'nullable|string|max:1000',
            'team_id'         => 'nullable|integer',
            'fund_storage_id' => 'nullable|integer',
            'shares'          => 'required|array|min:1',
            'shares.*.partner_id' => 'required|integer',
            'shares.*.amount'     => 'required|numeric|min:0',
            'payment_date'    => 'nullable|date',
        ]);

        // Takım ya da depo zorunlu
        if (! $request->team_id && ! $request->fund_storage_id) {
            return response()->json(['message' => 'Takım ya da fon deposu seçimi zorunludur.'], 422);
        }

        $expenseId = DB::table('paylira_expenses')->insertGetId([
            'amount'          => $request->amount,
            'description'     => $request->description,
            'team_id'         => $request->team_id,
            'fund_storage_id' => $request->fund_storage_id,
            'created_by'      => auth()->id(),
            'created_at'      => $request->payment_date ? $request->payment_date . ' ' . now()->format('H:i:s') : now(),
        ]);

        // Takımdan düş
        if ($request->team_id) {
            DB::table('teams')
                ->where('id', $request->team_id)
                ->decrement('overturn', $request->amount);
        }

        // Depodan düş
        if ($request->fund_storage_id) {
            DB::table('fund_storages')
                ->where('id', $request->fund_storage_id)
                ->decrement('balance', $request->amount);
        }

        foreach ($request->shares as $share) {
            DB::table('paylira_expense_shares')->insert([
                'expense_id' => $expenseId,
                'partner_id' => $share['partner_id'],
                'amount'     => $share['amount'],
            ]);
        }

        return response()->json(['message' => 'Masraf eklendi.']);
    }

    public function deleteExpense(int $id): JsonResponse
    {
        $expense = DB::table('paylira_expenses')->where('id', $id)->first();
        if (! $expense) return response()->json(['message' => 'Masraf bulunamadı.'], 404);

        $today = now()->toDateString();
        if (date('Y-m-d', strtotime($expense->created_at)) < $today) {
            return response()->json(['message' => 'Sadece bugüne ait masraflar silinebilir.'], 422);
        }

        // Audit log — snapshot al + shares de dahil
        $shares = DB::table('paylira_expense_shares')->where('expense_id', $id)->get();
        $snapshot = (array) $expense;
        $snapshot['shares'] = $shares;
        $user = auth()->user();
        DB::table('team_action_log')->insert([
            'team_id'        => $expense->team_id,
            'entity_type'    => 'paylira_expense',
            'entity_id'      => (int) $expense->id,
            'action'         => 'delete',
            'amount'         => $expense->amount,
            'description'    => isset($expense->description) ? substr((string) $expense->description, 0, 1000) : null,
            'data_snapshot'  => json_encode($snapshot, JSON_UNESCAPED_UNICODE),
            'performed_by'   => $user?->id,
            'performer_name' => $user?->name ?: $user?->username,
            'performed_at'   => now(),
        ]);

        // Takıma geri ekle
        if ($expense->team_id) {
            DB::table('teams')
                ->where('id', $expense->team_id)
                ->increment('overturn', $expense->amount);
        }

        // Depoya geri ekle
        if ($expense->fund_storage_id) {
            DB::table('fund_storages')
                ->where('id', $expense->fund_storage_id)
                ->increment('balance', $expense->amount);
        }

        DB::table('paylira_expense_shares')->where('expense_id', $id)->delete();
        DB::table('paylira_expenses')->where('id', $id)->delete();

        return response()->json(['message' => 'Masraf silindi.']);
    }

    public function addPartnerTransfer(int $id, Request $request): JsonResponse
    {
        $request->validate([
            'to_partner_id' => 'required|integer',
            'amount'        => 'required|numeric|min:0.01',
            'description'   => 'nullable|string|max:1000',
            'payment_date'  => 'nullable|date',
        ]);

        if ($id == $request->to_partner_id) {
            return response()->json(['message' => 'Aynı partnere transfer yapılamaz.'], 422);
        }

        if (! DB::table('paylira_partners')->where('id', $request->to_partner_id)->exists()) {
            return response()->json(['message' => 'Hedef partner bulunamadı.'], 404);
        }

        DB::table('partner_transfers')->insert([
            'from_partner_id' => $id,
            'to_partner_id'   => $request->to_partner_id,
            'amount'          => $request->amount,
            'description'     => $request->description,
            'created_by'      => auth()->id(),
            'created_at'      => $request->payment_date ? $request->payment_date . ' ' . now()->format('H:i:s') : now(),
        ]);

        return response()->json(['message' => 'Partner transferi yapıldı.']);
    }

    public function deletePartnerTransfer(int $id, int $transferId): JsonResponse
    {
        $transfer = DB::table('partner_transfers')->where('id', $transferId)->where('from_partner_id', $id)->first();
        if (! $transfer) return response()->json(['message' => 'Transfer bulunamadı.'], 404);

        $today = now()->toDateString();
        if (date('Y-m-d', strtotime($transfer->created_at)) < $today) {
            return response()->json(['message' => 'Sadece bugüne ait transferler silinebilir.'], 422);
        }

        DB::table('partner_transfers')->where('id', $transferId)->delete();

        return response()->json(['message' => 'Transfer silindi.']);
    }

    private function getPayliraCurrentNet(string $today, float $previousSnap): float
    {
        $merchants = DB::table('merchantUser')->where('status', '1')
            ->select('id', 'commission', 'withdrawCommission')->get();

        $depComm = 0; $wdComm = 0; $delComm = 0; $delProfit = 0; $teamComm = 0;
        foreach ($merchants as $m) {
            $dep = (float) DB::table('invest')->where('firm_id', $m->id)->where('type', 1)->where('status', 3)->whereDate('finalize_date', $today)->sum('amount');
            $wd = (float) DB::table('invest')->where('firm_id', $m->id)->where('type', 2)->where('status', 3)->whereDate('finalize_date', $today)->sum('amount');
            $depComm += $dep * $m->commission / 100;
            $wdComm += $wd * $m->withdrawCommission / 100;
            $delComm += (float) DB::table('merchant_payments')->where('merchant_id', $m->id)->whereDate('created_at', $today)->sum('delivery_commission_amount');
            // Kripto teslimat karı — snapshot ve payliraDailyNet ile tutarlı olması için dahil
            $delProfit += (float) DB::table('merchant_payments')->where('merchant_id', $m->id)->whereDate('created_at', $today)->sum('delivery_profit');
            $tc = (float) DB::table('invest')->join('teams', 'invest.team_id', '=', 'teams.id')->where('invest.firm_id', $m->id)->where('invest.type', 1)->where('invest.status', 3)->whereDate('invest.finalize_date', $today)->selectRaw('SUM(invest.amount * teams.commission / 100) as total')->value('total') ?? 0;
            $teamComm += $tc;
        }

        $interComm = 0;
        $inters = DB::table('new_intermediaries')->where('status', 1)->get();

        // Sistem aracısı (type=3): tüm sistem cirosu × oran. Tek sorguyla toplam ciroyu çek.
        $totalSystemDeposits = null;
        foreach ($inters as $i) {
            if ((int) $i->type === 3) {
                if ($totalSystemDeposits === null) {
                    $totalSystemDeposits = (float) DB::table('invest')
                        ->where('type', 1)->where('status', 3)
                        ->whereDate('finalize_date', $today)
                        ->sum('amount');
                }
                $interComm += $totalSystemDeposits * (float) $i->commission_rate / 100;
                continue;
            }

            // Team-attached aracı
            $trels = DB::table('new_intermediary_team')->where('intermediary_id', $i->id)->where('status', 1)->get();
            foreach ($trels as $tr) {
                $d = (float) DB::table('invest')->where('team_id', $tr->team_id)->where('type', 1)->where('status', 3)->whereDate('finalize_date', $today)->sum('amount');
                $interComm += $d * $tr->commission_rate / 100;
            }

            // Merchant-attached aracı
            $mrels = DB::table('new_intermediary_merchant')->where('intermediary_id', $i->id)->where('status', 1)->get();
            foreach ($mrels as $mr) {
                $d = (float) DB::table('invest')->where('firm_id', $mr->merchant_id)->where('type', 1)->where('status', 3)->whereDate('finalize_date', $today)->sum('amount');
                $interComm += $d * $mr->commission_rate / 100;
            }
        }

        $todayNet = ($depComm + $wdComm + $delComm + $delProfit) - $teamComm - $interComm;
        return $previousSnap + $todayNet;
    }
}
