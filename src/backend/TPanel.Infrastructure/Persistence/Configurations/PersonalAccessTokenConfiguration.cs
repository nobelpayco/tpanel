using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TPanel.Domain.Entities;

namespace TPanel.Infrastructure.Persistence.Configurations;

public class PersonalAccessTokenConfiguration : IEntityTypeConfiguration<PersonalAccessToken>
{
    public void Configure(EntityTypeBuilder<PersonalAccessToken> b)
    {
        b.ToTable("personal_access_tokens");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TokenableType).HasColumnName("tokenable_type");
        b.Property(x => x.TokenableId).HasColumnName("tokenable_id");
        b.Property(x => x.Name).HasColumnName("name");
        b.Property(x => x.Token).HasColumnName("token");
        b.Property(x => x.Abilities).HasColumnName("abilities");
        b.Property(x => x.LastUsedAt).HasColumnName("last_used_at");
        b.Property(x => x.ExpiresAt).HasColumnName("expires_at");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.UpdatedAt).HasColumnName("updated_at");
    }
}
