using FluentValidation;

namespace OISM.Application.Features.ShopIntegrations;

public class CreateShopIntegrationRequest
{
    /// <summary>Kênh bán: 'Shopee', 'TikTok', 'Lazada' (không phân biệt hoa thường).</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>Mã gian hàng trên sàn, duy nhất trong mỗi Tenant + Channel.</summary>
    public string ExternalShopId { get; set; } = string.Empty;

    /// <summary>Chi nhánh xử lý và giữ chỗ cho đơn của gian hàng.</summary>
    public Guid BranchId { get; set; }
}

public class UpdateShopIntegrationRequest
{
    public Guid BranchId { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ShopIntegrationResponse
{
    public Guid Id { get; set; }
    public string Channel { get; set; } = string.Empty;
    public string ExternalShopId { get; set; } = string.Empty;
    public Guid BranchId { get; set; }
    public string BranchName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}

/// <summary>
/// Kết quả tạo kết nối hoặc đổi secret. <see cref="WebhookSecret"/> chỉ trả về một lần,
/// server chỉ lưu bản băm.
/// </summary>
public class ShopIntegrationSecretResponse
{
    public ShopIntegrationResponse Integration { get; set; } = new();
    public string WebhookSecret { get; set; } = string.Empty;
}

public class CreateShopIntegrationValidator : AbstractValidator<CreateShopIntegrationRequest>
{
    private static readonly string[] AllowedChannels = ["shopee", "tiktok", "lazada"];

    public CreateShopIntegrationValidator()
    {
        RuleFor(x => x.Channel)
            .NotEmpty().WithMessage("Kênh bán không được để trống.")
            .Must(c => AllowedChannels.Contains(c.Trim().ToLowerInvariant()))
            .WithMessage("Kênh bán chỉ hỗ trợ 'Shopee', 'TikTok', 'Lazada'.");

        RuleFor(x => x.ExternalShopId)
            .NotEmpty().WithMessage("Mã gian hàng không được để trống.")
            .Must(s => s.Trim().Length <= 100).WithMessage("Mã gian hàng không được vượt quá 100 ký tự.");

        RuleFor(x => x.BranchId)
            .NotEmpty().WithMessage("Chi nhánh (BranchId) không được để trống.");
    }
}

public class UpdateShopIntegrationValidator : AbstractValidator<UpdateShopIntegrationRequest>
{
    public UpdateShopIntegrationValidator()
    {
        RuleFor(x => x.BranchId)
            .NotEmpty().WithMessage("Chi nhánh (BranchId) không được để trống.");
    }
}
