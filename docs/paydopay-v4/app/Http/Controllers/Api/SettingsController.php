<?php

namespace App\Http\Controllers\Api;

use App\Http\Controllers\Controller;
use Illuminate\Http\JsonResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Facades\DB;

class SettingsController extends Controller
{
    public function index(): JsonResponse
    {
        $settings = DB::table('system_settings')->pluck('value', 'key')->toArray();

        // anthropic_api_key maskele (key'in son 4 karakterini göster)
        if (! empty($settings['anthropic_api_key'])) {
            $k = $settings['anthropic_api_key'];
            $settings['anthropic_api_key_masked'] = strlen($k) > 10
                ? substr($k, 0, 7) . str_repeat('*', 8) . substr($k, -4)
                : str_repeat('*', strlen($k));
            $settings['anthropic_api_key_set'] = true;
            unset($settings['anthropic_api_key']);
        } else {
            $settings['anthropic_api_key_set'] = false;
        }

        // Bu ayki AI kullanım istatistiği (invest_receipts.verification_data JSON içinden _usage)
        $monthStart = now()->startOfMonth()->toDateTimeString();
        $rows = DB::table('invest_receipts')
            ->whereNotNull('verification_data')
            ->where('verified_at', '>=', $monthStart)
            ->select('verification_data')
            ->get();

        $totalIn = 0; $totalOut = 0; $totalCost = 0; $count = 0;
        foreach ($rows as $r) {
            $data = json_decode($r->verification_data, true);
            if (! is_array($data) || ! isset($data['_usage'])) continue;
            $count++;
            $in = (int) ($data['_usage']['input_tokens'] ?? 0);
            $out = (int) ($data['_usage']['output_tokens'] ?? 0);
            $totalIn += $in;
            $totalOut += $out;
            $totalCost += \App\Services\ClaudeVisionService::estimateCost($in, $out, $data['_usage']['model'] ?? null);
        }

        $settings['anthropic_usage'] = [
            'month'              => now()->format('Y-m'),
            'analysis_count'     => $count,
            'total_input_tokens' => $totalIn,
            'total_output_tokens'=> $totalOut,
            'estimated_cost_usd' => round($totalCost, 4),
            'current_model'      => \App\Services\ClaudeVisionService::model(),
        ];

        return response()->json($settings);
    }

    public function testAnthropic(): JsonResponse
    {
        $user = auth()->user();
        if (! $user->isSuperAdmin()) {
            abort(403, __('auth.no_permission'));
        }
        $res = \App\Services\ClaudeVisionService::ping();
        return response()->json($res, $res['ok'] ? 200 : 422);
    }

    public function analyzeTestReceipt(Request $request): JsonResponse
    {
        $user = auth()->user();
        if (! $user->canApproveTransactions()) {
            abort(403, __('auth.no_permission'));
        }

        $request->validate([
            'file_base64' => 'required|string',
            'file_name'   => 'required|string|max:255',
            'mime_type'   => 'required|string|in:application/pdf,image/jpeg,image/png,image/webp',
            'amount'      => 'nullable|numeric',
            'iban'        => 'nullable|string|max:50',
            'recipient'   => 'nullable|string|max:200',
        ]);

        $raw = $request->input('file_base64');
        if (str_contains($raw, ',')) $raw = substr($raw, strpos($raw, ',') + 1);
        $binary = base64_decode($raw, true);
        if ($binary === false) {
            return response()->json(['message' => 'Geçersiz base64.'], 422);
        }
        if (strlen($binary) > 10 * 1024 * 1024) {
            return response()->json(['message' => 'Dosya 10 MB\'ı aşamaz.'], 422);
        }

        $expected = [
            'amount'         => (float) ($request->input('amount') ?: 0),
            'iban'           => (string) $request->input('iban', ''),
            'recipient_name' => (string) $request->input('recipient', ''),
        ];

        $result = \App\Services\ClaudeVisionService::analyzeReceipt($binary, $request->input('mime_type'), $expected);
        if ($result === null) {
            return response()->json(['message' => 'AI analiz başarısız (API key veya bağlantı sorunu).'], 502);
        }

        $cost = isset($result['_usage'])
            ? \App\Services\ClaudeVisionService::estimateCost(
                (int) $result['_usage']['input_tokens'],
                (int) $result['_usage']['output_tokens'],
                $result['_usage']['model'] ?? null
            )
            : 0;

        return response()->json([
            'result' => $result,
            'estimated_cost_usd' => round($cost, 5),
        ]);
    }

    public function update(Request $request): JsonResponse
    {
        $request->validate([
            'settings' => 'required|array',
        ]);

        foreach ($request->settings as $key => $value) {
            DB::table('system_settings')->updateOrInsert(
                ['key' => $key],
                ['value' => (string) $value, 'updated_at' => now()]
            );
        }

        return response()->json(['message' => 'Ayarlar kaydedildi.']);
    }

    /** Tek bir setting'i okumak için yardımcı (komutlardan kullan) */
    public static function get(string $key, $default = null)
    {
        return DB::table('system_settings')->where('key', $key)->value('value') ?? $default;
    }

    /**
     * GET /api/settings/logs
     * api_callback_logs + apiRequestLog birleşik feed. Filtre: ?direction=in|out&type=...&q=...&page=...
     */
    public function logs(Request $request): JsonResponse
    {
        $page = max(1, (int) $request->get('page', 1));
        $perPage = 30;
        $offset = ($page - 1) * $perPage;

        $direction = $request->get('direction');
        $type = $request->get('type');
        $q = trim((string) $request->get('q'));

        $query = DB::table('api_callback_logs')
            ->leftJoin('invest', 'invest.id', '=', 'api_callback_logs.invest_id')
            ->leftJoin('merchantUser', 'merchantUser.id', '=', 'api_callback_logs.merchant_id')
            ->leftJoin('users', 'users.id', '=', 'api_callback_logs.triggered_by')
            ->select(
                'api_callback_logs.id',
                'api_callback_logs.direction',
                'api_callback_logs.type',
                'api_callback_logs.url',
                'api_callback_logs.response_status',
                'api_callback_logs.duration_ms',
                'api_callback_logs.error',
                'api_callback_logs.request_payload',
                'api_callback_logs.response_body',
                'api_callback_logs.created_at',
                'api_callback_logs.invest_id',
                'invest.order_id as invest_order_id',
                'merchantUser.name as merchant_name',
                'users.username as triggered_by_user',
            );

        if ($direction) $query->where('api_callback_logs.direction', $direction);
        if ($type) $query->where('api_callback_logs.type', $type);
        if ($q !== '') {
            $query->where(function ($q2) use ($q) {
                $q2->where('api_callback_logs.url', 'like', "%{$q}%")
                   ->orWhere('invest.order_id', 'like', "%{$q}%")
                   ->orWhere('merchantUser.name', 'like', "%{$q}%");
            });
        }

        $total = (clone $query)->count();
        $items = $query->orderByDesc('api_callback_logs.id')
            ->limit($perPage)->offset($offset)->get();

        return response()->json([
            'items'    => $items,
            'total'    => $total,
            'page'     => $page,
            'per_page' => $perPage,
            'pages'    => (int) ceil($total / $perPage),
        ]);
    }

    /**
     * GET /api/settings/logs/{id} — tek bir log entry'sinin tam body'sini döner
     */
    public function logDetail(int $id): JsonResponse
    {
        $log = DB::table('api_callback_logs')->where('id', $id)->first();
        if (! $log) return response()->json(['message' => 'Log bulunamadı.'], 404);
        return response()->json($log);
    }

    /**
     * POST /api/settings/telegram/find-chat-id
     * Body: { group_name }
     * Bot'un son güncellemelerinde geçen group/supergroup'lar arasından adı eşleşeni döndürür.
     *
     * Kullanım: Admin botu Telegram grubuna ekler → grupta herhangi bir mesaj yazılır (örn. /start) →
     * bu endpoint çağrılır → eşleşen chat_id döner. Admin manuel olarak set eder.
     */
    public function findChatId(Request $request): JsonResponse
    {
        $request->validate(['group_name' => 'required|string|max:255']);

        $needle = trim($request->group_name);

        $allChats = \Illuminate\Support\Facades\DB::table('telegram_chats')
            ->orderByDesc('last_seen_at')
            ->limit(200)
            ->get()
            ->map(fn ($c) => [
                'chat_id'      => (int) $c->chat_id,
                'title'        => $c->title,
                'type'         => $c->type,
                'username'     => $c->username,
                'last_seen_at' => $c->last_seen_at,
            ])
            ->all();

        $matches = $needle === ''
            ? []
            : array_values(array_filter($allChats, fn ($c) => $c['title'] && mb_stripos($c['title'], $needle) !== false));

        return response()->json([
            'matches'   => $matches,
            'all_chats' => $allChats,
            'hint'      => empty($allChats)
                ? 'Henüz hiçbir grup kaydedilmedi. Botu gruba ekleyin ve grupta bir mesaj yazın (örn. /start), sonra tekrar deneyin.'
                : null,
        ]);
    }
}
