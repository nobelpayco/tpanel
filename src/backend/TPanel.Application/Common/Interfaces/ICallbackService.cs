using TPanel.Application.Features.PublicApi;

namespace TPanel.Application.Common.Interfaces;

public record CallbackResult(bool Success, int? Status, string? Body, string? Error);

/// <summary>Merchant'lara callback POST gönderir; her gönderimi api_callback_logs'a yazar.</summary>
public interface ICallbackService
{
    Task<CallbackResult> SendAsync(string url, object payload, string type,
        int? investId = null, int? merchantId = null, int? triggeredBy = null, CancellationToken ct = default);

    /// <summary>"Ödeme bulunmadı" / expire callback'i.</summary>
    Task<CallbackResult> SendExpireAsync(InvestRow invest, int? triggeredBy = null, CancellationToken ct = default);

    /// <summary>Onay/red callback'i (hash + form/json + log + callbackSended=1).</summary>
    Task<CallbackResult> SendForInvestAsync(InvestRow invest, bool approved, string detail = "",
        int? triggeredBy = null, bool force = false, string? type = null, CancellationToken ct = default);
}
