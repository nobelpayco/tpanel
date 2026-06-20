using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.Cases;

namespace TPanel.Api.Controllers;

[ApiController]
[Authorize]
public class FundStoragesController : AdminControllerBase
{
    private readonly IFundStorageService _service;
    private readonly ICurrentUser _currentUser;

    public FundStoragesController(IFundStorageService service, ICurrentUser currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    private string? Q(string k) => string.IsNullOrWhiteSpace(Request.Query[k]) ? null : Request.Query[k].ToString();

    private async Task<(Domain.Entities.User? u, IActionResult? e)> AuthAsync(CancellationToken ct)
    {
        var u = await _currentUser.GetUserAsync(ct);
        return u is null ? (null, Unauthorized(new { message = "Unauthenticated." })) : (u, null);
    }

    [HttpGet("api/fund-storages")]
    public async Task<IActionResult> Index(CancellationToken ct)
        => Result(await _service.IndexAsync(Q("status") ?? "1", ct));

    [HttpGet("api/fund-storages/{id:int}")]
    public async Task<IActionResult> Show(int id, CancellationToken ct)
        => Result(await _service.ShowAsync(id, Q("date_from"), Q("date_to"), ct));

    [HttpPost("api/fund-storages")]
    public async Task<IActionResult> Store([FromBody] FundStorageBody body, CancellationToken ct)
        => Result(await _service.CreateAsync(body, ct));

    [HttpPut("api/fund-storages/{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] FundStorageBody body, CancellationToken ct)
        => Result(await _service.UpdateAsync(id, body, ct));

    [HttpDelete("api/fund-storages/{id:int}")]
    public async Task<IActionResult> Destroy(int id, CancellationToken ct)
        => Result(await _service.DestroyAsync(id, ct));

    [HttpGet("api/fund-transfers")]
    public async Task<IActionResult> Transfers(CancellationToken ct)
        => Result(await _service.TransfersAsync(Q("date_from"), Q("date_to"), ct));

    [HttpPost("api/fund-transfers")]
    public async Task<IActionResult> CreateTransfer([FromBody] FundTransferBody body, CancellationToken ct)
    {
        var (u, e) = await AuthAsync(ct); if (e is not null) return e;
        return Result(await _service.CreateTransferAsync(u!, body, ct));
    }

    [HttpDelete("api/fund-transfers/{id:int}")]
    public async Task<IActionResult> DeleteTransfer(int id, CancellationToken ct)
        => Result(await _service.DeleteTransferAsync(id, ct));

    [HttpPost("api/fund-storage-syncs")]
    public async Task<IActionResult> AddSync([FromBody] FundSyncBody body, CancellationToken ct)
    {
        var (u, e) = await AuthAsync(ct); if (e is not null) return e;
        return Result(await _service.AddSyncAsync(u!, body, ct));
    }

    [HttpDelete("api/fund-storage-syncs/{id:int}")]
    public async Task<IActionResult> DeleteSync(int id, CancellationToken ct)
    {
        var (u, e) = await AuthAsync(ct); if (e is not null) return e;
        return Result(await _service.DeleteSyncAsync(u!, id, ct));
    }

    [HttpPost("api/tron-tx-lookup")]
    public async Task<IActionResult> TronTxLookup([FromBody] TronLookupBody body, CancellationToken ct)
        => Result(await _service.TronTxLookupAsync(body?.TxLink, ct));
}

public record TronLookupBody([property: System.Text.Json.Serialization.JsonPropertyName("tx_link")] string? TxLink);
