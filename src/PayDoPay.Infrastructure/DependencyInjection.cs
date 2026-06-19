using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PayDoPay.Application.Common.Interfaces;
using PayDoPay.Application.Features.PublicApi;
using PayDoPay.Infrastructure.Persistence;
using PayDoPay.Infrastructure.Services;

namespace PayDoPay.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Dapper: order_id→OrderId, ibanSeen→IbanSeen, u_id→UId vb. eşleşsin
        Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;

        var connectionString = configuration.GetConnectionString("MySql")
            ?? throw new InvalidOperationException("ConnectionStrings:MySql tanımlı değil.");

        // AutoDetect başlangıçta DB'ye bağlanır; sabit sürüm kullanıyoruz (config'ten override edilebilir).
        var versionText = configuration["Database:ServerVersion"] ?? "8.0.30-mysql";
        var serverVersion = ServerVersion.Parse(versionText);

        services.AddDbContext<AppDbContext>(options =>
            options.UseMySql(connectionString, serverVersion));

        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddSingleton<IDbConnectionFactory>(new MySqlConnectionFactory(connectionString));

        // ---- Servisler ----
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, Md5PasswordHasher>();
        services.AddSingleton<ITwoFactorService, TotpTwoFactorService>();
        services.AddSingleton<ITempTokenService, HmacTempTokenService>();

        var tokenOptions = new SanctumTokenOptions
        {
            ExpirationMinutes = int.TryParse(configuration["Auth:TokenExpirationMinutes"], out var exp) ? exp : 480,
            IdleMinutes = int.TryParse(configuration["Auth:IdleMinutes"], out var idle) ? idle : 30,
        };
        services.AddSingleton(tokenOptions);
        services.AddScoped<ITokenService, SanctumTokenService>();

        // ---- Public/Merchant API servisleri ----
        var publicApiOptions = new PublicApiOptions
        {
            AppUrl = configuration["App:Url"] ?? "http://localhost",
            PayrouteChatId = configuration["Telegram:PayrouteChatId"],
        };
        services.AddSingleton(publicApiOptions);

        services.AddHttpClient("callback", c => c.Timeout = TimeSpan.FromSeconds(10));
        services.AddHttpClient("telegram", c => c.Timeout = TimeSpan.FromSeconds(10));

        services.AddScoped<IMerchantApiStore, MerchantApiStore>();
        services.AddScoped<IMerchantBankService, MerchantBankService>();
        services.AddScoped<ICallbackService, CallbackService>();
        services.AddScoped<ISystemSettingService, SystemSettingService>();
        services.AddScoped<ITelegramService, TelegramService>();
        services.AddScoped<IReceiptStorage, FileReceiptStorage>();

        // ---- Admin işlem yönetimi (Faz 3) ----
        services.AddScoped<Application.Features.Transactions.ITransactionAdminStore, TransactionAdminStore>();
        services.AddScoped<Application.Features.Transactions.IWithdrawReceiptStorage, WithdrawReceiptStorage>();
        services.AddScoped<Application.Features.Transactions.IPerceptualHasher, PerceptualHasher>();
        // Dekont AI doğrulama (Faz 6b) — gerçek kuyruk + Claude Vision pipeline
        services.AddSingleton<ReceiptVerificationQueue>();
        services.AddSingleton<Application.Features.Transactions.IReceiptVerificationQueue>(sp => sp.GetRequiredService<ReceiptVerificationQueue>());
        services.AddHostedService<ReceiptVerificationBackgroundService>();
        services.AddHttpClient("anthropic");
        services.AddScoped<Application.Features.Receipts.IClaudeVisionService, ClaudeVisionService>();
        services.AddScoped<Application.Features.Receipts.IFileMetadataService, FileMetadataService>();
        services.AddScoped<Application.Features.Receipts.IReceiptVerifier, ReceiptVerifier>();

        // ---- Kasa muhasebesi (Faz 4) ----
        services.AddHttpClient("tron");
        services.AddScoped<Application.Features.Cases.ICaseStore, CaseStore>();
        services.AddScoped<Application.Features.Cases.ITronService, TronService>();

        // ---- Yönetim CRUD (Faz 5a) ----
        services.AddScoped<Application.Features.Management.IManagementStore, ManagementStore>();

        // ---- Dashboard + Export (Faz 5b) ----
        services.AddScoped<Application.Features.Dashboard.IManagementHelper, MerchantScopeHelper>();
        services.AddScoped<Application.Features.Dashboard.IDashboardStore, DashboardStore>();
        services.AddScoped<Application.Features.Export.IExportStore, ExportStore>();
        services.AddSingleton<Application.Features.Export.IExportQueue, ExportQueue>();
        services.AddHostedService<ExportBackgroundService>();
        services.AddScoped<Application.Features.Reports.IReportsStore, ReportsStore>();

        // ---- Arka plan işleri (Faz 6a) ----
        services.AddScoped<Application.Features.Background.IDailyCaseSnapshotJob, DailyCaseSnapshotJob>();
        services.AddScoped<Application.Features.Background.IExpirePendingJob, ExpirePendingJob>();
        services.AddScoped<Application.Features.Background.ICheckPendingNotificationsJob, CheckPendingNotificationsJob>();
        services.AddScoped<Application.Features.Background.ICheckLowAmountRiskJob, CheckLowAmountRiskJob>();
        services.AddHostedService<SchedulerHostedService>();

        return services;
    }
}
