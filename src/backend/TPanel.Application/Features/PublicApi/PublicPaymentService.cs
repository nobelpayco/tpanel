using TPanel.Application.Common.Interfaces;

namespace TPanel.Application.Features.PublicApi;

public interface IReceiptStorage
{
    /// <summary>Dekontu private depoya yazar. DB'ye yazılacak göreli yolu döner (receipts/...).</summary>
    Task<string> StoreReceiptAsync(string fileName, byte[] content, CancellationToken ct = default);

    /// <summary>DB'deki göreli yolu (receipts/...) tam dosya yoluna çözer.</summary>
    Task<(bool Exists, string FullPath)> ResolveAsync(string relativePath, CancellationToken ct = default);
}

public interface IPublicPaymentService
{
    Task<V1Result> ShowAsync(string uId, CancellationToken ct = default);
    Task<V1Result> SelectBankAsync(string uId, SelectBankRequest req, CancellationToken ct = default);
    Task<V1Result> MarkPaidAsync(string uId, CancellationToken ct = default);
    Task<V1Result> CancelAsync(string uId, CancellationToken ct = default);
    Task<V1Result> UploadReceiptAsync(string uId, byte[] content, string extension, CancellationToken ct = default);
}

public static class PayMessages
{
    public const string RequestNotFound = "Talep bulunamadı.";
    public const string ActionNotAllowed = "Bu durum için işlem yapılamaz.";
    public const string MarkPaidSuccess = "Ödeme bilginiz alındı, kontrol ediliyor.";
    public const string CancelSuccess = "Talep iptal edildi.";
    public const string BankAlreadySelected = "Bu talep için banka zaten seçilmiş.";
    public const string InvalidBank = "Geçerli olmayan veya uygun olmayan banka hesabı.";
    public const string ReceiptUploadBlocked = "Bu durum için dekont yüklenemez.";
    public const string ReceiptUploaded = "Dekont yüklendi. İşleminiz daha hızlı incelenecektir.";
}

public class PublicPaymentService : IPublicPaymentService
{
    private readonly IMerchantApiStore _store;
    private readonly IMerchantBankService _banks;
    private readonly ICallbackService _callbacks;
    private readonly ISystemSettingService _settings;
    private readonly IReceiptStorage _receipts;
    private readonly IClock _clock;

    public PublicPaymentService(IMerchantApiStore store, IMerchantBankService banks, ICallbackService callbacks,
        ISystemSettingService settings, IReceiptStorage receipts, IClock clock)
    {
        _store = store;
        _banks = banks;
        _callbacks = callbacks;
        _settings = settings;
        _receipts = receipts;
        _clock = clock;
    }

    public async Task<V1Result> ShowAsync(string uId, CancellationToken ct = default)
    {
        var tx = await _store.GetByUidAsync(uId, ct);
        if (tx is null) return V1Result.Error(404, PayMessages.RequestNotFound);

        // Link expiry
        var expiryEnabled = await _settings.GetAsync("pay_link_expiry_enabled", ct) == "1";
        var expiryMinutes = int.TryParse(await _settings.GetAsync("pay_link_expiry_minutes", ct), out var m) && m > 0 ? m : 15;
        var isPending = tx.Status is "0" or "1" or "2";

        if (expiryEnabled && expiryMinutes > 0 && isPending)
        {
            if (tx.CreatedAt < _clock.Now.AddMinutes(-expiryMinutes))
            {
                await _store.UpdateAsync(uId, new Dictionary<string, object?>
                {
                    ["status"] = "4",
                    ["finalize_date"] = _clock.Now,
                }, ct);
                tx.Status = "4";
                await _callbacks.SendExpireAsync(tx, ct: ct);
                return new V1Result(410, new { code = 410, status = false, message = "Ödeme bulunmadı", expired = true });
            }
        }

        // Otomatik IBAN ata
        if (tx.IbanSeen == 0 && tx.Status is "0" or "1")
        {
            var picked = await _banks.PickOneAsync(tx.Amount, tx.FirmId, ct: ct);
            if (picked is not null)
            {
                await _store.UpdateAsync(uId, new Dictionary<string, object?>
                {
                    ["bank_id"] = picked.Id,
                    ["team_id"] = picked.TeamId,
                    ["status"] = "1",
                    ["ibanSeen"] = 1,
                    ["form_at"] = _clock.Now,
                }, ct);
                tx = await _store.GetByUidAsync(uId, ct) ?? tx;
            }
            else
            {
                await _banks.AlertNoIbanAvailableAsync(tx.FirmId, tx.Amount, tx.Id, tx.PlayerId, tx.OrderId, tx.Name, ct);
            }
        }

        var statusLabel = tx.Status switch
        {
            "0" => "pending", "1" => "pending", "2" => "processing",
            "3" => "approved", "4" => "rejected", _ => "unknown",
        };

        object? bankData = null;
        if (tx.BankId is not null)
        {
            var bank = await _store.GetBankAccountAsync(tx.BankId.Value, ct);
            if (bank is not null)
                bankData = new { id = bank.Id, account_holder = bank.AccountHolder, account_iban = bank.AccountIban, bank_name = bank.BankName };
        }

        var expiresAt = (expiryEnabled && expiryMinutes > 0)
            ? tx.CreatedAt.AddMinutes(expiryMinutes).ToString("yyyy-MM-ddTHH:mm:sszzz")
            : null;

        return new V1Result(200, new
        {
            code = 200,
            status = true,
            data = new
            {
                u_id = tx.UId,
                order_id = tx.OrderId,
                amount = tx.Amount,
                name = tx.Name,
                status = statusLabel,
                iban_seen = tx.IbanSeen,
                bank = bankData,
                has_receipt = !string.IsNullOrEmpty(tx.ReceiptPath),
                success_url = tx.CallbackOkUrl,
                fail_url = tx.CallbackFailUrl,
                expires_at = expiresAt,
            },
        });
    }

    public async Task<V1Result> SelectBankAsync(string uId, SelectBankRequest req, CancellationToken ct = default)
    {
        if (req.bank_id is null || (req.amount is not null && req.amount < 1))
            return V1Result.Error(422, "Invalid request parameters.");

        var tx = await _store.GetByUidAsync(uId, ct);
        if (tx is null) return V1Result.Error(404, PayMessages.RequestNotFound);

        if (tx.IbanSeen == 1) return V1Result.Error(409, PayMessages.BankAlreadySelected);

        var finalAmount = req.amount is not null ? (double)req.amount.Value : tx.Amount;

        var bank = await _banks.ValidateAsync(req.bank_id.Value, finalAmount, tx.FirmId, ct);
        if (bank is null) return V1Result.Error(422, PayMessages.InvalidBank);

        await _store.UpdateAsync(uId, new Dictionary<string, object?>
        {
            ["bank_id"] = bank.Id,
            ["team_id"] = bank.TeamId,
            ["status"] = "1",
            ["ibanSeen"] = 1,
            ["amount"] = finalAmount,
            ["form_at"] = _clock.Now,
        }, ct);

        return new V1Result(200, new
        {
            code = 200,
            status = true,
            data = new
            {
                bank = new { id = bank.Id, account_holder = bank.AccountHolder, account_iban = bank.AccountIban, bank_name = bank.BankName },
                amount = finalAmount,
            },
        });
    }

    public async Task<V1Result> MarkPaidAsync(string uId, CancellationToken ct = default)
    {
        var tx = await _store.GetByUidAsync(uId, ct);
        if (tx is null) return V1Result.Error(404, PayMessages.RequestNotFound);
        if (tx.Status != "1") return V1Result.Error(409, PayMessages.ActionNotAllowed);

        await _store.UpdateAsync(uId, new Dictionary<string, object?>
        {
            ["status"] = "2",
            ["process_date"] = _clock.Now,
        }, ct);

        return new V1Result(200, new { code = 200, status = true, message = PayMessages.MarkPaidSuccess });
    }

    public async Task<V1Result> CancelAsync(string uId, CancellationToken ct = default)
    {
        var tx = await _store.GetByUidAsync(uId, ct);
        if (tx is null) return V1Result.Error(404, PayMessages.RequestNotFound);
        if (tx.Status is not ("0" or "1")) return V1Result.Error(409, PayMessages.ActionNotAllowed);

        await _store.UpdateAsync(uId, new Dictionary<string, object?>
        {
            ["status"] = "4",
            ["rejectType"] = 5,
            ["finalize_date"] = _clock.Now,
        }, ct);

        return new V1Result(200, new { code = 200, status = true, message = PayMessages.CancelSuccess });
    }

    public async Task<V1Result> UploadReceiptAsync(string uId, byte[] content, string extension, CancellationToken ct = default)
    {
        var tx = await _store.GetByUidAsync(uId, ct);
        if (tx is null) return V1Result.Error(404, PayMessages.RequestNotFound);
        if (tx.Status is not ("0" or "1" or "2")) return V1Result.Error(409, PayMessages.ReceiptUploadBlocked);

        var fileName = PublicApiHelpers.GenerateUId() + "." + extension;
        var relativePath = await _receipts.StoreReceiptAsync(fileName, content, ct);

        await _store.UpdateAsync(uId, new Dictionary<string, object?> { ["receipt_path"] = relativePath }, ct);

        return new V1Result(200, new { code = 200, status = true, message = PayMessages.ReceiptUploaded });
    }
}
