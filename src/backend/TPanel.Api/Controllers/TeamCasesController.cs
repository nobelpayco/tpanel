using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.Cases;

namespace TPanel.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/team-cases")]
public class TeamCasesController : AdminControllerBase
{
    private readonly ITeamCaseService _service;
    private readonly ICurrentUser _currentUser;

    public TeamCasesController(ITeamCaseService service, ICurrentUser currentUser)
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

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct) => Result(await _service.IndexAsync(ct));

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Show(int id, CancellationToken ct)
        => Result(await _service.ShowAsync(id, Q("date_from"), Q("date_to"), ct));

    [HttpGet("{id:int}/payments")]
    public async Task<IActionResult> Payments(int id, CancellationToken ct)
        => Result(await _service.PaymentsAsync(id, Q("date"), Q("date_from"), Q("date_to"), ct));

    [HttpPost("{id:int}/payments")]
    public async Task<IActionResult> AddPayment(int id, [FromBody] CasePaymentBody body, CancellationToken ct)
    {
        var (u, e) = await AuthAsync(ct); if (e is not null) return e;
        return Result(await _service.AddPaymentAsync(u!, id, body, ct));
    }

    [HttpDelete("{id:int}/payments/{paymentId:int}")]
    public async Task<IActionResult> DeletePayment(int id, int paymentId, CancellationToken ct)
    {
        var (u, e) = await AuthAsync(ct); if (e is not null) return e;
        return Result(await _service.DeletePaymentAsync(u!, id, paymentId, ct));
    }

    [HttpPost("{id:int}/transfers")]
    public async Task<IActionResult> AddTransfer(int id, [FromBody] TeamTransferBody body, CancellationToken ct)
    {
        var (u, e) = await AuthAsync(ct); if (e is not null) return e;
        return Result(await _service.AddTransferAsync(u!, id, body, ct));
    }

    [HttpDelete("{id:int}/transfers/{transferId:int}")]
    public async Task<IActionResult> DeleteTransfer(int id, int transferId, CancellationToken ct)
    {
        var (u, e) = await AuthAsync(ct); if (e is not null) return e;
        return Result(await _service.DeleteTransferAsync(u!, id, transferId, ct));
    }

    [HttpPost("{id:int}/syncs")]
    public async Task<IActionResult> AddSync(int id, [FromBody] TeamSyncBody body, CancellationToken ct)
    {
        var (u, e) = await AuthAsync(ct); if (e is not null) return e;
        return Result(await _service.AddSyncAsync(u!, id, body, ct));
    }

    [HttpDelete("{id:int}/syncs/{syncId:int}")]
    public async Task<IActionResult> DeleteSync(int id, int syncId, CancellationToken ct)
    {
        var (u, e) = await AuthAsync(ct); if (e is not null) return e;
        return Result(await _service.DeleteSyncAsync(u!, id, syncId, ct));
    }
}
