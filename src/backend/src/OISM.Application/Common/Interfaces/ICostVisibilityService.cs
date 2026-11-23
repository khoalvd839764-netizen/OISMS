namespace OISM.Application.Common.Interfaces;

/// <summary>
/// Quyết định quyền xem giá mua, giá vốn, WAC, giá trị tồn và lãi gộp dựa trên vai trò từ JWT xác thực.
/// Chỉ vai trò Chủ cửa hàng (Owner) mới có quyền xem thông tin tài chính này (AC-01 / Upgrade 12 Q10, Q24).
/// </summary>
public interface ICostVisibilityService
{
    bool CanViewCost { get; }
    bool IsOwner { get; }
}

/// <summary>
/// Bí danh ngắn gọn cho ICostVisibilityService theo đặc tả kiến trúc.
/// </summary>
public interface ICostVisibility : ICostVisibilityService
{
}
