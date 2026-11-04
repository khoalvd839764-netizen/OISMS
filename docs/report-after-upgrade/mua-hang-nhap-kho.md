# Báo cáo sau nâng cấp: Phiếu mua và nhập kho từ phiếu mua (Upgrade 01-A)

| Mục | Nội dung |
| --- | --- |
| Task | `OISMS-47-U01A` |
| Ngày kiểm tra | 02/11/2026 |
| Đặc tả | [Tài liệu 01](../Upgrade-recommended/01-mua-hang-nhap-kho-gia-von.md) |
| Phạm vi | Backend, test, tài liệu API. **Chưa có giao diện; chưa làm U01-B.** |
| Trạng thái | Backend đã kiểm chứng. **Chưa nên đưa lên môi trường đang dùng thật trước khi frontend cập nhật**, vì form nhập kho hiện tại không tạo được phiếu theo hợp đồng mới. |

> **Cập nhật 04/11/2026:** giao diện của phần này đã có. Kết quả nghiệm thu xem [báo cáo frontend](frontend-coming-soon-va-upgrade.md). Các mục "chưa có giao diện" bên dưới là hiện trạng tại ngày kiểm tra ghi trong bảng.

## Đã làm

- **Dữ liệu:** thêm `purchase_orders`, `purchase_order_items`; thêm `purchase_order_id`, `purchase_order_item_id` vào phiếu nhập. Migration: [`20261102_purchase_orders.sql`](../../docker/postgres/migrations/20261102_purchase_orders.sql).
- **Phiếu mua:** Staff hoặc Owner lập và sửa nháp; Owner duyệt, đóng, hủy. Phiếu đã duyệt không sửa được, kể cả UPDATE trực tiếp bằng SQL.
- **Nhập kho:** phiếu nhập lấy chi nhánh, nhà cung cấp, SKU, đơn vị, hệ số và giá từ phiếu mua. Client chỉ gửi kho và lượng thực nhận.
- **Chặn nhập dư:** kiểm tra lúc lập phiếu để báo sớm, kiểm tra lại sau khóa lúc xác nhận, và có `CHECK` trong database làm chốt cuối.
- **Khóa:** phiếu nhập → phiếu mua → SKU.
- **Phiếu nháp cũ:** không xác nhận được, nhưng hủy được qua API mới.

Contract chi tiết: [API tồn kho, mục 1.1 và 1.2](../05-api-contracts/03-inventory-endpoints.md).

## Kiểm chứng

Chạy trên PostgreSQL 16 riêng, cổng `55447`, biến `OISM_TEST_PORT`; không đụng database đang chạy.

| Lệnh hoặc phép kiểm | Kết quả |
| --- | --- |
| `dotnet build src/backend/OISM.sln -p:UseAppHost=false -v:minimal` | 0 lỗi |
| `dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj -p:UseAppHost=false` | PASS 173/173 |
| `dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -p:UseAppHost=false`, trước khi đồng bộ `develop` | PASS 335/335 |
| Cùng lệnh, trên bản đã đồng bộ `develop` (`df3bac7` về sau) | **Chưa có kết quả tại thời điểm cập nhật báo cáo** |
| `cd src/frontend/admin-dashboard && npm run build` | Đạt |
| `cd src/frontend/pos-app && npm install && npm run build && npm test` | Build đạt; test PASS 51/51 |
| `cd src/frontend/pos-app && npm run lint` | **Thoát mã 1** (xem mục Điểm còn lưu ý) |
| Lọc `PurchaseOrderIntegrationTests`, chạy 3 lần | PASS 7/7 cả 3 lần |
| 10 phiếu 20 kg trên dòng 100 kg xác nhận song song | Đúng 5 thành công, 5 `PURCHASE_ORDER_OVER_RECEIPT`, không có 500 |
| Tạo database từ `oisms_schema.sql`, chạy file migration 2 lần bằng `psql` | Cả 2 lần thoát mã 0; đủ 3 trigger; hàm bảo vệ dòng nhập đúng |

## Lỗi phát hiện khi nghiệm thu và đã sửa

| Lỗi | Hậu quả | Kiểm chứng |
| --- | --- | --- |
| SQL nhúng trong `DatabaseMigrationService` so `NEW.purchase_order_item_id` với chính nó | Có thể đổi liên kết phiếu mua của dòng nhập đã xác nhận | Test mới FAIL trước khi sửa, PASS sau khi sửa |
| Gửi lại cùng key với dòng rỗng chạy so payload trước khi kiểm tra đầu vào | Có thể trả 500 thay vì 400 | Thêm test, PASS |
| Chưa có test cho `PURCHASE_ORDER_ITEM_MISMATCH` | Mã lỗi không được kiểm chứng | Thêm test dòng của phiếu khác và dòng trùng |
| Chưa cập nhật tài liệu API, RBAC, mã lỗi, trạng thái | Người làm frontend thiếu contract | Đã bổ sung |

Các lỗi trên do người nghiệm thu vừa sửa vừa tự kiểm, chưa qua reviewer độc lập.

## Điểm còn lưu ý

- **Test có thể chập chờn:** `WebhookIntegrationTests.AC11_Concurrency_50SubmissionsSameKey...` FAIL 1 lần trong lần chạy toàn bộ đầu tiên (48 thay vì 49 phản hồi 200). Sau đó PASS 8/8 khi chạy riêng và PASS ở lần chạy toàn bộ cuối. Luồng webhook không gọi code phiếu mua. **Cập nhật 04/11/2026:** đã xác định nguyên nhân là `RangeAttribute` theo kiểu `decimal` không an toàn luồng ở lần dùng đầu, nên một request bị 500; đã sửa bằng `DecimalRangeAttribute`.
- **Lệch nhỏ so với thiết kế:** `ISqlLockService` trong `GoodsReceiptService` là tham số tùy chọn, có bản rỗng và SQL dự phòng để test cũ giữ constructor 3 tham số. Chạy thật vẫn dùng bản Infrastructure qua DI. Nên dọn khi có dịp.
- **Lint `pos-app`:** lỗi đã thấy là `react-hooks(rules-of-hooks)` tại `src/components/ReceiptPrintModal.tsx:15`, trên code của `develop`; đợt này không sửa `pos-app`.
- **Cài lại thư viện:** `develop` thêm thư viện mới cho POS, nên sau khi kéo code cần chạy `npm install` trong `src/frontend/pos-app`.
- **Chưa kiểm chứng đầu cuối từ giao diện**, vì frontend chưa làm.
- **Quy tắc chưa có GVHD xác nhận:** ngoài yêu cầu có phiếu mua, các chi tiết như giá 0, Staff không được hủy, tồn đầu kỳ qua kiểm kê vẫn là đề xuất của nhóm.

## Còn lại

- Frontend theo contract mới.
- U01-B: hóa đơn nhà cung cấp, đối chiếu ba bên, chứng từ chênh lệch.
- `scripts/verify_s2_fixes.cjs` còn gọi hợp đồng phiếu nhập cũ.
