using Microsoft.AspNetCore.Mvc;
using PayDoPay.Api.Spa;
using PayDoPay.Application.Features.PublicApi;

namespace PayDoPay.Api.Controllers.V1;

/// <summary>Oyuncu ödeme sayfası — public (u_id ile korunur, HMAC yok).</summary>
[ApiController]
[Route("api/v1/pay")]
public class PublicPaymentV1Controller : ControllerBase
{
    private readonly IPublicPaymentService _service;
    public PublicPaymentV1Controller(IPublicPaymentService service) => _service = service;

    private IActionResult V1(V1Result r) => StatusCode(r.HttpStatus, r.Body);

    [HttpGet("{uId}")]
    public async Task<IActionResult> Show(string uId, CancellationToken ct)
        => V1(await _service.ShowAsync(uId, ct));

    [HttpPost("{uId}/select-bank")]
    public async Task<IActionResult> SelectBank(string uId, [FromBody] SelectBankRequest req, CancellationToken ct)
        => V1(await _service.SelectBankAsync(uId, req, ct));

    [HttpPost("{uId}/paid")]
    public async Task<IActionResult> MarkPaid(string uId, CancellationToken ct)
        => V1(await _service.MarkPaidAsync(uId, ct));

    [HttpPost("{uId}/cancel")]
    public async Task<IActionResult> Cancel(string uId, CancellationToken ct)
        => V1(await _service.CancelAsync(uId, ct));

    [HttpPost("{uId}/receipt")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> UploadReceipt(string uId, [FromForm] IFormFile? receipt, CancellationToken ct)
    {
        if (receipt is null || receipt.Length == 0)
            return StatusCode(422, new { code = 422, status = false, message = "Sadece resim ve PDF dosyaları yüklenebilir." });

        if (receipt.Length > 10 * 1024 * 1024) // 10 MB
            return StatusCode(422, new { code = 422, status = false, message = "Dosya boyutu en fazla 10 MB olabilir." });

        await using var stream = receipt.OpenReadStream();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        var ext = ReceiptMimeDetector.DetectExtension(bytes);
        if (ext is null)
            return StatusCode(422, new { code = 422, status = false, message = "Sadece resim ve PDF dosyaları yüklenebilir." });

        return V1(await _service.UploadReceiptAsync(uId, bytes, ext, ct));
    }
}
