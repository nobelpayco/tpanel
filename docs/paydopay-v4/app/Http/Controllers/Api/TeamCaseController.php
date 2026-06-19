<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;

class TeamCaseController extends Controller
{
    public function index(): JsonResponse
    {
        $teamRows = DB::table('teams')->select('id', 'name')->get();
        $cashes = app(\App\Services\MerchantBankService::class)
            ->currentCashForTeams($teamRows->pluck('id')->all());

        $teams = $teamRows->map(fn ($t) => [
                'id'           => (int) $t->id,
                'name'         => $t->name,
                'current_case' => round((float) ($cashes[(int) $t->id] ?? 0), 2),
            ])
            ->filter(fn ($t) => $t['current_case'] != 0)
            ->values();

        return response()->json([
            'teams'      => $teams,
            'total_case' => $teams->sum('current_case'),
        ]);
    }

    public function show(int $id, \Illuminate\Http\Request $request): JsonResponse
    {
        $today = now()->toDateString();
        $dateFrom = $request->get('date_from');
        $dateTo = $request->get('date_to');

        $team = DB::table('teams')->where('id', $id)->first();
        if (! $team) {
            return response()->json(['message' => 'Takım bulunamadı.'], 404);
        }

        // Gün sonu kasaları (bugün hariç)
        $snapshotQuery = DB::table('daily_case_snapshots')
            ->where('entity_type', 'team')
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

                $payments = (float) DB::table('team_payments')
                    ->where('team_id', $id)
                    ->whereDate('created_at', $row->snapshot_date)
                    ->sum('amount');
                $expenses = (float) DB::table('paylira_expenses')
                    ->where('team_id', $id)->whereDate('created_at', $row->snapshot_date)->sum('amount');
                $partnerPay = (float) DB::table('paylira_partner_payments')
                    ->where('team_id', $id)->where('payment_type', '3')
                    ->whereDate('created_at', $row->snapshot_date)->sum('amount');
                $interPay = (float) DB::table('intermediary_payments')
                    ->where('team_id', $id)->where('payment_type', '3')
                    ->whereDate('created_at', $row->snapshot_date)->sum('amount');
                $transferOut = (float) DB::table('team_transfers')
                    ->where('from_team_id', $id)->whereDate('created_at', $row->snapshot_date)->sum('amount');
                $transferIn = (float) DB::table('team_transfers')
                    ->where('to_team_id', $id)->whereDate('created_at', $row->snapshot_date)->sum('amount');
                $syncs = (float) DB::table('team_syncs')
                    ->where('team_id', $id)->whereDate('created_at', $row->snapshot_date)->sum('amount');

                return [
                    'date'             => $row->snapshot_date,
                    'amount'           => $row->amount,
                    'previous_balance' => $details['overturn'] ?? 0,
                    'deposits'         => $details['deposits'] ?? 0,
                    'withdrawals'      => $details['withdrawals'] ?? 0,
                    'team_commission'  => $details['team_commission'] ?? 0,
                    // Manuel Ödeme = ödeme+gider+partner+inter+transfer_out + POZİTİF sync (kasadan dağıtım)
                    'payments'         => round($payments + $expenses + $partnerPay + $interPay + $transferOut + max($syncs, 0), 2),
                    // Toplam Takviye = transfer_in + |NEGATİF sync| (kasaya nakit girişi)
                    'transfers_in'     => round($transferIn + max(-$syncs, 0), 2),
                ];
            });

        // Bugünkü canlı
        $lastSnap = (float) DB::table('daily_case_snapshots')
            ->where('entity_type', 'team')
            ->where('entity_id', $id)
            ->where('snapshot_date', '<', $today)
            ->orderByDesc('snapshot_date')
            ->value('amount') ?? $team->overturn;

        $todayDeposits = (float) DB::table('invest')
            ->where('team_id', $id)->where('type', '1')->where('status', '3')
            ->whereDate('finalize_date', $today)->sum('amount');

        $todayWithdrawals = (float) DB::table('invest')
            ->where('team_id', $id)->where('type', '2')->where('status', '3')
            ->whereDate('finalize_date', $today)->sum('amount');

        $todayPayments = (float) DB::table('team_payments')
            ->where('team_id', $id)
            ->whereDate('created_at', $today)
            ->sum('amount');

        $todayExpenses = (float) DB::table('paylira_expenses')
            ->where('team_id', $id)->whereDate('created_at', $today)->sum('amount');
        $todayPartnerPay = (float) DB::table('paylira_partner_payments')
            ->where('team_id', $id)->where('payment_type', '3')
            ->whereDate('created_at', $today)->sum('amount');
        $todayInterPay = (float) DB::table('intermediary_payments')
            ->where('team_id', $id)->where('payment_type', '3')
            ->whereDate('created_at', $today)->sum('amount');
        $todayTransferOut = (float) DB::table('team_transfers')
            ->where('from_team_id', $id)->whereDate('created_at', $today)->sum('amount');
        $todayTransferIn = (float) DB::table('team_transfers')
            ->where('to_team_id', $id)->whereDate('created_at', $today)->sum('amount');
        $todaySyncs = (float) DB::table('team_syncs')
            ->where('team_id', $id)->whereDate('created_at', $today)->sum('amount');

        $teamComm = $todayDeposits * $team->commission / 100;
        $currentCase = $lastSnap + $todayDeposits - $teamComm - $todayWithdrawals
            - $todayPayments - $todayExpenses - $todayPartnerPay - $todayInterPay
            - $todayTransferOut + $todayTransferIn - $todaySyncs;

        $todayRow = [
            'date'             => $today,
            'amount'           => round($currentCase, 2),
            'previous_balance' => round($lastSnap, 2),
            'deposits'         => round($todayDeposits, 2),
            'withdrawals'      => round($todayWithdrawals, 2),
            'team_commission'  => round($teamComm, 2),
            'payments'         => round($todayPayments + $todayExpenses + $todayPartnerPay + $todayInterPay + $todayTransferOut + max($todaySyncs, 0), 2),
            'transfers_in'     => round($todayTransferIn + max(-$todaySyncs, 0), 2),
            'is_today'         => true,
        ];

        $dailyCases->prepend($todayRow);

        $fundStorages = DB::table('fund_storages')->where('status', 1)->select('id', 'name', 'type')->orderBy('name')->get();

        return response()->json([
            'team'          => $team,
            'current_case'  => round($currentCase, 2),
            'daily_cases'   => $dailyCases,
            'fund_storages' => $fundStorages,
        ]);
    }

    public function payments(int $id, Request $request): JsonResponse
    {
        $date = $request->get('date');
        $dateFrom = $request->get('date_from');
        $dateTo = $request->get('date_to');

        $query = DB::table('team_payments')
            ->where('team_id', $id)
            ->orderByDesc('created_at');

        if ($date) {
            $query->whereDate('created_at', $date);
        } elseif ($dateFrom && $dateTo) {
            $query->whereDate('created_at', '>=', $dateFrom)
                  ->whereDate('created_at', '<=', $dateTo);
        }

        $payments = $query->limit(200)->get()->map(function ($p) {
            return [
                'id'              => $p->id,
                'source'          => 'team_payment',
                'payment_type'    => (int) $p->payment_type,
                'amount'          => (float) $p->amount,
                'crypto_quantity' => $p->crypto_quantity,
                'crypto_rate'     => $p->crypto_rate,
                'tx_link'         => $p->tx_link,
                'fund_storage_name' => $p->fund_storage_id
                    ? DB::table('fund_storages')->where('id', $p->fund_storage_id)->value('name')
                    : null,
                'description'     => $p->description,
                'created_at'      => $p->created_at,
            ];
        });

        // Bu takıma yönelik partner ödemeleri (payment_type=3)
        $partnerPayQuery = DB::table('paylira_partner_payments')
            ->join('paylira_partners', 'paylira_partner_payments.partner_id', '=', 'paylira_partners.id')
            ->where('paylira_partner_payments.team_id', $id)
            ->where('paylira_partner_payments.payment_type', '3')
            ->select(
                'paylira_partner_payments.id',
                'paylira_partner_payments.amount',
                'paylira_partner_payments.description',
                'paylira_partner_payments.created_at',
                'paylira_partners.name as partner_name'
            )
            ->orderByDesc('paylira_partner_payments.created_at');

        if ($date) {
            $partnerPayQuery->whereDate('paylira_partner_payments.created_at', $date);
        } elseif ($dateFrom && $dateTo) {
            $partnerPayQuery->whereDate('paylira_partner_payments.created_at', '>=', $dateFrom)
                            ->whereDate('paylira_partner_payments.created_at', '<=', $dateTo);
        }

        $partnerPayments = $partnerPayQuery->limit(200)->get()->map(function ($p) {
            return [
                'id'           => 'pp_' . $p->id,
                'source'       => 'partner_payment',
                'amount'       => (float) $p->amount,
                'partner_name' => $p->partner_name,
                'description'  => $p->description,
                'created_at'   => $p->created_at,
            ];
        });

        // Bu takımdan düşülen masraflar
        $expensesQuery = DB::table('paylira_expenses')
            ->where('team_id', $id)
            ->select('id', 'amount', 'description', 'created_at')
            ->orderByDesc('created_at');

        if ($date) {
            $expensesQuery->whereDate('created_at', $date);
        } elseif ($dateFrom && $dateTo) {
            $expensesQuery->whereDate('created_at', '>=', $dateFrom)
                          ->whereDate('created_at', '<=', $dateTo);
        }

        $expenses = $expensesQuery->limit(200)->get()->map(function ($e) {
            return [
                'id'          => 'exp_' . $e->id,
                'source'      => 'expense',
                'amount'      => (float) $e->amount,
                'description' => $e->description,
                'created_at'  => $e->created_at,
            ];
        });

        // Aracıdan grup mahsubu (intermediary_payments payment_type=3)
        $interOffsetQuery = DB::table('intermediary_payments')
            ->join('new_intermediaries', 'intermediary_payments.intermediary_id', '=', 'new_intermediaries.id')
            ->where('intermediary_payments.team_id', $id)
            ->where('intermediary_payments.payment_type', '3')
            ->select(
                'intermediary_payments.id',
                'intermediary_payments.amount',
                'intermediary_payments.description',
                'intermediary_payments.created_at',
                'new_intermediaries.name as intermediary_name'
            )
            ->orderByDesc('intermediary_payments.created_at');

        if ($date) {
            $interOffsetQuery->whereDate('intermediary_payments.created_at', $date);
        } elseif ($dateFrom && $dateTo) {
            $interOffsetQuery->whereDate('intermediary_payments.created_at', '>=', $dateFrom)
                             ->whereDate('intermediary_payments.created_at', '<=', $dateTo);
        }

        $interOffsets = $interOffsetQuery->limit(200)->get()->map(function ($i) {
            return [
                'id'                => 'io_' . $i->id,
                'source'            => 'intermediary_offset',
                'amount'            => (float) $i->amount,
                'intermediary_name' => $i->intermediary_name,
                'description'       => $i->description,
                'created_at'        => $i->created_at,
            ];
        });

        // Takım fon transferleri (giden)
        $transferOutQuery = DB::table('team_transfers')
            ->leftJoin('teams', 'team_transfers.to_team_id', '=', 'teams.id')
            ->where('team_transfers.from_team_id', $id)
            ->select('team_transfers.*', 'teams.name as to_team_name')
            ->orderByDesc('team_transfers.created_at');
        if ($date) $transferOutQuery->whereDate('team_transfers.created_at', $date);
        elseif ($dateFrom && $dateTo) $transferOutQuery->whereDate('team_transfers.created_at', '>=', $dateFrom)->whereDate('team_transfers.created_at', '<=', $dateTo);

        $transfersOut = $transferOutQuery->limit(200)->get()->map(function ($t) {
            return [
                'id'          => 'tto_' . $t->id,
                'source'      => 'team_transfer_out',
                'amount'      => (float) $t->amount,
                'to_team_name' => $t->to_team_name,
                'description' => $t->description,
                'created_at'  => $t->created_at,
            ];
        });

        // Takım fon transferleri (gelen)
        $transferInQuery = DB::table('team_transfers')
            ->leftJoin('teams', 'team_transfers.from_team_id', '=', 'teams.id')
            ->where('team_transfers.to_team_id', $id)
            ->select('team_transfers.*', 'teams.name as from_team_name')
            ->orderByDesc('team_transfers.created_at');
        if ($date) $transferInQuery->whereDate('team_transfers.created_at', $date);
        elseif ($dateFrom && $dateTo) $transferInQuery->whereDate('team_transfers.created_at', '>=', $dateFrom)->whereDate('team_transfers.created_at', '<=', $dateTo);

        $transfersIn = $transferInQuery->limit(200)->get()->map(function ($t) {
            return [
                'id'             => 'tti_' . $t->id,
                'source'         => 'team_transfer_in',
                'amount'         => (float) $t->amount,
                'from_team_name' => $t->from_team_name,
                'description'    => $t->description,
                'created_at'     => $t->created_at,
            ];
        });

        // Senkronlar
        $syncQuery = DB::table('team_syncs')
            ->leftJoin('users', 'team_syncs.created_by', '=', 'users.id')
            ->where('team_syncs.team_id', $id)
            ->select('team_syncs.*', 'users.name as created_by_name')
            ->orderByDesc('team_syncs.created_at');
        if ($date) $syncQuery->whereDate('team_syncs.created_at', $date);
        elseif ($dateFrom && $dateTo) $syncQuery->whereDate('team_syncs.created_at', '>=', $dateFrom)->whereDate('team_syncs.created_at', '<=', $dateTo);

        $syncs = $syncQuery->limit(200)->get()->map(function ($s) {
            return [
                'id'              => 'sync_' . $s->id,
                'source'          => 'team_sync',
                'amount'          => (float) $s->amount,
                'description'     => $s->description,
                'created_by_name' => $s->created_by_name,
                'created_at'      => $s->created_at,
            ];
        });

        $combined = $payments->concat($partnerPayments)->concat($expenses)->concat($interOffsets)
            ->concat($transfersOut)->concat($transfersIn)->concat($syncs)
            ->sortByDesc('created_at')->values();

        return response()->json([
            'payments' => $combined,
            'total'    => round($combined->sum('amount'), 2),
        ]);
    }

    public function addPayment(int $id, Request $request): JsonResponse
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

        DB::table('team_payments')->insert([
            'team_id'         => $id,
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

        // Overturn'den düş
        DB::table('teams')->where('id', $id)->decrement('overturn', $request->amount);

        // Fon kaynağına ekle
        DB::table('fund_storages')->where('id', $request->fund_storage_id)->increment('balance', $request->amount);

        app(\App\Services\MerchantBankService::class)->enforceMaxCase([$id]);

        return response()->json(['message' => 'Ödeme eklendi.']);
    }

    public function deletePayment(int $id, int $paymentId): JsonResponse
    {
        $payment = DB::table('team_payments')
            ->where('id', $paymentId)->where('team_id', $id)->first();

        if (! $payment) return response()->json(['message' => 'Ödeme bulunamadı.'], 404);

        $today = now()->toDateString();
        if (date('Y-m-d', strtotime($payment->created_at)) < $today) {
            return response()->json(['message' => 'Sadece bugüne ait ödemeler silinebilir.'], 422);
        }

        $this->logAction($id, 'team_payment', $payment);

        DB::table('teams')->where('id', $id)->increment('overturn', $payment->amount);

        // Fon kaynağından geri düş
        if ($payment->fund_storage_id) {
            DB::table('fund_storages')->where('id', $payment->fund_storage_id)->decrement('balance', $payment->amount);
        }

        DB::table('team_payments')->where('id', $paymentId)->delete();

        return response()->json(['message' => 'Ödeme silindi.']);
    }

    public function addTransfer(int $id, Request $request): JsonResponse
    {
        $request->validate([
            'to_team_id'   => 'required|integer|different:'.$id,
            'amount'       => 'required|numeric|min:0.01',
            'description'  => 'nullable|string|max:1000',
            'payment_date' => 'nullable|date',
        ]);

        if (! DB::table('teams')->where('id', $request->to_team_id)->exists()) {
            return response()->json(['message' => 'Hedef takım bulunamadı.'], 404);
        }

        DB::table('team_transfers')->insert([
            'from_team_id' => $id,
            'to_team_id'   => $request->to_team_id,
            'amount'       => $request->amount,
            'description'  => $request->description,
            'created_by'   => auth()->id(),
            'created_at'   => $request->payment_date ? $request->payment_date . ' ' . now()->format('H:i:s') : now(),
        ]);

        DB::table('teams')->where('id', $id)->decrement('overturn', $request->amount);
        DB::table('teams')->where('id', $request->to_team_id)->increment('overturn', $request->amount);

        app(\App\Services\MerchantBankService::class)->enforceMaxCase([$id, (int) $request->to_team_id]);

        return response()->json(['message' => 'Transfer yapıldı.']);
    }

    public function deleteTransfer(int $id, int $transferId): JsonResponse
    {
        $transfer = DB::table('team_transfers')->where('id', $transferId)->where('from_team_id', $id)->first();
        if (! $transfer) return response()->json(['message' => 'Transfer bulunamadı.'], 404);

        $today = now()->toDateString();
        if (date('Y-m-d', strtotime($transfer->created_at)) < $today) {
            return response()->json(['message' => 'Sadece bugüne ait transferler silinebilir.'], 422);
        }

        // Hem kaynak hem hedef takım için ayrı kayıt at
        $this->logAction((int) $transfer->from_team_id, 'team_transfer_out', $transfer);
        $this->logAction((int) $transfer->to_team_id, 'team_transfer_in', $transfer);

        DB::table('teams')->where('id', $transfer->from_team_id)->increment('overturn', $transfer->amount);
        DB::table('teams')->where('id', $transfer->to_team_id)->decrement('overturn', $transfer->amount);
        DB::table('team_transfers')->where('id', $transferId)->delete();

        return response()->json(['message' => 'Transfer silindi.']);
    }

    public function addSync(int $id, Request $request): JsonResponse
    {
        $request->validate([
            'amount'       => 'required|numeric|not_in:0',
            'description'  => 'required|string|max:1000',
            'payment_date' => 'nullable|date',
        ]);

        if (! DB::table('teams')->where('id', $id)->exists()) {
            return response()->json(['message' => 'Takım bulunamadı.'], 404);
        }

        DB::table('team_syncs')->insert([
            'team_id'     => $id,
            'amount'      => $request->amount,
            'description' => $request->description,
            'created_by'  => auth()->id(),
            'created_at'  => $request->payment_date ? $request->payment_date . ' ' . now()->format('H:i:s') : now(),
        ]);

        DB::table('teams')->where('id', $id)->decrement('overturn', $request->amount);

        app(\App\Services\MerchantBankService::class)->enforceMaxCase([$id]);

        return response()->json(['message' => 'Senkron eklendi.']);
    }

    public function deleteSync(int $id, int $syncId): JsonResponse
    {
        $sync = DB::table('team_syncs')->where('id', $syncId)->where('team_id', $id)->first();
        if (! $sync) return response()->json(['message' => 'Senkron bulunamadı.'], 404);

        $today = now()->toDateString();
        if (date('Y-m-d', strtotime($sync->created_at)) < $today) {
            return response()->json(['message' => 'Sadece bugüne ait senkronlar silinebilir.'], 422);
        }

        $this->logAction($id, 'team_sync', $sync);

        DB::table('teams')->where('id', $id)->increment('overturn', $sync->amount);
        DB::table('team_syncs')->where('id', $syncId)->delete();

        return response()->json(['message' => 'Senkron silindi.']);
    }

    /**
     * Silme öncesi audit log — team_action_log'a snapshot yazar.
     * Hangi kaydı kim sildi tespit edilebilsin.
     */
    private function logAction(?int $teamId, string $entityType, object $row): void
    {
        $user = auth()->user();
        DB::table('team_action_log')->insert([
            'team_id'        => $teamId,
            'entity_type'    => $entityType,
            'entity_id'      => (int) $row->id,
            'action'         => 'delete',
            'amount'         => $row->amount ?? null,
            'description'    => isset($row->description) ? substr((string) $row->description, 0, 1000) : null,
            'data_snapshot'  => json_encode($row, JSON_UNESCAPED_UNICODE),
            'performed_by'   => $user?->id,
            'performer_name' => $user?->name ?: $user?->username,
            'performed_at'   => now(),
        ]);
    }
}
