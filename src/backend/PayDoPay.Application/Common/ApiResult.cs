namespace PayDoPay.Application.Common;

/// <summary>Genel iç API sonucu — HTTP durum + gövde.</summary>
public record ApiResult(int HttpStatus, object Body)
{
    public static ApiResult Ok(object body) => new(200, body);
    public static ApiResult Msg(int status, string message) => new(status, new { message });
}

/// <summary>Rol bazlı sorgu kapsamı.</summary>
public enum ScopeKind { Global, Team, Merchant }

public record QueryScope(ScopeKind Kind, int? TeamId = null, IReadOnlyList<int>? MerchantIds = null)
{
    public static readonly QueryScope Global = new(ScopeKind.Global);
}
