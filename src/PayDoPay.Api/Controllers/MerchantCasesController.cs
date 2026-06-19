using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PayDoPay.Application.Common.Interfaces;
using PayDoPay.Application.Features.Cases;

namespace PayDoPay.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/merchant-cases")]
public class MerchantCasesController : AdminControllerBase
{
    private readonly IMerchantCaseService _service;
    private readonly ICurrentUser _currentUser;

    public MerchantCasesController(IMerchantCaseService service, ICurrentUser currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    private string? Q(string k) => string.IsNullOrWhiteSpace(Request.Query[k]) ? null : Request.Query[k].ToString();
    private bool IsGroup => Request.Query["type"] == "group";

    private async Task<(Domain.Entities.User? u, IActionResult? e)> AuthAsync(CancellationToken ct)
    {
        var u = await _currentUser.GetUserAsync(ct);
        return u is null ? (null, Unauthorized(new { message = "Unauthenticated." })) : (u, null);
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        var (u, e) = await AuthAsync(ct); if (e is not null) return e;
        return Result(await _service.IndexAsync(u!, ct));
    }

    [HttpGet("paylira-daily-net")]
    public async Task<IActionResult> PayliraDailyNet(CancellationToken ct)
    {
        var (u, e) = await AuthAsync(ct); if (e is not null) return e;
        return Result(await _service.PayliraDailyNetAsync(u!, Q("date_from"), Q("date_to"), ct));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Show(int id, CancellationToken ct)
    {
        var (u, e) = await AuthAsync(ct); if (e is not null) return e;
        return Result(await _service.ShowAsync(u!, id, IsGroup, Q("date_from"), Q("date_to"), ct));
    }

    [HttpGet("{id:int}/payments")]
    public async Task<IActionResult> Payments(int id, CancellationToken ct)
    {
        var (u, e) = await AuthAsync(ct); if (e is not null) return e;
        return Result(await _service.PaymentsAsync(u!, id, IsGroup, Q("date"), Q("date_from"), Q("date_to"), ct));
    }

    [HttpPost("{id:int}/payments")]
    public async Task<IActionResult> AddPayment(int id, [FromBody] MerchantPaymentBody body, CancellationToken ct)
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
}
