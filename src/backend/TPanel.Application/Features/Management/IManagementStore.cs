namespace TPanel.Application.Features.Management;

/// <summary>Yönetim CRUD veri erişimi (Dapper).</summary>
public interface IManagementStore
{
    // Team
    Task<object> TeamsAsync(string statusFilter, string? search, CancellationToken ct = default);
    Task<object?> TeamAsync(int id, CancellationToken ct = default);
    Task<MgmtResult> CreateTeamAsync(TeamUpsertBody b, CancellationToken ct = default);
    Task<MgmtResult> UpdateTeamAsync(int id, TeamUpsertBody b, CancellationToken ct = default);
    Task DisableTeamAsync(int id, CancellationToken ct = default);

    // Merchant
    Task<object> MerchantsAsync(string statusFilter, CancellationToken ct = default);
    Task<MgmtResult> CreateMerchantAsync(MerchantUpsertBody b, string apiKey, CancellationToken ct = default);
    Task UpdateMerchantAsync(int id, MerchantUpsertBody b, CancellationToken ct = default);
    Task DisableMerchantAsync(int id, CancellationToken ct = default);
    Task<object?> ShowCredentialsAsync(int id, CancellationToken ct = default);
    Task<MgmtResult> RotateSecretAsync(int id, string secret, CancellationToken ct = default);
    Task<MgmtResult> RotateKeyAsync(int id, string key, string secret, CancellationToken ct = default);
    Task<object> GroupsAsync(CancellationToken ct = default);
    Task<MgmtResult> CreateGroupAsync(string name, CancellationToken ct = default);
    Task UpdateGroupAsync(int id, GroupBody b, CancellationToken ct = default);
    Task DisableGroupAsync(int id, CancellationToken ct = default);
    Task AssignGroupAsync(int merchantId, int? groupId, CancellationToken ct = default);

    // BankAccount
    Task<object> BankAccountsAsync(int? scopeTeamId, string statusFilter, int? bankId, int? teamId, string? search, CancellationToken ct = default);
    Task<object> BanksAsync(CancellationToken ct = default);
    Task<object> TeamsListAsync(CancellationToken ct = default);
    Task<object?> BankAccountAsync(int id, int? scopeTeamId, CancellationToken ct = default);
    Task<MgmtResult> IdentifyBankAsync(string iban, CancellationToken ct = default);
    Task<MgmtResult> CreateBankAccountAsync(BankAccountUpsertBody b, CancellationToken ct = default);
    Task<MgmtResult> UpdateBankAccountAsync(int id, BankAccountUpsertBody b, CancellationToken ct = default);
    Task DisableBankAccountAsync(int id, CancellationToken ct = default);
    Task ReorderBankAccountsAsync(IReadOnlyList<int> ids, CancellationToken ct = default);
    Task<MgmtResult> SetBankAccountSortAsync(int id, int position, CancellationToken ct = default);

    // User
    Task<object> UsersAsync(int? teamAdminTeamId, int? teamAdminSelfId, string? userType, string? status, string? search, CancellationToken ct = default);
    Task<object> TeamsForUserOptionsAsync(CancellationToken ct = default);
    Task<object> MerchantsForUserOptionsAsync(CancellationToken ct = default);
    Task<TPanel.Domain.Entities.User?> GetUserEntityAsync(int id, CancellationToken ct = default);
    Task<bool> UsernameExistsAsync(string username, int? exceptId, CancellationToken ct = default);
    Task<bool> TeamExistsAsync(int id, CancellationToken ct = default);
    Task<bool> MerchantExistsAsync(int id, CancellationToken ct = default);
    Task<int> CreateUserAsync(string name, string username, string md5Password, int userType, int teamId, int? firmId, int status, int createdBy, CancellationToken ct = default);
    Task UpdateUserAsync(int id, IDictionary<string, object?> fields, bool killTokens, CancellationToken ct = default);
    Task DisableUserAsync(int id, CancellationToken ct = default);

    // Blacklist
    Task<object> BlacklistAsync(string? search, string? type, CancellationToken ct = default);
    Task<MgmtResult> CreateBlacklistAsync(BlacklistStoreBody b, CancellationToken ct = default);
    Task<MgmtResult> UpdateBlacklistAsync(int id, string? desc, CancellationToken ct = default);
    Task<MgmtResult> DeleteBlacklistAsync(int id, CancellationToken ct = default);
    Task<bool> BlacklistCheckAsync(string val, CancellationToken ct = default);
    Task<byte[]> ExportBlacklistAsync(string? search, string? type, CancellationToken ct = default);

    // Intermediary (management)
    Task<object> IntermediariesAsync(string statusFilter, CancellationToken ct = default);
    Task<MgmtResult> CreateIntermediaryAsync(IntermediaryStoreBody b, CancellationToken ct = default);
    Task UpdateIntermediaryAsync(int id, IntermediaryUpdateBody b, CancellationToken ct = default);
    Task DisableIntermediaryAsync(int id, CancellationToken ct = default);
    Task<MgmtResult> AttachMerchantAsync(AttachMerchantBody b, CancellationToken ct = default);
    Task DetachMerchantAsync(int pivotId, CancellationToken ct = default);
    Task UpdateMerchantRateAsync(int pivotId, UpdateRateBody b, CancellationToken ct = default);
    Task<MgmtResult> AttachTeamAsync(AttachTeamBody b, CancellationToken ct = default);
    Task DetachTeamAsync(int pivotId, CancellationToken ct = default);
    Task UpdateTeamRateAsync(int pivotId, UpdateRateBody b, CancellationToken ct = default);

    // Settings
    Task<object> SettingsIndexAsync(CancellationToken ct = default);
    Task UpdateSettingsAsync(IReadOnlyDictionary<string, string> settings, CancellationToken ct = default);
    Task<object> LogsAsync(string? direction, string? type, string? q, int page, CancellationToken ct = default);
    Task<object?> LogDetailAsync(int id, CancellationToken ct = default);
    Task<object> FindChatIdAsync(string? groupName, CancellationToken ct = default);
}
