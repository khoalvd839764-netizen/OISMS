namespace OISM.Application.Common.Interfaces;

/// <summary>
/// Giao diện trừu tượng hóa thao tác cập nhật chi nhánh của kho hàng ở tầng persistence,
/// giải quyết xung đột EF Core alternate key mutation và bảo đảm tính nguyên tử.
/// </summary>
public interface IWarehouseBranchReassignmentStore
{
    Task<bool> ReassignBranchAsync(
        Guid warehouseId,
        Guid tenantId,
        Guid newBranchId,
        string name,
        bool isActive,
        DateTimeOffset updatedAt,
        CancellationToken ct = default);
}
