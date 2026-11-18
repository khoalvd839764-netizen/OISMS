using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Auth.Login;
using OISM.Application.Features.Inventory.Dtos;
using OISM.Application.Features.SupplierInvoices;
using OISM.Domain.Entities;
using OISM.Infrastructure.Persistence;
using Xunit;

namespace OISM.IntegrationTests;

[Collection("PostgresIntegrationTests")]
public sealed class PostgresSupplierInvoiceIntegrationTests(WebApplicationFactoryWithPostgres factory) : IAsyncLifetime, IClassFixture<WebApplicationFactoryWithPostgres>
{
    private const string Invoices = "/api/supplier-invoices";
    private const string Receipts = "/api/goods-receipts";
    private static readonly Guid TenantId = OismsDatabaseSeeder.TenantAId;
    private HttpClient _owner = null!;
    private Guid _branchId;
    private Guid _warehouseId;
    private Guid _supplierId;
    private Guid _skuId;

    public async Task InitializeAsync()
    {
        await PostgresTestEnvironment.ResetTestDataAsync();
        await using (var seed = PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenant()))
        {
            await OismsDatabaseSeeder.SeedAsync(seed, PostgresTestEnvironment.AppPassword);
        }
        await using var db = OpenDb();
        _branchId = await db.Branches.Select(b => b.Id).SingleAsync();
        _warehouseId = await db.Warehouses.Select(w => w.Id).FirstAsync();
        _supplierId = await db.Suppliers.Select(s => s.Id).SingleAsync();
        _skuId = await db.ProductVariants.Select(s => s.Id).SingleAsync();
        _owner = await LoginAsync("owner@shopa.local");
    }

    public Task DisposeAsync()
    {
        _owner?.Dispose();
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Matched_UsesPurchasePrice_AndReturnsThreeWayComparison()
    {
        var receipt = await DraftReceiptAsync();
        var invoice = await CreateAsync(receipt, 120m);
        Assert.Equal("Draft", invoice.Status);
        using var blocked = await ConfirmAsync(receipt.Id);
        await ProblemAsync(blocked, HttpStatusCode.Conflict, "GOODS_RECEIPT_INVOICE_PENDING");

        var submitted = await ReadAsync<SupplierInvoiceResponseDto>(await ActAsync(invoice.Id, "submit"));
        Assert.Equal("Matched", submitted.Status);
        Assert.Null(submitted.Variance);
        Assert.Equal(240m, submitted.Subtotal);
        Assert.Equal(275m, submitted.TotalAmount);

        var details = await ReadAsync<SupplierInvoiceResponseDto>(await _owner.GetAsync($"{Invoices}/{invoice.Id}"));
        var line = Assert.Single(details.Comparison!);
        Assert.Equal(5m, line.PurchaseOrder.Quantity);
        Assert.Equal(2m, line.GoodsReceipt.Quantity);
        Assert.Equal(2m, line.Invoice!.Quantity);
        Assert.Equal(2m, line.ExpectedQuantity);
        Assert.Equal(120m, line.ExpectedUnitPrice);
        Assert.Equal(0m, line.AmountDiff);

        var confirmed = await ReadAsync<GoodsReceiptResponseDto>(await ConfirmAsync(receipt.Id));
        Assert.Equal(invoice.Id, confirmed.InvoiceId);
        Assert.Equal("Matched", confirmed.InvoiceStatus);
        Assert.Equal(new StockSnapshot(1, 2m, 120m, 120m, 120m), await StockAsync(receipt));
    }

    [Fact]
    public async Task Approved_UsesConvertedInvoicePrice_ForLedgerAndWac()
    {
        var unitId = await AddConvertedUnitAsync();
        var receipt = await DraftReceiptAsync(unitId: unitId);
        Assert.Equal(10m, Assert.Single(receipt.Items).FactorSnapshot);

        var invoice = await CreateAsync(receipt, 180m, quantity: 3m);
        var pending = await ReadAsync<SupplierInvoiceResponseDto>(await ActAsync(invoice.Id, "submit"));
        Assert.Equal("PendingApproval", pending.Status);
        Assert.Equal("AtReceipt", pending.Variance!.VarianceType);
        Assert.Equal(300m, pending.Variance.TotalAmountDiff);
        Assert.Equal(1m, Assert.Single(pending.Comparison!).QuantityDiff);
        using var blocked = await ConfirmAsync(receipt.Id);
        await ProblemAsync(blocked, HttpStatusCode.Conflict, "GOODS_RECEIPT_INVOICE_PENDING");

        var approved = await ReadAsync<SupplierInvoiceResponseDto>(await ActAsync(invoice.Id, "approve"));
        Assert.Equal("Approved", approved.Status);
        Assert.Equal("Approved", approved.Variance!.Status);
        Assert.NotNull(approved.Variance.DecidedBy);
        Assert.Equal(new StockSnapshot(0, 0m, 0m, 120m, null), await StockAsync(receipt));

        var confirmed = await ReadAsync<GoodsReceiptResponseDto>(await ConfirmAsync(receipt.Id));
        Assert.Equal(180m, Assert.Single(confirmed.Items).UnitPrice);
        Assert.Equal(new StockSnapshot(1, 20m, 18m, 180m, 18m), await StockAsync(receipt));
        await using var db = OpenDb();
        Assert.Equal(120m, await db.PurchaseOrderItems.Select(i => i.UnitPrice).SingleAsync());
    }

    [Fact]
    public async Task RejectAtReceipt_CancelsDraftAtomically_AndKeepsStock()
    {
        var receipt = await DraftReceiptAsync();
        var invoice = await CreateAsync(receipt, 180m);
        var before = await StockAsync(receipt);
        await ReadAsync<SupplierInvoiceResponseDto>(await ActAsync(invoice.Id, "submit"));

        using var missingReason = await ActAsync(invoice.Id, "reject");
        await ProblemAsync(missingReason, HttpStatusCode.BadRequest, "VALIDATION_ERROR");
        var rejected = await ReadAsync<SupplierInvoiceResponseDto>(
            await ActAsync(invoice.Id, "reject", new { decisionNote = "Giá không đúng" }));
        Assert.Equal("Rejected", rejected.Status);
        Assert.Equal("Rejected", rejected.Variance!.Status);
        Assert.Equal("Giá không đúng", rejected.Variance.DecisionNote);
        var cancelled = await ReadAsync<GoodsReceiptResponseDto>(await _owner.GetAsync($"{Receipts}/{receipt.Id}"));
        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Null(cancelled.InvoiceId);
        Assert.Equal(before, await StockAsync(receipt));
        using var newInvoice = await SendCreateAsync(Draft(receipt, 120m), Guid.NewGuid());
        await ProblemAsync(newInvoice, HttpStatusCode.Conflict, "SUPPLIER_INVOICE_INVALID_STATE");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AfterReceipt_DecisionsNeverChangeStockOrReceiptPrice(bool approve)
    {
        var receipt = await DraftReceiptAsync();
        await ReadAsync<GoodsReceiptResponseDto>(await ConfirmAsync(receipt.Id));
        var before = await StockAsync(receipt);

        var invoice = await CreateAsync(receipt, 999m, quantity: 1m);
        var submitted = await ReadAsync<SupplierInvoiceResponseDto>(await ActAsync(invoice.Id, "submit"));
        Assert.Equal("AfterReceipt", submitted.Variance!.VarianceType);
        Assert.Equal(before, await StockAsync(receipt));

        var decision = await ReadAsync<SupplierInvoiceResponseDto>(await ActAsync(invoice.Id,
            approve ? "approve" : "reject", new { decisionNote = "Kiểm tra chứng từ" }));
        Assert.Equal(approve ? "Approved" : "Rejected", decision.Status);
        Assert.Equal(before, await StockAsync(receipt));
        if (approve)
        {
            var cancelled = await ReadAsync<SupplierInvoiceResponseDto>(
                await ActAsync(invoice.Id, "cancel", new { reason = "Lưu lại bản đính chính" }));
            Assert.Equal("Cancelled", cancelled.Status);
            Assert.Equal(before, await StockAsync(receipt));
        }
        Assert.Equal("Confirmed", (await ReadAsync<GoodsReceiptResponseDto>(
            await _owner.GetAsync($"{Receipts}/{receipt.Id}"))).Status);
    }

    [Fact]
    public async Task DuplicateAndReplay_AreScopedCorrectly_AndDraftCanBeEdited()
    {
        var receipt = await DraftReceiptAsync();
        var key = Guid.NewGuid();
        var draft = Draft(receipt, 120m);
        var invoice = await ReadAsync<SupplierInvoiceResponseDto>(await SendCreateAsync(draft, key), HttpStatusCode.Created);

        var edited = await ReadAsync<SupplierInvoiceResponseDto>(await _owner.PutAsJsonAsync($"{Invoices}/{invoice.Id}",
            new SupplierInvoiceDraftDto
            {
                InvoiceSeries = draft.InvoiceSeries, InvoiceNumber = draft.InvoiceNumber, InvoiceDate = draft.InvoiceDate,
                VatAmount = 30m, Items = [new(draft.Items[0].GoodsReceiptItemId, 2m, 130m)]
            }));
        Assert.Equal(260m, edited.Subtotal);

        var replay = await ReadAsync<SupplierInvoiceResponseDto>(await SendCreateAsync(draft, key));
        Assert.Equal(invoice.Id, replay.Id);
        Assert.True(replay.IsReplay);
        using var different = await SendCreateAsync(Draft(receipt, 130m), key);
        await ProblemAsync(different, HttpStatusCode.Conflict, "IDEMPOTENCY_PAYLOAD_MISMATCH");
        using var sameReceipt = await SendCreateAsync(Draft(receipt, 120m, number: "02"), Guid.NewGuid());
        await ProblemAsync(sameReceipt, HttpStatusCode.Conflict, "SUPPLIER_INVOICE_ALREADY_EXISTS");

        var second = await DraftReceiptAsync();
        using var duplicate = await SendCreateAsync(Draft(second, 120m), Guid.NewGuid());
        await ProblemAsync(duplicate, HttpStatusCode.Conflict, "SUPPLIER_INVOICE_DUPLICATE");
        using var wrongLine = await SendCreateAsync(Draft(second, 120m, number: "02", lineId: draft.Items[0].GoodsReceiptItemId), Guid.NewGuid());
        await ProblemAsync(wrongLine, HttpStatusCode.UnprocessableEntity, "SUPPLIER_INVOICE_RECEIPT_MISMATCH");

        var otherSupplierId = await AddSupplierAsync();
        var third = await DraftReceiptAsync(otherSupplierId);
        Assert.NotEqual(invoice.Id, (await CreateAsync(third, 120m)).Id);

        await ReadAsync<SupplierInvoiceResponseDto>(await ActAsync(invoice.Id, "submit"));
        using var editLocked = await _owner.PutAsJsonAsync($"{Invoices}/{invoice.Id}", draft);
        await ProblemAsync(editLocked, HttpStatusCode.Conflict, "SUPPLIER_INVOICE_INVALID_STATE");
        var page = await ReadAsync<PagedResult<SupplierInvoiceResponseDto>>(await _owner.GetAsync(
            $"{Invoices}?supplierId={_supplierId}&goodsReceiptId={receipt.Id}&status=PendingApproval&pendingOnly=true&fromDate=2026-11-12&toDate=2026-11-12"));
        Assert.Equal(invoice.Id, Assert.Single(page.Items).Id);
        await using var db = OpenDb();
        Assert.Equal(2, await db.SupplierInvoices.CountAsync());
    }

    [Fact]
    public async Task ApproveRacingConfirm_UsesOnlyApprovedPrice()
    {
        var receipt = await DraftReceiptAsync();
        var invoice = await CreateAsync(receipt, 180m);
        await ReadAsync<SupplierInvoiceResponseDto>(await ActAsync(invoice.Id, "submit"));

        var results = await RaceAsync(() => ActAsync(invoice.Id, "approve"), () => ConfirmAsync(receipt.Id));
        using var approval = results[0];
        using var confirm = results[1];
        Assert.Equal("Approved", (await ReadAsync<SupplierInvoiceResponseDto>(approval)).Status);
        if (confirm.StatusCode == HttpStatusCode.Conflict)
        {
            await ProblemAsync(confirm, HttpStatusCode.Conflict, "GOODS_RECEIPT_INVOICE_PENDING");
            Assert.Equal(0, (await StockAsync(receipt)).LedgerCount);
            await ReadAsync<GoodsReceiptResponseDto>(await ConfirmAsync(receipt.Id));
        }
        else
        {
            Assert.Equal("Confirmed", (await ReadAsync<GoodsReceiptResponseDto>(confirm)).Status);
        }
        Assert.Equal(new StockSnapshot(1, 2m, 180m, 180m, 180m), await StockAsync(receipt));
    }

    [Fact]
    public async Task CreateRacingConfirm_SerializesDraftOrAfterReceipt()
    {
        var receipt = await DraftReceiptAsync();
        var results = await RaceAsync(
            () => SendCreateAsync(Draft(receipt, 180m), Guid.NewGuid()), () => ConfirmAsync(receipt.Id));
        using var create = results[0];
        using var confirm = results[1];
        var invoice = await ReadAsync<SupplierInvoiceResponseDto>(create, HttpStatusCode.Created);
        if (confirm.StatusCode == HttpStatusCode.Conflict)
        {
            await ProblemAsync(confirm, HttpStatusCode.Conflict, "GOODS_RECEIPT_INVOICE_PENDING");
            Assert.Equal(0, (await StockAsync(receipt)).LedgerCount);
            var atReceipt = await ReadAsync<SupplierInvoiceResponseDto>(await ActAsync(invoice.Id, "submit"));
            Assert.Equal("AtReceipt", atReceipt.Variance!.VarianceType);
        }
        else
        {
            Assert.Equal("Confirmed", (await ReadAsync<GoodsReceiptResponseDto>(confirm)).Status);
            var afterReceipt = await ReadAsync<SupplierInvoiceResponseDto>(await ActAsync(invoice.Id, "submit"));
            Assert.Equal("AfterReceipt", afterReceipt.Variance!.VarianceType);
            Assert.Equal(new StockSnapshot(1, 2m, 120m, 120m, 120m), await StockAsync(receipt));
        }
    }

    [Fact]
    public async Task CancelRacingConfirm_PreservesThePriceChosenAtConfirmation()
    {
        var receipt = await DraftReceiptAsync();
        var invoice = await CreateAsync(receipt, 180m);
        await ReadAsync<SupplierInvoiceResponseDto>(await ActAsync(invoice.Id, "submit"));
        await ReadAsync<SupplierInvoiceResponseDto>(await ActAsync(invoice.Id, "approve"));

        var results = await RaceAsync(
            () => ActAsync(invoice.Id, "cancel", new { reason = "Đính chính hóa đơn" }), () => ConfirmAsync(receipt.Id));
        using var cancel = results[0];
        using var confirm = results[1];
        Assert.Equal("Cancelled", (await ReadAsync<SupplierInvoiceResponseDto>(cancel)).Status);
        var confirmed = await ReadAsync<GoodsReceiptResponseDto>(confirm);
        Assert.Equal("Confirmed", confirmed.Status);
        var price = Assert.Single(confirmed.Items).UnitPrice;
        Assert.Contains(price, new[] { 120m, 180m });
        Assert.Equal(new StockSnapshot(1, 2m, price, price, price), await StockAsync(receipt));
    }

    [Fact]
    public async Task StaffCannotDecide_CashierCannotAccess_AndOtherTenantCannotReadOrWrite()
    {
        var receipt = await DraftReceiptAsync();
        using var staff = await LoginAsync("staff@shopa.local");
        using var cashier = await LoginAsync("cashier@shopa.local");
        using var other = await LoginAsync("owner@shopb.local");
        var invoice = await ReadAsync<SupplierInvoiceResponseDto>(
            await SendCreateAsync(Draft(receipt, 180m), Guid.NewGuid(), staff), HttpStatusCode.Created);
        var pending = await ReadAsync<SupplierInvoiceResponseDto>(await ActAsync(invoice.Id, "submit", client: staff));
        Assert.Equal("PendingApproval", pending.Status);

        using var staffApprove = await ActAsync(invoice.Id, "approve", client: staff);
        using var staffReject = await ActAsync(invoice.Id, "reject", new { decisionNote = "Test" }, staff);
        using var cashierList = await cashier.GetAsync(Invoices);
        using var cashierGet = await cashier.GetAsync($"{Invoices}/{invoice.Id}");
        using var cashierCreate = await SendCreateAsync(Draft(receipt, 180m), Guid.NewGuid(), cashier);
        foreach (var forbidden in new[] { staffApprove, staffReject, cashierList, cashierGet, cashierCreate })
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using var otherGet = await other.GetAsync($"{Invoices}/{invoice.Id}");
        using var otherApprove = await ActAsync(invoice.Id, "approve", client: other);
        using var otherCreate = await SendCreateAsync(Draft(receipt, 180m), Guid.NewGuid(), other);
        foreach (var missing in new[] { otherGet, otherApprove, otherCreate })
            await ProblemAsync(missing, HttpStatusCode.NotFound, "NOT_FOUND");
        var otherPage = await ReadAsync<PagedResult<SupplierInvoiceResponseDto>>(await other.GetAsync(Invoices));
        Assert.Empty(otherPage.Items);
    }

    [Fact]
    public async Task ListByPurchaseOrder_ReturnsOnlyInvoicesOfThatOrder_WithReceiptAndOrderInfo()
    {
        var first = await DraftReceiptAsync();
        var other = await DraftReceiptAsync();
        var invoice = await CreateAsync(first, 120m);
        await ReadAsync<SupplierInvoiceResponseDto>(await SendCreateAsync(Draft(other, 120m, number: "02"), Guid.NewGuid()),
            HttpStatusCode.Created);

        var page = await ReadAsync<PagedResult<SupplierInvoiceResponseDto>>(
            await _owner.GetAsync($"{Invoices}?purchaseOrderId={first.PurchaseOrderId}"));
        var item = Assert.Single(page.Items);
        Assert.Equal(invoice.Id, item.Id);
        Assert.Equal(first.PurchaseOrderId, item.PurchaseOrderId);
        await using (var db = OpenDb())
        {
            var poNumber = await db.PurchaseOrders.Where(p => p.Id == first.PurchaseOrderId).Select(p => p.Number).SingleAsync();
            Assert.Equal(poNumber, item.PurchaseOrderNumber);
        }
        Assert.Equal(first.Number, item.GoodsReceiptNumber);
        Assert.Equal("Draft", item.GoodsReceiptStatus);
        Assert.False(string.IsNullOrWhiteSpace(item.SupplierName));

        var details = await ReadAsync<SupplierInvoiceResponseDto>(await _owner.GetAsync($"{Invoices}/{invoice.Id}"));
        Assert.Equal(item.PurchaseOrderNumber, details.PurchaseOrderNumber);
        Assert.Equal(item.SupplierName, details.SupplierName);

        var none = await ReadAsync<PagedResult<SupplierInvoiceResponseDto>>(
            await _owner.GetAsync($"{Invoices}?purchaseOrderId={Guid.NewGuid()}"));
        Assert.Empty(none.Items);
    }

    [Fact]
    public async Task DatabaseGuards_BlockDeletingInvoiceAndChangingSubmittedItems()
    {
        var receipt = await DraftReceiptAsync();
        var invoice = await CreateAsync(receipt, 120m);
        await ReadAsync<SupplierInvoiceResponseDto>(await ActAsync(invoice.Id, "submit"));

        await using var conn = new NpgsqlConnection(PostgresTestEnvironment.AdminDatabaseConnectionString);
        await conn.OpenAsync();
        foreach (var sql in new[]
        {
            "DELETE FROM public.supplier_invoices WHERE id = @id",
            "UPDATE public.supplier_invoice_items SET unit_price = 42 WHERE invoice_id = @id",
            "DELETE FROM public.supplier_invoice_items WHERE invoice_id = @id"
        })
        {
            await using var command = new NpgsqlCommand(sql, conn);
            command.Parameters.AddWithValue("id", invoice.Id);
            var error = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal(PostgresErrorCodes.CheckViolation, error.SqlState);
        }
    }

    private async Task<Guid> AddConvertedUnitAsync()
    {
        await using var db = OpenDb();
        var unit = new Unit { TenantId = TenantId, Code = "box-u01b", Name = "Hộp", Symbol = "hộp", DecimalPlaces = 0 };
        db.Units.Add(unit);
        db.SkuUnits.Add(new SkuUnit
        {
            TenantId = TenantId, UnitId = unit.Id, SkuId = _skuId, FactorToBase = 10m,
            CanPurchase = true, QuantityDecimalPlaces = 0
        });
        await db.SaveChangesAsync();
        return unit.Id;
    }

    private async Task<Guid> AddSupplierAsync()
    {
        await using var db = OpenDb();
        var supplier = new Supplier { TenantId = TenantId, Name = "Nhà cung cấp thứ hai" };
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return supplier.Id;
    }

    private async Task<GoodsReceiptResponseDto> DraftReceiptAsync(Guid? supplierId = null, Guid? unitId = null)
    {
        var po = await PurchaseOrderSeed.CreateApprovedPoViaApiAsync(_owner, _branchId, supplierId ?? _supplierId,
            (_skuId, 5m, 120m, unitId));
        return await ReadAsync<GoodsReceiptResponseDto>(await _owner.PostAsJsonAsync(Receipts, new CreateGoodsReceiptDto
        {
            PurchaseOrderId = po.Id,
            WarehouseId = _warehouseId,
            Items = [new() { PurchaseOrderItemId = Assert.Single(po.Items).Id, Quantity = 2m }]
        }), HttpStatusCode.Created);
    }

    private static CreateSupplierInvoiceDto Draft(GoodsReceiptResponseDto receipt, decimal price, decimal quantity = 2m,
        string number = "01", Guid? lineId = null) => new()
    {
        GoodsReceiptId = receipt.Id, SupplierId = receipt.SupplierId, InvoiceSeries = "AA", InvoiceNumber = number,
        InvoiceDate = new DateOnly(2026, 11, 12), VatAmount = 25m, ShippingFee = 10m,
        Items = [new(lineId ?? Assert.Single(receipt.Items).Id, quantity, price)]
    };

    private async Task<SupplierInvoiceResponseDto> CreateAsync(GoodsReceiptResponseDto receipt, decimal price, decimal quantity = 2m) =>
        await ReadAsync<SupplierInvoiceResponseDto>(await SendCreateAsync(Draft(receipt, price, quantity), Guid.NewGuid()),
            HttpStatusCode.Created);

    private async Task<HttpResponseMessage> SendCreateAsync(CreateSupplierInvoiceDto dto, Guid key, HttpClient? client = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Invoices) { Content = JsonContent.Create(dto) };
        request.Headers.Add("Idempotency-Key", key.ToString());
        return await (client ?? _owner).SendAsync(request);
    }

    private Task<HttpResponseMessage> ConfirmAsync(Guid id) => _owner.PostAsync($"{Receipts}/{id}/confirm", null);

    private Task<HttpResponseMessage> ActAsync(Guid id, string action, object? body = null, HttpClient? client = null) =>
        (client ?? _owner).PostAsync($"{Invoices}/{id}/{action}", body is null ? null : JsonContent.Create(body));

    private async Task<HttpClient> LoginAsync(string email)
    {
        var client = factory.CreateClient();
        var auth = await ReadAsync<AuthResponse>(await client.PostAsJsonAsync("/api/auth/login",
            new LoginRequest { Email = email, Password = PostgresTestEnvironment.AppPassword }));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    private async Task<StockSnapshot> StockAsync(GoodsReceiptResponseDto receipt)
    {
        await using var db = OpenDb();
        var lineId = Assert.Single(receipt.Items).Id;
        var line = await db.GoodsReceiptItems.SingleAsync(i => i.Id == lineId);
        var balance = await db.InventoryBalances.SingleAsync(b => b.BranchId == _branchId && b.SkuId == _skuId);
        var cost = await db.InventoryTransactions.Where(t => t.ReceiptItemId == lineId && t.Type == "IN")
            .Select(t => (decimal?)t.UnitCost)
            .SingleOrDefaultAsync();
        return new StockSnapshot(await db.InventoryTransactions.CountAsync(), balance.OnHand,
            balance.AverageCost, line.UnitPrice, cost);
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{(int)response.StatusCode} expected {(int)expected}. {body}");
        return Assert.IsType<T>(JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    private static async Task ProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var problem = await ReadAsync<JsonElement>(response, status);
        Assert.Equal((int)status, problem.GetProperty("status").GetInt32());
        Assert.Equal(code, problem.GetProperty("errorCode").GetString());
    }

    private static async Task<HttpResponseMessage[]> RaceAsync(params Func<Task<HttpResponseMessage>>[] operations)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tasks = operations.Select(async operation =>
        {
            await start.Task;
            return await operation();
        }).ToArray();
        start.SetResult();
        return await Task.WhenAll(tasks);
    }

    private static AppDbContext OpenDb() => PostgresTestEnvironment.CreateRuntimeDbContext(new TestTenant(TenantId));
    private sealed class TestTenant(Guid? tenantId = null) : ICurrentTenantService
    {
        public Guid? TenantId { get; private set; } = tenantId;
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }
    private sealed record StockSnapshot(int LedgerCount, decimal OnHand, decimal Wac, decimal ReceiptUnitPrice, decimal? LedgerUnitCost);
}
