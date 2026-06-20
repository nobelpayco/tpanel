using System.Security.Cryptography;
using System.Text.RegularExpressions;
using PayDoPay.Application.Common.Interfaces;
using PayDoPay.Domain.Entities;
using PayDoPay.Domain.Enums;

namespace PayDoPay.Application.Features.Management;

public static class MgmtRandom
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
    public static string String(int len)
    {
        var bytes = RandomNumberGenerator.GetBytes(len);
        var chars = new char[len];
        for (var i = 0; i < len; i++) chars[i] = Alphabet[bytes[i] % Alphabet.Length];
        return new string(chars);
    }
    public static string Md5(string s) => Convert.ToHexStringLower(MD5.HashData(System.Text.Encoding.UTF8.GetBytes(s)));
}

// ---- Team ----
public interface ITeamMgmtService
{
    Task<MgmtResult> IndexAsync(string status, string? search, CancellationToken ct = default);
    Task<MgmtResult> ShowAsync(int id, CancellationToken ct = default);
    Task<MgmtResult> StoreAsync(User u, TeamUpsertBody b, CancellationToken ct = default);
    Task<MgmtResult> UpdateAsync(User u, int id, TeamUpsertBody b, CancellationToken ct = default);
    Task<MgmtResult> DestroyAsync(User u, int id, CancellationToken ct = default);
}
public class TeamMgmtService : ITeamMgmtService
{
    private readonly IManagementStore _s;
    public TeamMgmtService(IManagementStore s) => _s = s;
    public async Task<MgmtResult> IndexAsync(string status, string? search, CancellationToken ct = default) => MgmtResult.Ok(await _s.TeamsAsync(status, search, ct));
    public async Task<MgmtResult> ShowAsync(int id, CancellationToken ct = default) { var t = await _s.TeamAsync(id, ct); return t is null ? MgmtResult.Msg(404, "Takım bulunamadı.") : MgmtResult.Ok(t); }
    public async Task<MgmtResult> StoreAsync(User u, TeamUpsertBody b, CancellationToken ct = default)
    { if (!u.CanManageTeams) return MgmtResult.Msg(403, "Bu işlem için yetkiniz yok."); if (string.IsNullOrWhiteSpace(b.Name)) return MgmtResult.Msg(422, "İsim zorunludur."); return await _s.CreateTeamAsync(b, ct); }
    public async Task<MgmtResult> UpdateAsync(User u, int id, TeamUpsertBody b, CancellationToken ct = default)
    { if (!u.CanManageTeams) return MgmtResult.Msg(403, "Bu işlem için yetkiniz yok."); return await _s.UpdateTeamAsync(id, b, ct); }
    public async Task<MgmtResult> DestroyAsync(User u, int id, CancellationToken ct = default)
    { if (!u.CanManageTeams) return MgmtResult.Msg(403, "Bu işlem için yetkiniz yok."); await _s.DisableTeamAsync(id, ct); return MgmtResult.Msg(200, "Takım devre dışı bırakıldı."); }
}

// ---- Merchant ----
public interface IMerchantMgmtService
{
    Task<MgmtResult> IndexAsync(string status, CancellationToken ct = default);
    Task<MgmtResult> StoreAsync(User u, MerchantUpsertBody b, CancellationToken ct = default);
    Task<MgmtResult> UpdateAsync(User u, int id, MerchantUpsertBody b, CancellationToken ct = default);
    Task<MgmtResult> DestroyAsync(User u, int id, CancellationToken ct = default);
    Task<MgmtResult> ShowCredentialsAsync(int id, CancellationToken ct = default);
    Task<MgmtResult> RotateSecretAsync(int id, CancellationToken ct = default);
    Task<MgmtResult> RotateKeyAsync(int id, CancellationToken ct = default);
    Task<MgmtResult> GroupsAsync(CancellationToken ct = default);
    Task<MgmtResult> StoreGroupAsync(string? name, CancellationToken ct = default);
    Task<MgmtResult> UpdateGroupAsync(int id, GroupBody b, CancellationToken ct = default);
    Task<MgmtResult> DestroyGroupAsync(int id, CancellationToken ct = default);
    Task<MgmtResult> AssignToGroupAsync(AssignGroupBody b, CancellationToken ct = default);
}
public class MerchantMgmtService : IMerchantMgmtService
{
    private readonly IManagementStore _s;
    public MerchantMgmtService(IManagementStore s) => _s = s;
    public async Task<MgmtResult> IndexAsync(string status, CancellationToken ct = default) => MgmtResult.Ok(await _s.MerchantsAsync(status, ct));
    public async Task<MgmtResult> StoreAsync(User u, MerchantUpsertBody b, CancellationToken ct = default)
    { if (!u.CanManageMerchants) return MgmtResult.Msg(403, "Bu işlem için yetkiniz yok."); if (string.IsNullOrWhiteSpace(b.Name)) return MgmtResult.Msg(422, "İsim zorunludur."); return await _s.CreateMerchantAsync(b, MgmtRandom.String(48), ct); }
    public async Task<MgmtResult> UpdateAsync(User u, int id, MerchantUpsertBody b, CancellationToken ct = default)
    { if (!u.CanManageMerchants) return MgmtResult.Msg(403, "Bu işlem için yetkiniz yok."); await _s.UpdateMerchantAsync(id, b, ct); return MgmtResult.Msg(200, "Merchant güncellendi."); }
    public async Task<MgmtResult> DestroyAsync(User u, int id, CancellationToken ct = default)
    { if (!u.CanManageMerchants) return MgmtResult.Msg(403, "Bu işlem için yetkiniz yok."); await _s.DisableMerchantAsync(id, ct); return MgmtResult.Msg(200, "Merchant devre dışı bırakıldı."); }
    public async Task<MgmtResult> ShowCredentialsAsync(int id, CancellationToken ct = default) { var r = await _s.ShowCredentialsAsync(id, ct); return r is null ? MgmtResult.Msg(404, "Merchant bulunamadı.") : MgmtResult.Ok(r); }
    public async Task<MgmtResult> RotateSecretAsync(int id, CancellationToken ct = default) => await _s.RotateSecretAsync(id, MgmtRandom.String(64), ct);
    public async Task<MgmtResult> RotateKeyAsync(int id, CancellationToken ct = default) => await _s.RotateKeyAsync(id, MgmtRandom.String(48), MgmtRandom.String(64), ct);
    public async Task<MgmtResult> GroupsAsync(CancellationToken ct = default) => MgmtResult.Ok(await _s.GroupsAsync(ct));
    public async Task<MgmtResult> StoreGroupAsync(string? name, CancellationToken ct = default) { if (string.IsNullOrWhiteSpace(name)) return MgmtResult.Msg(422, "İsim zorunludur."); return await _s.CreateGroupAsync(name, ct); }
    public async Task<MgmtResult> UpdateGroupAsync(int id, GroupBody b, CancellationToken ct = default) { await _s.UpdateGroupAsync(id, b, ct); return MgmtResult.Msg(200, "Grup güncellendi."); }
    public async Task<MgmtResult> DestroyGroupAsync(int id, CancellationToken ct = default) { await _s.DisableGroupAsync(id, ct); return MgmtResult.Msg(200, "Grup devre dışı bırakıldı."); }
    public async Task<MgmtResult> AssignToGroupAsync(AssignGroupBody b, CancellationToken ct = default) { await _s.AssignGroupAsync(b.MerchantId, b.GroupId, ct); return MgmtResult.Msg(200, "Merchant gruba atandı."); }
}

// ---- BankAccount ----
public interface IBankAccountMgmtService
{
    Task<MgmtResult> IndexAsync(User u, string status, int? bank, int? team, string? search, CancellationToken ct = default);
    Task<MgmtResult> BanksAsync(CancellationToken ct = default);
    Task<MgmtResult> TeamsAsync(CancellationToken ct = default);
    Task<MgmtResult> ShowAsync(User u, int id, CancellationToken ct = default);
    Task<MgmtResult> IdentifyAsync(string? iban, CancellationToken ct = default);
    Task<MgmtResult> StoreAsync(User u, BankAccountUpsertBody b, CancellationToken ct = default);
    Task<MgmtResult> UpdateAsync(User u, int id, BankAccountUpsertBody b, CancellationToken ct = default);
    Task<MgmtResult> DestroyAsync(User u, int id, CancellationToken ct = default);
    Task<MgmtResult> ReorderAsync(User u, IReadOnlyList<int>? ids, CancellationToken ct = default);
    Task<MgmtResult> SetSortAsync(User u, int id, int position, CancellationToken ct = default);
}
public class BankAccountMgmtService : IBankAccountMgmtService
{
    private readonly IManagementStore _s;
    public BankAccountMgmtService(IManagementStore s) => _s = s;
    public async Task<MgmtResult> IndexAsync(User u, string status, int? bank, int? team, string? search, CancellationToken ct = default)
        => MgmtResult.Ok(await _s.BankAccountsAsync(u.IsTeamMember ? u.TeamId : null, status, bank, team, search, ct));
    public async Task<MgmtResult> BanksAsync(CancellationToken ct = default) => MgmtResult.Ok(await _s.BanksAsync(ct));
    public async Task<MgmtResult> TeamsAsync(CancellationToken ct = default) => MgmtResult.Ok(await _s.TeamsListAsync(ct));
    public async Task<MgmtResult> ShowAsync(User u, int id, CancellationToken ct = default)
    {
        var a = await _s.BankAccountAsync(id, u.IsTeamMember ? u.TeamId : null, ct);
        if (a is null) return MgmtResult.Msg(404, "Hesap bulunamadı.");
        if (a is string s && s == "FORBIDDEN") return MgmtResult.Msg(403, "Yetkiniz yok.");
        return MgmtResult.Ok(a);
    }
    public async Task<MgmtResult> IdentifyAsync(string? iban, CancellationToken ct = default) { if (string.IsNullOrEmpty(iban) || iban.Length < 26) return MgmtResult.Msg(422, "Geçersiz IBAN."); return await _s.IdentifyBankAsync(iban, ct); }
    public async Task<MgmtResult> StoreAsync(User u, BankAccountUpsertBody b, CancellationToken ct = default)
    {
        if (!u.CanManageBankAccounts) return MgmtResult.Msg(403, "Bu işlem için yetkiniz yok.");
        if (u.IsTeamMember) b = b with { TeamId = u.TeamId };
        if (b.TeamId is null || b.BankId is null || string.IsNullOrEmpty(b.AccountIban) || string.IsNullOrEmpty(b.AccountHolder) || string.IsNullOrEmpty(b.AccountCode))
            return MgmtResult.Msg(422, "Eksik bilgi.");
        return await _s.CreateBankAccountAsync(b, ct);
    }
    public async Task<MgmtResult> UpdateAsync(User u, int id, BankAccountUpsertBody b, CancellationToken ct = default)
    {
        if (!u.CanManageBankAccounts) return MgmtResult.Msg(403, "Bu işlem için yetkiniz yok.");
        if (u.IsTeamMember) b = b with { TeamId = u.TeamId };
        return await _s.UpdateBankAccountAsync(id, b, ct);
    }
    public async Task<MgmtResult> DestroyAsync(User u, int id, CancellationToken ct = default)
    { if (!u.CanManageBankAccounts) return MgmtResult.Msg(403, "Bu işlem için yetkiniz yok."); await _s.DisableBankAccountAsync(id, ct); return MgmtResult.Msg(200, "Hesap devre dışı bırakıldı."); }
    public async Task<MgmtResult> ReorderAsync(User u, IReadOnlyList<int>? ids, CancellationToken ct = default)
    { if (!u.IsAdmin) return MgmtResult.Msg(403, "Bu işlem için yetkiniz yok."); if (ids is null || ids.Count == 0) return MgmtResult.Msg(422, "ids zorunludur."); await _s.ReorderBankAccountsAsync(ids, ct); return MgmtResult.Msg(200, "Sıralama güncellendi."); }
    public async Task<MgmtResult> SetSortAsync(User u, int id, int position, CancellationToken ct = default)
    { if (!u.IsAdmin) return MgmtResult.Msg(403, "Bu işlem için yetkiniz yok."); if (position < 1) return MgmtResult.Msg(422, "Geçersiz pozisyon."); return await _s.SetBankAccountSortAsync(id, position, ct); }
}

// ---- User ----
public interface IUserMgmtService
{
    Task<MgmtResult> IndexAsync(User u, string? userType, string? status, string? search, CancellationToken ct = default);
    Task<MgmtResult> OptionsAsync(User u, CancellationToken ct = default);
    Task<MgmtResult> StoreAsync(User u, UserCreateBody b, CancellationToken ct = default);
    Task<MgmtResult> UpdateAsync(User actor, int id, UserUpdateBody b, CancellationToken ct = default);
    Task<MgmtResult> DestroyAsync(User actor, int id, CancellationToken ct = default);
}
public class UserMgmtService : IUserMgmtService
{
    private readonly IManagementStore _s;
    private static readonly Regex PwRegex = new(@"^(?=.*[A-Za-z])(?=.*\d).+$", RegexOptions.Compiled);
    public UserMgmtService(IManagementStore s) => _s = s;

    public async Task<MgmtResult> IndexAsync(User u, string? userType, string? status, string? search, CancellationToken ct = default)
    {
        if (!u.CanManageUsers) return MgmtResult.Msg(403, "Bu sayfaya erişim yetkiniz yok.");
        int? taTeam = u.IsTeamAdmin ? u.TeamId : null;
        return MgmtResult.Ok(await _s.UsersAsync(taTeam, u.Id, userType, status, search, ct));
    }
    public async Task<MgmtResult> OptionsAsync(User u, CancellationToken ct = default)
    {
        if (!u.CanManageUsers) return MgmtResult.Msg(403, "Bu sayfaya erişim yetkiniz yok.");
        var roleLabels = new Dictionary<int, string> { [1] = "Super Admin", [4] = "Sub Admin", [5] = "Team Admin", [2] = "Team Agent", [3] = "Merchant", [6] = "Blocked" };
        var allowed = u.CreatableUserTypes.Select(t => new { id = (int)t, label = roleLabels.GetValueOrDefault((int)t, "-") });
        object teams = u.IsTeamAdmin ? Array.Empty<object>() : await _s.TeamsForUserOptionsAsync(ct);
        var merchants = await _s.MerchantsForUserOptionsAsync(ct);
        return MgmtResult.Ok(new { allowed_roles = allowed, teams, merchants, role_labels = roleLabels });
    }

    public async Task<MgmtResult> StoreAsync(User u, UserCreateBody b, CancellationToken ct = default)
    {
        if (!u.CanManageUsers) return MgmtResult.Msg(403, "Bu sayfaya erişim yetkiniz yok.");
        if (string.IsNullOrWhiteSpace(b.Name) || string.IsNullOrWhiteSpace(b.Username) || string.IsNullOrEmpty(b.Password) || b.UserType is null || b.Status is null)
            return MgmtResult.Msg(422, "Eksik bilgi.");
        if (b.Password.Length < 6 || !PwRegex.IsMatch(b.Password)) return MgmtResult.Msg(422, "Şifre en az 6 karakter ve bir harf+rakam içermelidir.");
        if (await _s.UsernameExistsAsync(b.Username, null, ct)) return MgmtResult.Msg(422, "Bu kullanıcı adı zaten kullanımda.");

        var targetType = (UserType)b.UserType.Value;
        if (!u.CanCreateUserType(targetType)) return MgmtResult.Msg(403, "Bu kullanıcı tipini yaratma yetkiniz yok.");

        int teamId = 0; int? firmId = null;
        if (targetType == UserType.TeamAgent)
        {
            if (u.IsTeamAdmin) teamId = u.TeamId;
            else { if (b.TeamId is null) return MgmtResult.Msg(422, "Takım seçimi zorunludur."); teamId = b.TeamId.Value; if (!await _s.TeamExistsAsync(teamId, ct)) return MgmtResult.Msg(422, "Takım bulunamadı."); }
        }
        else if (targetType == UserType.TeamAdmin)
        { if (b.TeamId is null) return MgmtResult.Msg(422, "Takım seçimi zorunludur."); teamId = b.TeamId.Value; if (!await _s.TeamExistsAsync(teamId, ct)) return MgmtResult.Msg(422, "Takım bulunamadı."); }
        else if (targetType == UserType.Merchant)
        { if (b.FirmId is null) return MgmtResult.Msg(422, "Merchant seçimi zorunludur."); firmId = b.FirmId.Value; if (!await _s.MerchantExistsAsync(firmId.Value, ct)) return MgmtResult.Msg(422, "Merchant bulunamadı."); }

        var id = await _s.CreateUserAsync(b.Name, b.Username, MgmtRandom.Md5(b.Password), (int)targetType, teamId, firmId, b.Status.Value, u.Id, ct);
        return MgmtResult.Ok(new { message = "Kullanıcı eklendi.", id });
    }

    public async Task<MgmtResult> UpdateAsync(User actor, int id, UserUpdateBody b, CancellationToken ct = default)
    {
        if (!actor.CanManageUsers) return MgmtResult.Msg(403, "Bu sayfaya erişim yetkiniz yok.");
        var target = await _s.GetUserEntityAsync(id, ct);
        if (target is null) return MgmtResult.Msg(404, "Kullanıcı bulunamadı.");
        if (!actor.CanEditUser(target)) return MgmtResult.Msg(403, "Bu kullanıcıyı düzenleme yetkiniz yok.");
        if (actor.IsTeamAdmin && target.Id == actor.Id) return MgmtResult.Msg(403, "Kendi hesabınızı bu sayfadan düzenleyemezsiniz.");
        if (string.IsNullOrWhiteSpace(b.Name) || string.IsNullOrWhiteSpace(b.Username) || b.Status is null) return MgmtResult.Msg(422, "Eksik bilgi.");
        if (!string.IsNullOrEmpty(b.Password) && (b.Password.Length < 6 || !PwRegex.IsMatch(b.Password))) return MgmtResult.Msg(422, "Şifre en az 6 karakter ve bir harf+rakam içermelidir.");
        if (await _s.UsernameExistsAsync(b.Username, id, ct)) return MgmtResult.Msg(422, "Bu kullanıcı adı zaten kullanımda.");

        var fields = new Dictionary<string, object?> { ["name"] = b.Name, ["username"] = b.Username, ["status"] = b.Status.Value.ToString() };
        if (!string.IsNullOrEmpty(b.Password)) fields["password"] = MgmtRandom.Md5(b.Password);

        var becameBlocked = false;
        if (actor.IsAdmin && b.UserType is not null)
        {
            var newType = (UserType)b.UserType.Value;
            if (!actor.CanCreateUserType(newType)) return MgmtResult.Msg(403, "Bu kullanıcı tipine değiştirme yetkiniz yok.");
            fields["user_type"] = (int)newType;
            if (newType is UserType.TeamAgent or UserType.TeamAdmin) { fields["team_id"] = b.TeamId ?? target.TeamId; fields["firm_id"] = null; }
            else if (newType == UserType.Merchant) { fields["firm_id"] = b.FirmId ?? target.FirmId; fields["team_id"] = 0; }
            else { fields["team_id"] = 0; fields["firm_id"] = null; }
            becameBlocked = newType == UserType.Blocked;
        }
        var killTokens = b.Status.Value == 0 || becameBlocked;
        await _s.UpdateUserAsync(id, fields, killTokens, ct);
        return MgmtResult.Msg(200, "Kullanıcı güncellendi.");
    }

    public async Task<MgmtResult> DestroyAsync(User actor, int id, CancellationToken ct = default)
    {
        if (!actor.CanManageUsers) return MgmtResult.Msg(403, "Bu sayfaya erişim yetkiniz yok.");
        if (actor.IsTeamAdmin) return MgmtResult.Msg(403, "Kullanıcı silme yetkiniz yok.");
        var target = await _s.GetUserEntityAsync(id, ct);
        if (target is null) return MgmtResult.Msg(404, "Kullanıcı bulunamadı.");
        if (!actor.CanEditUser(target)) return MgmtResult.Msg(403, "Bu kullanıcıyı silme yetkiniz yok.");
        if (target.Id == actor.Id) return MgmtResult.Msg(422, "Kendinizi silemezsiniz.");
        await _s.DisableUserAsync(id, ct);
        return MgmtResult.Msg(200, "Kullanıcı pasif edildi ve oturumu sonlandırıldı.");
    }
}

// ---- Blacklist ----
public interface IBlacklistMgmtService
{
    Task<MgmtResult> IndexAsync(string? search, string? type, CancellationToken ct = default);
    Task<MgmtResult> StoreAsync(BlacklistStoreBody b, CancellationToken ct = default);
    Task<MgmtResult> UpdateAsync(int id, string? desc, CancellationToken ct = default);
    Task<MgmtResult> DestroyAsync(int id, CancellationToken ct = default);
    Task<MgmtResult> CheckAsync(string? val, CancellationToken ct = default);
    Task<byte[]> ExportAsync(string? search, string? type, CancellationToken ct = default);
}
public class BlacklistMgmtService : IBlacklistMgmtService
{
    private readonly IManagementStore _s;
    public BlacklistMgmtService(IManagementStore s) => _s = s;
    public async Task<MgmtResult> IndexAsync(string? search, string? type, CancellationToken ct = default) => MgmtResult.Ok(await _s.BlacklistAsync(search, type, ct));
    public async Task<MgmtResult> StoreAsync(BlacklistStoreBody b, CancellationToken ct = default) { if (string.IsNullOrWhiteSpace(b.Val)) return MgmtResult.Msg(422, "Değer zorunludur."); return await _s.CreateBlacklistAsync(b, ct); }
    public async Task<MgmtResult> UpdateAsync(int id, string? desc, CancellationToken ct = default) => await _s.UpdateBlacklistAsync(id, desc, ct);
    public async Task<MgmtResult> DestroyAsync(int id, CancellationToken ct = default) => await _s.DeleteBlacklistAsync(id, ct);
    public async Task<MgmtResult> CheckAsync(string? val, CancellationToken ct = default) { if (string.IsNullOrEmpty(val)) return MgmtResult.Msg(422, "Değer zorunludur."); return MgmtResult.Ok(new { blacklisted = await _s.BlacklistCheckAsync(val, ct) }); }
    public Task<byte[]> ExportAsync(string? search, string? type, CancellationToken ct = default) => _s.ExportBlacklistAsync(search, type, ct);
}

// ---- Intermediary (management) ----
public interface IIntermediaryMgmtService
{
    Task<MgmtResult> IndexAsync(string status, CancellationToken ct = default);
    Task<MgmtResult> StoreAsync(IntermediaryStoreBody b, CancellationToken ct = default);
    Task<MgmtResult> UpdateAsync(int id, IntermediaryUpdateBody b, CancellationToken ct = default);
    Task<MgmtResult> DestroyAsync(int id, CancellationToken ct = default);
    Task<MgmtResult> AttachMerchantAsync(AttachMerchantBody b, CancellationToken ct = default);
    Task<MgmtResult> DetachMerchantAsync(int pivotId, CancellationToken ct = default);
    Task<MgmtResult> UpdateMerchantRateAsync(int pivotId, UpdateRateBody b, CancellationToken ct = default);
    Task<MgmtResult> AttachTeamAsync(AttachTeamBody b, CancellationToken ct = default);
    Task<MgmtResult> DetachTeamAsync(int pivotId, CancellationToken ct = default);
    Task<MgmtResult> UpdateTeamRateAsync(int pivotId, UpdateRateBody b, CancellationToken ct = default);
}
public class IntermediaryMgmtService : IIntermediaryMgmtService
{
    private readonly IManagementStore _s;
    public IntermediaryMgmtService(IManagementStore s) => _s = s;
    public async Task<MgmtResult> IndexAsync(string status, CancellationToken ct = default) => MgmtResult.Ok(await _s.IntermediariesAsync(status, ct));
    public async Task<MgmtResult> StoreAsync(IntermediaryStoreBody b, CancellationToken ct = default) { if (string.IsNullOrWhiteSpace(b.Name) || b.Type is < 1 or > 3) return MgmtResult.Msg(422, "Geçersiz bilgi."); return await _s.CreateIntermediaryAsync(b, ct); }
    public async Task<MgmtResult> UpdateAsync(int id, IntermediaryUpdateBody b, CancellationToken ct = default) { await _s.UpdateIntermediaryAsync(id, b, ct); return MgmtResult.Msg(200, "Aracı güncellendi."); }
    public async Task<MgmtResult> DestroyAsync(int id, CancellationToken ct = default) { await _s.DisableIntermediaryAsync(id, ct); return MgmtResult.Msg(200, "Aracı devre dışı bırakıldı."); }
    public async Task<MgmtResult> AttachMerchantAsync(AttachMerchantBody b, CancellationToken ct = default) => await _s.AttachMerchantAsync(b, ct);
    public async Task<MgmtResult> DetachMerchantAsync(int pivotId, CancellationToken ct = default) { await _s.DetachMerchantAsync(pivotId, ct); return MgmtResult.Msg(200, "Bağlantı devre dışı bırakıldı."); }
    public async Task<MgmtResult> UpdateMerchantRateAsync(int pivotId, UpdateRateBody b, CancellationToken ct = default) { await _s.UpdateMerchantRateAsync(pivotId, b, ct); return MgmtResult.Msg(200, "Güncellendi."); }
    public async Task<MgmtResult> AttachTeamAsync(AttachTeamBody b, CancellationToken ct = default) => await _s.AttachTeamAsync(b, ct);
    public async Task<MgmtResult> DetachTeamAsync(int pivotId, CancellationToken ct = default) { await _s.DetachTeamAsync(pivotId, ct); return MgmtResult.Msg(200, "Bağlantı devre dışı bırakıldı."); }
    public async Task<MgmtResult> UpdateTeamRateAsync(int pivotId, UpdateRateBody b, CancellationToken ct = default) { await _s.UpdateTeamRateAsync(pivotId, b, ct); return MgmtResult.Msg(200, "Güncellendi."); }
}

// ---- Settings ----
public interface ISettingsMgmtService
{
    Task<MgmtResult> IndexAsync(CancellationToken ct = default);
    Task<MgmtResult> UpdateAsync(SettingsUpdateBody b, CancellationToken ct = default);
    Task<MgmtResult> LogsAsync(string? direction, string? type, string? q, int page, CancellationToken ct = default);
    Task<MgmtResult> LogDetailAsync(int id, CancellationToken ct = default);
    Task<MgmtResult> FindChatIdAsync(string? groupName, CancellationToken ct = default);
}
public class SettingsMgmtService : ISettingsMgmtService
{
    private readonly IManagementStore _s;
    public SettingsMgmtService(IManagementStore s) => _s = s;
    public async Task<MgmtResult> IndexAsync(CancellationToken ct = default) => MgmtResult.Ok(await _s.SettingsIndexAsync(ct));
    public async Task<MgmtResult> UpdateAsync(SettingsUpdateBody b, CancellationToken ct = default)
    {
        if (b.Settings is null) return MgmtResult.Msg(422, "settings zorunludur.");
        var dict = b.Settings.ToDictionary(kv => kv.Key, kv => kv.Value.ValueKind == System.Text.Json.JsonValueKind.String ? kv.Value.GetString() ?? "" : kv.Value.ToString());
        await _s.UpdateSettingsAsync(dict, ct);
        return MgmtResult.Msg(200, "Ayarlar kaydedildi.");
    }
    public async Task<MgmtResult> LogsAsync(string? direction, string? type, string? q, int page, CancellationToken ct = default) => MgmtResult.Ok(await _s.LogsAsync(direction, type, q, Math.Max(1, page), ct));
    public async Task<MgmtResult> LogDetailAsync(int id, CancellationToken ct = default) { var l = await _s.LogDetailAsync(id, ct); return l is null ? MgmtResult.Msg(404, "Log bulunamadı.") : MgmtResult.Ok(l); }
    public async Task<MgmtResult> FindChatIdAsync(string? groupName, CancellationToken ct = default) { if (string.IsNullOrWhiteSpace(groupName)) return MgmtResult.Msg(422, "group_name zorunludur."); return MgmtResult.Ok(await _s.FindChatIdAsync(groupName, ct)); }
}
