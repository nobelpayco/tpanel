using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TPanel.Api.Spa;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.Transactions;

namespace TPanel.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/withdrawals")]
public class WithdrawalsController : AdminControllerBase
{
    private readonly IWithdrawAdminService _service;
    private readonly ICurrentUser _currentUser;

    public WithdrawalsController(IWithdrawAdminService service, ICurrentUser currentUser)
    {
        _service = service;
        _currentUser = currentUser;
    }

    private async Task<(Domain.Entities.User? user, IActionResult? error)> AuthAsync(CancellationToken ct)
    {
        var user = await LoadUserAsync(_currentUser, ct);
        return user is null ? (null, Unauthorized(new { message = "Unauthenticated." })) : (user, null);
    }

    [HttpGet("pending")]
    public async Task<IActionResult> Pending(CancellationToken ct)
    {
        var (user, err) = await AuthAsync(ct); if (err is not null) return err;
        return Result(await _service.PendingAsync(user!, BuildFilter(), ct));
    }

    [HttpGet("all")]
    public async Task<IActionResult> All(CancellationToken ct)
    {
        var (user, err) = await AuthAsync(ct); if (err is not null) return err;
        return Result(await _service.AllAsync(user!, BuildFilter(), ct));
    }

    [HttpGet("receipt-review")]
    public async Task<IActionResult> ReceiptReview(CancellationToken ct)
    {
        var (user, err) = await AuthAsync(ct); if (err is not null) return err;
        var status = Request.Query["status"].ToString();
        int.TryParse(Request.Query["page"], out var page); if (page < 1) page = 1;
        if (!int.TryParse(Request.Query["per_page"], out var perPage)) perPage = 50;
        return Result(await _service.ReceiptReviewAsync(user!, string.IsNullOrEmpty(status) ? null : status, page, perPage, ct));
    }

    [HttpGet("{id:int}/detail")]
    public async Task<IActionResult> Detail(int id, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(ct); if (err is not null) return err;
        return Result(await _service.DetailAsync(user!, id, ct));
    }

    [HttpPost("take")]
    public async Task<IActionResult> Take([FromBody] IdBody body, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(ct); if (err is not null) return err;
        return Result(await _service.TakeAsync(user!, body.Id, ClientIp, ct));
    }

    [HttpPost("release")]
    public async Task<IActionResult> Release([FromBody] IdBody body, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(ct); if (err is not null) return err;
        return Result(await _service.ReleaseAsync(user!, body.Id, ClientIp, ct));
    }

    [HttpPost("approve")]
    public async Task<IActionResult> Approve([FromBody] IdBody body, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(ct); if (err is not null) return err;
        return Result(await _service.ApproveAsync(user!, body.Id, ClientIp, ct));
    }

    [HttpPost("reject")]
    public async Task<IActionResult> Reject([FromBody] RejectBody body, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(ct); if (err is not null) return err;
        return Result(await _service.RejectAsync(user!, body.Id, body.RejectType, ClientIp, ct));
    }

    [HttpPost("bulk-assign")]
    public async Task<IActionResult> BulkAssign([FromBody] BulkAssignBody body, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(ct); if (err is not null) return err;
        if (body.Ids is null || body.TeamId is null) return StatusCode(422, new { message = "ids ve team_id zorunludur." });
        return Result(await _service.BulkAssignAsync(user!, body.Ids, body.TeamId.Value, ClientIp, ct));
    }

    [HttpPost("notify-missing-receipts")]
    public async Task<IActionResult> NotifyMissing([FromBody] NotifyMissingBody body, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(ct); if (err is not null) return err;
        if (body.TeamId is null) return StatusCode(422, new { message = "team_id zorunludur." });
        return Result(await _service.NotifyMissingReceiptsAsync(user!, body.TeamId.Value, ct));
    }

    [HttpPost("{id:int}/resend-callback")]
    public async Task<IActionResult> ResendCallback(int id, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(ct); if (err is not null) return err;
        return Result(await _service.ResendCallbackAsync(user!, id, ct));
    }

    [HttpGet("{id:int}/receipts")]
    public async Task<IActionResult> Receipts(int id, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(ct); if (err is not null) return err;
        return Result(await _service.ReceiptsAsync(user!, id, ct));
    }

    [HttpGet("{id:int}/receipts/{rid:int}")]
    public async Task<IActionResult> DownloadReceipt(int id, int rid, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(ct); if (err is not null) return err;
        var dl = await _service.DownloadReceiptAsync(user!, id, rid, ct);
        if (!dl.Ok) return StatusCode(dl.ErrorStatus);
        Response.Headers["Content-Disposition"] = $"inline; filename=\"{dl.OriginalName}\"";
        return PhysicalFile(dl.FullPath!, dl.Mime!);
    }

    [HttpPost("{id:int}/receipts")]
    [RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> UploadReceipt(int id, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(ct); if (err is not null) return err;

        byte[] binary;
        string originalName, mime, ext;

        if (Request.HasFormContentType && Request.Form.Files.Count > 0)
        {
            var f = Request.Form.Files[0];
            using var ms = new MemoryStream();
            await f.CopyToAsync(ms, ct);
            binary = ms.ToArray();
            var detected = ReceiptMimeDetector.DetectExtension(binary);
            if (detected is null or "gif") return StatusCode(422, new { message = "Sadece PDF, JPG, PNG veya WEBP yükleyebilirsiniz." });
            ext = detected;
            mime = MimeFromExt(ext);
            originalName = string.IsNullOrEmpty(f.FileName) ? $"receipt.{ext}" : f.FileName;
        }
        else
        {
            using var reader = new StreamReader(Request.Body);
            var raw = await reader.ReadToEndAsync(ct);
            JsonElement json;
            try { json = JsonSerializer.Deserialize<JsonElement>(raw); }
            catch { return StatusCode(422, new { message = "Geçersiz istek." }); }

            var b64 = json.TryGetProperty("file_base64", out var fb) ? fb.GetString() : null;
            var fileName = json.TryGetProperty("file_name", out var fn) ? fn.GetString() : null;
            mime = json.TryGetProperty("mime_type", out var mt) ? mt.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(b64) || string.IsNullOrEmpty(fileName))
                return StatusCode(422, new { message = "file_base64 ve file_name zorunludur." });

            var extMap = new Dictionary<string, string>
            {
                ["application/pdf"] = "pdf", ["image/jpeg"] = "jpg", ["image/png"] = "png", ["image/webp"] = "webp",
            };
            if (!extMap.TryGetValue(mime, out var mappedExt))
                return StatusCode(422, new { message = "Sadece PDF, JPG, PNG veya WEBP yükleyebilirsiniz." });
            ext = mappedExt;

            var comma = b64.IndexOf(',');
            if (comma >= 0) b64 = b64[(comma + 1)..];
            try { binary = Convert.FromBase64String(b64); }
            catch { return StatusCode(422, new { message = "Geçersiz base64 dosya verisi." }); }
            originalName = fileName!;
        }

        return Result(await _service.UploadReceiptAsync(user!, id, binary, originalName, mime, ext, ClientIp, ct));
    }

    [HttpPost("{id:int}/receipts/{rid:int}/verify")]
    public async Task<IActionResult> Verify(int id, int rid, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(ct); if (err is not null) return err;
        return Result(await _service.VerifyReceiptAsync(user!, id, rid, ct));
    }

    [HttpPost("{id:int}/receipts/{rid:int}/manual-verify")]
    public async Task<IActionResult> ManualVerify(int id, int rid, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(ct); if (err is not null) return err;
        return Result(await _service.ManualVerifyReceiptAsync(user!, id, rid, ClientIp, ct));
    }

    [HttpPost("{id:int}/receipts/{rid:int}/flag-fake")]
    public async Task<IActionResult> FlagFake(int id, int rid, [FromBody] FlagFakeBody? body, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(ct); if (err is not null) return err;
        return Result(await _service.FlagFakeReceiptAsync(user!, id, rid, body?.Reason, ClientIp, ct));
    }

    // ---- Manuel çekim ekleme ----
    [HttpGet("manual/meta")]
    public async Task<IActionResult> ManualMeta([FromServices] ITransactionAdminStore store, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(ct); if (err is not null) return err;
        if (!user!.IsAdmin) return StatusCode(403, new { message = "Yetkisiz." });

        var merchants = await store.GetActiveMerchantsAsync(ct);
        var teams = await store.GetTeamsForFilterAsync(ct);
        if (user.HasTeamScope) teams = teams.Where(t => t.Id == user.TeamId).ToList();

        return Ok(new
        {
            merchants = merchants.Select(m => new { id = m.Id, name = m.Name }),
            teams = teams.Select(t => new { id = t.Id, name = t.Name, status = t.Status }),
        });
    }

    [HttpGet("manual/team/{teamId:int}")]
    public async Task<IActionResult> ManualTeamMeta(int teamId, [FromServices] ITransactionAdminStore store, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(ct); if (err is not null) return err;
        if (!user!.IsAdmin) return StatusCode(403, new { message = "Yetkisiz." });
        if (user.HasTeamScope && teamId != user.TeamId) return StatusCode(403, new { message = "Yetkisiz." });

        var banks = await store.GetTeamBankAccountsAsync(teamId, ct);
        var agents = await store.GetTeamAgentsAsync(teamId, ct);
        return Ok(new
        {
            banks = banks.Select(b => new { id = b.Id, name = b.Name }),
            agents = agents.Select(a => new { id = a.Id, name = a.Name }),
        });
    }

    [HttpPost("manual")]
    public async Task<IActionResult> CreateManual([FromBody] ManualWithdrawBody body,
        [FromServices] ITransactionAdminStore store, CancellationToken ct)
    {
        var (user, err) = await AuthAsync(ct); if (err is not null) return err;
        if (!user!.IsAdmin) return StatusCode(403, new { message = "Yetkisiz." });

        if (body.MerchantId is null or <= 0) return BadRequest(new { message = "Merchant seçilmeli." });
        var teamId = user.HasTeamScope ? user.TeamId : (body.TeamId ?? 0);
        if (teamId <= 0) return BadRequest(new { message = "Takım seçilmeli." });
        if (string.IsNullOrWhiteSpace(body.Name)) return BadRequest(new { message = "Müşteri adı girilmeli." });
        if (body.Amount is null or <= 0) return BadRequest(new { message = "Geçerli bir tutar girilmeli." });
        if (string.IsNullOrWhiteSpace(body.Iban)) return BadRequest(new { message = "IBAN girilmeli." });

        var id = await store.CreateManualWithdrawAsync(
            body.MerchantId.Value, teamId, body.BankId, body.AgentId,
            body.Name!.Trim(), body.Amount.Value, body.Iban!.Trim(), user.Id, ClientIp, ct);

        return Ok(new { id, message = "Manuel çekim eklendi." });
    }

    private static string MimeFromExt(string ext) => ext switch
    {
        "jpg" => "image/jpeg", "png" => "image/png", "webp" => "image/webp", "pdf" => "application/pdf", _ => "application/octet-stream",
    };
}
