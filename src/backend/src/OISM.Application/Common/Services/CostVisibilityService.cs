using OISM.Application.Common.Interfaces;

namespace OISM.Application.Common.Services;

/// <summary>
/// Triển khai dịch vụ che giá dùng chung toàn hệ thống (AC-01 / BE-12.9 / Upgrade 12 Q24).
/// Quyết định quyền xem dựa trên vai trò trích xuất từ JWT Claims đã xác thực qua ICurrentUserService.
/// Không nhận giá trị từ client, không cho phép Staff hoặc Cashier xem giá vốn/giá trị tồn/lãi gộp.
/// </summary>
public class CostVisibilityService : ICostVisibilityService, ICostVisibility
{
    private readonly ICurrentUserService _currentUserService;

    public CostVisibilityService(ICurrentUserService currentUserService)
    {
        _currentUserService = currentUserService;
    }

    public bool CanViewCost => _currentUserService.IsInRole("Owner");

    public bool IsOwner => CanViewCost;
}

/// <summary>
/// Triển khai mặc định cho phép xem giá (dùng cho unit tests không khởi tạo HttpContext hoặc tính toán nội bộ).
/// </summary>
public class AllowAllCostVisibilityService : ICostVisibilityService, ICostVisibility
{
    public static readonly AllowAllCostVisibilityService Instance = new();
    public bool CanViewCost => true;
    public bool IsOwner => true;
}
