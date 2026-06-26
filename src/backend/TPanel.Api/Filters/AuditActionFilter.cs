using Microsoft.AspNetCore.Mvc.Filters;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.Audit;

namespace TPanel.Api.Filters;

/// <summary>
/// Tüm DEĞİŞTİREN (POST/PUT/DELETE/PATCH) panel isteklerini audit_logs'a yazar.
/// Hariç: public merchant API (/api/v1/*), Telegram webhook. GET'ler loglanmaz.
/// Result filter olduğundan response status kodu kesinleşmiş olur.
/// </summary>
public class AuditActionFilter : IAsyncResultFilter
{
    private static readonly HashSet<string> Mutating = new(StringComparer.OrdinalIgnoreCase) { "POST", "PUT", "DELETE", "PATCH" };

    private readonly IAuditLogger _audit;
    private readonly ICurrentUser _currentUser;

    public AuditActionFilter(IAuditLogger audit, ICurrentUser currentUser) { _audit = audit; _currentUser = currentUser; }

    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        await next(); // result çalışsın → status kesinleşsin

        try
        {
            var http = context.HttpContext;
            var req = http.Request;
            if (!Mutating.Contains(req.Method)) return;

            var path = req.Path.Value ?? "";
            if (!path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)) return;
            if (path.StartsWith("/api/v1", StringComparison.OrdinalIgnoreCase)) return;        // public merchant API
            if (path.StartsWith("/api/telegram", StringComparison.OrdinalIgnoreCase)) return;  // webhook

            var user = await _currentUser.GetUserAsync(http.RequestAborted);
            var controller = context.RouteData.Values["controller"]?.ToString();
            var actionName = context.RouteData.Values["action"]?.ToString();
            var entityId = context.RouteData.Values["id"]?.ToString();

            await _audit.WriteAsync(new AuditEntry(
                UserId: user?.Id,
                UserName: user?.Username,
                Ip: http.Connection.RemoteIpAddress?.ToString(),
                Method: req.Method,
                Path: path,
                Action: controller is null ? actionName : $"{controller}.{actionName}",
                EntityType: controller,
                EntityId: entityId,
                Description: null,
                StatusCode: http.Response.StatusCode), http.RequestAborted);
        }
        catch { /* audit asla isteği kırmaz */ }
    }
}
