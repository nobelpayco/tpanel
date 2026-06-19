<?php

namespace App\Jobs;

use Illuminate\Bus\Queueable;
use Illuminate\Contracts\Queue\ShouldQueue;
use Illuminate\Foundation\Bus\Dispatchable;
use Illuminate\Queue\InteractsWithQueue;
use Illuminate\Queue\SerializesModels;
use Illuminate\Support\Carbon;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Log;

class ExportTransactionsJob implements ShouldQueue
{
    use Dispatchable, InteractsWithQueue, Queueable, SerializesModels;

    public int $tries = 1;
    public int $timeout = 300;

    public function __construct(
        protected int $exportJobId,
    ) {}

    public function handle(): void
    {
        $exportJob = DB::table('export_jobs')->where('id', $this->exportJobId)->first();

        if (!$exportJob) {
            return;
        }

        // Mark as processing
        DB::table('export_jobs')
            ->where('id', $this->exportJobId)
            ->update(['status' => 'processing']);

        try {
            $filters  = json_decode($exportJob->filters, true) ?? [];
            $filename = 'export_' . $this->exportJobId . '_' . now()->format('Ymd_His') . '.csv';
            $dir      = storage_path('app/exports');

            if (!is_dir($dir)) {
                mkdir($dir, 0755, true);
            }

            $filePath = $dir . '/' . $filename;
            $handle   = fopen($filePath, 'w');

            // UTF-8 BOM for Excel compatibility
            fwrite($handle, "\xEF\xBB\xBF");

            // Müşteri sütunu yalnızca admin (1) ve sub admin (4) için görünür;
            // team admin (5), team agent (2) ve merchant (3) bu sütunu görmemeli
            $ownerType = (int) (DB::table('users')->where('id', $exportJob->user_id)->value('user_type') ?? 0);
            $showMerchantCol = in_array($ownerType, [1, 4], true);

            // Header row
            $header = ['Id'];
            if ($showMerchantCol) $header[] = 'Musteri';
            $header = array_merge($header, [
                'Takim',
                'Islem ID',
                'Ad Soyad',
                'Tutar',
                'Hesap Sahibi',
                'Banka',
                'IBAN',
                'Durum',
                'Islem Tarihi',
                'Onay Tarihi',
            ]);
            fputcsv($handle, $header, ';');

            // Build query
            $query = DB::table('invest')
                ->leftJoin('merchantUser', 'invest.firm_id', '=', 'merchantUser.id')
                ->leftJoin('teams', 'invest.team_id', '=', 'teams.id')
                ->leftJoin('bankAccounts', 'invest.bank_id', '=', 'bankAccounts.id')
                ->leftJoin('banks', 'bankAccounts.bank_id', '=', 'banks.id')
                ->select([
                    'invest.id',
                    'merchantUser.name as merchant_name',
                    'teams.name as team_name',
                    'invest.order_id',
                    'invest.name as sender_name',
                    'invest.amount',
                    'bankAccounts.account_holder',
                    'banks.name as bank_name',
                    'invest.iban',
                    'invest.status',
                    'invest.created_at',
                    'invest.finalize_date',
                ]);

            // Date filter — finalize edilmişse finalize_date, edilmemişse created_at
            // (sonuçlanmış işlemler "ne zaman onaylandı/reddedildi", pending'ler "ne zaman oluştu" mantığı)
            if (!empty($filters['date_from'])) {
                $query->whereRaw('COALESCE(invest.finalize_date, invest.created_at) >= ?', [
                    Carbon::parse($filters['date_from'])->startOfDay(),
                ]);
            }
            if (!empty($filters['date_to'])) {
                $query->whereRaw('COALESCE(invest.finalize_date, invest.created_at) <= ?', [
                    Carbon::parse($filters['date_to'])->endOfDay(),
                ]);
            }

            // Type filter
            if (!empty($filters['type']) && $filters['type'] !== 'all') {
                $query->where('invest.type', $filters['type']);
            }

            // Status filter
            if (!empty($filters['status']) && $filters['status'] !== 'all') {
                $query->where('invest.status', $filters['status']);
            }

            // Merchant scope (Merchant kullanıcısı için zorlanmış — ExportController'da set edildi)
            if (!empty($filters['merchant_ids']) && is_array($filters['merchant_ids'])) {
                $query->whereIn('invest.firm_id', $filters['merchant_ids']);
            }
            // Admin/Team kullanıcısı için seçili merchant_id (supports 'g_' prefix for groups)
            elseif (!empty($filters['merchant_id'])) {
                $merchantParam = $filters['merchant_id'];

                if (str_starts_with($merchantParam, 'g_')) {
                    $groupId    = (int) substr($merchantParam, 2);
                    $merchantIds = DB::table('merchantUser')
                        ->where('group_id', $groupId)
                        ->pluck('id')
                        ->toArray();
                    $query->whereIn('invest.firm_id', $merchantIds);
                } else {
                    $query->where('invest.firm_id', (int) $merchantParam);
                }
            }

            // Team filter
            if (!empty($filters['team_id'])) {
                $query->where('invest.team_id', (int) $filters['team_id']);
            }

            $query->orderBy('invest.id', 'desc');

            // Status label map
            $statusLabels = [
                '0' => 'İptal',
                '1' => 'Beklemede',
                '2' => 'İşlemde',
                '3' => 'Onaylandı',
                '4' => 'Reddedildi',
            ];

            // Chunk to avoid memory issues
            $query->chunk(1000, function ($rows) use ($handle, $statusLabels, $showMerchantCol) {
                foreach ($rows as $row) {
                    $line = [$row->id];
                    if ($showMerchantCol) $line[] = $row->merchant_name ?? '';
                    $line = array_merge($line, [
                        $row->team_name ?? '',
                        $row->order_id ?? '',
                        $row->sender_name ?? '',
                        $row->amount ?? '0',
                        $row->account_holder ?? '',
                        $row->bank_name ?? '',
                        $row->iban ?? '',
                        $statusLabels[$row->status] ?? $row->status,
                        $row->created_at ?? '',
                        $row->finalize_date ?? '',
                    ]);
                    fputcsv($handle, $line, ';');
                }
            });

            fclose($handle);

            // Mark completed
            DB::table('export_jobs')
                ->where('id', $this->exportJobId)
                ->update([
                    'status'       => 'completed',
                    'filename'     => $filename,
                    'completed_at' => now(),
                ]);

        } catch (\Throwable $e) {
            Log::error('ExportTransactionsJob failed', [
                'job_id'  => $this->exportJobId,
                'message' => $e->getMessage(),
            ]);

            DB::table('export_jobs')
                ->where('id', $this->exportJobId)
                ->update(['status' => 'failed']);
        }
    }
}
