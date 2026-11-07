namespace OISM.Application.Common.Attributes;

/// <summary>
/// Đánh dấu endpoint xác thực chưa có phiên hợp lệ của người dùng đang thao tác
/// (đăng ký, đăng nhập, làm mới token, đăng xuất bằng refresh token).
/// Trên endpoint này <c>CurrentTenantService</c> không lấy TenantId từ access token gửi kèm,
/// vì token đó có thể là token còn hạn của một tenant khác (phiên cũ trong trình duyệt).
/// Tenant của luồng được xác định từ dữ liệu đăng nhập hoặc refresh token đã kiểm tra trong service.
/// Không gắn attribute này lên endpoint nghiệp vụ.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class IgnoreTenantClaimAttribute : Attribute
{
}
