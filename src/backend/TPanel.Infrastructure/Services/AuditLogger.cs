using System.Text;
using Dapper;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.Audit;

namespace TPanel.Infrastructure.Services;

/// <summary>audit_logs tablosuna Dapper ile yazma/sorgulama. Yazma hataları yutulur (isteği kırmaz).</summary>
public class AuditLogger : IAuditLogger
{
    private readonly IDbConnectionFactory _factory;
    private readonly IClock _clock;

    public AuditLogger(IDbConnectionFactory factory, IClock clock) { _factory = factory; _clock = clock; }

    public async Task WriteAsync(AuditEntry e, CancellationToken ct = default)
    {
        try
        {
            using var c = await _factory.CreateOpenConnectionAsync(ct);
            await c.ExecuteAsync(@"INSERT INTO audit_logs
                (created_at,user_id,user_name,ip,method,path,action,entity_type,entity_id,description,status_code,meta)
                VALUES (@at,@uid,@uname,@ip,@method,@path,@action,@etype,@eid,@desc,@status,@meta)",
                new
                {
                    at = _clock.Now, uid = e.UserId, uname = e.UserName, ip = e.Ip, method = e.Method,
                    path = Trunc(e.Path, 500), action = Trunc(e.Action, 100), etype = Trunc(e.EntityType, 100),
                    eid = Trunc(e.EntityId, 100), desc = Trunc(e.Description, 1000), status = e.StatusCode, meta = e.Meta,
                });
        }
        catch { /* audit asla isteği kırmaz */ }
    }

    public async Task<AuditPage> QueryAsync(AuditQuery q, CancellationToken ct = default)
    {
        using var c = await _factory.CreateOpenConnectionAsync(ct);
        var w = new StringBuilder(" WHERE 1=1");
        var p = new DynamicParameters();
        if (!string.IsNullOrWhiteSpace(q.Search))
        { w.Append(" AND (user_name LIKE @s OR path LIKE @s OR description LIKE @s OR action LIKE @s)"); p.Add("s", "%" + q.Search + "%"); }
        if (q.UserId is not null) { w.Append(" AND user_id=@uid"); p.Add("uid", q.UserId); }
        if (!string.IsNullOrWhiteSpace(q.Method)) { w.Append(" AND method=@m"); p.Add("m", q.Method); }
        if (!string.IsNullOrWhiteSpace(q.Action)) { w.Append(" AND action=@a"); p.Add("a", q.Action); }
        if (!string.IsNullOrWhiteSpace(q.From)) { w.Append(" AND created_at>=@from"); p.Add("from", q.From + " 00:00:00"); }
        if (!string.IsNullOrWhiteSpace(q.To)) { w.Append(" AND created_at<@to"); p.Add("to", q.To + " 23:59:59"); }

        var where = w.ToString();
        var total = await c.ExecuteScalarAsync<long>("SELECT COUNT(*) FROM audit_logs" + where, p);
        var per = q.PerPage is < 1 or > 200 ? 50 : q.PerPage;
        var page = q.Page < 1 ? 1 : q.Page;
        p.Add("lim", per); p.Add("off", (page - 1) * per);
        var rows = await c.QueryAsync(@"SELECT id, created_at, user_id, user_name, ip, method, path, action, entity_type, entity_id, description, status_code, meta
            FROM audit_logs" + where + " ORDER BY id DESC LIMIT @lim OFFSET @off", p);
        return new AuditPage(rows.Cast<object>().ToList(), total);
    }

    public async Task<int> CleanupAsync(int days, CancellationToken ct = default)
    {
        try
        {
            using var c = await _factory.CreateOpenConnectionAsync(ct);
            return await c.ExecuteAsync("DELETE FROM audit_logs WHERE created_at < @cut",
                new { cut = _clock.Now.AddDays(-Math.Abs(days)) });
        }
        catch { return 0; }
    }

    private static string? Trunc(string? s, int max) => s is null ? null : (s.Length <= max ? s : s[..max]);
}
