<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;

/**
 * Banka hesabı analiz raporu — verilen tarih aralığında her bir bankAccounts.id için
 * onay/red sayısı, son işlem zamanı ve hesap bilgilerini döndürür.
 * Çok sayıda red alan hesapları öne çıkarmak için rejected_count DESC sıralı.
 */
class BankAccountReportController extends Controller
{
    public function analysis(Request $request): JsonResponse
    {
        $request->validate([
            'date_from' => 'nullable|date',
            'date_to'   => 'nullable|date',
        ]);

        $dateFrom = $request->get('date_from') ?: now()->subDays(30)->format('Y-m-d H:i:s');
        $dateTo   = $request->get('date_to')   ?: now()->format('Y-m-d H:i:s');

        // Tarih sadece YYYY-MM-DD geldiyse, başlangıç=00:00, bitiş=23:59:59 olarak normalize et
        if (strlen($dateFrom) === 10) $dateFrom .= ' 00:00:00';
        if (strlen($dateTo) === 10)   $dateTo   .= ' 23:59:59';

        $rows = DB::table('invest as i')
            ->join('bankAccounts as ba', 'ba.id', '=', 'i.bank_id')
            ->leftJoin('banks as b', 'b.id', '=', 'ba.bank_id')
            ->leftJoin('teams as t', 't.id', '=', 'ba.team_id')
            ->where('i.type', 1)
            ->whereNotNull('i.bank_id')
            ->where('i.created_at', '>=', $dateFrom)
            ->where('i.created_at', '<=', $dateTo)
            ->groupBy('ba.id', 'ba.account_holder', 'ba.account_iban', 'ba.team_id', 'ba.bank_id', 'ba.status', 'b.name', 'b.logo', 'b.code', 't.name')
            ->select(
                'ba.id as account_id',
                'ba.account_holder',
                'ba.account_iban',
                'ba.team_id',
                'ba.status as account_status',
                'b.name as bank_name',
                'b.logo as bank_logo',
                'b.code as bank_code',
                't.name as team_name',
                DB::raw('SUM(CASE WHEN i.status = 3 THEN 1 ELSE 0 END) as approved_count'),
                DB::raw('SUM(CASE WHEN i.status = 4 THEN 1 ELSE 0 END) as rejected_count'),
                DB::raw('COUNT(*) as total_count'),
                DB::raw('SUM(CASE WHEN i.status = 3 THEN i.amount ELSE 0 END) as approved_amount'),
                DB::raw('SUM(CASE WHEN i.status = 4 THEN i.amount ELSE 0 END) as rejected_amount'),
                DB::raw('MAX(i.created_at) as last_transaction')
            )
            ->orderByDesc('rejected_count')
            ->orderBy('approved_count')
            ->get();

        $result = $rows->map(function ($r) {
            $total = (int) $r->total_count;
            $rejected = (int) $r->rejected_count;
            $approved = (int) $r->approved_count;
            return [
                'account_id'       => (int) $r->account_id,
                'account_holder'   => $r->account_holder,
                'account_iban'     => $r->account_iban,
                'team_id'          => $r->team_id,
                'team_name'        => $r->team_name,
                'account_status'   => (int) $r->account_status,
                'bank_name'        => $r->bank_name,
                'bank_logo'        => $r->bank_logo,
                'bank_code'        => $r->bank_code,
                'approved_count'   => $approved,
                'rejected_count'   => $rejected,
                'total_count'      => $total,
                'approved_amount'  => (float) $r->approved_amount,
                'rejected_amount'  => (float) $r->rejected_amount,
                'reject_ratio'     => $total > 0 ? round($rejected * 100 / $total, 1) : 0,
                'last_transaction' => $r->last_transaction,
            ];
        });

        return response()->json([
            'date_from' => $dateFrom,
            'date_to'   => $dateTo,
            'total_accounts' => $result->count(),
            'totals' => [
                'approved_count'  => (int) $result->sum('approved_count'),
                'rejected_count'  => (int) $result->sum('rejected_count'),
                'approved_amount' => (float) $result->sum('approved_amount'),
                'rejected_amount' => (float) $result->sum('rejected_amount'),
            ],
            'rows' => $result,
        ]);
    }
}
