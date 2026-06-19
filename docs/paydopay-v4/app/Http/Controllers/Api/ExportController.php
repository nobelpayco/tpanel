<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use App\Jobs\ExportTransactionsJob;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Facades\Storage;
use Symfony\Component\HttpFoundation\BinaryFileResponse;

class ExportController extends Controller
{
    /**
     * Queue a new export job and return immediately.
     */
    public function create(Request $request): JsonResponse
    {
        $request->validate([
            'date_from' => 'required|date',
            'date_to'   => 'required|date|after_or_equal:date_from',
            'type'      => 'nullable|string|in:all,1,2',
            'merchant_id' => 'nullable|string',
            'team_id'   => 'nullable|integer',
            'status'    => 'nullable|string|in:all,0,1,2,3,4',
        ]);

        $pendingCount = DB::table('export_jobs')
            ->where('user_id', auth()->id())
            ->whereIn('status', ['pending', 'processing'])
            ->count();

        if ($pendingCount >= 2) {
            return response()->json(['message' => 'En fazla 2 rapor sıraya alınabilir. Lütfen mevcut raporların tamamlanmasını bekleyin.'], 422);
        }

        $filters = $request->only(['date_from', 'date_to', 'type', 'merchant_id', 'team_id', 'status']);

        // Merchant kullanıcısı sadece kendi merchant'larını export edebilir.
        // İstediği merchant_id ne olursa olsun override edilir.
        $user = $request->user();
        if ($user->hasMerchantScope()) {
            $filters['merchant_ids'] = $user->merchant_ids;
            unset($filters['merchant_id']);
        }
        // Team kullanıcısı sadece kendi takımının exportunu alabilir
        if ($user->hasTeamScope()) {
            $filters['team_id'] = $user->team_id;
        }

        $jobId = DB::table('export_jobs')->insertGetId([
            'user_id'    => auth()->id(),
            'status'     => 'pending',
            'filters'    => json_encode($filters),
            'created_at' => now(),
        ]);

        ExportTransactionsJob::dispatch($jobId);

        return response()->json([
            'success' => true,
            'job_id'  => $jobId,
            'message' => 'Export başlatıldı.',
        ]);
    }

    /**
     * List all exports for the current user (notification bell).
     */
    public function status(): JsonResponse
    {
        $exports = DB::table('export_jobs')
            ->where('user_id', auth()->id())
            ->orderByDesc('created_at')
            ->limit(20)
            ->get()
            ->map(function ($row) {
                $row->filters = json_decode($row->filters, true);
                return $row;
            });

        return response()->json($exports);
    }

    /**
     * Download a completed export file.
     */
    public function download(int $id, Request $request): JsonResponse|BinaryFileResponse
    {
        // Token auth for direct download links
        if ($token = $request->get('token')) {
            $personalToken = DB::table('personal_access_tokens')
                ->where('token', hash('sha256', explode('|', $token, 2)[1] ?? ''))
                ->first();
            if ($personalToken) {
                auth()->loginUsingId($personalToken->tokenable_id);
            }
        }

        $job = DB::table('export_jobs')
            ->where('id', $id)
            ->where('user_id', auth()->id())
            ->first();

        if (!$job) {
            return response()->json(['message' => 'Export bulunamadı.'], 404);
        }

        if ($job->status !== 'completed') {
            return response()->json(['message' => 'Export henüz hazır değil.'], 422);
        }

        $path = storage_path('app/exports/' . $job->filename);

        if (!file_exists($path)) {
            return response()->json(['message' => 'Dosya süresi dolmuş, lütfen tekrar export alın.'], 410);
        }

        return response()->download($path, $job->filename, [
            'Content-Type' => 'text/csv; charset=UTF-8',
        ]);
    }

    /**
     * Delete export files older than 30 minutes. Called by scheduler.
     */
    public function clear(): JsonResponse
    {
        $jobs = DB::table('export_jobs')
            ->where('user_id', auth()->id())
            ->get();

        foreach ($jobs as $job) {
            if ($job->filename) {
                $path = storage_path('app/exports/' . $job->filename);
                if (file_exists($path)) @unlink($path);
            }
        }

        DB::table('export_jobs')->where('user_id', auth()->id())->delete();

        return response()->json(['message' => 'Bildirimler temizlendi.']);
    }

    public static function cleanup(): void
    {
        $threshold = now()->subMinutes(30);

        // Get old completed/failed jobs
        $oldJobs = DB::table('export_jobs')
            ->where('created_at', '<', $threshold)
            ->get();

        foreach ($oldJobs as $job) {
            if ($job->filename) {
                $path = storage_path('app/exports/' . $job->filename);
                if (file_exists($path)) {
                    @unlink($path);
                }
            }
        }

        // Delete old records
        DB::table('export_jobs')
            ->where('created_at', '<', $threshold)
            ->delete();
    }
}
