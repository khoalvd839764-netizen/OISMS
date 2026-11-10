namespace OISM.Infrastructure.Services.Ai;

public sealed class AiOptions
{
    public const string SectionName = "Ai";
    public const string PrimaryProviderId = "groq";

    /// <summary>Công tắc chung. Tắt thì không model nào được dùng.</summary>
    public bool Enabled { get; set; }

    // Nhà cung cấp chính (Groq), giữ các khóa cũ Ai:BaseUrl, Ai:Model, Ai:ApiKey.
    public string Label { get; set; } = "Groq · GPT-OSS 120B";
    public string BaseUrl { get; set; } = "https://api.groq.com/openai/v1";
    public string Model { get; set; } = "openai/gpt-oss-120b";
    public string? ApiKey { get; set; }

    /// <summary>Mã model mặc định khi người dùng chưa chọn.</summary>
    public string DefaultProvider { get; set; } = PrimaryProviderId;

    /// <summary>Nhà cung cấp bổ sung, ví dụ Ai:Providers:gemini:ApiKey.</summary>
    public Dictionary<string, AiProviderOptions> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int TimeoutSeconds { get; set; } = 30;
    public int MaxTokens { get; set; } = 1024;
    public int MaxHistoryMessages { get; set; } = 10;
    public int MaxToolRounds { get; set; } = 3;
}

public sealed class AiProviderOptions
{
    public bool Enabled { get; set; } = true;
    public string Label { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? ApiKey { get; set; }

    /// <summary>Thời gian chờ riêng (giây); trống thì dùng Ai:TimeoutSeconds.</summary>
    public int? TimeoutSeconds { get; set; }
}

/// <summary>Cấu hình đã kiểm tra đủ để gọi một nhà cung cấp.</summary>
public sealed record AiProviderSettings(
    string Id, string Label, string BaseUrl, string Model, string ApiKey, int MaxTokens, int TimeoutSeconds = 30);
