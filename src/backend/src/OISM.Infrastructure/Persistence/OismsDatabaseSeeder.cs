using Microsoft.EntityFrameworkCore;
using OISM.Domain.Entities;

namespace OISM.Infrastructure.Persistence;

/// <summary>
/// Seeder dữ liệu mẫu an toàn cho môi trường Dev / Test.
/// Đảm bảo các đặc tính:
/// 1. Nguyên tử (Atomic): Thực thi trong Database Transaction tương thích với IExecutionStrategy của Npgsql.
/// 2. Idempotent & Tự phục hồi (Self-healing): Chạy lại an toàn, tự động bổ sung dữ liệu còn thiếu từ lần chạy dở trước.
/// 3. Bảo toàn dữ liệu người dùng: Không ghi đè mật khẩu hoặc thông tin đã chỉnh sửa; không reset tồn kho thực tế.
/// 4. Tồn kho ban đầu = 0: Tuyệt đối không tạo tồn khống không có chứng từ/ledger.
/// </summary>
public static class OismsDatabaseSeeder
{
    public static readonly Guid TenantAId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid TenantBId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    public static async Task SeedAsync(
        AppDbContext context,
        string demoPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(demoPassword))
        {
            throw new InvalidOperationException(
                "Demo seed requires an explicit, non-empty password configured via 'Database:DemoSeedPassword'. Hardcoded fallback is prohibited.");
        }

        var defaultPasswordHash = BCrypt.Net.BCrypt.HashPassword(demoPassword);

        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            using var privilegedScope = context.OpenPrivilegedScope();
            using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            // =================================================================
            // 1. SEED TENANT A (Thời trang nam)
            // =================================================================
            await SeedTenantAAsync(context, defaultPasswordHash, cancellationToken);

            // =================================================================
            // 2. SEED TENANT B (Mỹ phẩm & Chăm sóc da)
            // =================================================================
            await SeedTenantBAsync(context, defaultPasswordHash, cancellationToken);

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private static async Task SeedTenantAAsync(
        AppDbContext context,
        string passwordHash,
        CancellationToken ct)
    {
        // 1. Tenant A
        var tenantA = await context.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == TenantAId, ct);

        if (tenantA == null)
        {
            tenantA = new Tenant
            {
                Id = TenantAId,
                Name = "Cửa hàng Thời trang ABC",
                IsActive = true
            };
            context.Tenants.Add(tenantA);
        }

        // 2. Users của Tenant A
        var usersA = new[]
        {
            new
            {
                Id = Guid.Parse("11111111-0000-0000-0000-000000000001"),
                Email = "owner@shopa.local",
                Name = "Chủ shop Thời trang A",
                Phone = "+84901000001",
                Role = "Owner"
            },
            new
            {
                Id = Guid.Parse("11111111-0000-0000-0000-000000000002"),
                Email = "staff@shopa.local",
                Name = "Nhân viên kho shop A",
                Phone = "+84901000002",
                Role = "Staff"
            },
            new
            {
                Id = Guid.Parse("11111111-0000-0000-0000-000000000003"),
                Email = "cashier@shopa.local",
                Name = "Thu ngân shop A",
                Phone = "+84901000003",
                Role = "Cashier"
            }
        };

        foreach (var u in usersA)
        {
            var exists = await context.Users
                .IgnoreQueryFilters()
                .AnyAsync(x => x.Id == u.Id || x.Email == u.Email, ct);

            if (!exists)
            {
                context.Users.Add(new User
                {
                    Id = u.Id,
                    TenantId = TenantAId,
                    Email = u.Email,
                    DisplayName = u.Name,
                    Phone = u.Phone,
                    Role = u.Role,
                    PasswordHash = passwordHash,
                    IsActive = true
                });
            }
        }

        // 3. Chi nhánh & Kho của Tenant A
        var branchAId = Guid.Parse("11111111-0000-0000-0001-000000000001");
        var branchAExists = await context.Branches
            .IgnoreQueryFilters()
            .AnyAsync(b => b.Id == branchAId, ct);

        if (!branchAExists)
        {
            context.Branches.Add(new Branch
            {
                Id = branchAId,
                TenantId = TenantAId,
                Name = "Chi nhánh Quận 1",
                Address = "123 Lê Lợi, Phường Bến Thành, Quận 1, TP.HCM",
                IsActive = true
            });
        }

        var warehouseAId = Guid.Parse("11111111-0000-0000-0002-000000000001");
        var warehouseAExists = await context.Warehouses
            .IgnoreQueryFilters()
            .AnyAsync(w => w.Id == warehouseAId, ct);

        if (!warehouseAExists)
        {
            context.Warehouses.Add(new Warehouse
            {
                Id = warehouseAId,
                TenantId = TenantAId,
                BranchId = branchAId,
                Name = "Kho Tổng Quận 1",
                IsActive = true
            });
        }

        // 4. Danh mục & Thương hiệu
        var categoryAId = Guid.Parse("11111111-0000-0000-0003-000000000001");
        var catAExists = await context.Categories
            .IgnoreQueryFilters()
            .AnyAsync(c => c.Id == categoryAId, ct);

        if (!catAExists)
        {
            context.Categories.Add(new Category
            {
                Id = categoryAId,
                TenantId = TenantAId,
                Name = "Thời trang nam",
                IsActive = true
            });
        }

        var brandAId = Guid.Parse("11111111-0000-0000-0004-000000000001");
        var brandAExists = await context.Brands
            .IgnoreQueryFilters()
            .AnyAsync(b => b.Id == brandAId, ct);

        if (!brandAExists)
        {
            context.Brands.Add(new Brand
            {
                Id = brandAId,
                TenantId = TenantAId,
                Name = "OISM Fashion",
                IsActive = true
            });
        }

        // 5. Sản phẩm & SKU
        var productAId = Guid.Parse("11111111-0000-0000-0005-000000000001");
        var prodAExists = await context.Products
            .IgnoreQueryFilters()
            .AnyAsync(p => p.Id == productAId, ct);

        if (!prodAExists)
        {
            context.Products.Add(new Product
            {
                Id = productAId,
                TenantId = TenantAId,
                CategoryId = categoryAId,
                BrandId = brandAId,
                Name = "Áo Polo Nam Thể Thao",
                Description = "Chất liệu cotton co giãn 4 chiều",
                IsActive = true
            });
        }

        // 5. Đơn vị cơ sở & SKU Tenant A
        var unitAId = Guid.Parse("11111111-0000-0000-0004-000000000001");
        var unitA = await context.Units
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.TenantId == TenantAId && u.Code == "piece", ct);
        if (unitA == null)
        {
            unitA = new Unit
            {
                Id = unitAId,
                TenantId = TenantAId,
                Code = "piece",
                Name = "Cái",
                Symbol = "cái",
                MeasureGroup = "Count",
                DecimalPlaces = 3,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            context.Units.Add(unitA);
        }

        var skuAId = Guid.Parse("11111111-0000-0000-0006-000000000001");
        var skuAExists = await context.ProductVariants
            .IgnoreQueryFilters()
            .AnyAsync(v => v.Id == skuAId || (v.TenantId == TenantAId && v.SKU == "POLO-NAM-DEN-M"), ct);

        if (!skuAExists)
        {
            context.ProductVariants.Add(new ProductVariant
            {
                Id = skuAId,
                TenantId = TenantAId,
                ProductId = productAId,
                Code = "POLO-NAM-DEN-M",
                Unit = "piece",
                BaseUnitId = unitA.Id,
                BarcodeType = "Code128",
                Barcode = "POLONAMDENM",
                RetailPrice = 299000,
                WholesalePrice = 199000,
                AttributesJson = "{\"color\":\"Đen\",\"size\":\"M\",\"variant_name\":\"Đen / Size M\"}",
                IsActive = true
            });
        }

        var skuUnitAExists = await context.SkuUnits
            .IgnoreQueryFilters()
            .AnyAsync(su => su.TenantId == TenantAId && su.SkuId == skuAId && su.UnitId == unitA.Id, ct);
        if (!skuUnitAExists)
        {
            context.SkuUnits.Add(new SkuUnit
            {
                Id = Guid.NewGuid(),
                TenantId = TenantAId,
                SkuId = skuAId,
                UnitId = unitA.Id,
                FactorToBase = 1m,
                CanPurchase = true,
                CanSell = true,
                IsDefaultPurchase = true,
                IsDefaultSale = true,
                QuantityDecimalPlaces = 3,
                Version = 1,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        // 6. Nhà cung cấp
        var supplierAId = Guid.Parse("11111111-0000-0000-0007-000000000001");
        var supAExists = await context.Suppliers
            .IgnoreQueryFilters()
            .AnyAsync(s => s.Id == supplierAId, ct);

        if (!supAExists)
        {
            context.Suppliers.Add(new Supplier
            {
                Id = supplierAId,
                TenantId = TenantAId,
                Name = "Công ty Cổ phần May Nhà Bè",
                Phone = "+842838001122",
                Email = "contact@nhabe.local",
                Address = "TP. Hồ Chí Minh",
                IsActive = true
            });
        }

        // 7. Số dư tồn kho khởi tạo: Tồn ban đầu = 0
        // Tuyệt đối không reset về 0 nếu đã có số dư thực tế
        var balanceAId = Guid.Parse("11111111-0000-0000-0008-000000000001");
        var balanceAExists = await context.InventoryBalances
            .IgnoreQueryFilters()
            .AnyAsync(ib => ib.Id == balanceAId || (ib.TenantId == TenantAId && ib.BranchId == branchAId && ib.SkuId == skuAId), ct);

        if (!balanceAExists)
        {
            context.InventoryBalances.Add(new InventoryBalance
            {
                Id = balanceAId,
                TenantId = TenantAId,
                BranchId = branchAId,
                SkuId = skuAId,
                OnHand = 0,
                Reserved = 0,
                AverageCost = 0
            });
        }

        // 8. Bảng giá mặc định Tenant A & Giá khởi tạo
        var retailAList = await context.PriceLists.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == TenantAId && p.Code == "retail", ct);
        if (retailAList == null)
        {
            retailAList = new PriceList { Id = Guid.NewGuid(), TenantId = TenantAId, Code = "retail", Name = "Giá lẻ", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            context.PriceLists.Add(retailAList);
        }

        var wholesaleAList = await context.PriceLists.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == TenantAId && p.Code == "wholesale", ct);
        if (wholesaleAList == null)
        {
            wholesaleAList = new PriceList { Id = Guid.NewGuid(), TenantId = TenantAId, Code = "wholesale", Name = "Giá sỉ", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            context.PriceLists.Add(wholesaleAList);
        }

        var openReqA = await context.PriceChangeRequests.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.TenantId == TenantAId && r.Kind == "Opening", ct);
        if (openReqA == null)
        {
            openReqA = new PriceChangeRequest
            {
                Id = Guid.NewGuid(),
                TenantId = TenantAId,
                RequestKey = Guid.NewGuid(),
                Kind = "Opening",
                Status = "Approved",
                Reason = "Giá đầu kỳ",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                ReviewedAt = DateTimeOffset.UtcNow.AddDays(-1)
            };
            context.PriceChangeRequests.Add(openReqA);
        }

        var priceRetailAExists = await context.SkuUnitPrices.IgnoreQueryFilters().AnyAsync(p => p.TenantId == TenantAId && p.PriceListId == retailAList.Id && p.SkuId == skuAId && p.UnitId == unitA.Id, ct);
        if (!priceRetailAExists)
        {
            var reqItem = new PriceChangeRequestItem
            {
                Id = Guid.NewGuid(),
                TenantId = TenantAId,
                RequestId = openReqA.Id,
                PriceListId = retailAList.Id,
                SkuId = skuAId,
                UnitId = unitA.Id,
                NewPrice = 299000,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            };
            context.PriceChangeRequestItems.Add(reqItem);

            context.SkuUnitPrices.Add(new SkuUnitPrice
            {
                Id = Guid.NewGuid(),
                TenantId = TenantAId,
                PriceListId = retailAList.Id,
                SkuId = skuAId,
                UnitId = unitA.Id,
                Price = 299000,
                ValidFrom = DateTimeOffset.UtcNow.AddDays(-1),
                SourceItemId = reqItem.Id,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            });
        }

        var priceWholesaleAExists = await context.SkuUnitPrices.IgnoreQueryFilters().AnyAsync(p => p.TenantId == TenantAId && p.PriceListId == wholesaleAList.Id && p.SkuId == skuAId && p.UnitId == unitA.Id, ct);
        if (!priceWholesaleAExists)
        {
            var reqItem = new PriceChangeRequestItem
            {
                Id = Guid.NewGuid(),
                TenantId = TenantAId,
                RequestId = openReqA.Id,
                PriceListId = wholesaleAList.Id,
                SkuId = skuAId,
                UnitId = unitA.Id,
                NewPrice = 199000,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            };
            context.PriceChangeRequestItems.Add(reqItem);

            context.SkuUnitPrices.Add(new SkuUnitPrice
            {
                Id = Guid.NewGuid(),
                TenantId = TenantAId,
                PriceListId = wholesaleAList.Id,
                SkuId = skuAId,
                UnitId = unitA.Id,
                Price = 199000,
                ValidFrom = DateTimeOffset.UtcNow.AddDays(-1),
                SourceItemId = reqItem.Id,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            });
        }
    }

    private static async Task SeedTenantBAsync(
        AppDbContext context,
        string passwordHash,
        CancellationToken ct)
    {
        // 1. Tenant B
        var tenantB = await context.Tenants
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == TenantBId, ct);

        if (tenantB == null)
        {
            tenantB = new Tenant
            {
                Id = TenantBId,
                Name = "Mỹ phẩm & Chăm sóc da XYZ",
                IsActive = true
            };
            context.Tenants.Add(tenantB);
        }

        // 2. Users của Tenant B
        var usersB = new[]
        {
            new
            {
                Id = Guid.Parse("22222222-0000-0000-0000-000000000001"),
                Email = "owner@shopb.local",
                Name = "Chủ shop Mỹ phẩm B",
                Phone = "+84902000001",
                Role = "Owner"
            },
            new
            {
                Id = Guid.Parse("22222222-0000-0000-0000-000000000002"),
                Email = "staff@shopb.local",
                Name = "Nhân viên kho shop B",
                Phone = "+84902000002",
                Role = "Staff"
            },
            new
            {
                Id = Guid.Parse("22222222-0000-0000-0000-000000000003"),
                Email = "cashier@shopb.local",
                Name = "Thu ngân shop B",
                Phone = "+84902000003",
                Role = "Cashier"
            }
        };

        foreach (var u in usersB)
        {
            var exists = await context.Users
                .IgnoreQueryFilters()
                .AnyAsync(x => x.Id == u.Id || x.Email == u.Email, ct);

            if (!exists)
            {
                context.Users.Add(new User
                {
                    Id = u.Id,
                    TenantId = TenantBId,
                    Email = u.Email,
                    DisplayName = u.Name,
                    Phone = u.Phone,
                    Role = u.Role,
                    PasswordHash = passwordHash,
                    IsActive = true
                });
            }
        }

        // 3. Chi nhánh & Kho của Tenant B
        var branchBId = Guid.Parse("22222222-0000-0000-0001-000000000001");
        var branchBExists = await context.Branches
            .IgnoreQueryFilters()
            .AnyAsync(b => b.Id == branchBId, ct);

        if (!branchBExists)
        {
            context.Branches.Add(new Branch
            {
                Id = branchBId,
                TenantId = TenantBId,
                Name = "Chi nhánh Đống Đa",
                Address = "456 Xã Đàn, Quận Đống Đa, Hà Nội",
                IsActive = true
            });
        }

        var warehouseBId = Guid.Parse("22222222-0000-0000-0002-000000000001");
        var warehouseBExists = await context.Warehouses
            .IgnoreQueryFilters()
            .AnyAsync(w => w.Id == warehouseBId, ct);

        if (!warehouseBExists)
        {
            context.Warehouses.Add(new Warehouse
            {
                Id = warehouseBId,
                TenantId = TenantBId,
                BranchId = branchBId,
                Name = "Kho Tổng Đống Đa",
                IsActive = true
            });
        }

        // 4. Danh mục & Thương hiệu
        var categoryBId = Guid.Parse("22222222-0000-0000-0003-000000000001");
        var catBExists = await context.Categories
            .IgnoreQueryFilters()
            .AnyAsync(c => c.Id == categoryBId, ct);

        if (!catBExists)
        {
            context.Categories.Add(new Category
            {
                Id = categoryBId,
                TenantId = TenantBId,
                Name = "Chăm sóc da mặt",
                IsActive = true
            });
        }

        var brandBId = Guid.Parse("22222222-0000-0000-0004-000000000001");
        var brandBExists = await context.Brands
            .IgnoreQueryFilters()
            .AnyAsync(b => b.Id == brandBId, ct);

        if (!brandBExists)
        {
            context.Brands.Add(new Brand
            {
                Id = brandBId,
                TenantId = TenantBId,
                Name = "Aura Cosmetics",
                IsActive = true
            });
        }

        // Bảng giá mặc định Tenant B & Đơn vị cơ sở
        var unitBId = Guid.Parse("22222222-0000-0000-0004-000000000001");
        var unitB = await context.Units
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.TenantId == TenantBId && u.Code == "bottle", ct);
        if (unitB == null)
        {
            unitB = new Unit
            {
                Id = unitBId,
                TenantId = TenantBId,
                Code = "bottle",
                Name = "Chai",
                Symbol = "chai",
                MeasureGroup = "Count",
                DecimalPlaces = 3,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            context.Units.Add(unitB);
        }

        var retailBList = await context.PriceLists.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == TenantBId && p.Code == "retail", ct);
        if (retailBList == null)
        {
            retailBList = new PriceList { Id = Guid.NewGuid(), TenantId = TenantBId, Code = "retail", Name = "Giá lẻ", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            context.PriceLists.Add(retailBList);
        }

        var wholesaleBList = await context.PriceLists.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == TenantBId && p.Code == "wholesale", ct);
        if (wholesaleBList == null)
        {
            wholesaleBList = new PriceList { Id = Guid.NewGuid(), TenantId = TenantBId, Code = "wholesale", Name = "Giá sỉ", IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
            context.PriceLists.Add(wholesaleBList);
        }

        // 5. Sản phẩm & SKU
        var productBId = Guid.Parse("22222222-0000-0000-0005-000000000001");
        var prodBExists = await context.Products
            .IgnoreQueryFilters()
            .AnyAsync(p => p.Id == productBId, ct);

        if (!prodBExists)
        {
            context.Products.Add(new Product
            {
                Id = productBId,
                TenantId = TenantBId,
                CategoryId = categoryBId,
                BrandId = brandBId,
                Name = "Serum Phục Hồi Da B5",
                Description = "Dưỡng ẩm sâu và làm dịu da nhạy cảm",
                IsActive = true
            });
        }

        var skuBId = Guid.Parse("22222222-0000-0000-0006-000000000001");
        var skuBExists = await context.ProductVariants
            .IgnoreQueryFilters()
            .AnyAsync(v => v.Id == skuBId || (v.TenantId == TenantBId && v.SKU == "SERUM-B5-30ML"), ct);

        if (!skuBExists)
        {
            context.ProductVariants.Add(new ProductVariant
            {
                Id = skuBId,
                TenantId = TenantBId,
                ProductId = productBId,
                Code = "SERUM-B5-30ML",
                Unit = "bottle",
                BaseUnitId = unitB.Id,
                BarcodeType = "Code128",
                Barcode = "SERUMB530ML",
                RetailPrice = 450000,
                WholesalePrice = 320000,
                AttributesJson = "{\"capacity\":\"30ml\",\"variant_name\":\"Chai 30ml\"}",
                IsActive = true
            });
        }

        var skuUnitBExists = await context.SkuUnits
            .IgnoreQueryFilters()
            .AnyAsync(su => su.TenantId == TenantBId && su.SkuId == skuBId && su.UnitId == unitB.Id, ct);
        if (!skuUnitBExists)
        {
            context.SkuUnits.Add(new SkuUnit
            {
                Id = Guid.NewGuid(),
                TenantId = TenantBId,
                SkuId = skuBId,
                UnitId = unitB.Id,
                FactorToBase = 1m,
                CanPurchase = true,
                CanSell = true,
                IsDefaultPurchase = true,
                IsDefaultSale = true,
                QuantityDecimalPlaces = 3,
                Version = 1,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        var openReqB = await context.PriceChangeRequests.IgnoreQueryFilters().FirstOrDefaultAsync(r => r.TenantId == TenantBId && r.Kind == "Opening", ct);
        if (openReqB == null)
        {
            openReqB = new PriceChangeRequest
            {
                Id = Guid.NewGuid(),
                TenantId = TenantBId,
                RequestKey = Guid.NewGuid(),
                Kind = "Opening",
                Status = "Approved",
                Reason = "Giá đầu kỳ",
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1),
                ReviewedAt = DateTimeOffset.UtcNow.AddDays(-1)
            };
            context.PriceChangeRequests.Add(openReqB);
        }

        var priceRetailBExists = await context.SkuUnitPrices.IgnoreQueryFilters().AnyAsync(p => p.TenantId == TenantBId && p.PriceListId == retailBList.Id && p.SkuId == skuBId && p.UnitId == unitB.Id, ct);
        if (!priceRetailBExists)
        {
            var reqItem = new PriceChangeRequestItem
            {
                Id = Guid.NewGuid(),
                TenantId = TenantBId,
                RequestId = openReqB.Id,
                PriceListId = retailBList.Id,
                SkuId = skuBId,
                UnitId = unitB.Id,
                NewPrice = 450000,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            };
            context.PriceChangeRequestItems.Add(reqItem);

            context.SkuUnitPrices.Add(new SkuUnitPrice
            {
                Id = Guid.NewGuid(),
                TenantId = TenantBId,
                PriceListId = retailBList.Id,
                SkuId = skuBId,
                UnitId = unitB.Id,
                Price = 450000,
                ValidFrom = DateTimeOffset.UtcNow.AddDays(-1),
                SourceItemId = reqItem.Id,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            });
        }

        var priceWholesaleBExists = await context.SkuUnitPrices.IgnoreQueryFilters().AnyAsync(p => p.TenantId == TenantBId && p.PriceListId == wholesaleBList.Id && p.SkuId == skuBId && p.UnitId == unitB.Id, ct);
        if (!priceWholesaleBExists)
        {
            var reqItem = new PriceChangeRequestItem
            {
                Id = Guid.NewGuid(),
                TenantId = TenantBId,
                RequestId = openReqB.Id,
                PriceListId = wholesaleBList.Id,
                SkuId = skuBId,
                UnitId = unitB.Id,
                NewPrice = 320000,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            };
            context.PriceChangeRequestItems.Add(reqItem);

            context.SkuUnitPrices.Add(new SkuUnitPrice
            {
                Id = Guid.NewGuid(),
                TenantId = TenantBId,
                PriceListId = wholesaleBList.Id,
                SkuId = skuBId,
                UnitId = unitB.Id,
                Price = 320000,
                ValidFrom = DateTimeOffset.UtcNow.AddDays(-1),
                SourceItemId = reqItem.Id,
                CreatedAt = DateTimeOffset.UtcNow.AddDays(-1)
            });
        }

        // 6. Nhà cung cấp
        var supplierBId = Guid.Parse("22222222-0000-0000-0007-000000000001");
        var supBExists = await context.Suppliers
            .IgnoreQueryFilters()
            .AnyAsync(s => s.Id == supplierBId, ct);

        if (!supBExists)
        {
            context.Suppliers.Add(new Supplier
            {
                Id = supplierBId,
                TenantId = TenantBId,
                Name = "Công ty TNHH Dược Phẩm Aura",
                Phone = "+842439002233",
                Email = "order@aurapharm.local",
                Address = "Hà Nội",
                IsActive = true
            });
        }

        // 7. Số dư tồn kho khởi tạo: Tồn ban đầu = 0
        var balanceBId = Guid.Parse("22222222-0000-0000-0008-000000000001");
        var balanceBExists = await context.InventoryBalances
            .IgnoreQueryFilters()
            .AnyAsync(ib => ib.Id == balanceBId || (ib.TenantId == TenantBId && ib.BranchId == branchBId && ib.SkuId == skuBId), ct);

        if (!balanceBExists)
        {
            context.InventoryBalances.Add(new InventoryBalance
            {
                Id = balanceBId,
                TenantId = TenantBId,
                BranchId = branchBId,
                SkuId = skuBId,
                OnHand = 0,
                Reserved = 0,
                AverageCost = 0
            });
        }
    }
}
