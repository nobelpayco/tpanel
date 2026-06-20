using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TPanel.Domain.Entities;

namespace TPanel.Infrastructure.Persistence.Configurations;

public class MerchantConfiguration : IEntityTypeConfiguration<Merchant>
{
    public void Configure(EntityTypeBuilder<Merchant> b)
    {
        b.ToTable("merchantUser");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).HasColumnName("id");
        b.Property(x => x.GroupId).HasColumnName("group_id");
        b.Property(x => x.Status).HasColumnName("status");
        b.Property(x => x.Name).HasColumnName("name");
        b.Property(x => x.Email).HasColumnName("email");
        b.Property(x => x.Password).HasColumnName("password");
        b.Property(x => x.ApiKey).HasColumnName("apiKey");
        b.Property(x => x.ApiSecret).HasColumnName("apiSecret");
        b.Property(x => x.DepositLimit).HasColumnName("depositLimit");
        b.Property(x => x.MinDeposit).HasColumnName("minDeposit");
        b.Property(x => x.MaxDeposit).HasColumnName("maxDeposit");
        b.Property(x => x.Commission).HasColumnName("commission");
        b.Property(x => x.WithdrawCommission).HasColumnName("withdrawCommission");
        b.Property(x => x.DeliveryCommission).HasColumnName("deliveryCommission");
        b.Property(x => x.CreatedAt).HasColumnName("created_At");
        b.Property(x => x.CaseNow).HasColumnName("caseNow");
        b.Property(x => x.UseWallet).HasColumnName("useWallet");
        b.Property(x => x.ApprovedIp).HasColumnName("approved_ip");
        b.Property(x => x.NewApi).HasColumnName("new_api");
    }
}
