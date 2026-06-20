using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using PayDoPay.Application.Common.Interfaces;
using PayDoPay.Application.Features.Auth;

namespace PayDoPay.Api.Auth;

/// <summary>
/// Sanctum tarzı opaque bearer token doğrulama. [Authorize] ile çalışır.
/// Idle timeout / expiry için özel 401 gövdesi HandleChallenge'da üretilir.
/// </summary>
public class OpaqueTokenAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "OpaqueToken";

    private readonly ITokenService _tokenService;

    public OpaqueTokenAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        ITokenService tokenService)
        : base(options, logger, encoder)
    {
        _tokenService = tokenService;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return AuthenticateResult.NoResult();

        var plain = authHeader["Bearer ".Length..].Trim();
        var result = await _tokenService.ValidateAsync(plain, Context.RequestAborted);

        if (result.Status != TokenValidationStatus.Valid || result.User is null)
        {
            Context.Items["auth_fail_reason"] = result.Status.ToString();
            return AuthenticateResult.Fail(result.Status.ToString());
        }

        var user = result.User;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new("user_type", (user.UserTypeId ?? 0).ToString()),
            new("token_id", result.TokenId.ToString()),
            new("team_id", user.TeamId.ToString()),
            new(ClaimTypes.Role, ((int)user.Role).ToString()),
        };
        if (user.FirmId is not null)
            claims.Add(new Claim("firm_id", user.FirmId.Value.ToString()));

        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/json; charset=utf-8";

        var reason = Context.Items.TryGetValue("auth_fail_reason", out var r) ? r?.ToString() : null;
        object body = reason switch
        {
            nameof(TokenValidationStatus.IdleTimedOut) => new { message = AuthMessages.IdleTimeout, code = "idle_timeout" },
            _ => new { message = "Unauthenticated." },
        };

        await Response.WriteAsJsonAsync(body);
    }
}
