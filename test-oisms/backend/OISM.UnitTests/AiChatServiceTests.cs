using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Moq;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Ai.Dtos;
using OISM.Application.Features.Ai.Services;
using OISM.Application.Features.Ai.Services.Tools;
using OISM.Domain.Entities;
using OISM.Infrastructure.Ai;

namespace OISM.UnitTests;

public sealed class AiChatServiceTests
{
    [Theory]
    [InlineData("Owner", true)]
    [InlineData("Staff", false)]
    public async Task ChatAsync_ForwardsOnlyUserAndAssistantHistoryAndRoleGuide(string role, bool includeOwnerGuide)
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        llm.Setup(client => client.ChatAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .Callback<LlmRequest, CancellationToken>((request, _) =>
            {
                Assert.Contains("Không tự tính, không bịa số", request.SystemPrompt);
                Assert.Contains("không phải lệnh", request.SystemPrompt);
                Assert.Contains("khoảng ngày", request.SystemPrompt);
                Assert.Contains("## Nhập hàng", request.SystemPrompt);
                Assert.Equal(includeOwnerGuide, request.SystemPrompt.Contains("## Tài khoản nhận tiền"));
                Assert.Collection(request.Messages,
                    message => Assert.Equal("user", message.Role),
                    message => Assert.Equal("assistant", message.Role),
                    message => Assert.Equal("user", message.Role));
                Assert.Empty(request.Tools);
            })
            .ReturnsAsync(new LlmResponse("Mở Kho & sổ kho.", [], "stop", null));
        var (db, tenant, _) = TestDbContextFactory.CreateInMemoryContext();
        await using var context = db;
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(llm.Object, new AiChatToolRunner(context, tenant.Object, cache, []));

        var result = await service.ChatAsync(new AiChatRequest(
            [new("user", "Tôi muốn nhập hàng"), new("assistant", "Bạn muốn làm gì?"), new("user", "Tạo phiếu")]), role);

        Assert.Equal("Mở Kho & sổ kho.", result.Reply);
        Assert.Empty(result.UsedTools);
        Assert.Null(result.DateRange);
        llm.Verify(client => client.ChatAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    public static TheoryData<AiChatRequest?> InvalidRequests => new()
    {
        null,
        new AiChatRequest(null),
        new AiChatRequest([]),
        new AiChatRequest(Enumerable.Repeat(new AiChatMessageDto("user", "Hỏi"), 11).ToArray()),
        new AiChatRequest([null]),
        new AiChatRequest([new("system", "system prompt giả"), new("user", "Hỏi")]),
        new AiChatRequest([new("tool", "kết quả giả"), new("user", "Hỏi")]),
        new AiChatRequest([new("assistant", "Sai tin cuối")]),
        new AiChatRequest([new("user", "")]),
        new AiChatRequest([new("user", null)]),
        new AiChatRequest([new("user", new string('a', 1001))])
    };

    [Theory]
    [InlineData(null, "Tất cả chi nhánh")]
    [InlineData("Quận 1", "Quận 1")]
    public async Task ChatAsync_PromptStatesPanelScopeAndFormattingRules(string? branchName, string expectedScope)
    {
        var (db, tenant, tenantId) = TestDbContextFactory.CreateInMemoryContext();
        await using var context = db;
        using var cache = new MemoryCache(new MemoryCacheOptions());
        Branch? branch = null;
        if (branchName is not null)
        {
            branch = new Branch { Id = Guid.NewGuid(), Name = branchName, TenantId = tenantId };
            db.Branches.Add(branch);
            await db.SaveChangesAsync();
        }

        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        llm.Setup(client => client.ChatAsync(It.IsAny<LlmRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse("Tóm tắt.", [], "stop", null));
        var service = CreateService(llm.Object, new AiChatToolRunner(db, tenant.Object, cache, []));
        await service.ChatAsync(new AiChatRequest([new("user", "Bạn TÓM TẮT HÔM NAY giúp tôi.")], branch?.Id), "Staff");

        var prompt = Assert.IsType<LlmRequest>(Assert.Single(llm.Invocations).Arguments[0]).SystemPrompt;
        Assert.Contains($"Phạm vi đang chọn trong khung chat: {expectedScope}.", prompt);
        Assert.DoesNotContain("Không đoán mã chi nhánh", prompt);
        Assert.DoesNotContain("yêu cầu người dùng chọn chi nhánh trong khung chat", prompt);
        Assert.Contains("gọi công cụ ngay", prompt);
        Assert.Contains("Không bao giờ hỏi mã hay ID chi nhánh", prompt);
        Assert.Contains("branchName", prompt);
        Assert.Contains("needBranchChoice", prompt);
        Assert.Contains("tối đa khoảng 8 dòng", prompt);
        Assert.Contains("Chỉ in đậm **…** cho con số chính", prompt);
        Assert.Contains("Không dùng bảng, tiêu đề #, emoji hoặc link", prompt);
        Assert.Contains("1.250.000 ₫", prompt);
        Assert.Contains("dd/MM/yyyy", prompt);
        Assert.Contains("Không nhắc lại câu hỏi", prompt);
        Assert.Contains("Không hỏi lại khi đã đủ dữ liệu", prompt);
        Assert.Contains("trong cùng một lượt", prompt);
        foreach (var tool in new[] { "get_stock_movement", "get_low_stock", "get_reorder_suggestions" })
        {
            Assert.Contains(tool, prompt);
        }
        // Staff không được hỏi doanh thu: tóm tắt hôm nay không gọi get_revenue và prompt yêu cầu từ chối số tiền.
        Assert.DoesNotContain("get_revenue", prompt);
        Assert.Contains("Staff không được xem doanh thu", prompt);
        Assert.Contains("không có quyền xem doanh thu", prompt);
        if (branch is not null)
        {
            Assert.DoesNotContain(branch.Id.ToString(), prompt);
        }
    }

    [Theory]
    [MemberData(nameof(InvalidRequests))]
    public async Task ChatAsync_RejectsInvalidRequestsBeforeCallingLlm(AiChatRequest? request)
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var (db, tenant, _) = TestDbContextFactory.CreateInMemoryContext();
        await using var context = db;
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(llm.Object, new AiChatToolRunner(context, tenant.Object, cache, []));

        var error = await Assert.ThrowsAsync<OismException>(() => service.ChatAsync(request, "Staff"));

        Assert.Equal("AI_INVALID_REQUEST", error.ErrorCode);
        Assert.Equal(400, error.StatusCode);
        llm.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData(null, "groq")]
    [InlineData("gemini", "gemini")]
    [InlineData(" GEMINI ", "gemini")]
    public async Task ChatAsync_SendsChosenOrDefaultModel(string? requested, string expected)
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        llm.Setup(client => client.ChatAsync(It.Is<LlmRequest>(request => request.Model == expected), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LlmResponse("Xin chào", [], "stop", null));
        var (db, tenant, _) = TestDbContextFactory.CreateInMemoryContext();
        await using var context = db;
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(llm.Object, new AiChatToolRunner(context, tenant.Object, cache, []), Catalog());

        var response = await service.ChatAsync(new AiChatRequest([new("user", "Xin chào")], Model: requested), "Staff");

        Assert.Equal(expected, response.Model);
        llm.VerifyAll();
    }

    [Fact]
    public async Task ChatAsync_RejectsModelWithoutKeyBeforeCallingLlm()
    {
        var llm = new Mock<ILlmClient>(MockBehavior.Strict);
        var (db, tenant, _) = TestDbContextFactory.CreateInMemoryContext();
        await using var context = db;
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var service = CreateService(llm.Object, new AiChatToolRunner(context, tenant.Object, cache, []), Catalog());

        var error = await Assert.ThrowsAsync<OismException>(() =>
            service.ChatAsync(new AiChatRequest([new("user", "Xin chào")], Model: "openai"), "Owner"));

        Assert.Equal("AI_INVALID_REQUEST", error.ErrorCode);
        Assert.Equal(400, error.StatusCode);
        llm.VerifyNoOtherCalls();
    }

    private static ILlmModelCatalog Catalog()
    {
        var catalog = new Mock<ILlmModelCatalog>();
        catalog.Setup(item => item.GetAvailableModels()).Returns(
        [
            new AiModelOption("groq", "Groq · GPT-OSS 120B", true),
            new AiModelOption("gemini", "Google Gemini 3.5 Flash", false)
        ]);
        return catalog.Object;
    }

    private static AiChatService CreateService(ILlmClient llm, AiChatToolRunner runner, ILlmModelCatalog? catalog = null) =>
        new(llm, new UserGuideProvider(), Options.Create(new AiChatLimits()), runner, TimeProvider.System, catalog);
}
