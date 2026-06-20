using Microsoft.Extensions.DependencyInjection;
using TPanel.Application.Features.Auth;
using TPanel.Application.Features.PublicApi;
using TPanel.Application.Features.Transactions;

namespace TPanel.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuthService, AuthService>();

        // Public/Merchant v1 API akış servisleri
        services.AddScoped<IDepositApiService, DepositApiService>();
        services.AddScoped<IWithdrawApiService, WithdrawApiService>();
        services.AddScoped<ITransactionApiService, TransactionApiService>();
        services.AddScoped<IPublicPaymentService, PublicPaymentService>();

        // Admin işlem yönetimi (Faz 3)
        services.AddScoped<IDepositAdminService, DepositAdminService>();
        services.AddScoped<IWithdrawAdminService, WithdrawAdminService>();

        // Kasa muhasebesi (Faz 4)
        services.AddScoped<Features.Cases.ITeamCaseService, Features.Cases.TeamCaseService>();
        services.AddScoped<Features.Cases.IMerchantCaseService, Features.Cases.MerchantCaseService>();
        services.AddScoped<Features.Cases.IFundStorageService, Features.Cases.FundStorageService>();
        services.AddScoped<Features.Cases.IIntermediaryCaseService, Features.Cases.IntermediaryCaseService>();
        services.AddScoped<Features.Cases.IPartnerCaseService, Features.Cases.PartnerCaseService>();
        services.AddScoped<Features.Cases.ICaseReportService, Features.Cases.CaseReportService>();
        services.AddScoped<Features.Cases.IInitialBalanceService, Features.Cases.InitialBalanceService>();

        // Yönetim CRUD (Faz 5a)
        services.AddScoped<Features.Management.ITeamMgmtService, Features.Management.TeamMgmtService>();
        services.AddScoped<Features.Management.IMerchantMgmtService, Features.Management.MerchantMgmtService>();
        services.AddScoped<Features.Management.IBankAccountMgmtService, Features.Management.BankAccountMgmtService>();
        services.AddScoped<Features.Management.IUserMgmtService, Features.Management.UserMgmtService>();
        services.AddScoped<Features.Management.IBlacklistMgmtService, Features.Management.BlacklistMgmtService>();
        services.AddScoped<Features.Management.IIntermediaryMgmtService, Features.Management.IntermediaryMgmtService>();
        services.AddScoped<Features.Management.ISettingsMgmtService, Features.Management.SettingsMgmtService>();

        // Dashboard + Export (Faz 5b)
        services.AddScoped<Features.Dashboard.IDashboardService, Features.Dashboard.DashboardService>();
        services.AddScoped<Features.Export.IExportService, Features.Export.ExportService>();
        services.AddScoped<Features.Reports.IReportsService, Features.Reports.ReportsService>();

        return services;
    }
}
