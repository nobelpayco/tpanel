using Microsoft.AspNetCore.Mvc;
using TPanel.Application.Common;
using TPanel.Application.Common.Interfaces;
using TPanel.Application.Features.Transactions;
using TPanel.Domain.Entities;

namespace TPanel.Api.Controllers;

/// <summary>Admin controller'ları için ortak yardımcılar (kullanıcı yükleme, sonuç dönüşümü, filtre bağlama).</summary>
public abstract class AdminControllerBase : ControllerBase
{
    protected IActionResult Result(ApiResult r) => StatusCode(r.HttpStatus, r.Body);

    protected string ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";

    protected async Task<User?> LoadUserAsync(ICurrentUser currentUser, CancellationToken ct)
        => await currentUser.GetUserAsync(ct);

    protected TxFilter BuildFilter()
    {
        var q = Request.Query;
        int? I(string k) => int.TryParse(q[k], out var v) ? v : null;
        double? D(string k) => double.TryParse(q[k], System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;
        string? S(string k) => string.IsNullOrWhiteSpace(q[k]) ? null : q[k].ToString();
        bool B(string k) => q[k] == "1" || string.Equals(q[k], "true", StringComparison.OrdinalIgnoreCase);

        return new TxFilter
        {
            Merchant = I("merchant"),
            Team = I("team"),
            Bank = I("bank"),
            Name = S("name"),
            PlayerId = S("player_id"),
            OrderId = S("order_id"),
            UId = S("u_id"),
            Id = I("id"),
            Status = I("status"),
            MinAmount = D("min_amount"),
            MaxAmount = D("max_amount"),
            DateFrom = S("date_from"),
            DateTo = S("date_to"),
            ConvertedOnly = B("converted_only"),
            MissingReceipt = B("missing_receipt"),
            AddedType = I("added_type"),
            Page = I("page") ?? 1,
            PerPage = I("per_page") ?? 50,
        };
    }
}

// ---- İstek gövdeleri (frontend snake_case) ----
public record ApproveDepositBody(
    [property: System.Text.Json.Serialization.JsonPropertyName("id")] int Id,
    [property: System.Text.Json.Serialization.JsonPropertyName("amount")] double? Amount);

public record RejectBody(
    [property: System.Text.Json.Serialization.JsonPropertyName("id")] int Id,
    [property: System.Text.Json.Serialization.JsonPropertyName("reject_type")] int RejectType);

public record IdBody(
    [property: System.Text.Json.Serialization.JsonPropertyName("id")] int Id);

public record BulkAssignBody(
    [property: System.Text.Json.Serialization.JsonPropertyName("ids")] List<int>? Ids,
    [property: System.Text.Json.Serialization.JsonPropertyName("team_id")] int? TeamId);

public record FlagFakeBody(
    [property: System.Text.Json.Serialization.JsonPropertyName("reason")] string? Reason);

public record NotifyMissingBody(
    [property: System.Text.Json.Serialization.JsonPropertyName("team_id")] int? TeamId);

public record ManualDepositBody(
    [property: System.Text.Json.Serialization.JsonPropertyName("merchant_id")] int? MerchantId,
    [property: System.Text.Json.Serialization.JsonPropertyName("team_id")] int? TeamId,
    [property: System.Text.Json.Serialization.JsonPropertyName("bank_id")] int? BankId,
    [property: System.Text.Json.Serialization.JsonPropertyName("agent_id")] int? AgentId,
    [property: System.Text.Json.Serialization.JsonPropertyName("name")] string? Name,
    [property: System.Text.Json.Serialization.JsonPropertyName("amount")] double? Amount);

public record ManualWithdrawBody(
    [property: System.Text.Json.Serialization.JsonPropertyName("merchant_id")] int? MerchantId,
    [property: System.Text.Json.Serialization.JsonPropertyName("team_id")] int? TeamId,
    [property: System.Text.Json.Serialization.JsonPropertyName("bank_id")] int? BankId,
    [property: System.Text.Json.Serialization.JsonPropertyName("agent_id")] int? AgentId,
    [property: System.Text.Json.Serialization.JsonPropertyName("name")] string? Name,
    [property: System.Text.Json.Serialization.JsonPropertyName("amount")] double? Amount,
    [property: System.Text.Json.Serialization.JsonPropertyName("iban")] string? Iban);
