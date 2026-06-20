using Microsoft.AspNetCore.Authorization;
using TPanel.Domain.Enums;
using TPanel.Domain.Security;

namespace TPanel.Api.Auth;

/// <summary>user_type claim'ine dayalı yetki politikaları (RolePermissions tek kaynak).</summary>
public static class AuthorizationPolicies
{
    public const string ManageUsers = "ManageUsers";
    public const string ManageTeams = "ManageTeams";
    public const string ManageMerchants = "ManageMerchants";
    public const string ManageBankAccounts = "ManageBankAccounts";
    public const string ApproveTransactions = "ApproveTransactions";
    public const string BlockUsers = "BlockUsers";
    public const string SystemSettings = "SystemSettings";
    public const string FinancialReports = "FinancialReports";
    public const string PerformanceReports = "PerformanceReports";

    public static void AddPayDoPayPolicies(this AuthorizationOptions options)
    {
        Add(options, ManageUsers, RolePermissions.ManageUsers);
        Add(options, ManageTeams, RolePermissions.ManageTeams);
        Add(options, ManageMerchants, RolePermissions.ManageMerchants);
        Add(options, ManageBankAccounts, RolePermissions.ManageBankAccounts);
        Add(options, ApproveTransactions, RolePermissions.ApproveTransactions);
        Add(options, BlockUsers, RolePermissions.BlockUsers);
        Add(options, SystemSettings, RolePermissions.SystemSettings);
        Add(options, FinancialReports, RolePermissions.FinancialReports);
        Add(options, PerformanceReports, RolePermissions.PerformanceReports);
    }

    private static void Add(AuthorizationOptions options, string name, Func<UserType, bool> rule)
    {
        options.AddPolicy(name, policy => policy.RequireAssertion(ctx =>
        {
            var claim = ctx.User.FindFirst("user_type")?.Value;
            return int.TryParse(claim, out var ut) && rule((UserType)ut);
        }));
    }
}
