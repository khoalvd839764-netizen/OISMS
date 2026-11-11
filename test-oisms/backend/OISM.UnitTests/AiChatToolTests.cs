using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Ai.Dtos;
using OISM.Application.Features.Ai.Services;
using OISM.Application.Features.Ai.Services.Tools;
using OISM.Application.Features.Reports.Dtos;
using OISM.Application.Features.Reports.Services;
using OISM.Domain.Entities;
using OISM.Infrastructure.Ai;

namespace OISM.UnitTests;

public sealed class AiChatToolTests
{
    private static readonly AiChatRequest ChatRequest = new([new("user", "Tồn kho ra sao?")]);
    private const string LowStock = "get_low_stock";
    private const string Reorder = "get_reorder_suggestions";

    [Theory]
    [InlineData("Staff", "unknown_tool")]
    [InlineData("Cashier", LowStock)]
    public async Task Chat_ReturnsErrorToModelWithoutRunningUnknownOrUnauthorizedTool(string role, string tool)
    {
        await using var fixture = new Fixture();
        fixture.Llm.SetupSequence(client => client.ChatAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolCall(tool))
            .ReturnsAsync(Reply());

        var response = await fixture.Service.ChatAsync(ChatRequest, role);

        Assert.Empty(response.UsedTools);
        Assert.Contains("error", fixture.Payloads.Last().Messages[^1].Content);
        Assert.Equal("call-1", fixture.Payloads.Last().Messages[^1].ToolCallId);
        if (role == "Cashier")
        {
            Assert.Empty(fixture.Payloads.First().Tools);
        }
        fixture.Reports.VerifyNoOtherCalls();
        fixture.Forecasting.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Chat_StopsAfterThreeToolRoundsAndKeepsCurrentText()
    {
        await using var fixture = new Fixture();
        fixture.Llm.SetupSequence(client => client.ChatAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ToolCall("unknown_tool"))
            .ReturnsAsync(ToolCall("unknown_tool"))
            .ReturnsAsync(ToolCall("unknown_tool"))
            .ReturnsAsync(ToolCall("unknown_tool") with { Text = "Thông tin hiện có." });

        var response = await fixture.Service.ChatAsync(ChatRequest, "Owner");

        Assert.Equal("Thông tin hiện có.", response.Reply);
        Assert.Equal(4, fixture.Payloads.Count);
        Assert.Equal(3, fixture.Payloads.Last().Messages.Count(message => message.Role == "tool"));
        Assert.Empty(response.UsedTools);
    }

    [Fact]
    public async Task Chat_SendsOnlyAllowedStockAlertFields()
    {
        await using var fixture = new Fixture();
        fixture.Reports.Setup(reports => reports.GetStockAlertsAsync(null, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new StockAlertDto
            {
                SkuCode = "SKU-1", SkuName = "Sản phẩm", BranchName = "Chi nhánh A",
                Available = 1, ReorderPoint = 5, AlertLevel = "Critical"
            }]);
        fixture.CallThenReply(LowStock);

        var response = await fixture.Service.ChatAsync(ChatRequest, "Staff");

        Assert.Equal(LowStock, Assert.Single(response.UsedTools));
        Assert.Equal("Tất cả chi nhánh", response.Scope);
        Assert.Equal(2, fixture.Payloads[0].Tools.Count);
        Assert.All(fixture.Payloads[0].Tools, tool => Assert.Equal("object", tool.Parameters.GetProperty("type").GetString()));
        var payload = fixture.Payloads.Last().Messages[^1].Content!;
        Assert.DoesNotContain("cost", payload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("giá vốn", payload, StringComparison.OrdinalIgnoreCase);
        using var document = JsonDocument.Parse(payload);
        var row = document.RootElement.GetProperty("data")[0];
        Assert.Equal(6, row.EnumerateObject().Count());
        Assert.Equal("SKU-1", row.GetProperty("skuCode").GetString());
        Assert.Equal("Chi nhánh A", row.GetProperty("branchName").GetString());
        Assert.Equal(5, row.GetProperty("reorderPoint").GetInt32());
    }

    [Fact]
    public async Task Chat_SendsFirstTwentyRecommendationsOrderedByStockout()
    {
        await using var fixture = new Fixture();
        var recommendations = Enumerable.Range(1, 23).Reverse().Select(day => new ReorderRecommendationDto
        {
            SkuCode = $"SKU-{day}", DaysUntilStockout = day, NeedsReorder = true,
            SuggestedQuantity = 9, DailySalesVelocity = 2, BaseUnit = "cái",
            ConfidenceLevel = "High", Reason = "Tồn thấp"
        }).ToList();
        fixture.Forecasting.Setup(service => service.GetReorderRecommendationsAsync(
                It.Is<ReorderRecommendationQueryDto>(query => query.OnlyNeedsReorder && query.LeadTimeDays == 4),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReorderRecommendationSummaryDto { Recommendations = recommendations });
        fixture.CallThenReply(Reorder, """{"leadTimeDays":4}""");

        var response = await fixture.Service.ChatAsync(ChatRequest, "Staff");

        Assert.Equal(Reorder, Assert.Single(response.UsedTools));
        using var document = JsonDocument.Parse(fixture.Payloads.Last().Messages[^1].Content!);
        var rows = document.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(Enumerable.Range(1, 20), rows.Select(row => row.GetProperty("daysUntilStockout").GetInt32()));
        Assert.Equal(9, rows[0].GetProperty("suggestedQuantity").GetInt32());
        Assert.Equal(2, rows[0].GetProperty("dailySalesVelocity").GetInt32());
        Assert.Equal("cái", rows[0].GetProperty("baseUnit").GetString());
        Assert.Equal("High", rows[0].GetProperty("confidenceLevel").GetString());
        Assert.Equal("Tồn thấp", rows[0].GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Chat_TodayRevenueIsReadAgainAfterNewSale()
    {
        // Lỗi người dùng báo 10/11/2026: hỏi doanh thu, bán một đơn, hỏi lại vẫn nhận số cũ do cache 5 phút.
        await using var fixture = new Fixture(includeRevenue: true);
        fixture.Reports.SetupSequence(service => service.GetRevenueProfitReportAsync(
                It.IsAny<ReportFilterDto>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RevenueProfitReportDto { NetRevenue = 0, TotalOrders = 0 })
            .ReturnsAsync(new RevenueProfitReportDto { NetRevenue = 2_093_000, TotalOrders = 1 });

        fixture.CallThenReply("get_revenue");
        await fixture.Service.ChatAsync(ChatRequest, "Owner");
        fixture.CallThenReply("get_revenue");
        await fixture.Service.ChatAsync(ChatRequest, "Owner");

        using var payload = JsonDocument.Parse(fixture.Payloads.Last().Messages[^1].Content!);
        Assert.Equal(2_093_000, payload.RootElement.GetProperty("data").GetProperty("netRevenue").GetInt32());
        Assert.Equal(1, payload.RootElement.GetProperty("data").GetProperty("orderCount").GetInt32());
    }

    [Fact]
    public async Task Chat_AlwaysReadsCurrentStockStateAgain()
    {
        await using var fixture = new Fixture();
        fixture.Forecasting.Setup(service => service.GetReorderRecommendationsAsync(
                It.Is<ReorderRecommendationQueryDto>(query => query.OnlyNeedsReorder && query.LeadTimeDays == 7),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReorderRecommendationSummaryDto());
        fixture.CallThenReply(Reorder);
        await fixture.Service.ChatAsync(ChatRequest, "Owner");
        fixture.CallThenReply(Reorder, """{"leadTimeDays":7}""");
        await fixture.Service.ChatAsync(ChatRequest, "Owner");

        fixture.Forecasting.Verify(service => service.GetReorderRecommendationsAsync(
            It.IsAny<ReorderRecommendationQueryDto>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Chat_CachesClosedPeriodOnlyWithinTheSameTenant()
    {
        await using var fixture = new Fixture(includeRevenue: true);
        fixture.Reports.Setup(service => service.GetRevenueProfitReportAsync(
                It.IsAny<ReportFilterDto>(), true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RevenueProfitReportDto());
        const string closedPeriod = """{"from":"2026-11-01","to":"2026-11-05"}""";
        fixture.CallThenReply("get_revenue", closedPeriod);
        await fixture.Service.ChatAsync(ChatRequest, "Owner");
        fixture.CallThenReply("get_revenue", closedPeriod);
        await fixture.Service.ChatAsync(ChatRequest, "Owner");
        fixture.Reports.Verify(service => service.GetRevenueProfitReportAsync(
            It.IsAny<ReportFilterDto>(), true, It.IsAny<CancellationToken>()), Times.Once);

        fixture.Tenant.SetupGet(tenant => tenant.TenantId).Returns(Guid.NewGuid());
        fixture.CallThenReply("get_revenue", closedPeriod);
        await fixture.Service.ChatAsync(ChatRequest, "Owner");
        fixture.Reports.Verify(service => service.GetRevenueProfitReportAsync(
            It.IsAny<ReportFilterDto>(), true, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Chat_RejectsPanelIdFromAnotherTenantBeforeCallingModel()
    {
        await using var fixture = new Fixture();
        var otherTenant = Guid.NewGuid();
        fixture.Tenant.SetupGet(tenant => tenant.TenantId).Returns(otherTenant);
        var otherBranch = await fixture.AddBranchAsync("Chi nhánh bí mật", tenantId: otherTenant);
        fixture.Tenant.SetupGet(tenant => tenant.TenantId).Returns(fixture.TenantId);

        var request = ChatRequest with { BranchId = otherBranch.Id, ConfirmBranchChoice = true };
        var error = await Assert.ThrowsAsync<OismException>(() => fixture.Service.ChatAsync(request, "Owner"));

        Assert.Equal("AI_INVALID_REQUEST", error.ErrorCode);
        Assert.Equal(400, error.StatusCode);
        Assert.DoesNotContain(otherBranch.Name, error.Message);
        Assert.Empty(fixture.Payloads);
        fixture.Reports.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("Quận 1")]
    [InlineData("  qUAN   1  ")]
    [InlineData("quan 10")]
    [InlineData("  dONG    da ")]
    public async Task Chat_UsesUniqueNameAheadOfPanelScope(string name)
    {
        await using var fixture = new Fixture(new StockMovementChatTool(Mock.Of<IStockMovementQuery>()), includeRevenue: true);
        var districtOne = await fixture.AddBranchAsync("Quận 1");
        var districtTen = await fixture.AddBranchAsync("Kho Quận 10");
        var dongDa = await fixture.AddBranchAsync("Đống Đa");
        var panel = await fixture.AddBranchAsync("Chi nhánh đang chọn");
        var expected = name == "quan 10" ? districtTen : name.Contains("dONG") ? dongDa : districtOne;
        fixture.Reports.Setup(service => service.GetStockAlertsAsync(expected.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        fixture.CallThenReply(LowStock, JsonSerializer.Serialize(new { branchName = name }));

        var response = await fixture.Service.ChatAsync(ChatRequest with { BranchId = panel.Id }, "Staff");

        Assert.Equal(expected.Name, response.Scope);
        Assert.Equal(LowStock, Assert.Single(response.UsedTools));
        Assert.Empty(response.BranchChoices);
        Assert.Equal(3, fixture.Payloads[0].Tools.Count);
        Assert.DoesNotContain(fixture.Payloads[0].Tools, tool => tool.Name == "get_revenue");
        Assert.All(fixture.Payloads[0].Tools, tool =>
        {
            var properties = tool.Parameters.GetProperty("properties");
            Assert.False(properties.TryGetProperty("branchId", out _));
            Assert.Equal("string", properties.GetProperty("branchName").GetProperty("type").GetString());
        });
        Assert.DoesNotContain(expected.Id.ToString(), fixture.Payloads.Last().Messages[^1].Content);
        fixture.Reports.VerifyAll();
    }

    [Theory]
    [InlineData("Q.1", false)]
    [InlineData("quận", true)]
    [InlineData("chi nhánh bí mật", false)]
    [InlineData("chi nhánh đã đóng", false)]
    public async Task Chat_OffersOnlyActiveTenantBranchesWithoutExecutingTool(string name, bool ambiguous)
    {
        await using var fixture = new Fixture();
        var first = await fixture.AddBranchAsync("Quận 1");
        var second = await fixture.AddBranchAsync("Quận 2");
        var other = await fixture.AddBranchAsync("Tân Bình");
        await fixture.AddBranchAsync("Chi nhánh đã đóng", active: false);
        await fixture.AddBranchAsync("Quận 3", active: false);
        var otherTenant = Guid.NewGuid();
        fixture.Tenant.SetupGet(tenant => tenant.TenantId).Returns(otherTenant);
        await fixture.AddBranchAsync("Chi nhánh bí mật", tenantId: otherTenant);
        await fixture.AddBranchAsync("Quận 4", tenantId: otherTenant);
        fixture.Tenant.SetupGet(tenant => tenant.TenantId).Returns(fixture.TenantId);

        fixture.CallThenReply(LowStock, JsonSerializer.Serialize(new { branchName = name }));
        var response = await fixture.Service.ChatAsync(ChatRequest with { BranchId = other.Id }, "Staff");

        Assert.Empty(response.UsedTools);
        Assert.Null(response.Scope);
        var expected = ambiguous ? new[] { first, second } : new[] { first, second, other };
        Assert.Equal(expected.Select(branch => new AiChatBranchChoice(branch.Id, branch.Name)), response.BranchChoices);
        var content = fixture.Payloads.Last().Messages[^1].Content!;
        using var data = JsonDocument.Parse(content);
        Assert.True(data.RootElement.GetProperty("needBranchChoice").GetBoolean());
        Assert.Equal(expected.Select(branch => branch.Name), data.RootElement.GetProperty("choices").EnumerateArray().Select(item => item.GetString()));
        Assert.DoesNotContain(first.Id.ToString(), content);
        fixture.Reports.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Chat_EquivalentNamesAndConfirmedChoiceResolveSameBranch()
    {
        await using var fixture = new Fixture();
        var branch = await fixture.AddBranchAsync("Quận 1");
        fixture.Reports.Setup(service => service.GetStockAlertsAsync(branch.Id, It.IsAny<CancellationToken>())).ReturnsAsync([]);
        fixture.CallThenReply(LowStock, """{"branchName":"quan 1"}""");
        var byName = await fixture.Service.ChatAsync(ChatRequest, "Owner");
        fixture.CallThenReply(LowStock, """{"branchName":"Q.1"}""");
        var byChoice = await fixture.Service.ChatAsync(ChatRequest with { BranchId = branch.Id, ConfirmBranchChoice = true }, "Owner");

        Assert.Equal("Quận 1", byName.Scope);
        Assert.Equal(byName.Scope, byChoice.Scope);
        Assert.Empty(byChoice.BranchChoices);
        fixture.Reports.Verify(service => service.GetStockAlertsAsync(branch.Id, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Chat_StockMovementDefaultsToTodayInVietnamAndReturnsActualDates()
    {
        var query = new Mock<IStockMovementQuery>(MockBehavior.Strict);
        var today = new DateOnly(2026, 11, 10);
        var range = new AiChatDateRange(today, today);
        query.Setup(service => service.GetAsync(range, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StockMovementResult([], [], []));
        await using var fixture = new Fixture(new StockMovementChatTool(query.Object));
        fixture.CallThenReply("get_stock_movement");

        var response = await fixture.Service.ChatAsync(ChatRequest, "Owner");

        Assert.Equal(range, response.DateRange);
        Assert.Equal("get_stock_movement", Assert.Single(response.UsedTools));
        Assert.Contains("2026-11-10", fixture.Payloads[0].SystemPrompt);
        Assert.Contains("2026-11-10", fixture.Payloads[1].Messages[^1].Content);
        query.VerifyAll();
    }

    [Theory]
    [InlineData("""{"from":"2026-11-10","to":"2026-11-09"}""")]
    [InlineData("""{"from":"2026-01-01","to":"2026-04-03"}""")]
    [InlineData("""{"from":"10/11/2026"}""")]
    public async Task Chat_RejectsInvalidStockMovementDates(string arguments)
    {
        var query = new Mock<IStockMovementQuery>(MockBehavior.Strict);
        await using var fixture = new Fixture(new StockMovementChatTool(query.Object));
        fixture.CallThenReply("get_stock_movement", arguments);

        var error = await Assert.ThrowsAsync<OismException>(() => fixture.Service.ChatAsync(ChatRequest, "Owner"));

        Assert.Equal("AI_INVALID_REQUEST", error.ErrorCode);
        Assert.Equal(400, error.StatusCode);
        query.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Chat_OwnerGetsRevenueWithProfit_StaffCannotRunRevenueTool()
    {
        await using var fixture = new Fixture(includeRevenue: true);
        var expectedStart = new DateTimeOffset(2026, 11, 3, 17, 0, 0, TimeSpan.Zero);
        var expectedEnd = new DateTimeOffset(2026, 11, 10, 17, 0, 0, TimeSpan.Zero);
        var report = new RevenueProfitReportDto
        {
            Revenue = 1000, NetRevenue = 800, GrossProfit = 500, NetCogs = 300, ReturnCogs = 20, TotalOrders = 7,
            ByChannel = [new ChannelRevenueBreakdownDto { Channel = "POS", NetRevenue = 800, GrossProfit = 500, CostOfGoodsSold = 320 }],
            DailyTrend = [new DailyRevenueBreakdownDto { Date = "2026-11-10", NetRevenue = 800, NetCogs = 300 }]
        };
        fixture.Reports.Setup(service => service.GetRevenueProfitReportAsync(
                It.Is<ReportFilterDto>(filter => filter.StartDate == expectedStart && filter.EndDate == expectedEnd),
                It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(report);
        const string arguments = """{"from":"2026-11-04","to":"2026-11-10"}""";
        fixture.CallThenReply("get_revenue", arguments);
        await fixture.Service.ChatAsync(ChatRequest, "Owner");
        var ownerPayload = fixture.Payloads.Last().Messages[^1].Content!;
        using var owner = JsonDocument.Parse(ownerPayload);
        Assert.Equal(500, owner.RootElement.GetProperty("data").GetProperty("grossProfit").GetInt32());

        Assert.Equal(800, owner.RootElement.GetProperty("data").GetProperty("netRevenue").GetInt32());
        Assert.Contains(fixture.Payloads.Last().Tools, tool => tool.Name == "get_revenue");

        // Người dùng chốt 10/11/2026: Staff không được hỏi doanh thu qua trợ lý.
        fixture.CallThenReply("get_revenue", arguments);
        var response = await fixture.Service.ChatAsync(ChatRequest, "Staff");
        var staffRequest = fixture.Payloads.Last();
        var staffPayload = staffRequest.Messages[^1].Content!;
        Assert.DoesNotContain(staffRequest.Tools, tool => tool.Name == "get_revenue");
        Assert.Contains("không có sẵn", staffPayload);
        Assert.DoesNotContain("netRevenue", staffPayload, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("800", staffPayload);
        Assert.Empty(response.UsedTools);
        Assert.Null(response.DateRange);
        fixture.Reports.Verify(service => service.GetRevenueProfitReportAsync(
            It.IsAny<ReportFilterDto>(), true, It.IsAny<CancellationToken>()), Times.Once);
        fixture.Reports.Verify(service => service.GetRevenueProfitReportAsync(
            It.IsAny<ReportFilterDto>(), false, It.IsAny<CancellationToken>()), Times.Never);
    }

    private static LlmResponse ToolCall(string name, string arguments = "{}") =>
        new(null, [new LlmToolCall("call-1", name, arguments)], "tool_calls", null);

    private static LlmResponse Reply() => new("Câu trả lời.", [], "stop", null);

    private sealed class Fixture : IAsyncDisposable
    {
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        public OISM.Infrastructure.Persistence.AppDbContext Db { get; }
        public Mock<ICurrentTenantService> Tenant { get; }
        public Guid TenantId { get; }
        public Mock<IReportService> Reports { get; } = new(MockBehavior.Strict);
        public Mock<IAiForecastingService> Forecasting { get; } = new(MockBehavior.Strict);
        public Mock<ILlmClient> Llm { get; } = new(MockBehavior.Strict);
        public List<LlmRequest> Payloads => Llm.Invocations.Select(invocation => (LlmRequest)invocation.Arguments[0]!).ToList();
        public AiChatService Service { get; }

        public Fixture(IAiChatTool? additionalTool = null, bool includeRevenue = false)
        {
            (Db, Tenant, TenantId) = TestDbContextFactory.CreateInMemoryContext();
            var tools = new List<IAiChatTool> { new LowStockChatTool(Reports.Object), new ReorderSuggestionsChatTool(Forecasting.Object) };
            if (additionalTool is not null)
            {
                tools.Add(additionalTool);
            }
            if (includeRevenue)
            {
                tools.Add(new RevenueChatTool(Reports.Object));
            }
            var runner = new AiChatToolRunner(Db, Tenant.Object, _cache, tools);
            Service = new AiChatService(Llm.Object, new KnowledgeBase(), Options.Create(new AiChatLimits()), runner, new FixedClock());
        }

        public async Task<Branch> AddBranchAsync(string name, bool active = true, Guid? tenantId = null)
        {
            var branch = new Branch { Id = Guid.NewGuid(), TenantId = tenantId ?? TenantId, Name = name, IsActive = active };
            Db.Branches.Add(branch);
            await Db.SaveChangesAsync();
            return branch;
        }

        public void CallThenReply(string name, string arguments = "{}") =>
            Llm.SetupSequence(client => client.ChatAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(ToolCall(name, arguments))
                .ReturnsAsync(Reply());

        public async ValueTask DisposeAsync()
        {
            _cache.Dispose();
            await Db.DisposeAsync();
        }
    }

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 11, 9, 18, 0, 0, TimeSpan.Zero);
    }
}
