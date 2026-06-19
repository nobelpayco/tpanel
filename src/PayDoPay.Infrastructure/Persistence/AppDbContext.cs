using System.Reflection;
using Microsoft.EntityFrameworkCore;
using PayDoPay.Application.Common.Interfaces;
using PayDoPay.Domain.Entities;

namespace PayDoPay.Infrastructure.Persistence;

/// <summary>
/// Mevcut MySQL şemasına (paydopay_crm) birebir eşlenen EF Core context'i.
/// Yeni migration üretmez; var olan tabloları kullanır.
/// </summary>
public class AppDbContext : DbContext, IApplicationDbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Team> Teams => Set<Team>();
    public DbSet<Merchant> Merchants => Set<Merchant>();
    public DbSet<PersonalAccessToken> PersonalAccessTokens => Set<PersonalAccessToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        base.OnModelCreating(modelBuilder);
    }
}
