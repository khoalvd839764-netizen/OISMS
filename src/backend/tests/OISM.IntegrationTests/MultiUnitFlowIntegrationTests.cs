using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Npgsql;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Auth.RegisterTenant;
using OISM.Application.Features.Catalog.Products;
using OISM.Application.Features.Catalog.Units;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.Orders.Dtos;
using OISM.Application.Features.Pos;
using OISM.Application.Features.Reports.Dtos;
using Xunit;

namespace OISM.IntegrationTests;

/// <summary>
/// Kiểm chứng Upgrade 05 phần P2 trên PostgreSQL thật, bám theo tiêu chí nghiệm thu mục 8 của tài liệu 05:
/// nhập, bán, giữ chỗ, giá vốn và báo cáo đều theo lượng gốc; chứng từ giữ hệ số snapshot.
/// </summary>
public class MultiUnitFlowIntegrationTests : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private const decimal RetailPerKg = 15000m;
    private const decimal BaoPrice = 350000m;
    private const string BaoBarcode = "8930000000251";

    private readonly WebApplicationFactoryWithPostgres _factory;
    private readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    public MultiUnitFlowIntegrationTests(WebApplicationFactoryWithPostgres factory) => _factory = factory;

    public async Task InitializeAsync() => await PostgresTestEnvironment.ResetTestDataAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private sealed class Shop
    {
        public required HttpClient Client { get; init; }
        public required Guid TenantId { get; init; }
        public required Guid BranchId { get; init; }
        public required Guid WarehouseId { get; init; }
        public required Guid SupplierId { get; init; }
        public required Guid ProductId { get; init; }
        public required ProductVariantResponse Sku { get; init; }
        public required Guid KgId { get; init; }
        public required Guid TanId { get; init; }
        public required Guid BaoId { get; init; }
        public required Guid LitId { get; init; }
        public required Guid ThungId { get; init; }
        public required Guid BaoSkuUnitId { get; init; }
    }

    private async Task<Shop> SetupShopAsync(string code)
    {
        var client = _factory.CreateClient();
        var email = $"{code}@flow.test";
        (await client.PostAsJsonAsync("/api/auth/register-tenant", new RegisterTenantRequest
        {
            TenantName = code,
            TenantCode = code,
            FullName = "Owner",
            Email = email,
            Password = "Password@123"
        })).EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/api/auth/login", new LoginRequest { Email = email, Password = "Password@123" });
        login.EnsureSuccessStatusCode();
        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>(_json);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        var tenantId = auth.User.TenantId;
        var branchId = Guid.NewGuid();
        var warehouseId = Guid.NewGuid();
        var supplierId = Guid.NewGuid();
        await ExecuteAsync(@"
            INSERT INTO public.branches (id, tenant_id, name) VALUES (@b, @t, 'CN1');
            INSERT INTO public.warehouses (id, tenant_id, branch_id, name) VALUES (@w, @t, @b, 'Kho1');
            INSERT INTO public.suppliers (id, tenant_id, name) VALUES (@s, @t, 'NCC1');",
            ("b", branchId), ("t", tenantId), ("w", warehouseId), ("s", supplierId));

        var units = (await client.GetFromJsonAsync<List<UnitResponse>>("/api/units", _json))!;
        Guid UnitId(string unitCode) => units.Single(u => u.Code == unitCode).Id;

        var created = await client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "Gạo A",
            Variants = new() { new() { SKU = $"GAO-{code}".ToUpperInvariant(), VariantName = "Gạo", RetailPrice = RetailPerKg, BaseUnitId = UnitId("kg") } }
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var productId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("productId").GetGuid();
        var product = await client.GetFromJsonAsync<ProductResponse>($"/api/products/{productId}", _json);
        var sku = product!.Variants.Single();

        await AddUnitAsync(client, productId, sku.Id, new CreateSkuUnitRequest
        {
            UnitId = UnitId("tan"), FactorToBase = 1000, CanPurchase = true, CanSell = false, QuantityDecimalPlaces = 3
        });
        var bao = await AddUnitAsync(client, productId, sku.Id, new CreateSkuUnitRequest
        {
            UnitId = UnitId("bao"), FactorToBase = 25, CanPurchase = true, CanSell = true, QuantityDecimalPlaces = 0, SalePrice = BaoPrice, Barcode = BaoBarcode
        });
        await AddUnitAsync(client, productId, sku.Id, new CreateSkuUnitRequest
        {
            UnitId = UnitId("thung"), FactorToBase = 50, CanPurchase = true, CanSell = true, QuantityDecimalPlaces = 0
        });

        return new Shop
        {
            Client = client,
            TenantId = tenantId,
            BranchId = branchId,
            WarehouseId = warehouseId,
            SupplierId = supplierId,
            ProductId = productId,
            Sku = sku,
            KgId = UnitId("kg"),
            TanId = UnitId("tan"),
            BaoId = UnitId("bao"),
            LitId = UnitId("lit"),
            ThungId = UnitId("thung"),
            BaoSkuUnitId = bao.Id
        };
    }

    private async Task<SkuUnitResponse> AddUnitAsync(HttpClient client, Guid productId, Guid skuId, CreateSkuUnitRequest request)
    {
        var res = await client.PostAsJsonAsync($"/api/products/{productId}/variants/{skuId}/units", request);
        res.StatusCode.Should().Be(HttpStatusCode.OK, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<SkuUnitResponse>(_json))!;
    }

    private async Task<GoodsReceiptResponseDto> CreateReceiptAsync(Shop shop, Guid? unitId, decimal quantity, decimal unitPrice, Guid? warehouseId = null)
    {
        var res = await shop.Client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            BranchId = shop.BranchId,
            WarehouseId = warehouseId ?? shop.WarehouseId,
            SupplierId = shop.SupplierId,
            Items = new() { new() { SkuId = shop.Sku.Id, UnitId = unitId, Quantity = quantity, UnitPrice = unitPrice } }
        });
        res.StatusCode.Should().Be(HttpStatusCode.Created, await res.Content.ReadAsStringAsync());
        return (await res.Content.ReadFromJsonAsync<GoodsReceiptResponseDto>(_json))!;
    }

    private async Task ConfirmReceiptAsync(Shop shop, Guid receiptId)
    {
        var res = await shop.Client.PostAsync($"/api/goods-receipts/{receiptId}/confirm", null);
        res.IsSuccessStatusCode.Should().BeTrue(await res.Content.ReadAsStringAsync());
    }

    private async Task ReceiveAsync(Shop shop, Guid? unitId, decimal quantity, decimal unitPrice, Guid? warehouseId = null)
    {
        var receipt = await CreateReceiptAsync(shop, unitId, quantity, unitPrice, warehouseId);
        await ConfirmReceiptAsync(shop, receipt.Id);
    }

    private OrderItemRequestDto Line(Shop shop, Guid? unitId, decimal quantity, decimal unitPrice) => new()
    {
        SkuId = shop.Sku.Id,
        SkuCode = shop.Sku.SKU,
        UnitId = unitId,
        Quantity = quantity,
        UnitPrice = unitPrice
    };

    private static PosCheckoutRequestDto Cart(Shop shop, Guid requestKey, params OrderItemRequestDto[] items)
    {
        var total = items.Sum(i => i.Quantity * i.UnitPrice);
        return new PosCheckoutRequestDto
        {
            BranchId = shop.BranchId,
            RequestKey = requestKey,
            PaymentMethod = "Cash",
            AmountGiven = total,
            TotalAmount = total,
            Items = items.ToList()
        };
    }

    private async Task<HttpResponseMessage> CheckoutAsync(Shop shop, PosCheckoutRequestDto cart)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/pos/checkout") { Content = JsonContent.Create(cart) };
        request.Headers.Add("Idempotency-Key", cart.RequestKey.ToString());
        return await shop.Client.SendAsync(request);
    }

    private static async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task<T> ScalarAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        foreach (var (name, value) in parameters) cmd.Parameters.AddWithValue(name, value);
        return (T)Convert.ChangeType((await cmd.ExecuteScalarAsync())!, typeof(T));
    }

    private static Task<decimal> OnHandAsync(Shop shop) =>
        ScalarAsync<decimal>("SELECT COALESCE(sum(on_hand),0) FROM public.inventory_balances WHERE sku_id=@s", ("s", shop.Sku.Id));

    private static Task<decimal> ReservedAsync(Shop shop) =>
        ScalarAsync<decimal>("SELECT COALESCE(sum(reserved),0) FROM public.inventory_balances WHERE sku_id=@s", ("s", shop.Sku.Id));

    private static Task<decimal> AverageCostAsync(Shop shop) =>
        ScalarAsync<decimal>("SELECT average_cost FROM public.inventory_balances WHERE sku_id=@s", ("s", shop.Sku.Id));

    private static Task<long> LedgerCountAsync(Shop shop) =>
        ScalarAsync<long>("SELECT count(*) FROM public.inventory_transactions WHERE sku_id=@s", ("s", shop.Sku.Id));

    // Tiêu chí 1, 2, 3 và phần báo cáo của tiêu chí 6
    [Fact]
    public async Task Nhap1Tan_Ban2Kg5Va1Bao_Con972Kg5_GiaVonVaBaoCaoTheoLuongGoc()
    {
        var shop = await SetupShopAsync("flow_main");

        var receipt = await CreateReceiptAsync(shop, shop.TanId, 1, 12_000_000m);
        var receiptItem = receipt.Items.Single();
        receiptItem.UnitId.Should().Be(shop.TanId);
        receiptItem.FactorSnapshot.Should().Be(1000m);
        receiptItem.BaseQuantity.Should().Be(1000m);
        receiptItem.LineTotal.Should().Be(12_000_000m);
        await ConfirmReceiptAsync(shop, receipt.Id);

        (await OnHandAsync(shop)).Should().Be(1000m);
        (await AverageCostAsync(shop)).Should().Be(12000m);
        (await ScalarAsync<decimal>("SELECT quantity FROM public.inventory_transactions WHERE sku_id=@s AND type='IN'", ("s", shop.Sku.Id))).Should().Be(1000m);
        (await ScalarAsync<decimal>("SELECT unit_cost FROM public.inventory_transactions WHERE sku_id=@s AND type='IN'", ("s", shop.Sku.Id))).Should().Be(12000m);

        var checkout = await CheckoutAsync(shop, Cart(shop, Guid.NewGuid(),
            Line(shop, null, 2.5m, RetailPerKg),
            Line(shop, shop.BaoId, 1, BaoPrice)));
        checkout.IsSuccessStatusCode.Should().BeTrue(await checkout.Content.ReadAsStringAsync());

        (await OnHandAsync(shop)).Should().Be(972.5m);
        (await AverageCostAsync(shop)).Should().Be(12000m, "bán hàng không đổi WAC");
        (await ScalarAsync<decimal>("SELECT sum(quantity) FROM public.inventory_transactions WHERE sku_id=@s AND type='OUT'", ("s", shop.Sku.Id))).Should().Be(27.5m);

        var orderId = await ScalarAsync<Guid>("SELECT order_id FROM public.order_items WHERE sku_id=@s LIMIT 1", ("s", shop.Sku.Id));
        var order = await shop.Client.GetFromJsonAsync<OrderResponseDto>($"/api/orders/{orderId}", _json);
        order!.TotalAmount.Should().Be(2.5m * RetailPerKg + BaoPrice);
        order.Items.Should().HaveCount(2, "cùng SKU khác đơn vị phải là hai dòng");
        order.TotalCogs.Should().Be(27.5m * 12000m);

        var baoLine = order.Items.Single(i => i.UnitId == shop.BaoId);
        baoLine.Quantity.Should().Be(1m);
        baoLine.UnitPrice.Should().Be(BaoPrice);
        baoLine.BaseQuantity.Should().Be(25m);
        baoLine.FactorSnapshot.Should().Be(25m);
        baoLine.UnitName.Should().Be("Bao");
        baoLine.Subtotal.Should().Be(BaoPrice);
        baoLine.TotalCost.Should().Be(25m * 12000m);

        var kgLine = order.Items.Single(i => i.UnitId == shop.KgId);
        kgLine.Quantity.Should().Be(2.5m);
        kgLine.BaseQuantity.Should().Be(2.5m);

        var report = await shop.Client.GetFromJsonAsync<RevenueProfitReportDto>("/api/reports/revenue-profit", _json);
        report!.TotalCostOfGoodsSold.Should().Be(27.5m * 12000m);
        report.TotalItemsSold.Should().Be(27.5m);
        report.TotalNetRevenue.Should().Be(2.5m * RetailPerKg + BaoPrice);
    }

    // Tiêu chí 4
    [Fact]
    public async Task DauVaoSai_BiTuChoi_VaKhongDoiTon()
    {
        var shop = await SetupShopAsync("flow_reject");
        await ReceiveAsync(shop, null, 1000, 12000m);
        var ledgerBefore = await LedgerCountAsync(shop);

        async Task ExpectAsync(string errorCode, params OrderItemRequestDto[] items)
        {
            var res = await CheckoutAsync(shop, Cart(shop, Guid.NewGuid(), items));
            var body = await res.Content.ReadAsStringAsync();
            res.IsSuccessStatusCode.Should().BeFalse($"phải trả {errorCode} nhưng nhận: {body}");
            body.Should().Contain(errorCode);
        }

        // Giá bao lấy từ giá kg × 25 hoặc giá kg đều sai
        await ExpectAsync("PRICE_MISMATCH", Line(shop, shop.BaoId, 1, 25 * RetailPerKg));
        await ExpectAsync("PRICE_MISMATCH", Line(shop, shop.BaoId, 1, RetailPerKg));
        await ExpectAsync("UNIT_NOT_ALLOWED", Line(shop, shop.TanId, 1, 1000));
        await ExpectAsync("UNIT_NOT_ALLOWED", Line(shop, shop.LitId, 1, 1000));
        await ExpectAsync("UNIT_PRICE_NOT_CONFIGURED", Line(shop, shop.ThungId, 1, 1000));
        await ExpectAsync("INVALID_UNIT_QUANTITY", Line(shop, shop.BaoId, 1.5m, BaoPrice));
        await ExpectAsync("INSUFFICIENT_STOCK", Line(shop, shop.BaoId, 41, BaoPrice));

        var receiptRes = await shop.Client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            BranchId = shop.BranchId,
            WarehouseId = shop.WarehouseId,
            SupplierId = shop.SupplierId,
            Items = new() { new() { SkuId = shop.Sku.Id, UnitId = shop.LitId, Quantity = 1, UnitPrice = 1 } }
        });
        receiptRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await receiptRes.Content.ReadAsStringAsync()).Should().Contain("UNIT_NOT_ALLOWED");

        (await OnHandAsync(shop)).Should().Be(1000m);
        (await LedgerCountAsync(shop)).Should().Be(ledgerBefore);
    }

    // EF không được biến giá trị 0/false thành mặc định của cột khi lưu
    [Fact]
    public async Task CauHinhBangKhongVaFalse_DuocLuuDung()
    {
        var shop = await SetupShopAsync("flow_zero");

        var units = await shop.Client.GetFromJsonAsync<List<UnitResponse>>("/api/units", _json);
        units!.Single(u => u.Code == "bao").DecimalPlaces.Should().Be((short)0);

        var customRes = await shop.Client.PostAsJsonAsync("/api/units", new CreateUnitRequest
        {
            Code = "kien", Name = "Kiện", Symbol = "kiện", MeasureGroup = "Packaging", DecimalPlaces = 0
        });
        var custom = (await customRes.Content.ReadFromJsonAsync<UnitResponse>(_json))!;
        (await ScalarAsync<short>("SELECT decimal_places FROM public.units WHERE id=@i", ("i", custom.Id))).Should().Be((short)0);

        var kien = await AddUnitAsync(shop.Client, shop.ProductId, shop.Sku.Id, new CreateSkuUnitRequest
        {
            UnitId = custom.Id, FactorToBase = 40, CanPurchase = false, CanSell = false, QuantityDecimalPlaces = 0, IsDefaultSale = false
        });
        (await ScalarAsync<short>("SELECT quantity_decimal_places FROM public.sku_units WHERE id=@i", ("i", kien.Id))).Should().Be((short)0);
        (await ScalarAsync<bool>("SELECT can_purchase FROM public.sku_units WHERE id=@i", ("i", kien.Id))).Should().BeFalse();
        (await ScalarAsync<bool>("SELECT can_sell FROM public.sku_units WHERE id=@i", ("i", kien.Id))).Should().BeFalse();

        var receipt = await shop.Client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            BranchId = shop.BranchId,
            WarehouseId = shop.WarehouseId,
            SupplierId = shop.SupplierId,
            Items = new() { new() { SkuId = shop.Sku.Id, UnitId = custom.Id, Quantity = 1, UnitPrice = 1 } }
        });
        receipt.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity, "đơn vị đã tắt chiều mua");

        var tenthBao = await shop.Client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            BranchId = shop.BranchId,
            WarehouseId = shop.WarehouseId,
            SupplierId = shop.SupplierId,
            Items = new() { new() { SkuId = shop.Sku.Id, UnitId = shop.BaoId, Quantity = 1.5m, UnitPrice = 1 } }
        });
        tenthBao.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await tenthBao.Content.ReadAsStringAsync()).Should().Contain("INVALID_UNIT_QUANTITY");
    }

    // Tiêu chí 5
    [Fact]
    public async Task DoiHeSoSauGiaoDich_ChungTuCuGiuNguyen_GiaoDichMoiDungHeSoMoi()
    {
        var shop = await SetupShopAsync("flow_factor");
        await ReceiveAsync(shop, null, 1000, 12000m);

        var firstKey = Guid.NewGuid();
        (await CheckoutAsync(shop, Cart(shop, firstKey, Line(shop, shop.BaoId, 1, BaoPrice)))).IsSuccessStatusCode.Should().BeTrue();
        var draft = await CreateReceiptAsync(shop, shop.BaoId, 2, 250000m);

        var update = await shop.Client.PutAsJsonAsync(
            $"/api/products/{shop.ProductId}/variants/{shop.Sku.Id}/units/{shop.BaoSkuUnitId}",
            new UpdateSkuUnitRequest { FactorToBase = 50 });
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        (await ScalarAsync<decimal>("SELECT base_quantity FROM public.order_items WHERE sku_id=@s", ("s", shop.Sku.Id))).Should().Be(25m);
        (await OnHandAsync(shop)).Should().Be(975m);

        // Phiếu lập trước khi đổi hệ số vẫn xác nhận theo snapshot 25
        await ConfirmReceiptAsync(shop, draft.Id);
        (await OnHandAsync(shop)).Should().Be(1025m);

        // Giao dịch mới dùng hệ số 50
        (await CheckoutAsync(shop, Cart(shop, Guid.NewGuid(), Line(shop, shop.BaoId, 1, BaoPrice)))).IsSuccessStatusCode.Should().BeTrue();
        (await OnHandAsync(shop)).Should().Be(975m);

        // Trigger chặn sửa thẳng snapshot trên phiếu nhập đã xác nhận
        var tamper = async () => await ExecuteAsync("UPDATE public.goods_receipt_items SET factor_snapshot = 100 WHERE id=@i", ("i", draft.Items.Single().Id));
        await tamper.Should().ThrowAsync<PostgresException>().Where(e => e.SqlState == "23514");
    }

    [Fact]
    public async Task DonAdmin_GiuChoVaXuatKhoTheoLuongGoc_ChiaHaiKho_HuyTraDungLuong()
    {
        var shop = await SetupShopAsync("flow_admin");
        var secondWarehouse = Guid.NewGuid();
        await ExecuteAsync("INSERT INTO public.warehouses (id, tenant_id, branch_id, name) VALUES (@w, @t, @b, 'Kho2')",
            ("w", secondWarehouse), ("t", shop.TenantId), ("b", shop.BranchId));
        await ReceiveAsync(shop, null, 20, 12000m);
        await ReceiveAsync(shop, null, 20, 12000m, secondWarehouse);

        var request = new CreateOrderRequestDto
        {
            BranchId = shop.BranchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new() { Line(shop, shop.BaoId, 1, BaoPrice), Line(shop, null, 10, RetailPerKg) }
        };
        var created = await shop.Client.PostAsJsonAsync("/api/orders", request);
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var order = (await created.Content.ReadFromJsonAsync<OrderResponseDto>(_json))!;

        order.Items.Should().HaveCount(2);
        order.TotalAmount.Should().Be(BaoPrice + 10 * RetailPerKg);
        (await OnHandAsync(shop)).Should().Be(40m);
        (await ReservedAsync(shop)).Should().Be(35m);

        // Mỗi dòng được giữ đúng lượng gốc của nó, dù 35 kg phải chia qua hai kho
        foreach (var item in order.Items)
        {
            (await ScalarAsync<decimal>("SELECT sum(quantity) FROM public.reservations WHERE order_item_id=@i", ("i", item.Id))).Should().Be(item.BaseQuantity);
        }
        (await ScalarAsync<long>("SELECT count(DISTINCT warehouse_id) FROM public.reservations WHERE order_id=@o", ("o", order.Id))).Should().Be(2);

        var confirm = await shop.Client.PostAsJsonAsync($"/api/orders/{order.Id}/confirm", new ConfirmOrderRequestDto());
        confirm.IsSuccessStatusCode.Should().BeTrue(await confirm.Content.ReadAsStringAsync());
        var confirmed = (await confirm.Content.ReadFromJsonAsync<OrderResponseDto>(_json))!;

        (await OnHandAsync(shop)).Should().Be(5m);
        (await ReservedAsync(shop)).Should().Be(0m);
        confirmed.TotalCogs.Should().Be(35m * 12000m);

        var cancelledOrder = await shop.Client.PostAsJsonAsync("/api/orders", new CreateOrderRequestDto
        {
            BranchId = shop.BranchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new() { Line(shop, null, 5, RetailPerKg) }
        });
        cancelledOrder.StatusCode.Should().Be(HttpStatusCode.Created);
        var toCancel = (await cancelledOrder.Content.ReadFromJsonAsync<OrderResponseDto>(_json))!;
        (await ReservedAsync(shop)).Should().Be(5m);
        var cancel = await shop.Client.PostAsJsonAsync($"/api/orders/{toCancel.Id}/cancel", new CancelOrderRequestDto { Reason = "Khách đổi ý" });
        cancel.IsSuccessStatusCode.Should().BeTrue(await cancel.Content.ReadAsStringAsync());
        (await ReservedAsync(shop)).Should().Be(0m);
        (await OnHandAsync(shop)).Should().Be(5m);
    }

    // Tiêu chí 7
    [Fact]
    public async Task Retry_KhongTaoThemTonHaySoCai()
    {
        var shop = await SetupShopAsync("flow_retry");
        var receipt = await CreateReceiptAsync(shop, shop.TanId, 1, 12_000_000m);
        await ConfirmReceiptAsync(shop, receipt.Id);
        (await shop.Client.PostAsync($"/api/goods-receipts/{receipt.Id}/confirm", null)).IsSuccessStatusCode.Should().BeFalse();
        (await OnHandAsync(shop)).Should().Be(1000m);
        (await LedgerCountAsync(shop)).Should().Be(1);

        var key = Guid.NewGuid();
        var first = await CheckoutAsync(shop, Cart(shop, key, Line(shop, null, 25, RetailPerKg), Line(shop, shop.BaoId, 1, BaoPrice)));
        first.IsSuccessStatusCode.Should().BeTrue(await first.Content.ReadAsStringAsync());
        var ledgerAfterFirst = await LedgerCountAsync(shop);

        // Gửi unitId bằng đơn vị gốc thay vì để trống vẫn là cùng một giỏ
        var replay = await CheckoutAsync(shop, Cart(shop, key, Line(shop, shop.KgId, 25, RetailPerKg), Line(shop, shop.BaoId, 1, BaoPrice)));
        replay.IsSuccessStatusCode.Should().BeTrue(await replay.Content.ReadAsStringAsync());
        (await OnHandAsync(shop)).Should().Be(950m);
        (await LedgerCountAsync(shop)).Should().Be(ledgerAfterFirst);

        // Cùng key nhưng khác đơn vị là payload khác
        var mismatch = await CheckoutAsync(shop, Cart(shop, key, Line(shop, null, 26, RetailPerKg), Line(shop, shop.BaoId, 1, BaoPrice)));
        mismatch.IsSuccessStatusCode.Should().BeFalse();
        (await LedgerCountAsync(shop)).Should().Be(ledgerAfterFirst);

        var adminRequest = new CreateOrderRequestDto
        {
            BranchId = shop.BranchId,
            Channel = "Admin",
            RequestKey = Guid.NewGuid(),
            Items = new() { Line(shop, shop.BaoId, 1, BaoPrice), Line(shop, null, 5, RetailPerKg) }
        };
        (await shop.Client.PostAsJsonAsync("/api/orders", adminRequest)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await shop.Client.PostAsJsonAsync("/api/orders", adminRequest)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReservedAsync(shop)).Should().Be(30m);
    }

    // Tiêu chí 6: không phát sinh tồn âm khi nhiều giao dịch quy đổi tranh cùng tồn
    [Fact]
    public async Task BanBaoDongThoi_KhongOversell_KhongAmTon()
    {
        var shop = await SetupShopAsync("flow_race");
        await ReceiveAsync(shop, null, 100, 12000m);

        var results = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ =>
            CheckoutAsync(shop, Cart(shop, Guid.NewGuid(), Line(shop, shop.BaoId, 1, BaoPrice)))));

        results.Count(r => r.IsSuccessStatusCode).Should().Be(4, "100 kg chỉ đủ 4 bao 25 kg");
        foreach (var failed in results.Where(r => !r.IsSuccessStatusCode))
        {
            failed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        }
        (await OnHandAsync(shop)).Should().Be(0m);
    }

    // Tiêu chí 3
    [Fact]
    public async Task QuetMaVachQuyCach_ChonDungDonVi()
    {
        var shop = await SetupShopAsync("flow_scan");
        await ReceiveAsync(shop, null, 1000, 12000m);

        var byPack = await shop.Client.GetFromJsonAsync<PosProductSearchResponse>($"/api/pos/barcode/{BaoBarcode}?branchId={shop.BranchId}", _json);
        byPack!.VariantId.Should().Be(shop.Sku.Id);
        byPack.MatchedUnitId.Should().Be(shop.BaoId);
        byPack.Available.Should().Be(1000m);
        byPack.Units.Single(u => u.UnitId == shop.BaoId).SalePrice.Should().Be(BaoPrice);
        byPack.Units.Single(u => u.UnitId == shop.BaoId).Factor.Should().Be(25m);
        byPack.Units.Single(u => u.IsBase).SalePrice.Should().Be(RetailPerKg);
        byPack.Units.Should().NotContain(u => u.UnitId == shop.TanId, "tấn chỉ dùng để mua");

        var bySku = await shop.Client.GetFromJsonAsync<PosProductSearchResponse>($"/api/pos/barcode/{shop.Sku.Barcode}?branchId={shop.BranchId}", _json);
        bySku!.MatchedUnitId.Should().BeNull();

        var search = await shop.Client.GetFromJsonAsync<List<PosProductSearchResponse>>($"/api/pos/search?query={BaoBarcode}&branchId={shop.BranchId}", _json);
        search!.Should().ContainSingle(p => p.VariantId == shop.Sku.Id);
    }

    [Fact]
    public async Task TenantKhac_KhongDungDuocDonViCuaNhauKhiBanVaNhap()
    {
        var a = await SetupShopAsync("flow_ten_a");
        var b = await SetupShopAsync("flow_ten_b");
        await ReceiveAsync(b, null, 100, 12000m);

        var sale = await CheckoutAsync(b, Cart(b, Guid.NewGuid(), Line(b, a.BaoId, 1, BaoPrice)));
        sale.IsSuccessStatusCode.Should().BeFalse();
        (await sale.Content.ReadAsStringAsync()).Should().Contain("UNIT_NOT_ALLOWED");

        var receipt = await b.Client.PostAsJsonAsync("/api/goods-receipts", new CreateGoodsReceiptDto
        {
            BranchId = b.BranchId,
            WarehouseId = b.WarehouseId,
            SupplierId = b.SupplierId,
            Items = new() { new() { SkuId = b.Sku.Id, UnitId = a.BaoId, Quantity = 1, UnitPrice = 1 } }
        });
        receipt.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        (await OnHandAsync(b)).Should().Be(100m);
    }

    // Các lỗi nhỏ của vòng P1: đơn vị đang tắt và đổi đơn vị gốc
    [Fact]
    public async Task DonViDangTat_VaDoiDonViGoc_GiuDungHeSoVaLichSu()
    {
        var shop = await SetupShopAsync("flow_units");
        var url = $"/api/products/{shop.ProductId}/variants/{shop.Sku.Id}/units";
        var all = (await shop.Client.GetFromJsonAsync<List<SkuUnitResponse>>(url, _json))!;

        // Tắt mọi đơn vị quy đổi để được phép đổi gốc
        foreach (var conversion in all.Where(u => !u.IsBase))
        {
            (await shop.Client.PutAsJsonAsync($"{url}/{conversion.Id}", new UpdateSkuUnitRequest { IsActive = false }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Đơn vị đang tắt không được làm mặc định, nên SKU không bị mất mặc định
        var setDefault = await shop.Client.PutAsJsonAsync($"{url}/{shop.BaoSkuUnitId}", new UpdateSkuUnitRequest { IsDefaultSale = true });
        setDefault.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ScalarAsync<long>("SELECT count(*) FROM public.sku_units WHERE sku_id=@s AND is_default_sale AND is_active", ("s", shop.Sku.Id))).Should().Be(1);

        // Bao thành đơn vị gốc: hệ số 25 về 1 có ghi lịch sử và người đổi
        var variantUrl = $"/api/products/{shop.ProductId}/variants/{shop.Sku.Id}";
        var toBao = await shop.Client.PutAsJsonAsync(variantUrl, new UpdateVariantRequest
        {
            VariantName = shop.Sku.VariantName, RetailPrice = shop.Sku.RetailPrice, WholesalePrice = shop.Sku.WholesalePrice, BaseUnitId = shop.BaoId
        });
        toBao.StatusCode.Should().Be(HttpStatusCode.OK, await toBao.Content.ReadAsStringAsync());

        var history = (await shop.Client.GetFromJsonAsync<List<SkuUnitFactorHistoryResponse>>($"{url}/{shop.BaoSkuUnitId}/history", _json))!;
        history.Should().Contain(h => h.OldFactor == 25m && h.NewFactor == 1m && h.NewVersion == 2 && h.ChangedBy != null);

        // Đổi về kg: dòng bao có lịch sử nên phải được giữ lại ở trạng thái tắt, không được lỗi khóa ngoại
        var toKg = await shop.Client.PutAsJsonAsync(variantUrl, new UpdateVariantRequest
        {
            VariantName = shop.Sku.VariantName, RetailPrice = shop.Sku.RetailPrice, WholesalePrice = shop.Sku.WholesalePrice, BaseUnitId = shop.KgId
        });
        toKg.StatusCode.Should().Be(HttpStatusCode.OK, await toKg.Content.ReadAsStringAsync());
        (await ScalarAsync<bool>("SELECT is_active FROM public.sku_units WHERE id=@i", ("i", shop.BaoSkuUnitId))).Should().BeFalse();

        // Bật lại phải xác nhận hệ số
        var noFactor = await shop.Client.PutAsJsonAsync($"{url}/{shop.BaoSkuUnitId}", new UpdateSkuUnitRequest { IsActive = true });
        noFactor.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await noFactor.Content.ReadAsStringAsync()).Should().Contain("INVALID_UNIT_FACTOR");

        var reactivated = await shop.Client.PutAsJsonAsync($"{url}/{shop.BaoSkuUnitId}", new UpdateSkuUnitRequest { IsActive = true, FactorToBase = 25, SalePrice = BaoPrice });
        reactivated.StatusCode.Should().Be(HttpStatusCode.OK, await reactivated.Content.ReadAsStringAsync());
        (await ScalarAsync<decimal>("SELECT factor_to_base FROM public.sku_units WHERE id=@i", ("i", shop.BaoSkuUnitId))).Should().Be(25m);

        // Đơn vị đang là gốc không được tắt trong danh mục
        var disableBase = await shop.Client.PutAsJsonAsync($"/api/units/{shop.KgId}", new UpdateUnitRequest { IsActive = false });
        disableBase.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await disableBase.Content.ReadAsStringAsync()).Should().Contain("UNIT_IN_USE");

        // Đơn vị tắt trong danh mục: không thêm được vào SKU, không làm gốc được, và dòng đang bật không bán được
        var gram = (await shop.Client.GetFromJsonAsync<List<UnitResponse>>("/api/units", _json))!.Single(u => u.Code == "g");
        (await shop.Client.PutAsJsonAsync($"/api/units/{gram.Id}", new UpdateUnitRequest { IsActive = false })).StatusCode.Should().Be(HttpStatusCode.OK);
        var addInactive = await shop.Client.PostAsJsonAsync(url, new CreateSkuUnitRequest { UnitId = gram.Id, FactorToBase = 0.001m, QuantityDecimalPlaces = 0 });
        addInactive.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await addInactive.Content.ReadAsStringAsync()).Should().Contain("UNIT_NOT_ALLOWED");
        var inactiveBase = await shop.Client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Name = "X",
            Variants = new() { new() { SKU = "FLOW-X", VariantName = "x", RetailPrice = 1, BaseUnitId = gram.Id } }
        });
        inactiveBase.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        await ReceiveAsync(shop, null, 100, 12000m);
        (await shop.Client.PutAsJsonAsync($"/api/units/{shop.BaoId}", new UpdateUnitRequest { IsActive = false })).StatusCode.Should().Be(HttpStatusCode.OK);
        var sale = await CheckoutAsync(shop, Cart(shop, Guid.NewGuid(), Line(shop, shop.BaoId, 1, BaoPrice)));
        (await sale.Content.ReadAsStringAsync()).Should().Contain("UNIT_NOT_ALLOWED");
    }
}
