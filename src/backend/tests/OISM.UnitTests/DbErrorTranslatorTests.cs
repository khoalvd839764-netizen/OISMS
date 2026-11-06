using Microsoft.EntityFrameworkCore;
using Moq;
using Npgsql;
using OISM.Application.Common.Interfaces;
using OISM.Infrastructure.Persistence;

namespace OISM.UnitTests;

public class DbErrorTranslatorTests
{
    [Fact]
    public void UniqueViolation_RequiresBothSqlStateAndConstraint()
    {
        using var db = CreateContext();
        var duplicate = new DbUpdateException("Save failed", new PostgresException(
            "Duplicate receipt", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation,
            constraintName: "uq_goods_receipts_2"));

        Assert.True(db.DatabaseErrors.IsUniqueViolation(duplicate, "uq_goods_receipts_2"));
        Assert.False(db.DatabaseErrors.IsUniqueViolation(duplicate, "uq_purchase_orders_2"));
        Assert.False(db.DatabaseErrors.IsForeignKeyViolation(duplicate));
        Assert.False(db.DatabaseErrors.IsUniqueViolation(new Exception("23505: uq_goods_receipts_2")));
    }

    [Fact]
    public void ForeignKeyAndCheckViolations_AreRecognizedIndividually()
    {
        using var db = CreateContext();
        var foreignKey = new PostgresException("Referenced row", "ERROR", "ERROR", PostgresErrorCodes.ForeignKeyViolation);
        var check = new PostgresException("Check failed", "ERROR", "ERROR", PostgresErrorCodes.CheckViolation,
            constraintName: "ck_purchase_order_items_received_range");

        Assert.True(db.DatabaseErrors.IsForeignKeyViolation(foreignKey));
        Assert.False(db.DatabaseErrors.IsForeignKeyViolation(check));
        Assert.True(db.DatabaseErrors.IsCheckViolation(check, "ck_purchase_order_items_received_range"));
        Assert.False(db.DatabaseErrors.IsUniqueViolation(check));
    }

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().Options, Mock.Of<ICurrentTenantService>());
}
