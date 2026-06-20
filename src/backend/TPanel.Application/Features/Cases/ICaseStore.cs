namespace TPanel.Application.Features.Cases;

public record WriteResult(int Status, string Message)
{
    public static readonly WriteResult Ok = new(200, "");
    public static WriteResult Err(int s, string m) => new(s, m);
}

public record TeamBasic(int Id, string Name);
public record TeamMeta(int Id, string Name, double Overturn, double Commission);
public record ActorInfo(int UserId, string? Name);

/// <summary>Kasa muhasebesi veri erişimi (Dapper).</summary>
public interface ICaseStore
{
    // ---- Team ----
    Task<IReadOnlyList<TeamBasic>> GetTeamsBasicAsync(CancellationToken ct = default);
    Task<TeamMeta?> GetTeamMetaAsync(int id, CancellationToken ct = default);
    Task<object> TeamShowAsync(int id, string? from, string? to, CancellationToken ct = default);
    Task<object> TeamPaymentsAsync(int id, string? date, string? from, string? to, CancellationToken ct = default);
    Task<WriteResult> AddTeamPaymentAsync(int id, CasePaymentBody b, ActorInfo actor, CancellationToken ct = default);
    Task<WriteResult> DeleteTeamPaymentAsync(int id, int paymentId, ActorInfo actor, CancellationToken ct = default);
    Task<WriteResult> AddTeamTransferAsync(int id, TeamTransferBody b, ActorInfo actor, CancellationToken ct = default);
    Task<WriteResult> DeleteTeamTransferAsync(int id, int transferId, ActorInfo actor, CancellationToken ct = default);
    Task<WriteResult> AddTeamSyncAsync(int id, TeamSyncBody b, ActorInfo actor, CancellationToken ct = default);
    Task<WriteResult> DeleteTeamSyncAsync(int id, int syncId, ActorInfo actor, CancellationToken ct = default);

    // ---- Merchant ----
    Task<object> MerchantIndexAsync(CancellationToken ct = default);
    Task<object?> MerchantShowAsync(int merchantId, bool isGroup, string? from, string? to, CancellationToken ct = default);
    Task<object> PayliraDailyNetAsync(string? from, string? to, CancellationToken ct = default);
    Task<object> MerchantPaymentsAsync(int merchantId, bool isGroup, string? date, string? from, string? to, CancellationToken ct = default);
    Task<WriteResult> AddMerchantPaymentAsync(int merchantId, MerchantPaymentBody b, ActorInfo actor, CancellationToken ct = default);
    Task<WriteResult> DeleteMerchantPaymentAsync(int merchantId, int paymentId, CancellationToken ct = default);
    Task<IReadOnlyList<int>> GetMerchantIdsAsync(int? merchantGroupId, int? firmId, CancellationToken ct = default);

    // ---- Fund Storage ----
    Task<IReadOnlyList<FundStorageRow>> GetStoragesAsync(string statusFilter, CancellationToken ct = default);
    Task<FundStorageRow?> GetStorageAsync(int id, CancellationToken ct = default);
    Task<int> CreateStorageAsync(FundStorageBody b, CancellationToken ct = default);
    Task UpdateStorageAsync(int id, FundStorageBody b, CancellationToken ct = default);
    Task DisableStorageAsync(int id, CancellationToken ct = default);
    Task<object> FundStorageShowAsync(int id, string? from, string? to, CancellationToken ct = default);
    Task<object> FundTransfersAsync(string? from, string? to, CancellationToken ct = default);
    Task<WriteResult> CreateFundTransferAsync(FundTransferBody b, ActorInfo actor, CancellationToken ct = default);
    Task<WriteResult> DeleteFundTransferAsync(int id, CancellationToken ct = default);
    Task<WriteResult> AddFundSyncAsync(FundSyncBody b, ActorInfo actor, CancellationToken ct = default);
    Task<WriteResult> DeleteFundSyncAsync(int id, CancellationToken ct = default);

    // ---- Intermediary (Faz 4b) ----
    Task<object> IntermediaryIndexAsync(CancellationToken ct = default);
    Task<object?> IntermediaryShowAsync(int id, string? from, string? to, CancellationToken ct = default);
    Task<object> IntermediaryPaymentsAsync(int id, string? date, string? from, string? to, CancellationToken ct = default);
    Task<WriteResult> AddIntermediaryPaymentAsync(int id, IntermediaryPaymentBody b, ActorInfo actor, CancellationToken ct = default);
    Task<WriteResult> DeleteIntermediaryPaymentAsync(int id, int paymentId, CancellationToken ct = default);

    // ---- Partner (Faz 4b) ----
    Task<object> PartnerIndexAsync(CancellationToken ct = default);
    Task<object?> PartnerShowAsync(int id, string? from, string? to, CancellationToken ct = default);
    Task<object> PartnerPaymentsAsync(int id, string? date, string? from, string? to, CancellationToken ct = default);
    Task<WriteResult> AddPartnerPaymentAsync(int id, PartnerPaymentBody b, ActorInfo actor, CancellationToken ct = default);
    Task<WriteResult> DeletePartnerPaymentAsync(int id, int paymentId, CancellationToken ct = default);
    Task<object> CapitalsAsync(int id, string? from, string? to, CancellationToken ct = default);
    Task<WriteResult> AddCapitalAsync(int id, CapitalBody b, ActorInfo actor, CancellationToken ct = default);
    Task<WriteResult> DeleteCapitalAsync(int id, int capitalId, CancellationToken ct = default);
    Task<object> ExpensesAsync(string? from, string? to, CancellationToken ct = default);
    Task<WriteResult> AddExpenseAsync(ExpenseBody b, ActorInfo actor, CancellationToken ct = default);
    Task<WriteResult> DeleteExpenseAsync(int id, ActorInfo actor, CancellationToken ct = default);
    Task<object> AllPartnerPaymentsAsync(string? from, string? to, CancellationToken ct = default);
    Task<WriteResult> AddPartnerTransferAsync(int id, PartnerTransferBody b, ActorInfo actor, CancellationToken ct = default);
    Task<WriteResult> DeletePartnerTransferAsync(int id, int transferId, CancellationToken ct = default);

    // ---- CaseReport (Faz 4b) ----
    Task<object> CaseReportSummaryAsync(string? from, string? to, CancellationToken ct = default);
    Task<object> CaseReportIndexAsync(string? from, string? to, CancellationToken ct = default);

    // ---- InitialBalance (Faz 4b) ----
    Task<object> InitialBalanceEntitiesAsync(CancellationToken ct = default);
    Task SaveInitialBalanceAsync(string date, IReadOnlyList<InitialEntityInput> entities, string userName, CancellationToken ct = default);
    Task<WriteResult> ResetInitialBalanceAsync(string date, string userName, CancellationToken ct = default);
}
