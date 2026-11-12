using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence.Configurations;

public sealed class SupplierInvoiceConfiguration : IEntityTypeConfiguration<SupplierInvoice>
{
    public void Configure(EntityTypeBuilder<SupplierInvoice> builder)
    {
        builder.ToTable("supplier_invoices");
        builder.HasKey(i => new { i.TenantId, i.Id });
        builder.HasIndex(i => new { i.TenantId, i.RequestKey })
            .IsUnique().HasDatabaseName("uq_supplier_invoices_request_key");
        builder.HasIndex(i => new { i.TenantId, i.SupplierId, i.InvoiceSeries, i.InvoiceNumber })
            .IsUnique().HasFilter("status <> 'Cancelled'").HasDatabaseName("uq_supplier_invoices_number");
        builder.HasIndex(i => new { i.TenantId, i.GoodsReceiptId }, "ActiveReceipt")
            .IsUnique().HasFilter("status NOT IN ('Cancelled','Rejected')")
            .HasDatabaseName("uq_supplier_invoices_active_receipt");
        builder.HasIndex(i => new { i.TenantId, i.GoodsReceiptId })
            .HasDatabaseName("ix_supplier_invoices_receipt");
        builder.HasIndex(i => new { i.TenantId, i.SupplierId })
            .HasDatabaseName("ix_supplier_invoices_supplier");
        builder.HasIndex(i => new { i.TenantId, i.InvoiceDate, i.Id })
            .IsDescending(false, true, false).HasDatabaseName("ix_supplier_invoices_date");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(i => i.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.GoodsReceipt).WithMany()
            .HasForeignKey(i => new { i.TenantId, i.GoodsReceiptId })
            .HasPrincipalKey(r => new { r.TenantId, r.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Supplier>().WithMany().HasForeignKey(i => new { i.TenantId, i.SupplierId })
            .HasPrincipalKey(s => new { s.TenantId, s.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(i => new { i.TenantId, i.CreatedBy })
            .HasPrincipalKey(u => new { u.TenantId, u.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(i => new { i.TenantId, i.CancelledBy })
            .HasPrincipalKey(u => new { u.TenantId, u.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(i => i.Items).WithOne().HasForeignKey(i => new { i.TenantId, i.InvoiceId })
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.Variance).WithOne()
            .HasForeignKey<PurchaseVariance>(v => new { v.TenantId, v.InvoiceId })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class SupplierInvoiceItemConfiguration : IEntityTypeConfiguration<SupplierInvoiceItem>
{
    public void Configure(EntityTypeBuilder<SupplierInvoiceItem> builder)
    {
        builder.ToTable("supplier_invoice_items");
        builder.HasKey(i => new { i.TenantId, i.Id });
        builder.HasIndex(i => new { i.TenantId, i.InvoiceId, i.GoodsReceiptItemId })
            .IsUnique().HasDatabaseName("uq_supplier_invoice_items_receipt_item");
        builder.Property(i => i.Quantity).HasPrecision(18, 3);
        builder.Property(i => i.ExpectedQuantity).HasPrecision(18, 3);
        builder.Property(i => i.QuantityDiff).HasPrecision(18, 3);
        builder.Property(i => i.UnitPrice).HasPrecision(20, 6);
        builder.Property(i => i.ExpectedUnitPrice).HasPrecision(20, 6);
        builder.Property(i => i.PriceDiff).HasPrecision(20, 6);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(i => i.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GoodsReceiptItem>().WithMany().HasForeignKey(i => new { i.TenantId, i.GoodsReceiptItemId })
            .HasPrincipalKey(r => new { r.TenantId, r.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProductVariant>().WithMany().HasForeignKey(i => new { i.TenantId, i.SkuId })
            .HasPrincipalKey(s => new { s.TenantId, s.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class PurchaseVarianceConfiguration : IEntityTypeConfiguration<PurchaseVariance>
{
    public void Configure(EntityTypeBuilder<PurchaseVariance> builder)
    {
        builder.ToTable("purchase_variances");
        builder.HasKey(v => new { v.TenantId, v.Id });
        builder.HasIndex(v => v.InvoiceId).IsUnique().HasDatabaseName("uq_purchase_variances_invoice");

        builder.HasOne<Tenant>().WithMany().HasForeignKey(v => v.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GoodsReceipt>().WithMany().HasForeignKey(v => new { v.TenantId, v.GoodsReceiptId })
            .HasPrincipalKey(r => new { r.TenantId, r.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(v => new { v.TenantId, v.DecidedBy })
            .HasPrincipalKey(u => new { u.TenantId, u.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
