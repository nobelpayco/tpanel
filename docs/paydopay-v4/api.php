<?php

use Illuminate\Http\Request;
use App\Http\Controllers\Api\AuthController;
use App\Http\Controllers\Api\BankAccountController;
use App\Http\Controllers\Api\CaseReportController;
use App\Http\Controllers\Api\InitialBalanceController;
use App\Http\Controllers\Api\DepositController;
use App\Http\Controllers\Api\DashboardController;
use App\Http\Controllers\Api\FundStorageController;
use App\Http\Controllers\Api\IntermediaryController;
use App\Http\Controllers\Api\IntermediaryCaseController;
use App\Http\Controllers\Api\PayliraPartnerController;
use App\Http\Controllers\Api\TeamCaseController;
use App\Http\Controllers\Api\TeamController;
use App\Http\Controllers\Api\WithdrawController;
use App\Http\Controllers\Api\MerchantCaseController;
use App\Http\Controllers\Api\MerchantController;
use App\Http\Controllers\Api\BlacklistController;
use App\Http\Controllers\Api\ExportController;
use App\Http\Controllers\Api\MerchantReportController;
use App\Http\Controllers\Api\OperationsReportController;
use App\Http\Controllers\Api\PlayerRiskController;
use App\Http\Controllers\Api\TeamReportController;
use App\Http\Controllers\Api\ConversionReportController;
use App\Http\Controllers\Api\SettingsController;
use App\Http\Controllers\Api\UserController;
use App\Http\Controllers\Api\V1\DepositApiController;
use App\Http\Controllers\Api\V1\HealthController;
use App\Http\Controllers\Api\V1\PublicPaymentController;
use App\Http\Controllers\Api\TelegramWebhookController;
use App\Http\Controllers\Api\V1\TransactionApiController;
use App\Http\Controllers\Api\V1\WithdrawApiController;
use Illuminate\Support\Facades\Route;

// ===========================================================================
// Merchant API v1 — HMAC korumalı dış entegrasyon endpoint'leri
// ===========================================================================
// Public health check — auth gerekmez, monitör/uptime servisleri için
Route::get('/v1/health', [HealthController::class, 'index']);

// Telegram webhook (public — secret header ile korumalı)
Route::post('/telegram/webhook', [TelegramWebhookController::class, 'handle']);

Route::prefix('v1')->middleware(['api.locale.en', 'merchant.api', 'throttle:120,1'])->group(function () {
    Route::post('/deposit',                   [DepositApiController::class, 'store']);
    Route::post('/deposit/direct',            [DepositApiController::class, 'storeDirect']);
    Route::post('/withdraw',                  [WithdrawApiController::class, 'store']);
    Route::get ('/transaction/{order_id}',    [TransactionApiController::class, 'show']);
});

// Oyuncu ödeme sayfası için public endpoint'ler (u_id ile korunur)
// Locale TR kalır — mesajlar son kullanıcıya görünür.
Route::prefix('v1/pay')->middleware('throttle:30,1')->group(function () {
    Route::get ('/{u_id}',              [PublicPaymentController::class, 'show']);
    Route::post('/{u_id}/select-bank',  [PublicPaymentController::class, 'selectBank']);
    Route::post('/{u_id}/paid',         [PublicPaymentController::class, 'markPaid']);
    Route::post('/{u_id}/cancel',       [PublicPaymentController::class, 'cancel']);
    Route::post('/{u_id}/receipt',      [PublicPaymentController::class, 'uploadReceipt']);
});

// Eski merchant API'si — kalktı
$gone410 = fn () => response()->json([
    'code'    => 410,
    'status'  => false,
    'message' => 'Bu uç nokta kaldırıldı. Lütfen /api/v1/* endpoint\'lerini kullanın.',
], 410);
foreach ([
    'autoDeposit', 'whiteLabelDeposit', 'bankAccountList', 'paymentStatus',
    'autoWithdraw', 'mdtCallback', 'investComplete', 'sendUpdateAmount', 'getIban', 'retest',
] as $oldPath) {
    Route::any('/' . $oldPath, $gone410);
}
Route::any('/payDeposits/{any?}',     $gone410);
Route::any('/payDepositsNew/{any?}',  $gone410);
Route::any('/redirectWL/{any?}',      $gone410);
Route::any('/identyValidate/{any?}',  $gone410);

Route::prefix('auth')->group(function () {
    // Brute-force koruması: dakikada 10 deneme (IP başına)
    Route::post('/login',      [AuthController::class, 'login'])->middleware('throttle:10,1');
    Route::post('/two-factor', [AuthController::class, 'verifyTwoFactor'])->middleware('throttle:10,1');

    Route::middleware(['auth:sanctum', 'idle.timeout'])->group(function () {
        Route::post('/logout',          [AuthController::class, 'logout']);
        Route::get ('/me',              [AuthController::class, 'me']);
        Route::post('/change-password', [AuthController::class, 'changePassword'])->middleware('throttle:10,1');
    });
});

// Widget (public, token ile korumalı)
Route::get('/widget/{token}', function (Request $request, string $token) {
    if ($token !== 'paylira-w12-2026') {
        return response()->json(['error' => 'Unauthorized'], 401);
    }
    return app(DashboardController::class)->widget($request);
});

// Dashboard
Route::middleware(['auth:sanctum', 'idle.timeout'])->group(function () {
    Route::get('/dashboard/stats', [DashboardController::class, 'stats']);
    Route::get('/dashboard/merchant-cases', [DashboardController::class, 'merchantCases']);
    Route::get('/dashboard/yearly-volume', [DashboardController::class, 'yearlyVolume']);
    Route::get('/dashboard/recent-transactions', [DashboardController::class, 'recentTransactions']);
    Route::get('/dashboard/team-performance', [DashboardController::class, 'teamPerformance']);
    Route::get('/dashboard/player-stats/{playerId}', [DashboardController::class, 'playerStats']);
    Route::get('/dashboard/player-transactions/{playerId}', [DashboardController::class, 'playerTransactions']);
    Route::get('/dashboard/team-detail/{teamId}', [DashboardController::class, 'teamDetail']);

    // Merchant Raporları
    Route::get('/merchant-reports/filter-options', [MerchantReportController::class, 'filterOptions']);
    Route::get('/merchant-reports/volume-performance', [MerchantReportController::class, 'volumePerformance']);
    Route::get('/merchant-reports/player-analysis', [MerchantReportController::class, 'playerAnalysis']);
    Route::get('/merchant-reports/amount-analysis', [MerchantReportController::class, 'amountAnalysis']);
    Route::get('/merchant-reports/financial', [MerchantReportController::class, 'financialReport']);
    Route::get('/merchant-reports/risk', [MerchantReportController::class, 'riskReport']);

    // Oyuncu Risk Analizi
    Route::get('/player-risk/suspicious', [PlayerRiskController::class, 'suspiciousPlayers']);
    Route::get('/player-risk/segmentation', [PlayerRiskController::class, 'playerSegmentation']);
    Route::get('/player-risk/multi-name', [PlayerRiskController::class, 'multiNamePlayers']);

    // Operasyon Raporları
    Route::get('/operations/queue-analysis', [OperationsReportController::class, 'queueAnalysis']);
    Route::get('/operations/peak-hours', [OperationsReportController::class, 'peakHourAnalysis']);
    Route::get('/operations/sla', [OperationsReportController::class, 'slaReport']);

    // Takım Raporları
    Route::get('/team-reports/filter-options', [TeamReportController::class, 'filterOptions']);
    Route::get('/team-reports/overview', [TeamReportController::class, 'overview']);
    Route::get('/team-reports/trends', [TeamReportController::class, 'trends']);
    Route::get('/team-reports/hourly', [TeamReportController::class, 'hourly']);

    // Dönüşüm Oranı Raporu
    Route::get('/conversion-reports', [ConversionReportController::class, 'index']);

    // Kullanıcı Yönetimi
    Route::get('/users', [UserController::class, 'index']);
    Route::get('/users/options', [UserController::class, 'options']);
    Route::post('/users', [UserController::class, 'store']);
    Route::put('/users/{id}', [UserController::class, 'update']);
    Route::delete('/users/{id}', [UserController::class, 'destroy']);

    // Sistem Ayarları
    Route::get ('/settings',                       [SettingsController::class, 'index']);
    Route::put ('/settings',                       [SettingsController::class, 'update']);
    Route::post('/settings/telegram/find-chat-id', [SettingsController::class, 'findChatId']);
    Route::get('/settings/logs', [SettingsController::class, 'logs']);
    Route::get('/settings/logs/{id}', [SettingsController::class, 'logDetail']);

    // Kasa Raporu (sadece super admin — frontend + middleware kontrol eder)
    Route::get('/case-report', [CaseReportController::class, 'index']);
    Route::get('/case-report/summary', [CaseReportController::class, 'summary']);

    // Aracılar
    Route::get('/intermediaries', [IntermediaryController::class, 'index']);
    Route::post('/intermediaries', [IntermediaryController::class, 'store']);
    Route::put('/intermediaries/{id}', [IntermediaryController::class, 'update']);
    Route::delete('/intermediaries/{id}', [IntermediaryController::class, 'destroy']);
    Route::post('/intermediaries/attach-merchant', [IntermediaryController::class, 'attachMerchant']);
    Route::delete('/intermediaries/merchant/{pivotId}', [IntermediaryController::class, 'detachMerchant']);
    Route::put('/intermediaries/merchant/{pivotId}', [IntermediaryController::class, 'updateMerchantRate']);
    Route::post('/intermediaries/attach-team', [IntermediaryController::class, 'attachTeam']);
    Route::delete('/intermediaries/team/{pivotId}', [IntermediaryController::class, 'detachTeam']);
    Route::put('/intermediaries/team/{pivotId}', [IntermediaryController::class, 'updateTeamRate']);

    // Merchant Kasaları
    Route::get('/merchant-cases', [MerchantCaseController::class, 'index']);
    Route::get('/merchant-cases/paylira-daily-net', [MerchantCaseController::class, 'payliraDailyNet']);
    Route::get('/merchant-cases/{id}', [MerchantCaseController::class, 'show']);
    Route::get('/merchant-cases/{id}/payments', [MerchantCaseController::class, 'payments']);
    Route::post('/merchant-cases/{id}/payments', [MerchantCaseController::class, 'addPayment']);
    Route::delete('/merchant-cases/{id}/payments/{paymentId}', [MerchantCaseController::class, 'deletePayment']);

    // Takımlar
    Route::get('/teams', [TeamController::class, 'index']);
    Route::get('/teams/{id}', [TeamController::class, 'show']);
    Route::post('/teams', [TeamController::class, 'store']);
    Route::put('/teams/{id}', [TeamController::class, 'update']);
    Route::delete('/teams/{id}', [TeamController::class, 'destroy']);

    // Başlangıç Bakiyeleri
    Route::get('/initial-balance/entities', [InitialBalanceController::class, 'getEntities']);
    Route::post('/initial-balance', [InitialBalanceController::class, 'save']);
    Route::post('/initial-balance/reset', [InitialBalanceController::class, 'reset']);

    // Yatırımlar
    Route::get('/deposits/pending', [DepositController::class, 'pending']);
    Route::get('/deposits/all', [DepositController::class, 'all']);
    Route::get('/deposits/{id}/detail',  [DepositController::class, 'detail']);
    Route::get('/deposits/{id}/receipt', [DepositController::class, 'receipt']);
    Route::get('/deposits/filter-meta', [DepositController::class, 'filterMeta']);
    Route::post('/deposits/approve', [DepositController::class, 'approve']);
    Route::post('/deposits/reject', [DepositController::class, 'reject']);
    Route::post('/deposits/{id}/resend-callback', [DepositController::class, 'resendCallback']);

    // Çekimler
    Route::get('/withdrawals/pending', [WithdrawController::class, 'pending']);
    Route::get('/withdrawals/all', [WithdrawController::class, 'all']);
    Route::get('/withdrawals/{id}/detail', [WithdrawController::class, 'detail']);
    Route::post('/withdrawals/take', [WithdrawController::class, 'take']);
    Route::post('/withdrawals/release', [WithdrawController::class, 'release']);
    Route::post('/withdrawals/approve', [WithdrawController::class, 'approve']);
    Route::post('/withdrawals/reject', [WithdrawController::class, 'reject']);
    Route::post('/withdrawals/bulk-assign', [WithdrawController::class, 'bulkAssign']);
    Route::post('/withdrawals/{id}/resend-callback', [WithdrawController::class, 'resendCallback']);
    Route::get('/withdrawals/{id}/receipts', [WithdrawController::class, 'receipts']);
    Route::post('/withdrawals/{id}/receipts', [WithdrawController::class, 'uploadReceipt']);
    Route::get('/withdrawals/{id}/receipts/{rid}', [WithdrawController::class, 'downloadReceipt']);
    Route::post('/withdrawals/{id}/receipts/{rid}/verify', [WithdrawController::class, 'verifyReceipt']);

    // Takım Kasa Detay
    Route::get('/team-cases', [TeamCaseController::class, 'index']);
    Route::get('/team-cases/{id}', [TeamCaseController::class, 'show']);
    Route::get('/team-cases/{id}/payments', [TeamCaseController::class, 'payments']);
    Route::post('/team-cases/{id}/payments', [TeamCaseController::class, 'addPayment']);
    Route::delete('/team-cases/{id}/payments/{paymentId}', [TeamCaseController::class, 'deletePayment']);
    Route::post('/team-cases/{id}/transfers', [TeamCaseController::class, 'addTransfer']);
    Route::delete('/team-cases/{id}/transfers/{transferId}', [TeamCaseController::class, 'deleteTransfer']);
    Route::post('/team-cases/{id}/syncs', [TeamCaseController::class, 'addSync']);
    Route::delete('/team-cases/{id}/syncs/{syncId}', [TeamCaseController::class, 'deleteSync']);

    // Banka Hesapları
    Route::get('/bank-accounts', [BankAccountController::class, 'index']);
    Route::get('/bank-accounts/banks', [BankAccountController::class, 'banks']);
    Route::get('/bank-accounts/teams', [BankAccountController::class, 'teams']);
    Route::post('/bank-accounts/reorder', [BankAccountController::class, 'reorder']);
    Route::post('/bank-accounts/identify', [BankAccountController::class, 'identifyBank']);
    Route::post('/bank-accounts', [BankAccountController::class, 'store']);
    Route::get('/bank-accounts/{id}', [BankAccountController::class, 'show']);
    Route::put('/bank-accounts/{id}', [BankAccountController::class, 'update']);
    Route::post('/bank-accounts/{id}/sort-order', [BankAccountController::class, 'setSortOrder']);
    Route::delete('/bank-accounts/{id}', [BankAccountController::class, 'destroy']);

    // Paylira Ortaklar
    Route::get('/paylira-partners', [PayliraPartnerController::class, 'index']);
    Route::get('/paylira-partners/{id}', [PayliraPartnerController::class, 'show']);
    Route::get('/paylira-partners/{id}/payments', [PayliraPartnerController::class, 'payments']);
    Route::post('/paylira-partners/{id}/payments', [PayliraPartnerController::class, 'addPayment']);
    Route::delete('/paylira-partners/{id}/payments/{paymentId}', [PayliraPartnerController::class, 'deletePayment']);
    Route::get('/paylira-expenses', [PayliraPartnerController::class, 'expenses']);
    Route::get('/paylira-partner-payments-all', [PayliraPartnerController::class, 'allPartnerPayments']);
    Route::post('/paylira-expenses', [PayliraPartnerController::class, 'addExpense']);
    Route::delete('/paylira-expenses/{id}', [PayliraPartnerController::class, 'deleteExpense']);
    Route::get('/paylira-partners/{id}/capitals', [PayliraPartnerController::class, 'capitals']);
    Route::post('/paylira-partners/{id}/capitals', [PayliraPartnerController::class, 'addCapital']);
    Route::delete('/paylira-partners/{id}/capitals/{capitalId}', [PayliraPartnerController::class, 'deleteCapital']);
    Route::post('/paylira-partners/{id}/transfers', [PayliraPartnerController::class, 'addPartnerTransfer']);
    Route::delete('/paylira-partners/{id}/transfers/{transferId}', [PayliraPartnerController::class, 'deletePartnerTransfer']);

    // Aracı Kasa Detay
    Route::get('/intermediary-cases', [IntermediaryCaseController::class, 'index']);
    Route::get('/intermediary-cases/{id}', [IntermediaryCaseController::class, 'show']);
    Route::get('/intermediary-cases/{id}/payments', [IntermediaryCaseController::class, 'payments']);
    Route::post('/intermediary-cases/{id}/payments', [IntermediaryCaseController::class, 'addPayment']);
    Route::delete('/intermediary-cases/{id}/payments/{paymentId}', [IntermediaryCaseController::class, 'deletePayment']);

    // Merchant Yönetimi
    Route::get('/merchants', [MerchantController::class, 'index']);
    Route::post('/merchants', [MerchantController::class, 'store']);
    Route::put('/merchants/{id}', [MerchantController::class, 'update']);
    Route::delete('/merchants/{id}', [MerchantController::class, 'destroy']);
    Route::get('/merchants/{id}/credentials', [MerchantController::class, 'showCredentials']);
    Route::post('/merchants/{id}/rotate-secret', [MerchantController::class, 'rotateSecret']);
    Route::post('/merchants/{id}/rotate-key', [MerchantController::class, 'rotateKey']);
    Route::get('/merchant-groups', [MerchantController::class, 'groups']);
    Route::post('/merchant-groups', [MerchantController::class, 'storeGroup']);
    Route::put('/merchant-groups/{id}', [MerchantController::class, 'updateGroup']);
    Route::delete('/merchant-groups/{id}', [MerchantController::class, 'destroyGroup']);
    Route::post('/merchant-groups/assign', [MerchantController::class, 'assignToGroup']);

    // Fon Depoları
    Route::get('/fund-storages', [FundStorageController::class, 'index']);
    Route::get('/fund-storages/{id}', [FundStorageController::class, 'show']);
    Route::post('/fund-storages', [FundStorageController::class, 'store']);
    Route::put('/fund-storages/{id}', [FundStorageController::class, 'update']);
    Route::delete('/fund-storages/{id}', [FundStorageController::class, 'destroy']);
    Route::get('/fund-transfers', [FundStorageController::class, 'transfers']);
    Route::post('/fund-transfers', [FundStorageController::class, 'createTransfer']);
    Route::delete('/fund-transfers/{id}', [FundStorageController::class, 'deleteTransfer']);
    Route::post('/fund-storage-syncs', [FundStorageController::class, 'addSync']);
    Route::delete('/fund-storage-syncs/{id}', [FundStorageController::class, 'deleteSync']);

    // Tron TX lookup
    Route::post('/tron-tx-lookup', [FundStorageController::class, 'tronTxLookup']);

    // Blacklist
    Route::get('/blacklist', [BlacklistController::class, 'index']);
    Route::post('/blacklist', [BlacklistController::class, 'store']);
    Route::put('/blacklist/{id}', [BlacklistController::class, 'update']);
    Route::delete('/blacklist/{id}', [BlacklistController::class, 'destroy']);
    Route::post('/blacklist/check', [BlacklistController::class, 'check']);

    // Export
    Route::post('/exports', [ExportController::class, 'create']);
    Route::get('/exports', [ExportController::class, 'status']);
    Route::delete('/exports/clear', [ExportController::class, 'clear']);
});

// Download — middleware dışında, token query param ile auth
Route::get('/exports/{id}/download', [ExportController::class, 'download']);
