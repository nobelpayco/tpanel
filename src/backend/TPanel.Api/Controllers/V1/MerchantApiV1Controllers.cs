using Microsoft.AspNetCore.Mvc;
using TPanel.Application.Features.PublicApi;

namespace TPanel.Api.Controllers.V1;

/// <summary>HMAC ile korunan controller'lar için ortak taban.</summary>
public abstract class MerchantApiControllerBase : ControllerBase
{
    protected MerchantContext Merchant => (MerchantContext)HttpContext.Items["_merchant"]!;
    protected IActionResult V1(V1Result r) => StatusCode(r.HttpStatus, r.Body);
}

[ApiController]
[Route("api/v1")]
public class DepositApiV1Controller : MerchantApiControllerBase
{
    private readonly IDepositApiService _service;
    public DepositApiV1Controller(IDepositApiService service) => _service = service;

    [HttpPost("deposit")]
    public async Task<IActionResult> Store([FromBody] DepositApiRequest req, CancellationToken ct)
        => V1(await _service.StoreAsync(Merchant, req, ct));

    [HttpPost("deposit/direct")]
    public async Task<IActionResult> StoreDirect([FromBody] DirectDepositApiRequest req, CancellationToken ct)
        => V1(await _service.StoreDirectAsync(Merchant, req, ct));
}

[ApiController]
[Route("api/v1")]
public class WithdrawApiV1Controller : MerchantApiControllerBase
{
    private readonly IWithdrawApiService _service;
    public WithdrawApiV1Controller(IWithdrawApiService service) => _service = service;

    [HttpPost("withdraw")]
    public async Task<IActionResult> Store([FromBody] WithdrawApiRequest req, CancellationToken ct)
        => V1(await _service.StoreAsync(Merchant, req, ct));
}

[ApiController]
[Route("api/v1")]
public class TransactionApiV1Controller : MerchantApiControllerBase
{
    private readonly ITransactionApiService _service;
    public TransactionApiV1Controller(ITransactionApiService service) => _service = service;

    [HttpGet("transaction/{orderId}")]
    public async Task<IActionResult> Show(string orderId, CancellationToken ct)
        => V1(await _service.ShowAsync(Merchant, orderId, ct));
}
