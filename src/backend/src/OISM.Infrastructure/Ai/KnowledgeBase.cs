using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using OISM.Application.Common.Interfaces;

namespace OISM.Infrastructure.Ai;

public sealed class KnowledgeBase : IKnowledgeBase
{
    public const double MinScoreThreshold = 10.0;
    private const string ResourcePrefix = "OISM.Infrastructure.Ai.Knowledge.";

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "va", "hoac", "cua", "cho", "o", "tai", "voi", "cac", "nhung",
        "duoc", "bi", "co", "khong", "la", "thi", "ma", "de", "ra", "vao",
        "lai", "da", "dang", "se", "mot", "nhieu", "rat", "qua", "tu", "den",
        "trong", "ngoai", "tren", "duoi", "toi", "ban", "giup", "xin", "hoi",
        "the", "nao", "sao", "gi", "hom", "nay", "a", "oi", "nhu", "lam", "ve",
        "giua", "bao", "nhieu", "chi"
    };

    private static readonly (string Condition, string[] Synonyms)[] SynonymRules =
    [
        ("nhap hang", ["nhap kho", "phieu nhap", "nha cung cap"]),
        ("chuyen kho", ["chuyen kho hai buoc"]),
        ("kiem ke", ["phieu kiem ke", "kiem ke kho"]),
        ("ton kha dung", ["khai niem ton kha dung"]),
        ("he so quy doi", ["quy cach", "don vi tinh"]),
        ("don vi tinh", ["quy cach", "don vi tinh"]),
        ("tra hang", ["phieu tra hang", "tra hang tu khach"]),
        ("so kho", ["so kho", "xuat nhap ton"]),
        ("out of stock", ["ton kho khong du", "insufficient stock"]),
        ("het hang", ["ton kho khong du", "insufficient stock"]),
        ("negative stock", ["ton kho am"]),
        ("ton kho am", ["ton kho am"])
    ];

    private sealed class DocEntry
    {
        public required string Id { get; init; }
        public required string Title { get; init; }
        public required IReadOnlyList<string> Roles { get; init; }
        public required IReadOnlyList<string> Keywords { get; init; }
        public required string Source { get; init; }
        public required string Content { get; init; }
        public required string TitleNormalized { get; init; }
        public required string KeywordsNormalized { get; init; }
        public required string FullNormalized { get; init; }
        public required string[] Tokens { get; init; }
        public required HashSet<string> TitleTokens { get; init; }
        public required HashSet<string> KeywordTokens { get; init; }
    }

    private readonly DocEntry[] _docs;

    public KnowledgeBase()
    {
        var assembly = typeof(KnowledgeBase).Assembly;
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".md", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        var docs = new List<DocEntry>(resourceNames.Count);

        foreach (var resName in resourceNames)
        {
            using var stream = assembly.GetManifestResourceStream(resName)
                ?? throw new InvalidOperationException($"Cannot open manifest stream for '{resName}'.");
            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();

            var sections = Regex.Split(content.Replace("\r\n", "\n"), @"^---[ \t]*$", RegexOptions.Multiline);
            if (sections.Length < 3 || sections[0].Trim().Length != 0)
            {
                throw new InvalidOperationException($"Invalid front matter delimiter in resource '{resName}'.");
            }

            var frontMatterText = sections[1];
            var body = string.Join("---", sections.Skip(2)).Trim();
            if (string.IsNullOrWhiteSpace(body))
            {
                throw new InvalidOperationException($"Empty body in resource '{resName}'.");
            }

            string id = string.Empty;
            string title = string.Empty;
            string source = string.Empty;
            var roles = new List<string>();
            var keywords = new List<string>();

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

                var key = line[..colonIndex].Trim();
                var value = line[(colonIndex + 1)..].Trim();

                switch (key)
                {
                    case "id":
                        id = value;
                        break;
                    case "title":
                        title = value;
                        break;
                    case "source":
                        source = value;
                        break;
                    case "roles":
                        roles.AddRange(ParseList(value));
                        break;
                    case "keywords":
                        keywords.AddRange(ParseList(value));
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(title) ||
                string.IsNullOrWhiteSpace(source) || roles.Count == 0 || keywords.Count == 0)
            {
                throw new InvalidOperationException($"Missing required front matter in resource '{resName}'.");
            }

            var titleNorm = NormalizeText(title);
            var keywordsNorm = NormalizeText(string.Join(' ', keywords));
            var bodyNorm = NormalizeText(body);
            var fullNorm = $"{titleNorm} {keywordsNorm} {bodyNorm}";
            var allTokens = Tokenize(fullNorm);
            var titleTokens = new HashSet<string>(Tokenize(titleNorm), StringComparer.Ordinal);
            var keywordTokens = new HashSet<string>(Tokenize(keywordsNorm), StringComparer.Ordinal);

            docs.Add(new DocEntry
            {
                Id = id,
                Title = title,
                Roles = roles,
                Keywords = keywords,
                Source = source,
                Content = body,
                TitleNormalized = titleNorm,
                KeywordsNormalized = keywordsNorm,
                FullNormalized = fullNorm,
                Tokens = allTokens,
                TitleTokens = titleTokens,
                KeywordTokens = keywordTokens
            });
        }

        _docs = [.. docs];
    }

    public IReadOnlyList<KnowledgeSearchResult> Search(string query, string role, int top = 4)
    {
        if (string.IsNullOrWhiteSpace(query) || top <= 0)
        {
            return [];
        }

        // Lọc theo vai trò TRƯỚC khi xếp hạng
        var roleDocs = _docs.Where(d => d.Roles.Contains(role, StringComparer.OrdinalIgnoreCase)).ToArray();
        if (roleDocs.Length == 0)
        {
            return [];
        }

        var normQuery = NormalizeText(query);
        var rawTokens = Tokenize(query);
        var queryTokens = rawTokens.Where(t => !StopWords.Contains(t)).ToArray();
        if (queryTokens.Length == 0)
        {
            queryTokens = rawTokens;
        }
        if (queryTokens.Length == 0)
        {
            return [];
        }

        var synonyms = new List<string>();
        foreach (var (condition, syns) in SynonymRules)
        {
            if (normQuery.Contains(condition, StringComparison.Ordinal))
            {
                if (condition == "nhap hang" && normQuery.Contains("goi y", StringComparison.Ordinal))
                {
                    continue;
                }
                synonyms.AddRange(syns);
            }
        }

        var docCount = roleDocs.Length;
        var avgDocLength = roleDocs.Average(d => d.Tokens.Length);
        if (avgDocLength <= 0) avgDocLength = 1.0;

        var df = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var doc in roleDocs)
        {
            var uniqueTokens = new HashSet<string>(doc.Tokens, StringComparer.Ordinal);
            foreach (var token in uniqueTokens)
            {
                df[token] = df.GetValueOrDefault(token, 0) + 1;
            }
        }

        const double k1 = 1.2;
        const double b = 0.75;
        var candidates = new List<KnowledgeSearchResult>();

        foreach (var doc in roleDocs)
        {
            var tf = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var token in doc.Tokens)
            {
                tf[token] = tf.GetValueOrDefault(token, 0) + 1;
            }

            double score = 0.0;
            int matchedQueryTokens = 0;

            foreach (var qt in queryTokens)
            {
                if (tf.TryGetValue(qt, out var count))
                {
                    matchedQueryTokens++;
                    var docFreq = df.GetValueOrDefault(qt, 0);
                    var idf = Math.Max(0.5, Math.Log(1.0 + (docCount - docFreq + 0.5) / (docFreq + 0.5)));
                    var termTf = (double)count;
                    var bm25 = idf * (termTf * (k1 + 1.0)) / (termTf + k1 * (1.0 - b + b * (doc.Tokens.Length / avgDocLength)));
                    score += bm25;

                    if (doc.TitleTokens.Contains(qt))
                    {
                        score += 3.0 * idf + 2.0;
                    }
                    if (doc.KeywordTokens.Contains(qt))
                    {
                        score += 2.0 * idf + 1.5;
                    }
                }
            }

            foreach (var syn in synonyms)
            {
                if (doc.TitleNormalized.Contains(syn, StringComparison.Ordinal) ||
                    doc.KeywordsNormalized.Contains(syn, StringComparison.Ordinal))
                {
                    score += 15.0;
                }
                else if (doc.FullNormalized.Contains(syn, StringComparison.Ordinal))
                {
                    score += 8.0;
                }
            }

            for (int i = 0; i < rawTokens.Length - 1; i++)
            {
                var pair = rawTokens[i] + " " + rawTokens[i + 1];
                if (StopWords.Contains(pair) || pair.Length < 4)
                {
                    continue;
                }
                if (doc.TitleNormalized.Contains(pair, StringComparison.Ordinal))
                {
                    score += 12.0;
                }
                else if (doc.KeywordsNormalized.Contains(pair, StringComparison.Ordinal))
                {
                    score += 8.0;
                }
                else if (doc.FullNormalized.Contains(pair, StringComparison.Ordinal))
                {
                    score += 4.0;
                }
            }

            var coverage = queryTokens.Length > 0 ? (double)matchedQueryTokens / queryTokens.Length : 0.0;
            if (queryTokens.Length >= 2 && coverage < 0.5)
            {
                score = 0.0;
            }

            if (score >= MinScoreThreshold)
            {
                candidates.Add(new KnowledgeSearchResult(doc.Id, doc.Title, doc.Content, score));
            }
        }

        return candidates
            .OrderByDescending(c => c.Score)
            .Take(top)
            .ToArray();
    }

    public static string NormalizeText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var characters = text.ToLowerInvariant().Normalize(NormalizationForm.FormD)
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Select(character => character == 'đ' ? 'd' : character);
        return new string(characters.ToArray());
    }

    public static string[] Tokenize(string text)
    {
        var normalized = NormalizeText(text);
        if (string.IsNullOrWhiteSpace(normalized)) return [];
        return Regex.Split(normalized, @"[^a-z0-9]+")
            .Where(t => t.Length > 0)
            .ToArray();
    }

    private static List<string> ParseList(string raw)
    {
        var trimmed = raw.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            trimmed = trimmed[1..^1];
        }

        return trimmed
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}
