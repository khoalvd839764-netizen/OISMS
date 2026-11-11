using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using OISM.Infrastructure.Ai;
using Xunit;
using Xunit.Abstractions;

namespace OISM.UnitTests;

public sealed class KnowledgeBaseTests
{
    private readonly ITestOutputHelper _output;

    public KnowledgeBaseTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Search_NhapHangTheNao_ReturnsNhapKhoDocument()
    {
        var kb = new KnowledgeBase();
        var results = kb.Search("nhap hang the nao", "Owner", top: 4);

        results.Should().NotBeEmpty();
        results[0].Id.Should().Be("man-hinh-nhap-kho-nha-cung-cap");
    }

    [Fact]
    public void Search_StaffRole_NeverReceivesOwnerOnlyDocument()
    {
        var kb = new KnowledgeBase();
        var ownerDocList = LoadOwnerOnlyDocuments();

        Assert.True(ownerDocList.Count >= 10);

        foreach (var (id, title) in ownerDocList)
        {
            var results = kb.Search(title, "Staff", top: 10);
            results.Should().NotContain(r => r.Id == id,
                $"Staff must never receive Owner-only document '{id}' when searching '{title}'.");
        }
    }

    [Theory]
    [InlineData("thời tiết hôm nay")]
    [InlineData("bầu cử")]
    [InlineData("kết quả bầu cử tổng thống mỹ")]
    public void Search_OutOfScope_ReturnsZeroDocuments(string query)
    {
        var kb = new KnowledgeBase();
        var results = kb.Search(query, "Owner", top: 4);
        results.Should().BeEmpty();
    }

    [Fact]
    public void Search_Cashier_ReturnsZeroDocuments()
    {
        var kb = new KnowledgeBase();
        var results = kb.Search("nhap hang the nao", "Cashier", top: 4);
        results.Should().BeEmpty();
    }

    [Fact]
    public void Search_RespectsTopLimit()
    {
        var kb = new KnowledgeBase();
        var results = kb.Search("quản lý sản phẩm đơn hàng", "Owner", top: 2);
        results.Count.Should().BeInRange(0, 2);
    }

    public sealed record EvalQuestion(int Id, string Question, string Role, List<string> ExpectedIds);

    [Fact]
    public void Evaluate_RetrievalSuite_MeetsThresholds()
    {
        var basePath = AppContext.BaseDirectory;
        var directPath = Path.Combine(basePath, "Ai", "knowledge-questions.json");
        var fallbackPath = Path.Combine(basePath, "..", "..", "..", "Ai", "knowledge-questions.json");
        var path = File.Exists(directPath) ? directPath : fallbackPath;

        File.Exists(path).Should().BeTrue($"Question suite file must exist at '{path}'.");

        var json = File.ReadAllText(path);
        var questions = JsonSerializer.Deserialize<List<EvalQuestion>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        var kb = new KnowledgeBase();
        var inScope = questions.Where(q => q.ExpectedIds.Count > 0).ToList();
        var outOfScope = questions.Where(q => q.ExpectedIds.Count == 0).ToList();

        Assert.True(inScope.Count >= 30);
        Assert.Equal(5, outOfScope.Count);

        int top3Hits = 0;
        foreach (var q in inScope)
        {
            var results = kb.Search(q.Question, q.Role, top: 3);
            var hit = results.Any(r => q.ExpectedIds.Contains(r.Id));
            if (hit)
            {
                top3Hits++;
            }
            else
            {
                var topId = results.Count > 0 ? results[0].Id : "NONE";
                _output.WriteLine($"MISS: #{q.Id} '{q.Question}' -> Top: {topId}, Expected: {string.Join(", ", q.ExpectedIds)}");
            }
        }

        int outOfScopePass = 0;
        foreach (var q in outOfScope)
        {
            var results = kb.Search(q.Question, q.Role, top: 3);
            if (results.Count == 0)
            {
                outOfScopePass++;
            }
            else
            {
                _output.WriteLine($"OOS FAIL: #{q.Id} '{q.Question}' returned {results.Count} items (top: {results[0].Id})");
            }
        }

        double hitRate = (double)top3Hits / inScope.Count;
        _output.WriteLine($"=== KNOWLEDGE RETRIEVAL EVALUATION ===");
        _output.WriteLine($"MinScoreThreshold: {KnowledgeBase.MinScoreThreshold}");
        _output.WriteLine($"In-scope questions: {inScope.Count}");
        _output.WriteLine($"Top-3 hit count: {top3Hits}/{inScope.Count} ({hitRate:P1})");
        _output.WriteLine($"Out-of-scope questions passed: {outOfScopePass}/{outOfScope.Count}");

        Assert.True(hitRate >= 0.90, $"Top-3 hit rate must be >= 90%, actual: {hitRate:P1}");
        Assert.Equal(outOfScope.Count, outOfScopePass);
    }

    private static List<(string Id, string Title)> LoadOwnerOnlyDocuments()
    {
        var assembly = typeof(KnowledgeBase).Assembly;
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith("OISM.Infrastructure.Ai.Knowledge.", StringComparison.Ordinal) && name.EndsWith(".md", StringComparison.Ordinal));

        var list = new List<(string Id, string Title)>();
        foreach (var resName in resourceNames)
        {
            using var stream = assembly.GetManifestResourceStream(resName)!;
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();

            var sections = Regex.Split(content.Replace("\r\n", "\n"), @"^---[ \t]*$", RegexOptions.Multiline);
            if (sections.Length < 3) continue;

            var fm = sections[1];
            string id = string.Empty;
            string title = string.Empty;
            var roles = new List<string>();

            var lines = fm.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var idx = line.IndexOf(':');
                if (idx <= 0) continue;
                var k = line[..idx].Trim();
                var v = line[(idx + 1)..].Trim();
                if (k == "id") id = v;
                if (k == "title") title = v;
                if (k == "roles")
                {
                    var clean = v.Trim('[', ']');
                    roles.AddRange(clean.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                }
            }

            if (roles.Count == 1 && roles.Contains("Owner", StringComparer.OrdinalIgnoreCase))
            {
                list.Add((id, title));
            }
        }

        return list;
    }
}
