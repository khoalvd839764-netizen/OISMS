# BF-035 — Domain anemic, trạng thái so bằng chuỗi, gói MediatR không dùng

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa và kiểm chứng |
| **Ưu tiên** | P3 |
| **Khu vực** | Backend / Clean Architecture / Domain |
| **Phát hiện** | 06/11/2026, rà soát lại Clean Architecture sau Upgrade 06 (commit `6ad3cf6`) |
| **Đợt fix** | Đợt 4 (tùy chọn); gỡ MediatR ở Đợt 2 |
| **Kế hoạch chung** | [CHECKLIST.md mục E](../CHECKLIST.md#e-kế-hoạch-hoàn-thiện-clean-architecture-bf-028035) |

## Mô tả lỗi

- 41 entity trong `OISM.Domain/Entities` chỉ chứa dữ liệu; toàn bộ luật chuyển trạng thái nằm trong service.
- Có 59 chỗ trong Application so sánh trạng thái bằng chuỗi (`order.Status == "Reserved"`, `Status = "Cancelled"`...). Gõ sai chuỗi chỉ phát hiện lúc chạy (CHECK ở PostgreSQL).
- `OISM.Application.csproj` tham chiếu `MediatR` nhưng không có handler nào dùng.

## Kế hoạch fix

1. Đợt 2: gỡ `PackageReference MediatR`.
2. Đợt 4: thêm hằng số hoặc kiểu cho trạng thái (`OrderStatus`, `ReservationStatus`, `StockCountStatus`, `PurchaseOrderStatus`...), giá trị trong DB giữ nguyên.
3. Đợt 4: đưa luật chuyển trạng thái vào entity (`Order.Confirm()`, `StockCount.Submit()`...) ném `INVALID_STATE_TRANSITION`; service chỉ điều phối khóa, transaction, ghi sổ.
4. Đợt 4: tách `OrderService` thành checkout POS, vòng đời đơn Admin, nhận đơn từ sàn, truy vấn đơn.
5. Test: unit test cho từng luật chuyển trạng thái của entity; integration test hiện có vẫn PASS.

## Cách đã sửa

Đợt 2 đã gỡ gói `MediatR`. Đợt 4 dùng hằng số trạng thái theo loại chứng từ và đưa các bước chuyển trạng thái cùng trường thời gian, người thực hiện vào entity Domain. Cột PostgreSQL và DTO vẫn lưu trạng thái dạng chuỗi với giá trị hiện hành.

`OrderService` phụ trách vòng đời đơn và giữ giao diện hiện có. Checkout, tiếp nhận đơn sàn, lõi giữ chỗ dùng chung với Admin và truy vấn đơn lần lượt nằm trong `PosCheckoutService`, `MarketplaceOrderService`, `OrderIntakeService` và `OrderQueryService`.

## Kiểm chứng

Việc gỡ `MediatR` đã qua build và test backend .NET 8. [GitHub Actions #134](https://github.com/khoalvd839764-netizen/OISMS/actions/runs/42185858352)

Đợt 4 đã qua 237 unit test, trong đó có 14 ca kiểm tra chuyển trạng thái Domain, cùng 15 integration test cục bộ và 63 test POS. Toàn bộ suite backend trên PostgreSQL 16 và job frontend đều qua trên commit `df26020`. [GitHub Actions #138](https://github.com/khoalvd839764-netizen/OISMS/actions/runs/42197568799)

## Điều kiện đóng

- Không còn vi phạm mô tả ở trên trong code (kiểm bằng `grep`/build).
- Toàn bộ unit test, integration test liên quan và `npm test` của POS PASS; thay đổi chạm transaction, khóa, ledger phải có bằng chứng trên PostgreSQL thật.
- Cập nhật trạng thái trong file này và [CHECKLIST.md](../CHECKLIST.md).
