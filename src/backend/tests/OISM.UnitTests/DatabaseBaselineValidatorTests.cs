using FluentAssertions;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.UnitTests;

public class DatabaseBaselineValidatorTests
{
    [Theory]
    [InlineData("(retail_price >= (0)::numeric)", "retail_price>=(0)::numeric")]
    [InlineData("((retail_price >= (0)::numeric))", "retail_price>=(0)::numeric")]
    [InlineData("   ( retail_price   >=   (0)::numeric )  ", "retail_price>=(0)::numeric")]
    [InlineData("retail_price >= (0)::numeric", "retail_price>=(0)::numeric")]
    [InlineData("(reserved <= on_hand)", "reserved<=on_hand")]
    [InlineData("((status)::text = ANY ((ARRAY['Active'::character varying, 'Consumed'::character varying])::text[]))",
                "(status)::text=any((array['Active'::character varying,'Consumed'::character varying])::text[])")]
    public void NormalizeExpression_ShouldStandardizeCanonicalExpressions(string input, string expected)
    {
        var normalized = DatabaseMigrationService.NormalizeExpression(input);
        normalized.Should().Be(expected);
    }

    [Fact]
    public void NormalizeExpression_ShouldPreserveStringLiteralsAndCasing_BugA()
    {
        // Bug A: 'Owner', 'owner', 'Ow ner' must not be normalized to the same string
        var exprOwner = DatabaseMigrationService.NormalizeExpression("((role)::text = 'Owner'::text)");
        var exprOwnerLower = DatabaseMigrationService.NormalizeExpression("((role)::text = 'owner'::text)");
        var exprOwnerSpace = DatabaseMigrationService.NormalizeExpression("((role)::text = 'Ow ner'::text)");

        exprOwner.Should().NotBe(exprOwnerLower, "String literal case sensitivity must be preserved");
        exprOwner.Should().NotBe(exprOwnerSpace, "String literal internal whitespace must be preserved");

        // Canonical expressions with formatting differences outside literals should still match
        var formattedExpr = DatabaseMigrationService.NormalizeExpression(" ( ( role ) :: text  =  'Owner' :: text ) ");
        formattedExpr.Should().Be(exprOwner);

        // Quoted identifiers preserve exact casing and spacing
        var quotedCol1 = DatabaseMigrationService.NormalizeExpression("\"Role\"");
        var quotedCol2 = DatabaseMigrationService.NormalizeExpression("\"role\"");
        quotedCol1.Should().NotBe(quotedCol2);

        // Escaped single quotes within literals are preserved
        var escapedQuotes = DatabaseMigrationService.NormalizeExpression("name = 'O''Reilly'");
        escapedQuotes.Should().Be("name='O''Reilly'");
    }

    [Fact]
    public void NormalizeExpression_ShouldDistinguishSemanticallyDifferentExpressions()
    {
        var originalCanonical = "(retail_price >= (0)::numeric)";
        var driftedNegative = "(retail_price >= ('-1'::integer)::numeric)";
        var driftedNegativeRaw = "(retail_price >= (-1)::numeric)";

        var normOriginal = DatabaseMigrationService.NormalizeExpression(originalCanonical);
        var normDrifted1 = DatabaseMigrationService.NormalizeExpression(driftedNegative);
        var normDrifted2 = DatabaseMigrationService.NormalizeExpression(driftedNegativeRaw);

        normOriginal.Should().NotBe(normDrifted1);
        normOriginal.Should().NotBe(normDrifted2);
    }

    [Fact]
    public void ExpectedTriggers_LedgerAppendOnly_MustRequireDeleteAndTruncateAndStatementLevel()
    {
        var ledgerTrigger = DatabaseMigrationService.ExpectedTriggers
            .FirstOrDefault(t => t.TableName == "inventory_transactions" && t.TriggerName == "ledger_append_only");

        ledgerTrigger.Should().NotBeNull();
        ledgerTrigger!.Timing.Should().Be("BEFORE");
        ledgerTrigger.IsRowLevel.Should().BeFalse("ledger_append_only phải là STATEMENT-level trigger");
        ledgerTrigger.HasDelete.Should().BeTrue("ledger_append_only bắt buộc phải chặn sự kiện DELETE");
        ledgerTrigger.HasUpdate.Should().BeTrue("ledger_append_only bắt buộc phải chặn sự kiện UPDATE");
        ledgerTrigger.HasTruncate.Should().BeTrue("ledger_append_only bắt buộc phải chặn sự kiện TRUNCATE");
        ledgerTrigger.FunctionName.Should().Be("oisms_deny_mutation");
        ledgerTrigger.FunctionSchema.Should().Be("public");
    }

    [Fact]
    public void ExpectedTriggers_LedgerAppendOnly_MustDisallowWhenClauseAndColumnRestrictions_BugB()
    {
        // Bug B: ledger_append_only cannot have WHEN condition (e.g. WHEN (false)) or column restrictions
        var ledgerTrigger = DatabaseMigrationService.ExpectedTriggers
            .FirstOrDefault(t => t.TableName == "inventory_transactions" && t.TriggerName == "ledger_append_only");

        ledgerTrigger.Should().NotBeNull();
        ledgerTrigger!.ExpectedWhenClause.Should().BeNull("ledger_append_only must be unconditional (no WHEN clause)");
        ledgerTrigger.DisallowColumnRestrictions.Should().BeTrue("ledger_append_only must protect the entire table, not limited to specific columns");
    }

    [Fact]
    public void ExpectedForeignKeys_CatalogAndSku_MustRequireRestrictDeleteAndNoActionUpdate()
    {
        var fkSkuProduct = DatabaseMigrationService.ExpectedForeignKeys
            .FirstOrDefault(f => f.ConstraintName == "fk_skus_product_id");

        fkSkuProduct.Should().NotBeNull();
        fkSkuProduct!.SourceTable.Should().Be("skus");
        fkSkuProduct.TargetTable.Should().Be("products");
        fkSkuProduct.OnDelete.Should().Be('r', "Khóa ngoại phải tuân thủ ON DELETE RESTRICT");
        fkSkuProduct.OnUpdate.Should().Be('a', "Khóa ngoại phải tuân thủ ON UPDATE NO ACTION");
        fkSkuProduct.IsDeferrable.Should().BeFalse();
        fkSkuProduct.IsValidated.Should().BeTrue();
    }

    [Fact]
    public void ExpectedCriticalConstraints_MustContainRetailPriceNonNegativeCheck()
    {
        var ckRetailPrice = DatabaseMigrationService.ExpectedCriticalConstraints
            .FirstOrDefault(c => c.ConstraintName == "ck_skus_retail_price_nonnegative");

        ckRetailPrice.Should().NotBeNull();
        ckRetailPrice!.ConstraintType.Should().Be('c');
        ckRetailPrice.TableName.Should().Be("skus");
        ckRetailPrice.ExpectedNormalizedExpression.Should().NotBeNullOrWhiteSpace();
        DatabaseMigrationService.NormalizeExpression(ckRetailPrice.ExpectedNormalizedExpression)
            .Should().Be("retail_price>=(0)::numeric");
    }

    [Fact]
    public void ExpectedCriticalConstraints_MustContainUserRoleValuesWithExactCasing()
    {
        var ckRole = DatabaseMigrationService.ExpectedCriticalConstraints
            .FirstOrDefault(c => c.ConstraintName == "ck_users_role_values");

        ckRole.Should().NotBeNull();
        ckRole!.ConstraintType.Should().Be('c');
        ckRole.TableName.Should().Be("users");
        
        var normalized = DatabaseMigrationService.NormalizeExpression(ckRole.ExpectedNormalizedExpression);
        normalized.Should().Contain("'Owner'");
        normalized.Should().Contain("'Staff'");
        normalized.Should().Contain("'Cashier'");
        normalized.Should().NotContain("'owner'");
    }
}
