using PayDoPay.Domain.Enums;

namespace PayDoPay.Domain.Security;

/// <summary>
/// Sadece user_type'a bağlı yetki kuralları (authorization policy'leri için tek kaynak).
/// User entity'sindeki Can* özellikleri ile aynı mantık.
/// </summary>
public static class RolePermissions
{
    public static bool ManageUsers(UserType r) => r is UserType.SuperAdmin or UserType.SubAdmin or UserType.TeamAdmin;
    public static bool ManageTeams(UserType r) => IsAdmin(r);
    public static bool ManageMerchants(UserType r) => IsAdmin(r);
    public static bool ManageBankAccounts(UserType r) => r is UserType.SuperAdmin or UserType.SubAdmin or UserType.TeamAdmin;
    public static bool ApproveTransactions(UserType r) => r is UserType.SuperAdmin or UserType.SubAdmin or UserType.TeamAdmin or UserType.TeamAgent;
    public static bool BlockUsers(UserType r) => IsAdmin(r);
    public static bool SystemSettings(UserType r) => r == UserType.SuperAdmin;
    public static bool FinancialReports(UserType r) => r is UserType.SuperAdmin or UserType.SubAdmin or UserType.TeamAdmin or UserType.Merchant;
    public static bool PerformanceReports(UserType r) => r is UserType.SuperAdmin or UserType.SubAdmin or UserType.TeamAdmin;

    public static bool IsAdmin(UserType r) => r is UserType.SuperAdmin or UserType.SubAdmin;
}
