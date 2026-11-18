using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public class DailySalesSummaryConfiguration : IEntityTypeConfiguration<DailySalesSummary>
{
    public void Configure(EntityTypeBuilder<DailySalesSummary> builder)
    {
        builder.ToTable("daily_sales_summaries");

        builder.HasKey(d => new { d.TenantId, d.Id });

        builder.HasIndex(d => new { d.TenantId, d.Id })
            .IsUnique()
            .HasDatabaseName("uq_daily_sales_summaries_1");

        builder.HasIndex(d => new { d.TenantId, d.BusinessDate, d.BranchId, d.Channel, d.SkuId })
            .IsUnique()
            .HasDatabaseName("uq_daily_sales_summaries_2");

        builder.Property(d => d.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()")
            .IsRequired();

        builder.Property(d => d.TenantId)
            .HasColumnName("tenant_id")
            .IsRequired();

        builder.Property(d => d.BusinessDate)
            .HasColumnName("business_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(d => d.BranchId)
            .HasColumnName("branch_id")
            .IsRequired();

        builder.Property(d => d.Channel)
            .HasColumnName("channel")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(d => d.SkuId)
            .HasColumnName("sku_id")
            .IsRequired();

        builder.Property(d => d.SoldQuantity)
            .HasColumnName("sold_quantity")
            .HasPrecision(18, 3)
            .IsRequired();

        builder.Property(d => d.ReturnedQuantity)
            .HasColumnName("returned_quantity")
            .HasPrecision(18, 3)
            .IsRequired();

        builder.Property(d => d.Revenue)
            .HasColumnName("revenue")
            .HasPrecision(20, 2)
            .IsRequired();

        builder.Property(d => d.ReturnAmount)
            .HasColumnName("return_amount")
            .HasPrecision(20, 2)
            .IsRequired();

        builder.Property(d => d.CostOfGoodsSold)
            .HasColumnName("cost_of_goods_sold")
            .HasPrecision(20, 2)
            .IsRequired();

        builder.Property(d => d.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(d => d.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired(false);

        // Foreign keys
        builder.HasOne(d => d.Tenant)
            .WithMany()
            .HasForeignKey(d => d.TenantId)
            .HasConstraintName("fk_daily_sales_summaries_tenant_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.Branch)
            .WithMany()
            .HasForeignKey(d => new { d.TenantId, d.BranchId })
            .HasPrincipalKey(b => new { b.TenantId, b.Id })
            .HasConstraintName("fk_daily_sales_summaries_branch_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(d => d.Sku)
            .WithMany()
            .HasForeignKey(d => new { d.TenantId, d.SkuId })
            .HasPrincipalKey(s => new { s.TenantId, s.Id })
            .HasConstraintName("fk_daily_sales_summaries_sku_id")
            .OnDelete(DeleteBehavior.Restrict);
    }
}
