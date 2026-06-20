using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.Cases;

namespace TPanel.Api.Controllers;

// ---- ortak yardımcı taban ----
[Authorize]
public abstract class CaseControllerBase : AdminControllerBase
{
    protected readonly ICurrentUser CurrentUser;
    protected CaseControllerBase(ICurrentUser cu) => CurrentUser = cu;
    protected string? Q(string k) => string.IsNullOrWhiteSpace(Request.Query[k]) ? null : Request.Query[k].ToString();
    protected async Task<(Domain.Entities.User? u, IActionResult? e)> AuthAsync(CancellationToken ct)
    {
        var u = await CurrentUser.GetUserAsync(ct);
        return u is null ? (null, Unauthorized(new { message = "Unauthenticated." })) : (u, null);
    }
}

[ApiController]
[Route("api/intermediary-cases")]
public class IntermediaryCasesController : CaseControllerBase
{
    private readonly IIntermediaryCaseService _s;
    public IntermediaryCasesController(IIntermediaryCaseService s, ICurrentUser cu) : base(cu) => _s = s;

    [HttpGet] public async Task<IActionResult> Index(CancellationToken ct) => Result(await _s.IndexAsync(ct));
    [HttpGet("{id:int}")] public async Task<IActionResult> Show(int id, CancellationToken ct) => Result(await _s.ShowAsync(id, Q("date_from"), Q("date_to"), ct));
    [HttpGet("{id:int}/payments")] public async Task<IActionResult> Payments(int id, CancellationToken ct) => Result(await _s.PaymentsAsync(id, Q("date"), Q("date_from"), Q("date_to"), ct));

    [HttpPost("{id:int}/payments")]
    public async Task<IActionResult> AddPayment(int id, [FromBody] IntermediaryPaymentBody b, CancellationToken ct)
    { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return Result(await _s.AddPaymentAsync(u!, id, b, ct)); }

    [HttpDelete("{id:int}/payments/{paymentId:int}")]
    public async Task<IActionResult> DeletePayment(int id, int paymentId, CancellationToken ct) => Result(await _s.DeletePaymentAsync(id, paymentId, ct));
}

[ApiController]
[Authorize]
public class PayliraPartnersController : CaseControllerBase
{
    private readonly IPartnerCaseService _s;
    public PayliraPartnersController(IPartnerCaseService s, ICurrentUser cu) : base(cu) => _s = s;

    [HttpGet("api/paylira-partners")] public async Task<IActionResult> Index(CancellationToken ct) => Result(await _s.IndexAsync(ct));
    [HttpGet("api/paylira-partners/{id:int}")] public async Task<IActionResult> Show(int id, CancellationToken ct) => Result(await _s.ShowAsync(id, Q("date_from"), Q("date_to"), ct));
    [HttpGet("api/paylira-partners/{id:int}/payments")] public async Task<IActionResult> Payments(int id, CancellationToken ct) => Result(await _s.PaymentsAsync(id, Q("date"), Q("date_from"), Q("date_to"), ct));

    [HttpPost("api/paylira-partners/{id:int}/payments")]
    public async Task<IActionResult> AddPayment(int id, [FromBody] PartnerPaymentBody b, CancellationToken ct)
    { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return Result(await _s.AddPaymentAsync(u!, id, b, ct)); }

    [HttpDelete("api/paylira-partners/{id:int}/payments/{paymentId:int}")]
    public async Task<IActionResult> DeletePayment(int id, int paymentId, CancellationToken ct) => Result(await _s.DeletePaymentAsync(id, paymentId, ct));

    [HttpGet("api/paylira-partners/{id:int}/capitals")] public async Task<IActionResult> Capitals(int id, CancellationToken ct) => Result(await _s.CapitalsAsync(id, Q("date_from"), Q("date_to"), ct));

    [HttpPost("api/paylira-partners/{id:int}/capitals")]
    public async Task<IActionResult> AddCapital(int id, [FromBody] CapitalBody b, CancellationToken ct)
    { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return Result(await _s.AddCapitalAsync(u!, id, b, ct)); }

    [HttpDelete("api/paylira-partners/{id:int}/capitals/{capitalId:int}")]
    public async Task<IActionResult> DeleteCapital(int id, int capitalId, CancellationToken ct) => Result(await _s.DeleteCapitalAsync(id, capitalId, ct));

    [HttpPost("api/paylira-partners/{id:int}/transfers")]
    public async Task<IActionResult> AddTransfer(int id, [FromBody] PartnerTransferBody b, CancellationToken ct)
    { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return Result(await _s.AddTransferAsync(u!, id, b, ct)); }

    [HttpDelete("api/paylira-partners/{id:int}/transfers/{transferId:int}")]
    public async Task<IActionResult> DeleteTransfer(int id, int transferId, CancellationToken ct) => Result(await _s.DeleteTransferAsync(id, transferId, ct));

    [HttpGet("api/paylira-expenses")] public async Task<IActionResult> Expenses(CancellationToken ct) => Result(await _s.ExpensesAsync(Q("date_from"), Q("date_to"), ct));

    [HttpPost("api/paylira-expenses")]
    public async Task<IActionResult> AddExpense([FromBody] ExpenseBody b, CancellationToken ct)
    { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return Result(await _s.AddExpenseAsync(u!, b, ct)); }

    [HttpDelete("api/paylira-expenses/{id:int}")]
    public async Task<IActionResult> DeleteExpense(int id, CancellationToken ct)
    { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return Result(await _s.DeleteExpenseAsync(u!, id, ct)); }

    [HttpGet("api/paylira-partner-payments-all")] public async Task<IActionResult> AllPayments(CancellationToken ct) => Result(await _s.AllPartnerPaymentsAsync(Q("date_from"), Q("date_to"), ct));
}

[ApiController]
[Route("api/case-report")]
public class CaseReportController : CaseControllerBase
{
    private readonly ICaseReportService _s;
    public CaseReportController(ICaseReportService s, ICurrentUser cu) : base(cu) => _s = s;

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken ct)
    { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return Result(await _s.IndexAsync(u!, Q("date_from"), Q("date_to"), ct)); }

    [HttpGet("summary")]
    public async Task<IActionResult> Summary(CancellationToken ct)
    { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return Result(await _s.SummaryAsync(u!, Q("date_from"), Q("date_to"), ct)); }
}

[ApiController]
[Route("api/initial-balance")]
public class InitialBalanceController : CaseControllerBase
{
    private readonly IInitialBalanceService _s;
    public InitialBalanceController(IInitialBalanceService s, ICurrentUser cu) : base(cu) => _s = s;

    [HttpGet("entities")] public async Task<IActionResult> Entities(CancellationToken ct) => Result(await _s.EntitiesAsync(ct));

    [HttpPost]
    public async Task<IActionResult> Save([FromBody] InitialBalanceSaveBody b, CancellationToken ct)
    { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return Result(await _s.SaveAsync(u!, b, ct)); }

    [HttpPost("reset")]
    public async Task<IActionResult> Reset([FromBody] InitialBalanceResetBody b, CancellationToken ct)
    { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return Result(await _s.ResetAsync(u!, b?.Date, ct)); }
}
