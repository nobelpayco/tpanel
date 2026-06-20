using Microsoft.EntityFrameworkCore;
using TPanel.Domain.Entities;

namespace TPanel.Application.Common.Interfaces;

/// <summary>
/// Uygulama katmanının EF Core'a bağımlı olmadan kullandığı DbContext sözleşmesi.
/// Infrastructure'daki AppDbContext bunu uygular.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<User> Users { get; }
    DbSet<Team> Teams { get; }
    DbSet<Merchant> Merchants { get; }
    DbSet<PersonalAccessToken> PersonalAccessTokens { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
