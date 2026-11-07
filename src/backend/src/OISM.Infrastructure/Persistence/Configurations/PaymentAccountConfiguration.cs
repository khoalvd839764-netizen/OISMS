using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class PaymentAccountConfiguration : IEntityTypeConfiguration<PaymentAccount>
{
    public void Configure(EntityTypeBuilder<PaymentAccount> builder)
    {
        builder.ToTable("payment_accounts");

        builder.HasKey(pa => new { pa.TenantId, pa.Id });

        builder.HasIndex(pa => new { pa.TenantId, pa.Id })
            .IsUnique()
            .HasDatabaseName("uq_payment_accounts_1");

        // Mỗi tenant chỉ có một tài khoản mặc định đang dùng (khai báo để EF biết ràng buộc của DB).
        builder.HasIndex(pa => pa.TenantId)
            .IsUnique()
            .HasFilter("is_default AND is_active")
            .HasDatabaseName("uq_payment_accounts_tenant_default_active");

        builder.HasIndex(pa => new { pa.TenantId, pa.BankBin, pa.AccountNumber })
            .IsUnique()
            .HasDatabaseName("uq_payment_accounts_2");

        builder.Property(pa => pa.TenantId).IsRequired();

        builder.Property(pa => pa.BankBin)
            .HasColumnName("bank_bin")
            .HasMaxLength(6)
            .IsRequired();

        builder.Property(pa => pa.BankName)
            .HasColumnName("bank_name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(pa => pa.BankShortName)
            .HasColumnName("bank_short_name")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(pa => pa.AccountNumber)
            .HasColumnName("account_number")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(pa => pa.AccountName)
            .HasColumnName("account_name")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(pa => pa.IsDefault)
            .HasColumnName("is_default")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(pa => pa.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(pa => pa.UpdatedByUserId)
            .HasColumnName("updated_by_user_id")
            .IsRequired(false);

        builder.Property(pa => pa.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(pa => pa.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        // Foreign keys
        builder.HasOne(pa => pa.Tenant)
            .WithMany()
            .HasForeignKey(pa => pa.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(pa => pa.UpdatedByUser)
            .WithMany()
            .HasForeignKey(pa => new { pa.TenantId, pa.UpdatedByUserId })
            // DB tự set NULL riêng cột updated_by_user_id (ON DELETE SET NULL (col)); EF không đụng tới tenant_id.
            .OnDelete(DeleteBehavior.NoAction);
    }
}
