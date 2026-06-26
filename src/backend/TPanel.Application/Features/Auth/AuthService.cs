using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using TPanel.Application.Common.Interfaces;
using TPanel.Domain.Entities;

namespace TPanel.Application.Features.Auth;

public interface IAuthService
{
    Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct = default);
    Task<AuthResult> VerifyTwoFactorAsync(TwoFactorRequest request, CancellationToken ct = default);
    Task<AuthResult> ChangePasswordAsync(User user, ChangePasswordRequest request, CancellationToken ct = default);
    UserPayload BuildUserPayload(User user);
}

public class AuthService : IAuthService
{
    private const string DummyHash = "00000000000000000000000000000000";

    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenService _tokenService;
    private readonly ITwoFactorService _twoFactor;
    private readonly ITempTokenService _tempToken;
    private readonly IClock _clock;

    public AuthService(
        IApplicationDbContext db,
        IPasswordHasher passwordHasher,
        ITokenService tokenService,
        ITwoFactorService twoFactor,
        ITempTokenService tempToken,
        IClock clock)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _tokenService = tokenService;
        _twoFactor = twoFactor;
        _tempToken = tempToken;
        _clock = clock;
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            return AuthResult.Bad(AuthOutcome.ValidationError, "Kullanıcı adı ve şifre zorunludur.");

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Username == request.Username && u.Status == "1", ct);

        // Sabit-zamanlı karşılaştırma — kullanıcı yoksa dummy hash ile (timing leak kapanır).
        var storedHash = user?.Password ?? DummyHash;
        var passwordOk = _passwordHasher.VerifyMd5(request.Password, storedHash);

        if (user is null || !passwordOk)
            return AuthResult.Bad(AuthOutcome.InvalidCredentials, AuthMessages.InvalidCredentials);

        if (user.IsBlocked)
            return AuthResult.Bad(AuthOutcome.AccountBlocked, AuthMessages.AccountBlocked);

        // 2FA
        if (user.HasOtpEnabled)
        {
            var tempToken = _tempToken.Create(user.Id, ttlMinutes: 5);
            if (string.IsNullOrEmpty(user.OtpCode))
            {
                var secret = _twoFactor.GenerateSecret();
                user.OtpCode = secret;
                await _db.SaveChangesAsync(ct);

                var qrSvg = _twoFactor.GetQrCodeSvg(user.Username, secret);
                return AuthResult.TwoFactor(new
                {
                    two_factor = true,
                    temp_token = tempToken,
                    message = AuthMessages.TwoFactorRequired,
                    setup_required = true,
                    qr_code = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(qrSvg)),
                });
            }

            return AuthResult.TwoFactor(new
            {
                two_factor = true,
                temp_token = tempToken,
                message = AuthMessages.TwoFactorRequired,
            });
        }

        user.LastLogin = _clock.Now;
        await _db.SaveChangesAsync(ct);

        var token = await _tokenService.CreateTokenAsync(user, ct: ct);
        return AuthResult.Success(new { token, user = BuildUserPayload(user) });
    }

    public async Task<AuthResult> VerifyTwoFactorAsync(TwoFactorRequest request, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(request.TempToken) || request.Code is not { Length: 6 })
            return AuthResult.Bad(AuthOutcome.ValidationError, "temp_token ve 6 haneli kod zorunludur.");

        var userId = _tempToken.Validate(request.TempToken);
        if (userId is null)
            return AuthResult.Bad(AuthOutcome.Invalid, AuthMessages.SessionExpired);

        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId.Value, ct);
        if (user is null)
            return AuthResult.Bad(AuthOutcome.Invalid, AuthMessages.InvalidToken);

        if (!_twoFactor.Verify(user.OtpCode, request.Code))
            return AuthResult.Bad(AuthOutcome.Invalid, AuthMessages.InvalidCode);

        user.LastLogin = _clock.Now;
        await _db.SaveChangesAsync(ct);

        var token = await _tokenService.CreateTokenAsync(user, ct: ct);
        return AuthResult.Success(new { token, user = BuildUserPayload(user) });
    }

    public async Task<AuthResult> ChangePasswordAsync(User user, ChangePasswordRequest request, CancellationToken ct = default)
    {
        var pw = request.NewPassword ?? string.Empty;
        if (pw.Length < 6)
            return AuthResult.Bad(AuthOutcome.ValidationError, AuthMessages.PwMin);
        if (!Regex.IsMatch(pw, @"^(?=.*[A-Za-z])(?=.*\d).+$"))
            return AuthResult.Bad(AuthOutcome.ValidationError, AuthMessages.PwRegex);
        if (pw == request.CurrentPassword)
            return AuthResult.Bad(AuthOutcome.ValidationError, AuthMessages.PwDifferent);

        if (!_passwordHasher.VerifyMd5(request.CurrentPassword ?? string.Empty, user.Password))
            return AuthResult.Bad(AuthOutcome.ValidationError, AuthMessages.CurrentPasswordWrong);

        user.Password = _passwordHasher.HashMd5(pw);
        await _db.SaveChangesAsync(ct);

        // Tüm token'ları iptal et — mevcut oturum dahil.
        await _tokenService.DeleteAllForUserAsync(user.Id, ct);

        return AuthResult.Success(new { message = AuthMessages.PasswordUpdatedReauth, reauth_required = true });
    }

    public UserPayload BuildUserPayload(User user) => new(
        id: user.Id,
        name: user.Name,
        username: user.Username,
        user_type: user.UserTypeId,
        role_label: user.RoleLabel,
        team_id: user.TeamId,
        firm_id: user.FirmId,
        is_sys_admin: user.IsSysAdmin,
        is_god_mode: user.IsGodMode,
        permissions: new PermissionsPayload(
            manage_users: user.CanManageUsers,
            manage_teams: user.CanManageTeams,
            manage_merchants: user.CanManageMerchants,
            manage_bank_accounts: user.CanManageBankAccounts,
            approve_transactions: user.CanApproveTransactions,
            block_users: user.CanBlockUsers,
            system_settings: user.CanAccessSystemSettings,
            financial_reports: user.CanViewFinancialReports,
            performance_reports: user.CanViewPerformanceReports));
}
