using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PayDoPay.Application.Common.Interfaces;
using PayDoPay.Domain.Entities;

namespace PayDoPay.Api.Auth;

/// <summary>HttpContext üzerinden aktif kullanıcı bilgisine erişim.</summary>
public class CurrentUser : ICurrentUser
{
    private readonly IHttpContextAccessor _accessor;
    private readonly IApplicationDbContext _db;

    public CurrentUser(IHttpContextAccessor accessor, IApplicationDbContext db)
    {
        _accessor = accessor;
        _db = db;
    }

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;

    public int? UserId
    {
        get
        {
            var v = Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return int.TryParse(v, out var id) ? id : null;
        }
    }

    public ulong? TokenId
    {
        get
        {
            var v = Principal?.FindFirstValue("token_id");
            return ulong.TryParse(v, out var id) ? id : null;
        }
    }

    public async Task<User?> GetUserAsync(CancellationToken ct = default)
    {
        var id = UserId;
        if (id is null) return null;
        return await _db.Users.FirstOrDefaultAsync(u => u.Id == id.Value, ct);
    }
}
