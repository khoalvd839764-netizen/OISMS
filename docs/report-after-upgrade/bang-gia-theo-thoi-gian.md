# Báo cáo sau nâng cấp: Bảng giá theo thời gian hiệu lực (Upgrade 04)

| Mục | Nội dung |
| --- | --- |
| Task | `OISMS-47-U04` |
| Ngày kiểm tra | 02/11/2026 |
| Đặc tả | [Tài liệu 04](../Upgrade-recommended/04-bang-gia-theo-thoi-gian.md) |
| Phạm vi | Backend P0–P5 và tài liệu. **Chưa có giao diện.** |
| Trạng thái | Backend đã kiểm chứng. **Chưa nên đưa lên môi trường đang dùng thật trước khi frontend cập nhật.** |

> **Cập nhật 04/11/2026:** giao diện của phần này đã có. Kết quả nghiệm thu xem [báo cáo frontend](frontend-coming-soon-va-upgrade.md). Các mục "chưa có giao diện" bên dưới là hiện trạng tại ngày kiểm tra ghi trong bảng.

## Đã làm

- **Dữ liệu:** thêm 4 bảng `price_lists`, `price_change_requests`, `price_change_request_items`, `sku_unit_prices`; thêm `sku_unit_price_id`, `price_source` trên `order_items`. Migration: [`20261102_price_lists.sql`](../../docker/postgres/migrations/20261102_price_lists.sql).
- **Đổi giá:** mọi lần đổi giá đi qua phiếu. Owner được duyệt ngay, Staff chờ Owner duyệt. Có lịch giá tương lai, hủy giá đã lên lịch và lịch sử giá.
- **Bán hàng:** POS, đơn Admin và giữ chỗ đọc giá qua `ISkuPriceResolver`, chụp đơn giá và ID dòng giá vào dòng đơn. Đơn từ sàn dùng giá do sàn gửi.
- **Đóng đường ghi cũ:** `PUT .../prices` trả `410`; sửa SKU hoặc đơn vị không còn đổi được giá.

Contract chi tiết: [API danh mục, mục 4](../05-api-contracts/02-catalog-endpoints.md#4-bảng-giá-theo-thời-gian--phiếu-đổi-giá-oisms-47-u04).

## Kiểm chứng

Chạy trên PostgreSQL 16 thật, cổng `55435`, biến `OISM_TEST_PORT`.

| Lệnh hoặc phép kiểm | Kết quả |
| --- | --- |
| `dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal` | 0 lỗi, 3 cảnh báo khi build sạch |
| `dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false` | PASS 173/173 |
| `dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false` | PASS 328/328 |
| Nâng cấp từ schema commit `ddb83d9` có dữ liệu mẫu | Giá lẻ, giá sỉ, giá đơn vị được chuyển đúng; SKU có giá lẻ 0 được liệt kê |
| Chạy migration lần hai sau khi có SKU mới | Không sinh thêm dòng; giá chưa duyệt không có hiệu lực |
| So `pg_dump -s` giữa bản nâng cấp và bản cài mới | Không lệch (không so thứ tự cột) |
| So migration với SQL nhúng trong `DatabaseMigrationService` | Khớp, chỉ khác lệnh mở và đóng transaction, cách viết một lệnh `REVOKE` |

**Chưa chạy:** giao diện Admin và POS với backend mới, stress test 50 đơn đồng thời, chạy thử trên dữ liệu thật.

Số unit test giảm 2 so với mức nền 175 của giai đoạn phát triển vì đã xóa validator của API `PUT .../prices`.

## Đối chiếu tiêu chí nghiệm thu

Các test nằm trong `PricingIntegrationTests`, trừ khi ghi khác.

| # | Tiêu chí | Kết quả | Bằng chứng |
| --- | --- | --- | --- |
| 1 | Mỗi khóa chỉ ra đúng một giá | Đạt | `SkuPriceResolverUnitTests`, `AC03_Cancellation_...`, partial unique index |
| 2 | Giá chuyển đúng mốc; đơn giữ giá đã chụp | Đạt phần đơn hàng | `AC06_PriceResolution_...`. Hóa đơn in lại và báo cáo không có test riêng cho tình huống đổi giá |
| 3 | Không đổi giá được bằng đường cũ; mọi dòng giá có phiếu | Đạt | `AC08_...`, `Review_UpdateVariant_...`, `AC05_...` |
| 4 | Staff chờ duyệt; Cashier không tạo được phiếu | Đạt | `AC04_StaffPendingFlow_...` |
| 5 | Hủy được giá đã lên lịch; không sửa, xóa được dòng đã duyệt | Đạt | `AC03_RuntimeRole_...`, `AC03_Cancellation_...` |
| 6 | Đơn đang giữ chỗ không lỗi khi giá mới có hiệu lực | Đạt | `AC06_PriceResolution_...` |
| 7 | Gửi lại cùng khóa sau khi giá đổi trả đơn cũ | Đạt | `AC07_Idempotency_...` |
| 8 | Chưa có giá thì báo lỗi, không bán giá 0 | Đạt | `AC07_Idempotency_...`, `AC05_...` |
| 9 | Cách ly giữa tenant | Đạt | `AC04_TenantIsolation_...` |

Luồng bán đồng thời với duyệt phiếu: `Review_AdminOrder_ReadsPriceAfterSkuLock`, `Review_PosCheckout_ConcurrentWithApproval_...`. Migration: `PriceListMigrationIntegrationTests`.

## Lỗi tìm thấy khi review và đã sửa

| Lỗi | Hậu quả nếu giữ nguyên |
| --- | --- |
| Sửa SKU hoặc đơn vị với giá cột cũ hoặc 0 vẫn trả `200` | Báo thành công nhưng giá không đổi |
| Đơn Admin đọc giá trước khi khóa SKU | Đơn có thể chụp giá cũ khi giá mới vừa có hiệu lực |
| Client tự đặt được loại phiếu | Sai dữ liệu truy vết; giá trị lạ gây lỗi 500 |
| Lý do dài hơn 500 ký tự không được kiểm tra | Lỗi 500 |
| So `requestedValidFrom` không tính giới hạn micro giây | Gửi lại đúng phiếu vẫn bị báo khác nội dung |
| Mã bảng giá sai trên đơn Admin báo nhầm mã lỗi | Khó chẩn đoán |
| Giá ban đầu bị bỏ qua khi thiếu bảng giá; người thao tác mặc định là Owner | Mất giá âm thầm; nếu quên truyền người thao tác thì phiếu được tự duyệt |

## Việc frontend cần làm

Cho đến khi xong phần này, nút đổi giá hiện tại trên Admin sẽ nhận `410` và không đổi được giá.

1. **Đổi giá:** bỏ `updateVariantPrices` trong `apiAdapter.ts`, thay bằng màn hình tạo phiếu. Giữ nguyên `Idempotency-Key` qua các lần retry.
2. **Màn hình mới:** danh sách phiếu chờ duyệt, duyệt và từ chối, lịch giá sắp tới, hủy giá đã lên lịch, lịch sử giá theo SKU.
3. **Form sửa SKU và đơn vị:** bỏ ô giá hoặc đừng gửi trường giá. Form tạo mới vẫn giữ ô giá ban đầu.
4. **Giá `null`:** `retailPrice`, `wholesalePrice`, `salePrice` có thể là `null`, kể cả trên POS. Hiển thị "Chưa có giá", không hiểu là 0đ; dùng `baseUnitPriceMissing` để cảnh báo.
5. **`PRICE_MISMATCH`:** đọc `extensions.currentPrices` (`skuId`, `skuCode`, `unitId`, `currentPrice`), hiện giá mới để người bán xác nhận rồi gửi lại.
6. **Đơn Admin:** thêm lựa chọn `priceListCode` (`retail`, `wholesale`). Đổi đơn vị gốc xong phải tạo phiếu giá cho đơn vị mới.

## Hạn chế còn lại

- Cột `retail_price`, `wholesale_price`, `sale_price` vẫn còn và vẫn được ghi lúc tạo SKU, đơn vị, nhưng luồng bán không đọc. Việc xóa để sang đợt sau.
- Trigger chặn sửa `order_items` sau khi xác nhận chưa bao gồm `sku_unit_price_id`, `price_source`; hiện không có đường code nào sửa hai cột này.
- Bước kiểm tra đầu migration chạy cả khi SQL nhúng chạy lại: nếu có SKU thiếu đơn vị gốc, bước nâng cấp sẽ báo lỗi. Code hiện tại luôn tạo đơn vị gốc cho SKU mới.
- Câu hỏi cho thầy ở mục [Cần xác nhận](../Upgrade-recommended/04-bang-gia-theo-thoi-gian.md#cần-xác-nhận) chưa có câu trả lời.
