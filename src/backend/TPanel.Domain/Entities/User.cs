using TPanel.Domain.Enums;

namespace TPanel.Domain.Entities;

/// <summary>Panel personeli — `users` tablosu. Şifre legacy MD5'tir.</summary>
public class User
{
    public int Id { get; set; }
    public int TeamId { get; set; }
    public int? UserTypeId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;

    /// <summary>enum('0','1') — '1' aktif.</summary>
    public string Status { get; set; } = "1";

    /// <summary>enum('0','1') — '1' ise 2FA zorunlu.</summary>
    public string OtpOk { get; set; } = "0";

    /// <summary>TOTP secret (google2fa).</summary>
    public string OtpCode { get; set; } = string.Empty;

    public int Collapse { get; set; }
    public DateTime? LastLogin { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? FirmId { get; set; }
    public uint? MerchantGroupId { get; set; }
    public int AutoReload { get; set; } = 1;
    public int AutoModeChange { get; set; }
    public int? CreatedBy { get; set; }

    /// <summary>users.is_sys_admin — Sistem Yöneticisi: "Ayarlar" + "API & Callback Logları" erişimi ve SuperAdmin oluşturma/atama yetkisi. DB'den elle set edilir.</summary>
    public bool IsSysAdmin { get; set; }

    // ---- Yardımcı (mapped değil) ----
    public UserType Role => (UserType)(UserTypeId ?? 0);
    public bool IsActive => Status == "1";
    public bool TwoFactorRequired => OtpOk == "1";

    // ---- Rol kontrolleri (Laravel User modeli birebir) ----
    public bool IsSuperAdmin => Role == UserType.SuperAdmin;
    public bool IsSubAdmin => Role == UserType.SubAdmin;
    public bool IsTeamAdmin => Role == UserType.TeamAdmin;
    public bool IsTeamAgent => Role == UserType.TeamAgent;
    public bool IsMerchant => Role == UserType.Merchant;
    public bool IsBlocked => Status == "0" || Role == UserType.Blocked;
    public bool HasOtpEnabled => OtpOk == "1";

    public bool IsAdmin => Role is UserType.SuperAdmin or UserType.SubAdmin;
    public bool IsTeamMember => Role is UserType.TeamAdmin or UserType.TeamAgent;
    public bool HasGlobalAccess => IsAdmin;
    public bool HasTeamScope => IsTeamMember;
    public bool HasMerchantScope => IsMerchant;
    public bool IsPrimaryTeamAdmin => IsTeamAdmin && CreatedBy is null;

    public string RoleLabel => Role switch
    {
        UserType.SuperAdmin => "Super Admin",
        UserType.SubAdmin => "Sub Admin",
        UserType.TeamAdmin => "Team Admin",
        UserType.TeamAgent => "Team Agent",
        UserType.Merchant => "Merchant",
        UserType.Blocked => "Blocked",
        _ => "Unknown",
    };

    // ---- Yetki kontrolleri ----
    public bool CanManageUsers => Role is UserType.SuperAdmin or UserType.SubAdmin or UserType.TeamAdmin;
    public bool CanManageTeams => IsAdmin;
    public bool CanToggleTeamStatus => IsAdmin;
    public bool CanManageMerchants => IsAdmin;
    public bool CanManageBankAccounts => Role is UserType.SuperAdmin or UserType.SubAdmin or UserType.TeamAdmin;
    public bool CanApproveTransactions => Role is UserType.SuperAdmin or UserType.SubAdmin or UserType.TeamAdmin or UserType.TeamAgent;
    public bool CanBlockUsers => IsAdmin;
    public bool CanAccessSystemSettings => IsSysAdmin;
    public bool CanViewFinancialReports => Role is UserType.SuperAdmin or UserType.SubAdmin or UserType.TeamAdmin or UserType.Merchant;
    public bool CanViewPerformanceReports => Role is UserType.SuperAdmin or UserType.SubAdmin or UserType.TeamAdmin;
    public bool CanManageTeamAdmins => IsSuperAdmin || IsPrimaryTeamAdmin;

    /// <summary>Bu kullanıcının yaratabileceği user_type listesi.</summary>
    public IReadOnlyList<UserType> CreatableUserTypes
    {
        get
        {
            var list = Role switch
            {
                UserType.SuperAdmin => new List<UserType> { UserType.SubAdmin, UserType.TeamAdmin, UserType.TeamAgent, UserType.Merchant },
                UserType.SubAdmin => new List<UserType> { UserType.TeamAdmin, UserType.TeamAgent, UserType.Merchant },
                UserType.TeamAdmin => new List<UserType> { UserType.TeamAgent },
                _ => new List<UserType>(),
            };
            // is_sys_admin: rolden bağımsız olarak SuperAdmin oluşturma/atama yetkisi
            if (IsSysAdmin && !list.Contains(UserType.SuperAdmin)) list.Insert(0, UserType.SuperAdmin);
            return list;
        }
    }

    public bool CanCreateUserType(UserType target) => CreatableUserTypes.Contains(target);

    public bool CanEditUser(User target)
    {
        if (target.IsSuperAdmin) return IsSysAdmin;   // SuperAdmin'i yalnızca Sistem Yöneticisi düzenleyebilir
        if (IsSuperAdmin) return true;
        if (IsSubAdmin) return true;
        if (IsTeamAdmin && target.TeamId == TeamId)
        {
            if (target.IsTeamAdmin && !IsPrimaryTeamAdmin) return false;
            return true;
        }
        return false;
    }
}
