using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TPanel.Domain.Entities;

namespace TPanel.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("users");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.TeamId).HasColumnName("team_id");
        b.Property(x => x.UserTypeId).HasColumnName("user_type");
        b.Property(x => x.Name).HasColumnName("name");
        b.Property(x => x.Username).HasColumnName("username");
        b.Property(x => x.Password).HasColumnName("password");
        b.Property(x => x.Status).HasColumnName("status");
        b.Property(x => x.OtpOk).HasColumnName("otp_ok");
        b.Property(x => x.OtpCode).HasColumnName("otp_code");
        b.Property(x => x.Collapse).HasColumnName("collapse");
        b.Property(x => x.LastLogin).HasColumnName("last_login");
        b.Property(x => x.CreatedAt).HasColumnName("created_at");
        b.Property(x => x.FirmId).HasColumnName("firm_id");
        b.Property(x => x.MerchantGroupId).HasColumnName("merchant_group_id");
        b.Property(x => x.AutoReload).HasColumnName("auto_reload");
        b.Property(x => x.AutoModeChange).HasColumnName("auto_mode_change");
        b.Property(x => x.CreatedBy).HasColumnName("created_by");
        b.Property(x => x.IsSysAdmin).HasColumnName("is_sys_admin");
        b.Property(x => x.IsGodMode).HasColumnName("is_god_mode");

        b.Ignore(x => x.Role);
        b.Ignore(x => x.IsActive);
        b.Ignore(x => x.TwoFactorRequired);
    }
}
