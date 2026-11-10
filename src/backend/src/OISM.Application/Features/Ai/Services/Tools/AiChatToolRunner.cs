using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using OISM.Application.Common.Interfaces;
using OISM.Application.Features.Ai.Dtos;

namespace OISM.Application.Features.Ai.Services.Tools;

public sealed class AiChatToolRunner(
    IAppDbContext db,
    ICurrentTenantService tenant,
    IMemoryCache cache,
    IEnumerable<IAiChatTool> tools)
{
    public const string AllBranches = "Tất cả chi nhánh";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<string, IAiChatTool> _tools = tools.ToDictionary(tool => tool.Definition.Name, StringComparer.Ordinal);

    public IReadOnlyList<LlmTool> GetDefinitions(string role) =>
        CanUseTools(role) ? _tools.Values.Select(tool => tool.Definition).ToArray() : [];

    public async Task<string?> ValidateBranchAsync(Guid? branchId, CancellationToken ct)
    {
        if (!branchId.HasValue)
        {
            return null;
        }

        var tenantId = RequireTenant();
        return await db.Branches.AsNoTracking()
            .Where(branch => branch.TenantId == tenantId && branch.Id == branchId.Value)
            .Select(branch => branch.Name)
            .SingleOrDefaultAsync(ct)
            ?? throw AiChatToolInput.Invalid("Chi nhánh không hợp lệ.");
    }

    public async Task<(string Content, string? Scope, AiChatDateRange? DateRange, IReadOnlyList<AiChatBranchChoice>? Choices)> RunAsync(
        LlmToolCall call, string role, Guid? selectedBranchId, string? selectedBranchName,
        bool confirmBranchChoice, DateOnly today, CancellationToken ct)
    {
        if (!CanUseTools(role) || !_tools.TryGetValue(call.Name, out var tool))
        {
            return ("""{"error":"Công cụ không có sẵn cho yêu cầu này."}""", null, null, null);
        }

        var tenantId = RequireTenant();
        var input = AiChatToolInput.Parse(call.Arguments);
        var branchId = selectedBranchId;
        var scope = selectedBranchName ?? AllBranches;
        // An explicit choice resolves the original question, even if the model repeats the ambiguous name.
        var requestedName = confirmBranchChoice ? null : AiChatToolInput.BranchName(input);
        if (!string.IsNullOrWhiteSpace(requestedName))
        {
            var branches = await db.Branches.AsNoTracking()
                .Where(branch => branch.TenantId == tenantId && branch.IsActive)
                .OrderBy(branch => branch.Name).ThenBy(branch => branch.Id)
                .Select(branch => new AiChatBranchChoice(branch.Id, branch.Name))
                .ToListAsync(ct);
            var name = NormalizeName(requestedName);
            var candidates = branches.Select(branch => (Branch: branch, Name: NormalizeName(branch.Name))).ToArray();
            var matches = candidates.Where(candidate => candidate.Name == name).ToArray();
            if (matches.Length == 0 && name.Length > 0)
            {
                matches = candidates.Where(candidate => candidate.Name.Contains(name, StringComparison.Ordinal)).ToArray();
            }

            if (matches.Length != 1)
            {
                var choices = (matches.Length == 0 ? branches : matches.Select(match => match.Branch)).Take(10).ToArray();
                var content = JsonSerializer.Serialize(new { NeedBranchChoice = true, Choices = choices.Select(choice => choice.Name) }, JsonOptions);
                return (content, null, null, choices);
            }

            branchId = matches[0].Branch.Id;
            scope = matches[0].Branch.Name;
        }

        var arguments = tool.Normalize(input, branchId, today);
        // Chỉ cache kỳ đã kết thúc. Kỳ có hôm nay và số liệu tồn hiện tại đổi ngay khi bán hàng hoặc nhập kho.
        var cacheable = arguments.DateRange is { } range && range.To < today;
        var key = new ToolCacheKey(tenantId, role, call.Name, arguments);
        AiChatToolResult? result = null;
        if (!cacheable || !cache.TryGetValue(key, out result) || result is null)
        {
            result = await tool.ExecuteAsync(arguments, role, ct);
            if (cacheable)
            {
                cache.Set(key, result, TimeSpan.FromMinutes(5));
            }
        }

        var payload = JsonSerializer.Serialize(new { Branch = scope, result.DateRange, result.Data }, JsonOptions);
        return (payload, scope, result.DateRange, null);
    }

    private static string NormalizeName(string name)
    {
        var characters = name.ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Select(character => character == 'đ' ? 'd' : character);
        return string.Join(' ', new string(characters.ToArray()).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private Guid RequireTenant() => tenant.TenantId is { } tenantId && tenantId != Guid.Empty
        ? tenantId
        : throw AiChatToolInput.Invalid("Không xác định được cửa hàng hiện tại.");

    private static bool CanUseTools(string role) => role is "Owner" or "Staff";

    private sealed record ToolCacheKey(Guid TenantId, string Role, string Tool, AiChatToolArguments Arguments);
}
