using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayDoPay.Application.Common.Interfaces;
using PayDoPay.Application.Features.Auth;

namespace PayDoPay.Api.Controllers;

/// <summary>Laravel: /api/auth/* — login, 2FA, me, logout, change-password.</summary>
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _auth;
    private readonly ICurrentUser _currentUser;
    private readonly ITokenService _tokenService;
    private readonly IClock _clock;

    public AuthController(IAuthService auth, ICurrentUser currentUser, ITokenService tokenService, IClock clock)
    {
        _auth = auth;
        _currentUser = currentUser;
        _tokenService = tokenService;
        _clock = clock;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var result = await _auth.LoginAsync(request, ct);
        return MapResult(result);
    }

    [HttpPost("two-factor")]
    public async Task<IActionResult> TwoFactor([FromBody] TwoFactorRequest request, CancellationToken ct)
    {
        var result = await _auth.VerifyTwoFactorAsync(request, ct);
        return MapResult(result);
    }

    [Authorize]
    [HttpGet("me")]
    public async Task<IActionResult> Me(CancellationToken ct)
    {
        var user = await _currentUser.GetUserAsync(ct);
        if (user is null)
            return Unauthorized(new { message = "Unauthenticated." });

        return Ok(new { user = _auth.BuildUserPayload(user) });
    }

    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var tokenId = _currentUser.TokenId;
        if (tokenId is not null)
            await _tokenService.DeleteAsync(tokenId.Value, ct);

        return Ok(new { message = AuthMessages.LoggedOut });
    }

    [Authorize]
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await _currentUser.GetUserAsync(ct);
        if (user is null)
            return Unauthorized(new { message = "Unauthenticated." });

        var result = await _auth.ChangePasswordAsync(user, request, ct);
        return MapResult(result);
    }

    private IActionResult MapResult(AuthResult result) => result.Outcome switch
    {
        AuthOutcome.Success => Ok(result.Body),
        AuthOutcome.TwoFactorRequired => Ok(result.Body),
        AuthOutcome.InvalidCredentials => Unauthorized(result.Body),
        AuthOutcome.AccountBlocked => StatusCode(403, result.Body),
        AuthOutcome.Invalid => Unauthorized(result.Body),
        AuthOutcome.ValidationError => UnprocessableEntity(result.Body),
        _ => BadRequest(result.Body),
    };
}
