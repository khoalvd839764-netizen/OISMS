using OISM.Application.Features.Orders.Dtos;

namespace OISM.Application.Features.Orders.Services;

/// <summary>
/// Giao diện chuyên biệt cho thao tác Thanh toán nhanh tại quầy POS (Atomic POS Fast Checkout).
/// </summary>
public interface IPosCheckoutService
{
    /// <summary>
    /// Thanh toán nguyên tử toàn bộ giỏ hàng quầy POS:
    /// Chuỗi xử lý Lock SKU (tăng dần) -> Lock RequestKey -> Validate Catalog Price -> Allocate Warehouse Stock ->
    /// Deduct on_hand -> Create Order (Completed) -> Create OrderItems (Snapshot CostPrice) ->
    /// Create Payment (Succeeded) -> Create Reservations (Consumed) -> Write Ledger OUT (WAC Snapshot)
    /// trong MỘT Transaction CSDL PostgreSQL nguyên tử duy nhất.
    /// </summary>
    Task<PosCheckoutResponseDto> CheckoutPosAsync(PosCheckoutRequestDto dto, Guid? cashierUserId, CancellationToken ct = default);
}
