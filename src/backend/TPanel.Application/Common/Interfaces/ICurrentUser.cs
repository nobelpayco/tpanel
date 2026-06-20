using TPanel.Domain.Entities;

namespace TPanel.Application.Common.Interfaces;

/// <summary>Aktif istekteki kimlik doğrulanmış kullanıcıya erişim.</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    int? UserId { get; }

    /// <summary>Aktif token id'si (logout için).</summary>
    ulong? TokenId { get; }

    /// <summary>DB'den tam User entity'sini getirir (null ise yetkisiz).</summary>
    Task<User?> GetUserAsync(CancellationToken ct = default);
}
