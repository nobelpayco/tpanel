using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.Management;

namespace TPanel.Api.Controllers;

[Authorize]
public abstract class MgmtControllerBase : ControllerBase
{
    protected readonly ICurrentUser CurrentUser;
    protected MgmtControllerBase(ICurrentUser cu) => CurrentUser = cu;
    protected IActionResult M(MgmtResult r) => StatusCode(r.Status, r.Body);
    protected string? Q(string k) => string.IsNullOrWhiteSpace(Request.Query[k]) ? null : Request.Query[k].ToString();
    protected int? Qi(string k) => int.TryParse(Request.Query[k], out var v) ? v : null;
    protected async Task<(Domain.Entities.User? u, IActionResult? e)> AuthAsync(CancellationToken ct)
    {
        var u = await CurrentUser.GetUserAsync(ct);
        return u is null ? (null, Unauthorized(new { message = "Unauthenticated." })) : (u, null);
    }
}

[ApiController, Route("api/teams")]
public class TeamsController : MgmtControllerBase
{
    private readonly ITeamMgmtService _s;
    public TeamsController(ITeamMgmtService s, ICurrentUser cu) : base(cu) => _s = s;
    [HttpGet] public async Task<IActionResult> Index(CancellationToken ct) => M(await _s.IndexAsync(Q("status") ?? "1", Q("search"), ct));
    [HttpGet("{id:int}")] public async Task<IActionResult> Show(int id, CancellationToken ct) => M(await _s.ShowAsync(id, ct));
    [HttpPost] public async Task<IActionResult> Store([FromBody] TeamUpsertBody b, CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return M(await _s.StoreAsync(u!, b, ct)); }
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, [FromBody] TeamUpsertBody b, CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return M(await _s.UpdateAsync(u!, id, b, ct)); }
    [HttpDelete("{id:int}")] public async Task<IActionResult> Destroy(int id, CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return M(await _s.DestroyAsync(u!, id, ct)); }
}

[ApiController, Authorize]
public class MerchantsController : MgmtControllerBase
{
    private readonly IMerchantMgmtService _s;
    public MerchantsController(IMerchantMgmtService s, ICurrentUser cu) : base(cu) => _s = s;
    [HttpGet("api/merchants")] public async Task<IActionResult> Index(CancellationToken ct) => M(await _s.IndexAsync(Q("status") ?? "1", ct));
    [HttpPost("api/merchants")] public async Task<IActionResult> Store([FromBody] MerchantUpsertBody b, CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return M(await _s.StoreAsync(u!, b, ct)); }
    [HttpPut("api/merchants/{id:int}")] public async Task<IActionResult> Update(int id, [FromBody] MerchantUpsertBody b, CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return M(await _s.UpdateAsync(u!, id, b, ct)); }
    [HttpDelete("api/merchants/{id:int}")] public async Task<IActionResult> Destroy(int id, CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return M(await _s.DestroyAsync(u!, id, ct)); }
    [HttpGet("api/merchants/{id:int}/credentials")] public async Task<IActionResult> Credentials(int id, CancellationToken ct) => M(await _s.ShowCredentialsAsync(id, ct));
    [HttpPost("api/merchants/{id:int}/rotate-secret")] public async Task<IActionResult> RotateSecret(int id, CancellationToken ct) => M(await _s.RotateSecretAsync(id, ct));
    [HttpPost("api/merchants/{id:int}/rotate-key")] public async Task<IActionResult> RotateKey(int id, CancellationToken ct) => M(await _s.RotateKeyAsync(id, ct));
    [HttpGet("api/merchant-groups")] public async Task<IActionResult> Groups(CancellationToken ct) => M(await _s.GroupsAsync(ct));
    [HttpPost("api/merchant-groups")] public async Task<IActionResult> StoreGroup([FromBody] GroupBody b, CancellationToken ct) => M(await _s.StoreGroupAsync(b.Name, ct));
    [HttpPut("api/merchant-groups/{id:int}")] public async Task<IActionResult> UpdateGroup(int id, [FromBody] GroupBody b, CancellationToken ct) => M(await _s.UpdateGroupAsync(id, b, ct));
    [HttpDelete("api/merchant-groups/{id:int}")] public async Task<IActionResult> DestroyGroup(int id, CancellationToken ct) => M(await _s.DestroyGroupAsync(id, ct));
    [HttpPost("api/merchant-groups/assign")] public async Task<IActionResult> Assign([FromBody] AssignGroupBody b, CancellationToken ct) => M(await _s.AssignToGroupAsync(b, ct));
}

[ApiController, Route("api/bank-accounts")]
public class BankAccountsController : MgmtControllerBase
{
    private readonly IBankAccountMgmtService _s;
    public BankAccountsController(IBankAccountMgmtService s, ICurrentUser cu) : base(cu) => _s = s;
    [HttpGet] public async Task<IActionResult> Index(CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return M(await _s.IndexAsync(u!, Q("status") ?? "1", Qi("bank_id"), Qi("team_id"), Q("search"), ct)); }
    [HttpGet("banks")] public async Task<IActionResult> Banks(CancellationToken ct) => M(await _s.BanksAsync(ct));
    [HttpGet("teams")] public async Task<IActionResult> Teams(CancellationToken ct) => M(await _s.TeamsAsync(ct));
    [HttpPost("identify")] public async Task<IActionResult> Identify([FromBody] IdentifyIbanBody b, CancellationToken ct) => M(await _s.IdentifyAsync(b.Iban, ct));
    [HttpPost("reorder")] public async Task<IActionResult> Reorder([FromBody] ReorderBody b, CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return M(await _s.ReorderAsync(u!, b.Ids, ct)); }
    [HttpPost] public async Task<IActionResult> Store([FromBody] BankAccountUpsertBody b, CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return M(await _s.StoreAsync(u!, b, ct)); }
    [HttpGet("{id:int}")] public async Task<IActionResult> Show(int id, CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return M(await _s.ShowAsync(u!, id, ct)); }
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, [FromBody] BankAccountUpsertBody b, CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return M(await _s.UpdateAsync(u!, id, b, ct)); }
    [HttpPost("{id:int}/sort-order")] public async Task<IActionResult> SetSort(int id, [FromBody] SetSortBody b, CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return M(await _s.SetSortAsync(u!, id, b.Position, ct)); }
    [HttpDelete("{id:int}")] public async Task<IActionResult> Destroy(int id, CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return M(await _s.DestroyAsync(u!, id, ct)); }
}

[ApiController, Route("api/users")]
public class UsersController : MgmtControllerBase
{
    private readonly IUserMgmtService _s;
    public UsersController(IUserMgmtService s, ICurrentUser cu) : base(cu) => _s = s;
    [HttpGet] public async Task<IActionResult> Index(CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return M(await _s.IndexAsync(u!, Q("user_type"), Q("status"), Q("search"), ct)); }
    [HttpGet("options")] public async Task<IActionResult> Options(CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return M(await _s.OptionsAsync(u!, ct)); }
    [HttpPost] public async Task<IActionResult> Store([FromBody] UserCreateBody b, CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return M(await _s.StoreAsync(u!, b, ct)); }
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, [FromBody] UserUpdateBody b, CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return M(await _s.UpdateAsync(u!, id, b, ct)); }
    [HttpDelete("{id:int}")] public async Task<IActionResult> Destroy(int id, CancellationToken ct) { var (u, e) = await AuthAsync(ct); if (e is not null) return e; return M(await _s.DestroyAsync(u!, id, ct)); }
}

[ApiController, Route("api/blacklist")]
public class BlacklistController : MgmtControllerBase
{
    private readonly IBlacklistMgmtService _s;
    public BlacklistController(IBlacklistMgmtService s, ICurrentUser cu) : base(cu) => _s = s;
    [HttpGet] public async Task<IActionResult> Index(CancellationToken ct) => M(await _s.IndexAsync(Q("search"), Q("type"), ct));
    [HttpGet("export")] public async Task<IActionResult> Export(CancellationToken ct)
    {
        var bytes = await _s.ExportAsync(Q("search"), Q("type"), ct);
        var name = $"blacklist_{DateTime.UtcNow:yyyy-MM-dd_HHmmss}.xlsx";
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", name);
    }
    [HttpPost] public async Task<IActionResult> Store([FromBody] BlacklistStoreBody b, CancellationToken ct) => M(await _s.StoreAsync(b, ct));
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, [FromBody] BlacklistUpdateBody b, CancellationToken ct) => M(await _s.UpdateAsync(id, b.Desc, ct));
    [HttpDelete("{id:int}")] public async Task<IActionResult> Destroy(int id, CancellationToken ct) => M(await _s.DestroyAsync(id, ct));
    [HttpPost("check")] public async Task<IActionResult> Check([FromBody] BlacklistCheckBody b, CancellationToken ct) => M(await _s.CheckAsync(b.Val, ct));
}

[ApiController, Route("api/intermediaries")]
public class IntermediariesController : MgmtControllerBase
{
    private readonly IIntermediaryMgmtService _s;
    public IntermediariesController(IIntermediaryMgmtService s, ICurrentUser cu) : base(cu) => _s = s;
    [HttpGet] public async Task<IActionResult> Index(CancellationToken ct) => M(await _s.IndexAsync(Q("status") ?? "1", ct));
    [HttpPost] public async Task<IActionResult> Store([FromBody] IntermediaryStoreBody b, CancellationToken ct) => M(await _s.StoreAsync(b, ct));
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, [FromBody] IntermediaryUpdateBody b, CancellationToken ct) => M(await _s.UpdateAsync(id, b, ct));
    [HttpDelete("{id:int}")] public async Task<IActionResult> Destroy(int id, CancellationToken ct) => M(await _s.DestroyAsync(id, ct));
    [HttpPost("attach-merchant")] public async Task<IActionResult> AttachMerchant([FromBody] AttachMerchantBody b, CancellationToken ct) => M(await _s.AttachMerchantAsync(b, ct));
    [HttpDelete("merchant/{pivotId:int}")] public async Task<IActionResult> DetachMerchant(int pivotId, CancellationToken ct) => M(await _s.DetachMerchantAsync(pivotId, ct));
    [HttpPut("merchant/{pivotId:int}")] public async Task<IActionResult> UpdateMerchantRate(int pivotId, [FromBody] UpdateRateBody b, CancellationToken ct) => M(await _s.UpdateMerchantRateAsync(pivotId, b, ct));
    [HttpPost("attach-team")] public async Task<IActionResult> AttachTeam([FromBody] AttachTeamBody b, CancellationToken ct) => M(await _s.AttachTeamAsync(b, ct));
    [HttpDelete("team/{pivotId:int}")] public async Task<IActionResult> DetachTeam(int pivotId, CancellationToken ct) => M(await _s.DetachTeamAsync(pivotId, ct));
    [HttpPut("team/{pivotId:int}")] public async Task<IActionResult> UpdateTeamRate(int pivotId, [FromBody] UpdateRateBody b, CancellationToken ct) => M(await _s.UpdateTeamRateAsync(pivotId, b, ct));
}

[ApiController, Route("api/settings")]
public class SettingsController : MgmtControllerBase
{
    private readonly ISettingsMgmtService _s;
    public SettingsController(ISettingsMgmtService s, ICurrentUser cu) : base(cu) => _s = s;
    [HttpGet] public async Task<IActionResult> Index(CancellationToken ct) => M(await _s.IndexAsync(ct));
    [HttpPut] public async Task<IActionResult> Update([FromBody] SettingsUpdateBody b, CancellationToken ct) => M(await _s.UpdateAsync(b, ct));
    [HttpGet("logs")] public async Task<IActionResult> Logs(CancellationToken ct) => M(await _s.LogsAsync(Q("direction"), Q("type"), Q("q"), Qi("page") ?? 1, ct));
    [HttpGet("logs/{id:int}")] public async Task<IActionResult> LogDetail(int id, CancellationToken ct) => M(await _s.LogDetailAsync(id, ct));
    [HttpPost("telegram/find-chat-id")] public async Task<IActionResult> FindChatId([FromBody] FindChatIdBody b, CancellationToken ct) => M(await _s.FindChatIdAsync(b.GroupName, ct));

    // AI test uçları (Faz 6b — ClaudeVisionService)
    [HttpPost("anthropic/test")]
    public async Task<IActionResult> TestAnthropic([FromServices] TPanel.Application.Features.Receipts.IClaudeVisionService vision, CancellationToken ct)
    {
        var u = await CurrentUser.GetUserAsync(ct);
        if (u is null) return Unauthorized(new { message = "Unauthenticated." });
        if (!u.IsSuperAdmin) return StatusCode(403, new { message = "Bu işlem için yetkiniz yok." });
        var (ok, msg) = await vision.PingAsync(ct);
        return StatusCode(ok ? 200 : 422, new { ok, message = msg });
    }

    [HttpPost("anthropic/analyze-test")]
    public async Task<IActionResult> AnalyzeTest([FromBody] AnalyzeTestBody b, [FromServices] TPanel.Application.Features.Receipts.IClaudeVisionService vision, CancellationToken ct)
    {
        var u = await CurrentUser.GetUserAsync(ct);
        if (u is null) return Unauthorized(new { message = "Unauthenticated." });
        if (!u.CanApproveTransactions) return StatusCode(403, new { message = "Bu işlem için yetkiniz yok." });
        if (string.IsNullOrEmpty(b?.FileBase64) || string.IsNullOrEmpty(b.MimeType)) return StatusCode(422, new { message = "Dosya zorunludur." });
        var allowed = new[] { "application/pdf", "image/jpeg", "image/png", "image/webp" };
        if (!allowed.Contains(b.MimeType)) return StatusCode(422, new { message = "Geçersiz mime type." });
        var raw = b.FileBase64; var comma = raw.IndexOf(','); if (comma >= 0) raw = raw[(comma + 1)..];
        byte[] binary; try { binary = Convert.FromBase64String(raw); } catch { return StatusCode(422, new { message = "Geçersiz base64." }); }
        if (binary.Length > 10 * 1024 * 1024) return StatusCode(422, new { message = "Dosya 10 MB'ı aşamaz." });
        var expected = new TPanel.Application.Features.Receipts.ReceiptExpected(b.Amount ?? 0, b.Iban ?? "", b.Recipient ?? "");
        var result = await vision.AnalyzeReceiptAsync(binary, b.MimeType, expected, ct);
        if (result is null) return StatusCode(502, new { message = "AI analiz başarısız (API key veya bağlantı sorunu)." });
        var cost = vision.EstimateCost(result.InputTokens, result.OutputTokens, result.Model);
        using var doc = System.Text.Json.JsonDocument.Parse(result.Json.GetRawText());
        return Ok(new { result = result.Json, estimated_cost_usd = Math.Round(cost, 5) });
    }
}

public record AnalyzeTestBody(
    [property: System.Text.Json.Serialization.JsonPropertyName("file_base64")] string? FileBase64,
    [property: System.Text.Json.Serialization.JsonPropertyName("file_name")] string? FileName,
    [property: System.Text.Json.Serialization.JsonPropertyName("mime_type")] string? MimeType,
    [property: System.Text.Json.Serialization.JsonPropertyName("amount")] double? Amount,
    [property: System.Text.Json.Serialization.JsonPropertyName("iban")] string? Iban,
    [property: System.Text.Json.Serialization.JsonPropertyName("recipient")] string? Recipient);
