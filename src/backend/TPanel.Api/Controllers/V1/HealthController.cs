using System.Diagnostics;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using TPanel.Application.Common.Interfaces;

namespace TPanel.Api.Controllers.V1;

/// <summary>
/// Public sağlık kontrolü — Laravel: GET /api/v1/health (auth gerekmez).
/// </summary>
[ApiController]
[Route("api/v1/health")]
public class HealthController : ControllerBase
{
    private readonly IDbConnectionFactory _connectionFactory;

    public HealthController(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var dbStatus = "ok";
        long latencyMs = 0;
        var sw = Stopwatch.StartNew();
        try
        {
            using var conn = await _connectionFactory.CreateOpenConnectionAsync(ct);
            await conn.ExecuteScalarAsync<int>("SELECT 1");
            latencyMs = sw.ElapsedMilliseconds;
        }
        catch
        {
            dbStatus = "error";
            latencyMs = sw.ElapsedMilliseconds;
        }

        var overall = dbStatus == "ok" ? "ok" : "down";
        var payload = new
        {
            status = overall,
            services = new
            {
                api = new { status = "ok" },
                database = new { status = dbStatus, latency_ms = latencyMs },
            },
            time = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            version = "v1",
        };

        return StatusCode(overall == "ok" ? 200 : 503, payload);
    }
}
