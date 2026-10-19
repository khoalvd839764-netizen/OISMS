namespace OISM.Domain.Common;

/// <summary>
/// Thực thể cơ sở cho mọi Entity trong hệ thống OISMS.
/// Cung cấp khóa chính Id chuẩn Guid và thời gian khởi tạo/cập nhật phục vụ Audit Log.
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; set; }
}
