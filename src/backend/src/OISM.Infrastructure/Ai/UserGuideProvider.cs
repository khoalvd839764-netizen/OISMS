using OISM.Application.Common.Interfaces;

namespace OISM.Infrastructure.Ai;

public sealed class UserGuideProvider : IUserGuideProvider
{
    private const string ResourcePrefix = "OISM.Infrastructure.Ai.Guide.";
    private static readonly string[] HeaderDelimiters = ["---", "\r\n", "\n", "roles:", "[", "]", ","];
    private readonly Dictionary<string, string> _guides;

    public UserGuideProvider()
    {
        var assembly = typeof(UserGuideProvider).Assembly;
        var pages = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal) && name.EndsWith(".md", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(name =>
            {
                using var reader = new StreamReader(assembly.GetManifestResourceStream(name)!);
                var source = reader.ReadToEnd();
                var sections = source.Split("---", 3, StringSplitOptions.None);
                if (sections.Length != 3 || sections[0].Length != 0)
                {
                    throw new InvalidOperationException($"Missing guide front matter in {name}.");
                }

                var roles = sections[1].Split(HeaderDelimiters, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                return (Roles: roles, Content: sections[2].Trim());
            })
            .ToArray();

        _guides = new[] { "Owner", "Staff" }.ToDictionary(
            role => role,
            role => string.Join("\n\n", pages.Where(page => page.Roles.Contains(role)).Select(page => page.Content)));
    }

    public string GetGuide(string role) => _guides.GetValueOrDefault(role, string.Empty);
}
