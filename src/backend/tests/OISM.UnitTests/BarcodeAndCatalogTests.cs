using FluentAssertions;
using OISM.Application.Common.Helpers;
using OISM.Application.Features.Catalog.Products;
using Xunit;

namespace OISM.UnitTests;

public class BarcodeAndCatalogTests
{
    private readonly CreateVariantValidator _variantValidator = new();

    [Fact]
    public void BarcodeHelper_ShouldGenerate_ValidEan13()
    {
        // Act
        var barcode = BarcodeHelper.GenerateEan13();

        // Assert
        barcode.Should().NotBeNullOrEmpty();
        barcode.Length.Should().Be(13);
        BarcodeHelper.IsValidEan13(barcode).Should().BeTrue();
    }

    [Fact]
    public void BarcodeHelper_ShouldValidate_KnownEan13Barcode()
    {
        // Mã chuẩn Việt Nam kiểm tra hợp lệ
        // 893456789012 + check digit = 8
        var twelveDigits = "893456789012";
        var checkDigit = BarcodeHelper.CalculateEan13CheckDigit(twelveDigits);
        var fullBarcode = twelveDigits + checkDigit;

        // Assert
        BarcodeHelper.IsValidEan13(fullBarcode).Should().BeTrue();
    }

    [Fact]
    public void BarcodeHelper_ShouldReject_WrongCheckDigit()
    {
        // Sai số kiểm tra
        var invalidBarcode = "8934567890129"; // 9 là sai, số đúng phải là khác

        // Assert
        BarcodeHelper.IsValidEan13(invalidBarcode).Should().BeFalse();
    }

    [Fact]
    public void CreateVariantValidator_ShouldPass_WhenInputIsValid()
    {
        // Arrange
        var request = new CreateVariantRequest
        {
            SKU = "AO-THUN-DEN-L",
            VariantName = "Áo thun Đen Size L",
            RetailPrice = 250000,
            WholesalePrice = 180000,
            Color = "Đen",
            Size = "L"
        };

        // Act
        var result = _variantValidator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void CreateVariantValidator_ShouldFail_WhenRetailPriceIsNegative()
    {
        // Arrange
        var request = new CreateVariantRequest
        {
            SKU = "AO-THUN-DEN-L",
            VariantName = "Áo thun Đen Size L",
            RetailPrice = -50000, // Giá âm
            WholesalePrice = 100000
        };

        // Act
        var result = _variantValidator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(request.RetailPrice));
    }

    [Fact]
    public void CreateVariantValidator_ShouldFail_WhenSkuHasSpecialCharacters()
    {
        // Arrange
        var request = new CreateVariantRequest
        {
            SKU = "AO THUN@DEN#L!", // Chứa khoảng trắng và ký tự đặc biệt
            VariantName = "Áo thun Đen Size L",
            RetailPrice = 100000
        };

        // Act
        var result = _variantValidator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(request.SKU));
    }

    [Fact]
    public void CreateWarehouseValidator_ShouldPass_WhenInputIsValid()
    {
        // Arrange
        var validator = new OISM.Application.Features.Warehouses.CreateWarehouseValidator();
        var request = new OISM.Application.Features.Warehouses.CreateWarehouseRequest
        {
            BranchId = Guid.NewGuid(),
            Name = "Kho Tổng Tân Bình"
        };

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void CreateWarehouseValidator_ShouldFail_WhenNameOrBranchIdEmpty()
    {
        // Arrange
        var validator = new OISM.Application.Features.Warehouses.CreateWarehouseValidator();
        var request = new OISM.Application.Features.Warehouses.CreateWarehouseRequest
        {
            BranchId = Guid.Empty,
            Name = ""
        };

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(request.BranchId));
        result.Errors.Should().Contain(e => e.PropertyName == nameof(request.Name));
    }

    [Fact]
    public void SkuAttributeHelper_ComputeCombinationSignature_ShouldNormalizeAndMatch_RegardlessOfCaseAndOrder()
    {
        // Arrange
        var sig1 = SkuAttributeHelper.ComputeCombinationSignature("Red", "L", "Áo Đỏ L", new Dictionary<string, object> { ["Material"] = "Cotton" });
        var sig2 = SkuAttributeHelper.ComputeCombinationSignature(" red ", " l ", "Tên khác hoàn toàn", new Dictionary<string, object> { ["material"] = "cotton " });

        // Assert
        sig1.Should().Be(sig2);
        sig1.Should().Be("color=s:red|material=s:cotton|size=s:l");
    }

    [Fact]
    public void SkuAttributeHelper_ComputeCombinationSignature_ShouldDetectDifferentAttributes()
    {
        // Arrange
        var sigRedL = SkuAttributeHelper.ComputeCombinationSignature("Red", "L", "Áo Đỏ L");
        var sigBlueL = SkuAttributeHelper.ComputeCombinationSignature("Blue", "L", "Áo Xanh L");

        // Assert
        sigRedL.Should().NotBe(sigBlueL);
    }

    [Fact]
    public void SkuAttributeHelper_SerializeAttributes_ShouldProduceValidJsonObject()
    {
        // Arrange & Act
        var json = SkuAttributeHelper.SerializeAttributes("Đen", "XL", "Áo Đen XL", new Dictionary<string, object> { ["ram"] = 16, ["is_waterproof"] = true });

        // Assert
        json.Should().NotBeNullOrEmpty();
        json.Should().StartWith("{").And.EndWith("}");
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        doc.RootElement.ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object);
        doc.RootElement.GetProperty("color").GetString().Should().Be("Đen");
        doc.RootElement.GetProperty("size").GetString().Should().Be("XL");
    }

    [Fact]
    public void SkuAttributeHelper_ResolveAttributes_AttributesColor_OverridesExisting_WhenExplicitColorIsNull()
    {
        // Tình huống tái hiện lỗi review:
        // SKU B cũ có color "Blue". Client cập nhật gửi Attributes: {"color": "Red"} và explicit Color là null.
        // Kết quả mong đợi: EffectiveColor là "Red", chữ ký tổ hợp là "color=s:red", không bị lọt lưới.
        var existingJson = "{\"color\":\"Blue\"}";
        var customAttrs = new Dictionary<string, object> { ["color"] = "Red" };

        var result = SkuAttributeHelper.ResolveAttributes(
            explicitColor: null,
            explicitSize: null,
            explicitVariantName: null,
            customAttributes: customAttrs,
            existingAttributesJson: existingJson);

        result.EffectiveColor.Should().Be("Red");
        result.CombinationSignature.Should().Be("color=s:red");
        result.SerializedJson.Should().Contain("\"color\":\"Red\"");
    }

    [Fact]
    public void SkuAttributeHelper_ResolveAttributes_ExplicitField_HasHighestPrecedence_OverAttributes()
    {
        // Quy tắc ưu tiên: Explicit field được gửi tường minh có độ ưu tiên cao hơn Custom Attributes
        var customAttrs = new Dictionary<string, object> { ["color"] = "Red" };

        var result = SkuAttributeHelper.ResolveAttributes(
            explicitColor: "Green",
            explicitSize: "L",
            explicitVariantName: null,
            customAttributes: customAttrs,
            existingAttributesJson: "{\"color\":\"Blue\"}");

        result.EffectiveColor.Should().Be("Green");
        result.CombinationSignature.Should().Be("color=s:green|size=s:l");
    }

    [Fact]
    public void SkuAttributeHelper_ResolveAttributes_DeleteAttribute_ViaExplicitEmptyString_OrNullValue()
    {
        // Xóa thuộc tính Size bằng cách truyền chuỗi rỗng explicitSize: ""
        var existingJson = "{\"color\":\"Red\",\"size\":\"M\"}";

        var resultExplicitDelete = SkuAttributeHelper.ResolveAttributes(
            explicitColor: null,
            explicitSize: "", // Chỉ định xóa Size
            explicitVariantName: null,
            customAttributes: null,
            existingAttributesJson: existingJson);

        resultExplicitDelete.EffectiveSize.Should().BeNull();
        resultExplicitDelete.MergedAttributes.Should().NotContainKey("size");
        resultExplicitDelete.CombinationSignature.Should().Be("color=s:red");

        // Xóa thuộc tính qua Attributes dictionary với value null
        var customAttrsDelete = new Dictionary<string, object> { ["size"] = null! };
        var resultCustomDelete = SkuAttributeHelper.ResolveAttributes(
            explicitColor: null,
            explicitSize: null,
            explicitVariantName: null,
            customAttributes: customAttrsDelete,
            existingAttributesJson: existingJson);

        resultCustomDelete.EffectiveSize.Should().BeNull();
        resultCustomDelete.MergedAttributes.Should().NotContainKey("size");
        resultCustomDelete.CombinationSignature.Should().Be("color=s:red");
    }

    [Fact]
    public void SkuAttributeHelper_ResolveAttributes_NormalizesCaseSpaceAndKeyOrder()
    {
        var set1 = new Dictionary<string, object> { ["Color"] = "  RED  ", ["Size"] = "  XL " };
        var set2 = new Dictionary<string, object> { ["size"] = "xl", ["color"] = "red" };

        var res1 = SkuAttributeHelper.ResolveAttributes(null, null, null, set1);
        var res2 = SkuAttributeHelper.ResolveAttributes(null, null, null, set2);

        res1.CombinationSignature.Should().Be("color=s:red|size=s:xl");
        res2.CombinationSignature.Should().Be("color=s:red|size=s:xl");
        res1.CombinationSignature.Should().Be(res2.CombinationSignature);
    }

    [Fact]
    public void SkuAttributeHelper_PreservesJsonTypes_Object_Array_Number_Boolean_String()
    {
        // Arrange: Attributes chứa đầy đủ các kiểu JSON
        var inputJson = """
        {
          "dimensions": {"width": 10, "height": 20},
          "sizes": ["S", "M"],
          "count": 5,
          "waterproof": true,
          "title": "Sample"
        }
        """;

        // Act: Giải mã chuỗi JSON thành Dictionary bằng SkuAttributeHelper.ExtractAttributes
        var extracted = SkuAttributeHelper.ExtractAttributes(inputJson);

        // Assert: Các kiểu dữ liệu phải được bảo toàn nguyên vẹn
        extracted.Should().NotBeNull();
        extracted!["dimensions"].Should().BeOfType<Dictionary<string, object>>();
        var dims = (Dictionary<string, object>)extracted["dimensions"];
        dims["width"].Should().Be(10L);
        dims["height"].Should().Be(20L);

        extracted["sizes"].Should().BeOfType<List<object>>();
        var sizes = (List<object>)extracted["sizes"];
        sizes.Should().Equal(new List<object> { "S", "M" });

        extracted["count"].Should().Be(5L);
        extracted["waterproof"].Should().Be(true);
        extracted["title"].Should().Be("Sample");

        // Act 2: Serialize lại sang chuỗi JSON
        var reserialized = SkuAttributeHelper.SerializeAttributes(null, null, null, extracted);
        using var doc = System.Text.Json.JsonDocument.Parse(reserialized);
        doc.RootElement.GetProperty("dimensions").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Object);
        doc.RootElement.GetProperty("sizes").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Array);
        doc.RootElement.GetProperty("count").ValueKind.Should().Be(System.Text.Json.JsonValueKind.Number);
        doc.RootElement.GetProperty("waterproof").ValueKind.Should().Be(System.Text.Json.JsonValueKind.True);
        doc.RootElement.GetProperty("title").ValueKind.Should().Be(System.Text.Json.JsonValueKind.String);
    }

    [Fact]
    public void SkuAttributeHelper_DistinguishesBetweenNumberAndStringInSignature()
    {
        // Number 10 vs String "10" phải sinh ra chữ ký khác nhau
        var sigNumber = SkuAttributeHelper.ComputeCombinationSignature(null, null, null, new Dictionary<string, object> { ["count"] = 10 });
        var sigString = SkuAttributeHelper.ComputeCombinationSignature(null, null, null, new Dictionary<string, object> { ["count"] = "10" });

        sigNumber.Should().Be("count=n:10");
        sigString.Should().Be("count=s:10");
        sigNumber.Should().NotBe(sigString);
    }

    [Fact]
    public void SkuAttributeHelper_DistinguishesBetweenBooleanAndStringInSignature()
    {
        // Boolean true vs String "true" phải sinh ra chữ ký khác nhau
        var sigBool = SkuAttributeHelper.ComputeCombinationSignature(null, null, null, new Dictionary<string, object> { ["active"] = true });
        var sigString = SkuAttributeHelper.ComputeCombinationSignature(null, null, null, new Dictionary<string, object> { ["active"] = "true" });

        sigBool.Should().Be("active=b:true");
        sigString.Should().Be("active=s:true");
        sigBool.Should().NotBe(sigString);
    }

    [Fact]
    public void SkuAttributeHelper_NestedObject_DifferentKeyOrder_YieldsIdenticalSignature()
    {
        // {"width": 10, "height": 20} vs {"height": 20, "width": 10}
        var obj1 = new Dictionary<string, object>
        {
            ["dimensions"] = new Dictionary<string, object> { ["width"] = 10, ["height"] = 20 }
        };
        var obj2 = new Dictionary<string, object>
        {
            ["dimensions"] = new Dictionary<string, object> { ["height"] = 20, ["width"] = 10 }
        };

        var sig1 = SkuAttributeHelper.ComputeCombinationSignature(null, null, null, obj1);
        var sig2 = SkuAttributeHelper.ComputeCombinationSignature(null, null, null, obj2);

        sig1.Should().Be("dimensions=o:{height:n:20,width:n:10}");
        sig2.Should().Be("dimensions=o:{height:n:20,width:n:10}");
        sig1.Should().Be(sig2);
    }

    [Fact]
    public void SkuAttributeHelper_Array_PreservesOrder_InSignature()
    {
        // ["S", "M"] vs ["M", "S"] có thứ tự khác nhau nên chữ ký phải khác nhau
        var arr1 = new Dictionary<string, object> { ["sizes"] = new List<object> { "S", "M" } };
        var arr2 = new Dictionary<string, object> { ["sizes"] = new List<object> { "M", "S" } };

        var sig1 = SkuAttributeHelper.ComputeCombinationSignature(null, null, null, arr1);
        var sig2 = SkuAttributeHelper.ComputeCombinationSignature(null, null, null, arr2);

        sig1.Should().Be("sizes=a:[s:s,s:m]");
        sig2.Should().Be("sizes=a:[s:m,s:s]");
        sig1.Should().NotBe(sig2);
    }

    [Fact]
    public void SkuAttributeHelper_PreservesNestedNulls_InObjectsAndArrays()
    {
        // 1. Arrange: Chuỗi JSON chứa null trong object và array lồng nhau
        var inputJson = """
        {
          "positions": ["S", null, "M"],
          "dimensions": {"width": 10, "depth": null}
        }
        """;

        // Act: Đọc qua ExtractAttributes
        var extracted = SkuAttributeHelper.ExtractAttributes(inputJson);

        // Assert:
        // - positions phải có đúng 3 phần tử: "S", null, "M"
        extracted.Should().ContainKey("positions");
        var positions = (List<object>)extracted["positions"];
        positions.Should().HaveCount(3);
        positions[0].Should().Be("S");
        positions[1].Should().BeNull();
        positions[2].Should().Be("M");

        // - dimensions phải giữ key depth với giá trị null
        extracted.Should().ContainKey("dimensions");
        var dims = (Dictionary<string, object>)extracted["dimensions"];
        dims.Should().ContainKey("width");
        dims["width"].Should().Be(10L);
        dims.Should().ContainKey("depth");
        dims["depth"].Should().BeNull();

        // 2. Act: ResolveAttributes với Custom Attributes truyền dưới dạng Dictionary chứa nested null
        var customAttrs = new Dictionary<string, object>
        {
            ["positions"] = new List<object?> { "S", null, "M" },
            ["dimensions"] = new Dictionary<string, object?> { ["width"] = 10, ["depth"] = null }
        };

        var resolved = SkuAttributeHelper.ResolveAttributes(null, null, null, customAttrs);

        // Assert: Chữ ký và chuỗi JSON lưu trữ phải bảo toàn null
        resolved.CombinationSignature.Should().Be("dimensions=o:{depth:null,width:n:10}|positions=a:[s:s,null,s:m]");
        resolved.SerializedJson.Should().Contain("\"depth\":null");
        resolved.SerializedJson.Should().Contain("[\"S\",null,\"M\"]");

        // Đọc lại từ SerializedJson
        var reExtracted = SkuAttributeHelper.ExtractAttributes(resolved.SerializedJson);
        var reDims = (Dictionary<string, object>)reExtracted["dimensions"];
        reDims.Should().ContainKey("depth");
        reDims["depth"].Should().BeNull();
        var rePositions = (List<object>)reExtracted["positions"];
        rePositions.Should().HaveCount(3);
        rePositions[1].Should().BeNull();
    }

    [Fact]
    public void SkuAttributeHelper_Signatures_DistinguishNestedNulls()
    {
        // ["S", null, "M"] khác ["S", "M"]
        var sigArrayWithNull = SkuAttributeHelper.ComputeCombinationSignature(null, null, null, new Dictionary<string, object>
        {
            ["positions"] = new List<object?> { "S", null, "M" }
        });
        var sigArrayWithoutNull = SkuAttributeHelper.ComputeCombinationSignature(null, null, null, new Dictionary<string, object>
        {
            ["positions"] = new List<object?> { "S", "M" }
        });

        sigArrayWithNull.Should().Be("positions=a:[s:s,null,s:m]");
        sigArrayWithoutNull.Should().Be("positions=a:[s:s,s:m]");
        sigArrayWithNull.Should().NotBe(sigArrayWithoutNull);

        // {"depth": null} khác {}
        var sigObjWithNull = SkuAttributeHelper.ComputeCombinationSignature(null, null, null, new Dictionary<string, object>
        {
            ["dimensions"] = new Dictionary<string, object?> { ["depth"] = null }
        });
        var sigObjEmpty = SkuAttributeHelper.ComputeCombinationSignature(null, null, null, new Dictionary<string, object>
        {
            ["dimensions"] = new Dictionary<string, object?>()
        });

        sigObjWithNull.Should().Be("dimensions=o:{depth:null}");
        sigObjEmpty.Should().Be("dimensions=o:{}");
        sigObjWithNull.Should().NotBe(sigObjEmpty);

        // Object cùng nội dung nhưng khác thứ tự key vẫn có cùng chữ ký
        var sigObjOrder1 = SkuAttributeHelper.ComputeCombinationSignature(null, null, null, new Dictionary<string, object>
        {
            ["dimensions"] = new Dictionary<string, object?> { ["width"] = 10, ["depth"] = null }
        });
        var sigObjOrder2 = SkuAttributeHelper.ComputeCombinationSignature(null, null, null, new Dictionary<string, object>
        {
            ["dimensions"] = new Dictionary<string, object?> { ["depth"] = null, ["width"] = 10 }
        });

        sigObjOrder1.Should().Be("dimensions=o:{depth:null,width:n:10}");
        sigObjOrder2.Should().Be("dimensions=o:{depth:null,width:n:10}");
        sigObjOrder1.Should().Be(sigObjOrder2);
    }

    [Fact]
    public void SkuAttributeHelper_TopLevelNull_DeletesAttribute_WhileNestedNullIsPreserved()
    {
        // Existing có color, size và dimensions: {"depth": null}
        var existingJson = """
        {
          "color": "Red",
          "size": "L",
          "dimensions": {"width": 10, "depth": null}
        }
        """;

        // Gửi xóa color ở top-level bằng null, cập nhật dimensions có nested null
        var customAttrs = new Dictionary<string, object>
        {
            ["color"] = null!, // Xóa top-level color
            ["dimensions"] = new Dictionary<string, object?> { ["width"] = 20, ["depth"] = null }
        };

        var resolved = SkuAttributeHelper.ResolveAttributes(
            explicitColor: null,
            explicitSize: null,
            explicitVariantName: null,
            customAttributes: customAttrs,
            existingAttributesJson: existingJson);

        // Top-level color bị xóa
        resolved.EffectiveColor.Should().BeNull();
        resolved.MergedAttributes.Should().NotContainKey("color");

        // Top-level size được giữ từ existing
        resolved.EffectiveSize.Should().Be("L");
        resolved.MergedAttributes.Should().ContainKey("size");

        // Nested depth: null được bảo toàn
        var dims = (Dictionary<string, object>)resolved.MergedAttributes["dimensions"];
        dims["width"].Should().Be(20L);
        dims.Should().ContainKey("depth");
        dims["depth"].Should().BeNull();

        resolved.CombinationSignature.Should().Be("dimensions=o:{depth:null,width:n:20}|size=s:l");
    }

    [Fact]
    public void UpdateVariantValidator_ShouldFail_WhenPricesAreNegative()
    {
        // Arrange
        var validator = new UpdateVariantValidator();
        var request = new UpdateVariantRequest
        {
            VariantName = "Biến thể A",
            RetailPrice = -100,
            WholesalePrice = -50
        };

        // Act
        var result = validator.Validate(request);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(request.RetailPrice));
        result.Errors.Should().Contain(e => e.PropertyName == nameof(request.WholesalePrice));
    }

    [Fact]
    public void UpdateVariantPricesValidator_ShouldValidateNonNegativePrices()
    {
        // Arrange
        var validator = new UpdateVariantPricesValidator();
        var validReq = new UpdateVariantPricesRequest { RetailPrice = 100000, WholesalePrice = 80000 };
        var invalidReq = new UpdateVariantPricesRequest { RetailPrice = -1, WholesalePrice = 80000 };

        // Act & Assert
        validator.Validate(validReq).IsValid.Should().BeTrue();
        validator.Validate(invalidReq).IsValid.Should().BeFalse();
    }

    [Fact]
    public void UpdateCategoryAndBrandValidator_ShouldFail_WhenNameIsEmpty()
    {
        // Arrange
        var catValidator = new OISM.Application.Features.Catalog.Categories.UpdateCategoryValidator();
        var brandValidator = new OISM.Application.Features.Catalog.Brands.UpdateBrandValidator();

        var catResult = catValidator.Validate(new OISM.Application.Features.Catalog.Categories.UpdateCategoryRequest { Name = "" });
        var brandResult = brandValidator.Validate(new OISM.Application.Features.Catalog.Brands.UpdateBrandRequest { Name = "   " });

        // Assert
        catResult.IsValid.Should().BeFalse();
        brandResult.IsValid.Should().BeFalse();
    }

    [Fact]
    public void UpdateBranchAndWarehouseValidator_ShouldValidateCorrectly()
    {
        // Arrange
        var branchValidator = new OISM.Application.Features.Branches.UpdateBranchValidator();
        var whValidator = new OISM.Application.Features.Warehouses.UpdateWarehouseValidator();

        var branchRes = branchValidator.Validate(new OISM.Application.Features.Branches.UpdateBranchRequest { Name = "" });
        var whRes = whValidator.Validate(new OISM.Application.Features.Warehouses.UpdateWarehouseRequest { BranchId = Guid.Empty, Name = "" });

        // Assert
        branchRes.IsValid.Should().BeFalse();
        whRes.IsValid.Should().BeFalse();
    }

    [Fact]
    public void CreateVariantValidator_ShouldFail_WhenBarcodeExceeds128Characters()
    {
        var validator = new CreateVariantValidator();
        var longBarcode = new string('A', 129);
        var req = new CreateVariantRequest
        {
            SKU = "VALID-SKU",
            VariantName = "Tên",
            RetailPrice = 100000,
            WholesalePrice = 80000,
            Barcode = longBarcode
        };

        var result = validator.Validate(req);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Barcode" && e.ErrorMessage.Contains("128"));
    }

    [Fact]
    public void CreateVariantValidator_ShouldFail_WhenPriceExceedsLimitOrHasMoreThanTwoDecimals()
    {
        var validator = new CreateVariantValidator();

        // Giá vượt quá 10^18 (giới hạn numeric(20,2))
        var overflowReq = new CreateVariantRequest
        {
            SKU = "VALID-SKU-1",
            VariantName = "Tên",
            RetailPrice = 1_000_000_000_000_000_000m,
            WholesalePrice = 50000
        };
        var overflowRes = validator.Validate(overflowReq);
        overflowRes.IsValid.Should().BeFalse();
        overflowRes.Errors.Should().Contain(e => e.PropertyName == "RetailPrice");

        // Giá có hơn 2 chữ số thập phân
        var scaleReq = new CreateVariantRequest
        {
            SKU = "VALID-SKU-2",
            VariantName = "Tên",
            RetailPrice = 100.555m,
            WholesalePrice = 50000
        };
        var scaleRes = validator.Validate(scaleReq);
        scaleRes.IsValid.Should().BeFalse();
        scaleRes.Errors.Should().Contain(e => e.PropertyName == "RetailPrice" && e.ErrorMessage.Contains("thập phân"));
    }

    [Fact]
    public void CreateProductValidator_ShouldFail_WhenVariantsContainNullItem()
    {
        var validator = new CreateProductValidator();
        var req = new CreateProductRequest
        {
            Name = "Sản phẩm hợp lệ",
            Variants = new List<CreateVariantRequest?> { null }!
        };

        var result = validator.Validate(req);
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("null"));
    }

    [Fact]
    public void UpdateVariantPricesValidator_ShouldFail_WhenPriceExceedsLimitOrHasMoreThanTwoDecimals()
    {
        var validator = new UpdateVariantPricesValidator();

        var overflowReq = new UpdateVariantPricesRequest
        {
            RetailPrice = 1_000_000_000_000_000_000m,
            WholesalePrice = 50000
        };
        validator.Validate(overflowReq).IsValid.Should().BeFalse();

        var scaleReq = new UpdateVariantPricesRequest
        {
            RetailPrice = 99.999m,
            WholesalePrice = 50000
        };
        validator.Validate(scaleReq).IsValid.Should().BeFalse();

        var validReq = new UpdateVariantPricesRequest
        {
            RetailPrice = 99.99m,
            WholesalePrice = 50000
        };
        validator.Validate(validReq).IsValid.Should().BeTrue();
    }
}


