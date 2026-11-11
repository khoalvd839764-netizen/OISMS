using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using OISM.Infrastructure.Ai;
using Xunit;

namespace OISM.UnitTests;

public sealed class KnowledgeFilesTests
{
    private const string ResourcePrefix = "OISM.Infrastructure.Ai.Knowledge.";
    private static readonly Regex KebabCaseRegex = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);
    private static readonly string[] ForbiddenTokens = ["gsk_", "AIza", "sk-", "whsec_"];
    private static readonly string[] ForbiddenPhrases = ["Coming soon", "Handoff", "Quên mật khẩu", "CS-06"];

    public sealed record KnowledgeDocument(
        string ResourceName,
        string Id,
        string Title,
        List<string> Roles,
        List<string> Keywords,
        string Source,
        string Body,
        List<string> FrontMatterKeys);

    private static List<KnowledgeDocument> LoadDocuments()
    {
        var assembly = typeof(KnowledgeBase).Assembly;
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".md", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var docs = new List<KnowledgeDocument>();
        foreach (var resName in resourceNames)
        {
            using var stream = assembly.GetManifestResourceStream(resName)!;
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();

            // Tách theo DÒNG "---", không theo chuỗi con: anchor slug hợp lệ có thể chứa "---".
            var sections = Regex.Split(content.Replace("\r\n", "\n"), @"^---[ \t]*$", RegexOptions.Multiline);
            if (sections.Length < 3 || sections[0].Trim().Length != 0)
            {
                throw new InvalidOperationException($"Invalid front matter delimiter in resource '{resName}'.");
            }

            var frontMatterText = sections[1];
            var body = string.Join("---", sections.Skip(2)).Trim();

            string id = string.Empty;
            string title = string.Empty;
            string source = string.Empty;
            var roles = new List<string>();
            var keywords = new List<string>();
            var keys = new List<string>();

            var lines = frontMatterText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var rawLine in lines)
            {
                var line = rawLine.Trim();
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
                {
                    continue;
                }

                var colonIndex = line.IndexOf(':');
                if (colonIndex <= 0)
                {
                    continue;
                }

                var key = line.Substring(0, colonIndex).Trim();
                var value = line.Substring(colonIndex + 1).Trim();
                keys.Add(key);

                switch (key)
                {
                    case "id":
                        id = value.Trim('"', '\'');
                        break;
                    case "title":
                        title = value.Trim('"', '\'');
                        break;
                    case "source":
                        source = value.Trim('"', '\'');
                        break;
                    case "roles":
                        roles = ParseList(value);
                        break;
                    case "keywords":
                        keywords = ParseList(value);
                        break;
                }
            }

            docs.Add(new KnowledgeDocument(resName, id, title, roles, keywords, source, body, keys));
        }

        return docs;
    }

    private static List<string> ParseList(string raw)
    {
        var cleaned = raw.Trim();
        if (cleaned.StartsWith('[') && cleaned.EndsWith(']'))
        {
            cleaned = cleaned.Substring(1, cleaned.Length - 2);
        }

        return cleaned
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(s => s.Trim('"', '\''))
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "OISM.sln")) ||
                Directory.Exists(Path.Combine(dir.FullName, ".git")) ||
                File.Exists(Path.Combine(dir.FullName, "src", "backend", "OISM.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root containing OISM.sln or .git");
    }

    [Fact]
    public void KnowledgeFiles_CountShouldBeBetween40And70()
    {
        var docs = LoadDocuments();
        docs.Count.Should().BeInRange(40, 70, "AC-02 requires 40 to 70 knowledge items");
    }

    [Fact]
    public void KnowledgeFiles_FrontMatter_ShouldHaveExactFiveKeys()
    {
        var docs = LoadDocuments();
        var expectedKeys = new[] { "id", "title", "roles", "keywords", "source" };

        foreach (var doc in docs)
        {
            doc.FrontMatterKeys.Should().BeEquivalentTo(expectedKeys,
                $"File '{doc.ResourceName}' must contain exactly the 5 specified front-matter keys (AC-01)");
            doc.Id.Should().NotBeNullOrWhiteSpace($"File '{doc.ResourceName}' must have a non-empty id");
            doc.Title.Should().NotBeNullOrWhiteSpace($"File '{doc.ResourceName}' must have a non-empty title");
            doc.Source.Should().NotBeNullOrWhiteSpace($"File '{doc.ResourceName}' must have a non-empty source");
            doc.Roles.Should().NotBeEmpty($"File '{doc.ResourceName}' must have non-empty roles");
            doc.Keywords.Should().NotBeEmpty($"File '{doc.ResourceName}' must have non-empty keywords");
        }
    }

    [Fact]
    public void KnowledgeFiles_Id_ShouldBeUniqueAndKebabCase()
    {
        var docs = LoadDocuments();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var doc in docs)
        {
            KebabCaseRegex.IsMatch(doc.Id).Should().BeTrue($"Id '{doc.Id}' in '{doc.ResourceName}' must be kebab-case (AC-01)");
            seenIds.Add(doc.Id).Should().BeTrue($"Duplicate knowledge id '{doc.Id}' found (AC-01)");

            var expectedFileNameSuffix = $"{doc.Id}.md";
            doc.ResourceName.EndsWith(expectedFileNameSuffix, StringComparison.Ordinal)
                .Should().BeTrue($"Resource name '{doc.ResourceName}' must end with '{expectedFileNameSuffix}'");
        }
    }

    [Fact]
    public void KnowledgeFiles_Roles_ShouldBeValidSubsetOfOwnerAndStaff()
    {
        var docs = LoadDocuments();
        var validRoles = new HashSet<string> { "Owner", "Staff" };

        foreach (var doc in docs)
        {
            doc.Roles.Should().OnlyContain(r => validRoles.Contains(r),
                $"File '{doc.Id}' has invalid roles: [{string.Join(", ", doc.Roles)}]. Allowed roles are Owner, Staff.");
            doc.Roles.Should().NotBeEmpty($"Roles in '{doc.Id}' cannot be empty");
        }
    }

    [Fact]
    public void KnowledgeFiles_Keywords_ShouldHaveBetween3And12ItemsWithAccentedAndUnaccented()
    {
        var docs = LoadDocuments();

        foreach (var doc in docs)
        {
            doc.Keywords.Count.Should().BeInRange(3, 12,
                $"File '{doc.Id}' must have 3 to 12 keywords, but has {doc.Keywords.Count} (AC-01)");

            var allKeywordsText = string.Join(" ", doc.Keywords);
            var hasDiacritics = Regex.IsMatch(allKeywordsText, @"[àáạảãâầấậẩẫăằắặẳẵèéẹẻẽêềếệểễìíịỉĩòóọỏõôồốộổỗơờớợởỡùúụủũưừứựửữỳýỵỷỹđ]", RegexOptions.IgnoreCase);
            var hasUnaccented = Regex.IsMatch(allKeywordsText, @"[a-z0-9]", RegexOptions.IgnoreCase);

            hasDiacritics.Should().BeTrue($"Keywords in '{doc.Id}' must include accented terms (AC-01)");
            hasUnaccented.Should().BeTrue($"Keywords in '{doc.Id}' must include unaccented terms (AC-01)");
        }
    }

    [Fact]
    public void KnowledgeFiles_Source_ShouldReferenceExistingFileUnderRepoRoot()
    {
        var repoRoot = FindRepoRoot();
        var docs = LoadDocuments();

        foreach (var doc in docs)
        {
            doc.Source.Should().Contain("#", $"Source '{doc.Source}' in '{doc.Id}' must contain '#mục' (AC-01)");

            var parts = doc.Source.Split('#', 2);
            var relativeFilePath = parts[0].Trim();
            var sectionAnchor = parts[1].Trim();

            relativeFilePath.Should().StartWith("docs/", $"Source in '{doc.Id}' must start with 'docs/'");
            sectionAnchor.Should().NotBeNullOrWhiteSpace($"Section anchor in '{doc.Id}' must not be empty");

            var fullPath = Path.Combine(repoRoot, relativeFilePath);
            File.Exists(fullPath).Should().BeTrue($"Referenced source file '{relativeFilePath}' for id '{doc.Id}' must exist in repository at '{fullPath}' (AC-06)");

            // Anchor phải khớp một heading thật theo cách GitHub tạo slug, để truy được đúng mục nguồn.
            var anchors = File.ReadLines(fullPath)
                .Where(line => Regex.IsMatch(line, @"^#{1,6}\s"))
                .Select(line => HeadingSlug(Regex.Replace(line, @"^#{1,6}\s+", string.Empty)))
                .ToHashSet(StringComparer.Ordinal);
            anchors.Should().Contain(sectionAnchor, $"Anchor '#{sectionAnchor}' in '{doc.Id}' must match a heading of '{relativeFilePath}'");
        }
    }

    private static string HeadingSlug(string heading) =>
        Regex.Replace(heading.Trim().ToLowerInvariant(), @"[^\w\- ]", string.Empty).Replace(' ', '-');

    [Fact]
    public void KnowledgeFiles_Body_LengthShouldBeBetween300And1200Chars()
    {
        var docs = LoadDocuments();

        foreach (var doc in docs)
        {
            var len = doc.Body.Length;
            len.Should().BeInRange(300, 1200,
                $"Body length of '{doc.Id}' must be between 300 and 1,200 characters, but was {len} (AC-01)");
        }
    }

    [Fact]
    public void KnowledgeFiles_ShouldNotContainSecretsOrForbiddenTokens()
    {
        var docs = LoadDocuments();

        foreach (var doc in docs)
        {
            var fullText = $"{doc.Id} {doc.Title} {string.Join(" ", doc.Keywords)} {doc.Body}";
            foreach (var token in ForbiddenTokens)
            {
                fullText.Should().NotContain(token,
                    $"File '{doc.Id}' must not contain sensitive token pattern '{token}' (AC-06)");
            }
        }
    }

    [Fact]
    public void KnowledgeFiles_ShouldNotContainForbiddenPhrases()
    {
        var docs = LoadDocuments();

        foreach (var doc in docs)
        {
            var fullText = $"{doc.Title} {doc.Body}";
            foreach (var phrase in ForbiddenPhrases)
            {
                fullText.Should().NotContainEquivalentOf(phrase,
                    $"File '{doc.Id}' must not contain forbidden phrase '{phrase}' (AC-05)");
            }
        }
    }

    [Fact]
    public void KnowledgeFiles_ShouldCoverFourRequiredGroups()
    {
        var docs = LoadDocuments();

        // 1. Thao tác theo màn hình (docs/11-ui-screens/)
        docs.Should().Contain(d => d.Source.Contains("docs/11-ui-screens/"),
            "Knowledge base must cover screen operations (AC-02)");

        // 2. Khái niệm nghiệp vụ (docs/00-overview/ or docs/04-architecture-and-design/)
        docs.Should().Contain(d => d.Source.Contains("docs/00-overview/") || d.Source.Contains("docs/04-architecture-and-design/"),
            "Knowledge base must cover core domain concepts and state machines (AC-02)");

        // 3. Quyền theo vai trò (docs/01-users-and-rbac/)
        docs.Should().Contain(d => d.Source.Contains("docs/01-users-and-rbac/"),
            "Knowledge base must cover role-based permissions (AC-02)");

        // 4. Mã lỗi thường gặp (docs/12-error-and-status-codes/)
        docs.Should().Contain(d => d.Source.Contains("docs/12-error-and-status-codes/"),
            "Knowledge base must cover common error codes (AC-02)");
    }

    [Fact]
    public void KnowledgeFiles_OwnerOnlyContent_ShouldBeRestrictedToOwner()
    {
        var docs = LoadDocuments();

        // Check specific owner-only topics
        var ownerOnlyIds = new[]
        {
            "man-hinh-duyet-phieu-doi-gia",
            "man-hinh-duyet-phieu-mua-hang",
            "man-hinh-lich-ky-kiem-ke",
            "man-hinh-chot-kiem-ke-can-bang",
            "man-hinh-bao-cao-doanh-thu-lai-gop",
            "man-hinh-tai-khoan-nhan-tien",
            "man-hinh-chi-nhanh-kho-hang",
            "man-hinh-nhan-vien-phan-quyen",
            "man-hinh-ket-noi-gian-hang-san",
            "tro-ly-oisms-doanh-thu-lai-gop",
            "gia-von-binh-quan-gia-quyen",
            "chup-gia-von-lich-su-khi-ban",
            "phan-quyen-vai-tro-chu-shop-owner",
            "co-che-kiem-soat-quyen-hai-lop",
            "ma-loi-tu-duyet-phieu-kiem-ke"
        };

        foreach (var id in ownerOnlyIds)
        {
            var doc = docs.FirstOrDefault(d => d.Id == id);
            doc.Should().NotBeNull($"Owner-only document '{id}' should exist");
            doc!.Roles.Should().ContainSingle(r => r == "Owner",
                $"Document '{id}' relates to owner-only functionality and must only have role 'Owner' (AC-04)");
        }
    }
}
