using Microsoft.Extensions.Options;
using OISM.Application.Common.Exceptions;
using OISM.Application.Common.Interfaces;

namespace OISM.Infrastructure.Services.Ai;

/// <summary>
/// Chọn nhà cung cấp theo model người dùng chọn. Chỉ nhà cung cấp có key mới xuất hiện trong danh sách.
/// </summary>
public sealed class LlmRouter(OpenAiCompatibleLlmClient transport, IOptionsMonitor<AiOptions> options)
    : ILlmClient, ILlmModelCatalog
{
    public IReadOnlyList<AiModelOption> GetAvailableModels()
    {
        var providers = Providers(options.CurrentValue);
        var defaultId = DefaultId(options.CurrentValue, providers);
        return providers.Select(provider => new AiModelOption(provider.Id, provider.Label, provider.Id == defaultId)).ToArray();
    }

    public Task<LlmResponse> ChatAsync(LlmRequest request, CancellationToken ct = default)
    {
        var settings = options.CurrentValue;
        var providers = Providers(settings);
        if (providers.Count == 0)
        {
            throw new OismException("AI_DISABLED", "Trợ lý AI chưa được bật.", 503);
        }

        var id = string.IsNullOrWhiteSpace(request.Model) ? DefaultId(settings, providers) : request.Model.Trim();
        var provider = providers.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase))
            ?? throw new OismException("AI_INVALID_REQUEST", "Model AI đã chọn không khả dụng.", 400);
        return transport.ChatAsync(request, provider, ct);
    }

    internal static IReadOnlyList<AiProviderSettings> Providers(AiOptions settings)
    {
        if (!settings.Enabled)
        {
            return [];
        }

        var result = new List<AiProviderSettings>();
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            result.Add(new AiProviderSettings(AiOptions.PrimaryProviderId, Label(settings.Label, settings.Model),
                settings.BaseUrl, settings.Model, settings.ApiKey, settings.MaxTokens, Timeout(settings.TimeoutSeconds)));
        }

        foreach (var (id, provider) in settings.Providers.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (provider.Enabled && !string.IsNullOrWhiteSpace(provider.ApiKey)
                && !string.IsNullOrWhiteSpace(provider.BaseUrl) && !string.IsNullOrWhiteSpace(provider.Model)
                && !string.Equals(id, AiOptions.PrimaryProviderId, StringComparison.OrdinalIgnoreCase))
            {
                result.Add(new AiProviderSettings(id.ToLowerInvariant(), Label(provider.Label, provider.Model),
                    provider.BaseUrl, provider.Model, provider.ApiKey, settings.MaxTokens,
                    Timeout(provider.TimeoutSeconds ?? settings.TimeoutSeconds)));
            }
        }

        return result;
    }

    private static string DefaultId(AiOptions settings, IReadOnlyList<AiProviderSettings> providers) =>
        providers.FirstOrDefault(provider => string.Equals(provider.Id, settings.DefaultProvider, StringComparison.OrdinalIgnoreCase))?.Id
        ?? providers.FirstOrDefault()?.Id
        ?? AiOptions.PrimaryProviderId;

    private static int Timeout(int seconds) => Math.Clamp(seconds, 5, 120);

    private static string Label(string label, string model) => string.IsNullOrWhiteSpace(label) ? model : label;
}
