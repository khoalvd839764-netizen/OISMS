namespace OISM.Application.Common.Interfaces;

public sealed record KnowledgeSearchResult(
    string Id,
    string Title,
    string Content,
    double Score);

public interface IKnowledgeBase
{
    IReadOnlyList<KnowledgeSearchResult> Search(string query, string role, int top = 4);
}
