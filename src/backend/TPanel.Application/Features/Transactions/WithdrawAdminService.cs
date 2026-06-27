using System.Security.Cryptography;
using TPanel.Application.Common;
using TPanel.Application.Common.Interfaces;
using TPanel.Domain.Entities;

namespace TPanel.Application.Features.Transactions;

public record ReceiptDownload(bool Ok, int ErrorStatus, string? Mime, string? OriginalName, string? FullPath);

public interface IWithdrawAdminService
{
    Task<ApiResult> PendingAsync(User user, TxFilter f, CancellationToken ct = default);
    Task<ApiResult> AllAsync(User user, TxFilter f, CancellationToken ct = default);
    Task<ApiResult> DetailAsync(User user, int id, CancellationToken ct = default);
    Task<ApiResult> TakeAsync(User user, int id, string ip, CancellationToken ct = default);
    Task<ApiResult> ReleaseAsync(User user, int id, string ip, CancellationToken ct = default);
    Task<ApiResult> ApproveAsync(User user, int id, string ip, CancellationToken ct = default);
    Task<ApiResult> RejectAsync(User user, int id, int rejectType, string ip, CancellationToken ct = default);
    Task<ApiResult> BulkAssignAsync(User user, IReadOnlyList<int> ids, int teamId, string ip, CancellationToken ct = default);
    Task<ApiResult> ReceiptsAsync(User user, int id, CancellationToken ct = default);
    Task<ApiResult> UploadReceiptAsync(User user, int id, byte[] binary, string originalName, string mime, string ext, string ip, CancellationToken ct = default);
    Task<ReceiptDownload> DownloadReceiptAsync(User user, int id, int rid, CancellationToken ct = default);
    Task<ApiResult> ManualVerifyReceiptAsync(User user, int id, int rid, string ip, CancellationToken ct = default);
    Task<ApiResult> FlagFakeReceiptAsync(User user, int id, int rid, string? reason, string ip, CancellationToken ct = default);
    Task<ApiResult> VerifyReceiptAsync(User user, int id, int rid, CancellationToken ct = default);
    Task<ApiResult> ReceiptReviewAsync(User user, string? status, int page, int perPage, CancellationToken ct = default);
    Task<ApiResult> NotifyMissingReceiptsAsync(User user, int teamId, CancellationToken ct = default);
    Task<ApiResult> ResendCallbackAsync(User user, int id, CancellationToken ct = default);
}

public class WithdrawAdminService : IWithdrawAdminService
{
    private readonly ITransactionAdminStore _store;
    private readonly ICallbackService _callbacks;
    private readonly IMerchantBankService _banks;
    private readonly ITelegramService _telegram;
    private readonly IWithdrawReceiptStorage _receipts;
    private readonly IReceiptVerificationQueue _verifyQueue;
    private readonly IPerceptualHasher _hasher;
    private readonly IClock _clock;

    private readonly TPanel.Application.Features.Audit.IAuditContext _audit;

    public WithdrawAdminService(ITransactionAdminStore store, ICallbackService callbacks, IMerchantBankService banks,
        ITelegramService telegram, IWithdrawReceiptStorage receipts, IReceiptVerificationQueue verifyQueue,
        IPerceptualHasher hasher, IClock clock, TPanel.Application.Features.Audit.IAuditContext audit)
    {
        _store = store; _callbacks = callbacks; _banks = banks; _telegram = telegram;
        _receipts = receipts; _verifyQueue = verifyQueue; _hasher = hasher; _clock = clock; _audit = audit;
    }

    private async Task<QueryScope> ScopeAsync(User user, CancellationToken ct)
    {
        if (user.IsTeamMember) return new QueryScope(ScopeKind.Team, user.TeamId);
        if (user.IsMerchant)
        {
            var ids = await _store.GetMerchantIdsForUserAsync(user.MerchantGroupId.HasValue ? (int)user.MerchantGroupId : null, user.FirmId, ct);
            return new QueryScope(ScopeKind.Merchant, MerchantIds: ids);
        }
        return QueryScope.Global;
    }

    public async Task<ApiResult> PendingAsync(User user, TxFilter f, CancellationToken ct = default)
    {
        var scope = await ScopeAsync(user, ct);
        var rows = await _store.GetWithdrawalsPendingAsync(scope, f, ct);
        return ApiResult.Ok(rows.Select(d => new
        {
            id = d.Id, status = int.Parse(d.Status), name = d.Name, amount = d.Amount,
            order_id = d.OrderId, player_id = d.PlayerId, iban = d.Iban, bank_name = d.BankName,
            merchant_name = user.IsTeamMember ? null : d.MerchantName,
            team_name = user.HasMerchantScope ? null : d.TeamName,
            agent_name = user.HasMerchantScope ? null : d.AgentName,
            agent_id = user.HasMerchantScope ? (int?)null : d.AgentId,
            receipt_count = (int)d.ReceiptCount, created_at = d.CreatedAt, form_at = d.FormAt, process_date = d.ProcessDate,
        }));
    }

    public async Task<ApiResult> AllAsync(User user, TxFilter f, CancellationToken ct = default)
    {
        var scope = await ScopeAsync(user, ct);
        var (rows, total, totalAmount) = await _store.GetWithdrawalsAllAsync(scope, f, ct);
        return ApiResult.Ok(new
        {
            withdrawals = rows.Select(d => new
            {
                id = d.Id, status = int.Parse(d.Status), name = d.Name, amount = d.Amount,
                order_id = d.OrderId, player_id = d.PlayerId, iban = d.Iban, bank_name = d.BankName, u_id = d.UId,
                merchant_name = user.IsTeamMember ? null : d.MerchantName,
                team_name = user.HasMerchantScope ? null : d.TeamName,
                agent_name = user.HasMerchantScope ? null : d.AgentName,
                reject_type = d.RejectType, receipt_count = (int)d.ReceiptCount,
                receipt_warning = int.Parse(d.Status) == 3 && d.ReceiptCount == 0
                    && d.TelegramMissingReceiptEnabledAt is not null && d.FinalizeDate is not null
                    && d.FinalizeDate >= d.TelegramMissingReceiptEnabledAt,
                created_at = d.CreatedAt, finalize_date = d.FinalizeDate,
            }),
            total, total_amount = totalAmount, page = f.Page, per_page = f.PerPage,
        });
    }

    public async Task<ApiResult> DetailAsync(User user, int id, CancellationToken ct = default)
    {
        var d = await _store.GetWithdrawDetailAsync(id, ct);
        if (d is null) return ApiResult.Msg(404, "İşlem bulunamadı.");
        if (user.IsTeamMember && d.TeamId != user.TeamId) return ApiResult.Msg(403, "Yetki yok.");
        if (user.IsMerchant)
        {
            var allowed = await _store.GetMerchantIdsForUserAsync(user.MerchantGroupId.HasValue ? (int)user.MerchantGroupId : null, user.FirmId, ct);
            if (!allowed.Contains(d.FirmId)) return ApiResult.Msg(403, "Yetki yok.");
        }
        var scope = await ScopeAsync(user, ct);
        var history = await _store.GetPlayerHistoryAsync(d.PlayerId ?? "", d.Id, 2, scope, ct);

        return ApiResult.Ok(new
        {
            withdraw = new
            {
                id = d.Id, status = int.Parse(d.Status), name = d.Name, amount = d.Amount,
                order_id = d.OrderId, player_id = d.PlayerId, iban = d.Iban,
                merchant_name = d.MerchantName,
                team_name = user.HasMerchantScope ? null : d.TeamName,
                agent_name = user.HasMerchantScope ? null : d.AgentName,
                created_at = d.CreatedAt, process_date = d.ProcessDate, finalize_date = d.FinalizeDate,
            },
            history,
        });
    }

    public async Task<ApiResult> TakeAsync(User user, int id, string ip, CancellationToken ct = default)
    {
        if (!user.CanApproveTransactions) return ApiResult.Msg(403, "Bu işlem için yetkiniz yok.");
        var invest = await _store.GetInvestRawAsync(id, ct);
        if (invest is null || invest.Status is not ("0" or "1") || invest.AgentId is not null)
            return ApiResult.Msg(422, "Bu işlem alınamaz.");

        if (!user.IsAdmin)
        {
            var isPool = invest.Status == "0" || invest.TeamId is null;
            if (!isPool && invest.TeamId != user.TeamId)
                return ApiResult.Msg(403, "Bu çekim sizin takımınıza atanmamış.");
            if (isPool && (user.TeamId == 0))
                return ApiResult.Msg(403, "Havuzdan üstlenmek için takıma atanmış olmalısınız.");
        }

        var newTeamId = invest.TeamId ?? user.TeamId;
        await _store.UpdateInvestAsync(id, new Dictionary<string, object?>
        {
            ["status"] = 2, ["agent_id"] = user.Id, ["team_id"] = newTeamId, ["process_date"] = _clock.Now,
        }, ct);
        await _store.InsertInvestLogAsync(id, user.Id, ip, 2, "Çekim üstlenildi", ct);
        return ApiResult.Msg(200, "İşlem üstlenildi.");
    }

    public async Task<ApiResult> ReleaseAsync(User user, int id, string ip, CancellationToken ct = default)
    {
        if (!user.CanApproveTransactions) return ApiResult.Msg(403, "Bu işlem için yetkiniz yok.");
        var invest = await _store.GetInvestRawAsync(id, ct);
        if (invest is null || invest.Status != "2") return ApiResult.Msg(422, "Bu işlem bırakılamaz.");
        if (!user.IsAdmin && invest.TeamId != user.TeamId)
            return ApiResult.Msg(403, "Bu işlemi yalnızca işlemin atandığı takımdaki kullanıcılar bırakabilir.");

        await _store.UpdateInvestAsync(id, new Dictionary<string, object?>
        {
            ["status"] = "0", ["agent_id"] = null, ["team_id"] = null, ["process_date"] = null,
        }, ct);
        await _store.InsertInvestLogAsync(id, user.Id, ip, 0, "Çekim bırakıldı (havuza döndü)", ct);
        return ApiResult.Msg(200, "İşlem bırakıldı.");
    }

    public async Task<ApiResult> ApproveAsync(User user, int id, string ip, CancellationToken ct = default)
    {
        if (!user.CanApproveTransactions) return ApiResult.Msg(403, "Bu işlem için yetkiniz yok.");
        var invest = await _store.GetInvestRawAsync(id, ct);
        if (invest is null) return ApiResult.Msg(404, "İşlem bulunamadı.");
        if (!user.IsAdmin && (invest.Status != "2" || invest.TeamId != user.TeamId))
            return ApiResult.Msg(403, "Bu işlemi onaylama yetkiniz yok.");
        if (!await _store.HasReceiptAsync(id, ct))
            return ApiResult.Msg(422, "Onay için en az bir dekont yüklemeniz gerekiyor.");

        await _store.UpdateInvestAsync(id, new Dictionary<string, object?>
        {
            ["status"] = 3, ["agent_id"] = user.Id, ["finalize_date"] = _clock.Now,
        }, ct);
        await _store.InsertInvestLogAsync(id, user.Id, ip, 3, "Çekim onaylandı", ct);

        var updated = await _store.GetInvestAsync(id, ct);
        if (updated is not null) await _callbacks.SendForInvestAsync(updated, true, triggeredBy: user.Id, ct: ct);
        if (invest.TeamId is not null) await _banks.EnforceMaxCaseAsync(new[] { invest.TeamId.Value }, ct);

        _audit.Set($"Çekim onaylandı — #{id} (₺{invest.Amount:N2})", "invest", id.ToString(),
            new { status = invest.Status }, new { status = "3" });

        return ApiResult.Msg(200, "Çekim onaylandı.");
    }

    public async Task<ApiResult> RejectAsync(User user, int id, int rejectType, string ip, CancellationToken ct = default)
    {
        if (rejectType is not (1 or 2)) return ApiResult.Msg(422, "Geçersiz red tipi.");
        if (!user.CanApproveTransactions) return ApiResult.Msg(403, "Bu işlem için yetkiniz yok.");
        var invest = await _store.GetInvestRawAsync(id, ct);
        if (invest is null) return ApiResult.Msg(404, "İşlem bulunamadı.");
        if (!user.IsAdmin && (invest.Status != "2" || invest.TeamId != user.TeamId))
            return ApiResult.Msg(403, "Bu işlemi reddetme yetkiniz yok.");

        var rejectMessages = new Dictionary<int, string> { [1] = "Çekim reddedildi", [2] = "Tekrarlanan talep" };
        await _store.UpdateInvestAsync(id, new Dictionary<string, object?>
        {
            ["status"] = 4, ["rejectType"] = rejectType, ["agent_id"] = user.Id, ["finalize_date"] = _clock.Now,
        }, ct);
        await _store.InsertInvestLogAsync(id, user.Id, ip, 4, rejectMessages[rejectType], ct);

        var updated = await _store.GetInvestAsync(id, ct);
        if (updated is not null) await _callbacks.SendForInvestAsync(updated, false, rejectMessages[rejectType], user.Id, ct: ct);

        _audit.Set($"Çekim reddedildi — #{id} ({rejectMessages[rejectType]})", "invest", id.ToString(),
            new { status = invest.Status }, new { status = "4", rejectType });

        return ApiResult.Msg(200, "Çekim reddedildi.");
    }

    public async Task<ApiResult> BulkAssignAsync(User user, IReadOnlyList<int> ids, int teamId, string ip, CancellationToken ct = default)
    {
        if (ids.Count == 0) return ApiResult.Msg(422, "İşlem listesi boş.");
        if (!user.IsAdmin) return ApiResult.Msg(403, "Toplu atama için admin yetkisi gerekir.");

        var team = await _store.GetTeamAsync(teamId, requireActive: false, ct);
        if (team is null) return ApiResult.Msg(404, "Takım bulunamadı.");

        var firstUser = await _store.GetFirstActiveTeamUserAsync(teamId, ct);
        if (firstUser is null) return ApiResult.Msg(422, "Bu takımda atanabilecek aktif kullanıcı yok.");

        var eligible = await _store.FilterEligibleForAssignAsync(ids, ct);
        if (eligible.Count == 0) return ApiResult.Msg(422, "Atanabilecek uygun çekim bulunamadı.");

        await _store.BulkAssignAsync(eligible, teamId, firstUser.Id, ct);
        foreach (var eid in eligible)
            await _store.InsertInvestLogAsync(eid, user.Id, ip, 2, $"Toplu atama → takım: {team.Name}", ct);

        // Telegram bildirimi
        if (team.TelegramEnabled && !string.IsNullOrEmpty(team.TelegramWithdrawChatId) && team.TelegramWithdrawAssignedEnabled)
        {
            // Basit özet mesaj (detay Faz 6'da zenginleşebilir)
            var msg = $"📥 *{eligible.Count} ÇEKİM ATANDI*\n*Atayan:* " + ITelegramService.Escape(user.Name);
            if (await _telegram.SendAsync(team.TelegramWithdrawChatId!, msg, ct))
                await _store.InsertTelegramNotificationsIgnoreAsync(eligible, "withdraw_assigned", _clock.Now, ct);
        }

        return ApiResult.Ok(new
        {
            message = $"{eligible.Count} çekim {team.Name} takımına atandı.",
            assigned = eligible.Count,
            team = new { id = team.Id, name = team.Name },
            agent = new { id = firstUser.Id, name = firstUser.Name },
        });
    }

    public async Task<ApiResult> ReceiptsAsync(User user, int id, CancellationToken ct = default)
    {
        if (!user.CanApproveTransactions) return ApiResult.Msg(403, "Bu işlem için yetkiniz yok.");
        var invest = await _store.GetInvestRawAsync(id, ct);
        if (invest is null || invest.Type != "2") return ApiResult.Msg(404, "İşlem bulunamadı.");
        if (user.IsTeamMember && invest.TeamId != user.TeamId) return ApiResult.Msg(403, "Bu işlem için yetkiniz yok.");

        var rows = await _store.GetReceiptsAsync(id, ct);
        return ApiResult.Ok(new
        {
            receipts = rows.Select(r => new
            {
                id = r.Id, original_name = r.OriginalName, mime_type = r.MimeType, file_size = (int)r.FileSize,
                is_image = (r.MimeType ?? "").StartsWith("image/"), is_pdf = r.MimeType == "application/pdf",
                uploaded_at = r.UploadedAt, uploaded_by_name = r.UploadedByName,
                url = $"/api/withdrawals/{id}/receipts/{r.Id}",
                verification_status = r.VerificationStatus, verification_score = r.VerificationScore,
                verification_data = ParseJson(r.VerificationData), verification_notes = r.VerificationNotes,
                metadata_flags = ParseJson(r.MetadataFlags), verified_at = r.VerifiedAt,
                manual_verifier_name = r.ManualVerifierName,
            }),
        });
    }

    public async Task<ApiResult> UploadReceiptAsync(User user, int id, byte[] binary, string originalName, string mime, string ext, string ip, CancellationToken ct = default)
    {
        if (!user.CanApproveTransactions) return ApiResult.Msg(403, "Bu işlem için yetkiniz yok.");
        var invest = await _store.GetInvestRawAsync(id, ct);
        if (invest is null || invest.Type != "2") return ApiResult.Msg(404, "İşlem bulunamadı.");
        if (user.IsTeamMember && invest.TeamId != user.TeamId) return ApiResult.Msg(403, "Bu işlem için yetkiniz yok.");
        if (binary.Length > 10 * 1024 * 1024) return ApiResult.Msg(422, "Dosya boyutu 10 MB'ı aşamaz.");

        var name = Guid.NewGuid().ToString() + "." + ext;
        var path = await _receipts.StoreAsync(id, name, binary, ct);
        var fileHash = Convert.ToHexStringLower(SHA256.HashData(binary));
        var perceptual = _hasher.DHash(binary, mime);

        var receiptId = await _store.InsertReceiptAsync(new ReceiptInsert(id, path, originalName, mime, binary.Length, fileHash, perceptual, user.Id), ct);
        await _verifyQueue.EnqueueAsync(receiptId, ct);
        await _store.InsertInvestLogAsync(id, user.Id, ip, int.Parse(invest.Status), $"Dekont yüklendi ({originalName})", ct);

        return new ApiResult(201, new { message = "Dekont yüklendi.", id = receiptId, url = $"/api/withdrawals/{id}/receipts/{receiptId}" });
    }

    public async Task<ReceiptDownload> DownloadReceiptAsync(User user, int id, int rid, CancellationToken ct = default)
    {
        if (!user.CanApproveTransactions) return new ReceiptDownload(false, 403, null, null, null);
        var invest = await _store.GetInvestRawAsync(id, ct);
        if (invest is null || invest.Type != "2") return new ReceiptDownload(false, 404, null, null, null);
        if (user.IsTeamMember && invest.TeamId != user.TeamId) return new ReceiptDownload(false, 403, null, null, null);

        var receipt = await _store.GetReceiptAsync(id, rid, ct);
        if (receipt is null) return new ReceiptDownload(false, 404, null, null, null);

        var (exists, fullPath) = await _receipts.ResolveAsync(receipt.FilePath, ct);
        if (!exists) return new ReceiptDownload(false, 404, null, null, null);

        return new ReceiptDownload(true, 200, receipt.MimeType ?? "application/octet-stream",
            receipt.OriginalName ?? Path.GetFileName(receipt.FilePath), fullPath);
    }

    public async Task<ApiResult> ManualVerifyReceiptAsync(User user, int id, int rid, string ip, CancellationToken ct = default)
    {
        if (!user.IsAdmin) return ApiResult.Msg(403, "Bu işlem için yetkiniz yok.");
        var invest = await _store.GetInvestRawAsync(id, ct);
        if (invest is null || invest.Type != "2") return ApiResult.Msg(404, "İşlem bulunamadı.");
        var receipt = await _store.GetReceiptAsync(id, rid, ct);
        if (receipt is null) return ApiResult.Msg(404, "Dekont bulunamadı.");
        if (receipt.VerificationStatus == "manually_verified") return ApiResult.Msg(422, "Bu dekont zaten manuel doğrulanmış.");

        var stamp = _clock.Now.ToString("dd.MM.yyyy HH:mm");
        var manualNote = $"Manuel doğrulandı: {user.Name} · {stamp}";
        var newNotes = string.IsNullOrEmpty(receipt.VerificationNotes) ? manualNote : receipt.VerificationNotes + "\n" + manualNote;

        await _store.UpdateReceiptAsync(rid, new Dictionary<string, object?>
        {
            ["verification_status"] = "manually_verified", ["verification_score"] = 100,
            ["verification_notes"] = newNotes, ["manual_verified_by"] = user.Id, ["verified_at"] = _clock.Now,
        }, ct);
        await _store.InsertInvestLogAsync(id, user.Id, ip, int.Parse(invest.Status), $"Dekont manuel doğrulandı (receipt #{rid})", ct);
        return ApiResult.Msg(200, "Manuel doğrulandı.");
    }

    public async Task<ApiResult> FlagFakeReceiptAsync(User user, int id, int rid, string? reason, string ip, CancellationToken ct = default)
    {
        if (!user.IsAdmin) return ApiResult.Msg(403, "Bu işlem için yetkiniz yok.");
        var invest = await _store.GetInvestRawAsync(id, ct);
        if (invest is null || invest.Type != "2") return ApiResult.Msg(404, "İşlem bulunamadı.");
        var receipt = await _store.GetReceiptAsync(id, rid, ct);
        if (receipt is null) return ApiResult.Msg(404, "Dekont bulunamadı.");

        int? templateId = null;
        if (!string.IsNullOrEmpty(receipt.PerceptualHash) || !string.IsNullOrEmpty(receipt.FileHash))
            templateId = await _store.InsertFakeTemplateAsync(rid, id, receipt.PerceptualHash ?? "", receipt.FileHash, reason, user.Id, ct);

        var stamp = _clock.Now.ToString("dd.MM.yyyy HH:mm");
        var note = $"🚫 Sahte olarak işaretlendi: {user.Name} · {stamp}" + (string.IsNullOrEmpty(reason) ? "" : $" · Sebep: {reason}");
        var newNotes = string.IsNullOrEmpty(receipt.VerificationNotes) ? note : receipt.VerificationNotes + "\n" + note;

        await _store.UpdateReceiptAsync(rid, new Dictionary<string, object?>
        {
            ["verification_status"] = "rejected", ["verification_score"] = 0,
            ["verification_notes"] = newNotes, ["verified_at"] = _clock.Now,
        }, ct);
        await _store.InsertInvestLogAsync(id, user.Id, ip, int.Parse(invest.Status),
            $"Dekont sahte olarak işaretlendi (receipt #{rid}{(templateId is not null ? $", template #{templateId}" : "")})", ct);

        return ApiResult.Ok(new { message = "Sahte olarak işaretlendi.", template_id = templateId });
    }

    public async Task<ApiResult> VerifyReceiptAsync(User user, int id, int rid, CancellationToken ct = default)
    {
        if (!user.CanApproveTransactions) return ApiResult.Msg(403, "Bu işlem için yetkiniz yok.");
        var invest = await _store.GetInvestRawAsync(id, ct);
        if (invest is null || invest.Type != "2") return ApiResult.Msg(404, "İşlem bulunamadı.");
        if (user.IsTeamMember && invest.TeamId != user.TeamId) return ApiResult.Msg(403, "Bu işlem için yetkiniz yok.");
        var receipt = await _store.GetReceiptAsync(id, rid, ct);
        if (receipt is null) return ApiResult.Msg(404, "Dekont bulunamadı.");

        await _store.UpdateReceiptAsync(rid, new Dictionary<string, object?>
        {
            ["verification_status"] = "pending", ["verification_score"] = null,
            ["verification_data"] = null, ["verification_notes"] = null, ["verified_at"] = null,
        }, ct);
        await _verifyQueue.EnqueueAsync(rid, ct);
        return ApiResult.Msg(200, "Yeniden doğrulama başlatıldı. Sonuç birkaç dakika içinde gelecek.");
    }

    public async Task<ApiResult> ReceiptReviewAsync(User user, string? status, int page, int perPage, CancellationToken ct = default)
    {
        if (!user.IsAdmin) return ApiResult.Msg(403, "Bu işlem için yetkiniz yok.");
        page = Math.Max(1, page);
        var (rows, total, counts) = await _store.GetReceiptReviewAsync(status, page, perPage, ct);

        long C(string k) => counts.TryGetValue(k, out var v) ? v : 0;
        return ApiResult.Ok(new
        {
            items = rows.Select(r => new
            {
                invest_id = r.InvestId, order_id = r.OrderId, amount = r.Amount, recipient = r.Recipient, iban = r.Iban,
                invest_status = r.InvestStatus, finalize_date = r.FinalizeDate, team_name = r.TeamName,
                merchant_name = r.MerchantName, agent_name = r.AgentName, receipt_id = r.ReceiptId,
                verification_status = r.VerificationStatus, verification_score = r.VerificationScore,
                verification_notes = r.VerificationNotes, verified_at = r.VerifiedAt,
                verification_data = ParseJson(r.VerificationData), manual_verifier_name = r.ManualVerifierName,
            }),
            total, page, per_page = perPage,
            counts = new
            {
                pending = C("pending"),
                verified = C("verified") + C("manually_verified"),
                manually_verified = C("manually_verified"),
                suspicious = C("suspicious"),
                rejected = C("rejected"),
            },
        });
    }

    public async Task<ApiResult> NotifyMissingReceiptsAsync(User user, int teamId, CancellationToken ct = default)
    {
        if (!user.CanApproveTransactions) return ApiResult.Msg(403, "Bu işlem için yetkiniz yok.");
        var team = await _store.GetTeamAsync(teamId, requireActive: false, ct);
        if (team is null) return ApiResult.Msg(404, "Takım bulunamadı.");
        if (string.IsNullOrEmpty(team.TelegramWithdrawChatId)) return ApiResult.Msg(422, "Takımın çekim chat ID'si tanımlı değil.");
        if (team.TelegramMissingReceiptEnabledAt is null) return ApiResult.Msg(422, "Bu takımda \"dekont yüklenmeyen\" bildirimi açık değil.");

        var missing = await _store.GetMissingReceiptsAsync(teamId, team.TelegramMissingReceiptEnabledAt.Value, ct);
        if (missing.Count == 0) return new ApiResult(422, new { message = "Bu takımda dekont yüklenmeyen çekim yok.", count = 0 });

        var now = _clock.Now;
        var lines = new List<string>();
        foreach (var w in missing)
        {
            var totalMinutes = Math.Max(0, (int)(now - w.FinalizeDate).TotalMinutes);
            var days = totalMinutes / 1440;
            var hours = (totalMinutes % 1440) / 60;
            var minutes = totalMinutes % 60;
            var parts = new List<string>();
            if (days > 0) parts.Add($"{days} gün");
            if (hours > 0) parts.Add($"{hours} saat");
            if (minutes > 0 || parts.Count == 0) parts.Add($"{minutes} dakika");
            var orderCode = string.IsNullOrEmpty(w.OrderId) ? "#" + w.Id : w.OrderId;
            lines.Add(ITelegramService.Escape(orderCode!) + " \\- " + ITelegramService.Escape(string.Join(" ", parts)) + " dekont yüklenmedi\\.");
        }

        var message = string.Join("\n", lines) + "\n\n*" + ITelegramService.Escape("Lütfen en kısa sürede dekontları yükleyin!") + "*";
        var ok = await _telegram.SendAsync(team.TelegramWithdrawChatId!, message, ct);
        if (!ok) return new ApiResult(500, new { message = "Telegram bildirimi gönderilemedi.", count = lines.Count });

        return ApiResult.Ok(new { message = "Bildirim gönderildi.", count = lines.Count, chat_id = team.TelegramWithdrawChatId });
    }

    public async Task<ApiResult> ResendCallbackAsync(User user, int id, CancellationToken ct = default)
    {
        if (!user.IsSuperAdmin) return ApiResult.Msg(403, "Bu işlem için yetkiniz yok.");
        var invest = await _store.GetInvestAsync(id, ct);
        if (invest is null) return ApiResult.Msg(404, "İşlem bulunamadı.");
        if (int.Parse(invest.Status) is not (3 or 4)) return ApiResult.Msg(422, "Yalnızca sonuçlanmış işlemler için tekrar gönderilebilir.");

        var approved = invest.Status == "3";
        var result = await _callbacks.SendForInvestAsync(invest, approved, approved ? "" : "Manuel yeniden gönderim",
            user.Id, force: true, type: "manual_resend", ct: ct);
        return new ApiResult(result.Success ? 200 : 502, new
        {
            message = result.Success ? "Callback yeniden gönderildi." : "Callback gönderilemedi.",
            result,
        });
    }

    private static object? ParseJson(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return System.Text.Json.JsonSerializer.Deserialize<object>(json); }
        catch { return null; }
    }
}
