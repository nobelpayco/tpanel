namespace TPanel.Application.Features.Auth;

// ---- İstekler ----
// Alanlar nullable: boş gövdede ASP.NET otomatik 400 yerine servis 422 (Laravel ile uyumlu) dönsün.
public record LoginRequest(string? Username, string? Password);
public record TwoFactorRequest(string? TempToken, string? Code);
public record ChangePasswordRequest(string? CurrentPassword, string? NewPassword);

// ---- Sonuç sarmalayıcı ----
public enum AuthOutcome { Success, TwoFactorRequired, InvalidCredentials, AccountBlocked, Invalid, ValidationError }

public record AuthResult(AuthOutcome Outcome, object? Body = null)
{
    public static AuthResult Success(object body) => new(AuthOutcome.Success, body);
    public static AuthResult TwoFactor(object body) => new(AuthOutcome.TwoFactorRequired, body);
    public static AuthResult Bad(AuthOutcome outcome, string message) => new(outcome, new { message });
}

// ---- /me ve login user payload ----
public record UserPayload(
    int id,
    string name,
    string username,
    int? user_type,
    string role_label,
    int team_id,
    int? firm_id,
    bool is_sys_admin,
    PermissionsPayload permissions);

public record PermissionsPayload(
    bool manage_users,
    bool manage_teams,
    bool manage_merchants,
    bool manage_bank_accounts,
    bool approve_transactions,
    bool block_users,
    bool system_settings,
    bool financial_reports,
    bool performance_reports);
