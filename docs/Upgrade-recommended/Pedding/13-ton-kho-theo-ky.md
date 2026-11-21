# Upgrade 13 — Tồn kho theo kỳ (đầu kỳ, cuối kỳ, cuối ngày, hiện tại)

Chốt ngày 19/11/2026 qua phiên grill-me, theo feedback "kho phải có rõ tồn theo kỳ, theo tháng, theo ngày". Trạng thái: **đã chốt nghiệp vụ, chưa triển khai.** Liên quan: [Upgrade 12 — ẩn giá với Staff](./12-don-mua-va-cau-hinh-gia.md).

## 1. Định nghĩa

Mọi mốc tính theo ngày nghiệp vụ UTC+07:00, khoảng nửa mở `[đầu kỳ, cuối kỳ)`.

| Loại | Nghĩa | Lấy từ |
| --- | --- | --- |
| **Tồn đầu kỳ** | Tồn thực tế ngay trước `00:00` ngày đầu kỳ | `balance_after` của dòng sổ cuối cùng có `created_at < đầu kỳ` (theo chi nhánh + SKU); không có dòng thì bằng 0 |
| **Tồn cuối kỳ** | Tồn thực tế ngay trước `00:00` ngày sau ngày cuối kỳ | `balance_after` của dòng sổ cuối cùng có `created_at < cuối kỳ` |
| **Tồn cuối ngày** | Tồn cuối kỳ của kỳ dài một ngày | Như tồn cuối kỳ |
| **Tồn hiện tại** | Tồn ngay lúc xem: thực tế, đang giữ chỗ, khả dụng | `inventory_balances` (đã có) |

Quy tắc:

- **Đầu kỳ + Nhập − Xuất = Cuối kỳ.**
- Tồn cuối kỳ này = tồn đầu kỳ kế tiếp.
- Kỳ chứa hôm nay thì tồn cuối kỳ = tồn hiện tại, màn hình ghi "tính đến hiện tại".
- Tồn theo kỳ là **tồn thực tế** (`OnHand`). Giữ chỗ không ghi sổ nên chỉ có ở tồn hiện tại.
- Sổ cái chỉ thêm dòng với thời điểm thật, nên số của ngày đã qua không đổi khi xem lại.

## 2. Quyết định đã chốt

| # | Quyết định |
| --- | --- |
| Q1 | Xem theo **chi nhánh**, có lựa chọn "Tất cả" để cộng tổng cửa hàng. Không xem theo kho con, vì `balance_after` và WAC tính theo chi nhánh. |
| Q2 | Bộ chọn kỳ: **Hôm nay, Hôm qua, một ngày bất kỳ, Tháng này, Tháng trước, chọn tháng, Khoảng tùy chọn**. Mặc định tự đặt: khoảng tối đa 366 ngày, không chọn ngày tương lai. |
| Q3 | Owner xem đầy đủ. **Staff chỉ xem số lượng**, các cột giá trị trả `null` (che qua dịch vụ dùng chung của Upgrade 12). Cashier không xem. |
| Q4 | Nhập trong kỳ tách: mua, khách trả hàng, chuyển đến, kiểm kê thừa/điều chỉnh. Xuất trong kỳ tách: bán POS, bán TMĐT (gộp các sàn), bán đơn Admin, chuyển đi, kiểm kê thiếu/điều chỉnh. Mặc định hiện tổng, bấm để mở chi tiết. Loại lấy từ cột nguồn của dòng sổ; kênh bán lấy từ đơn hàng. |
| Q5 | **Xuất Excel (.xlsx)** và **in từ trình duyệt** (CSS print như Gói 4). File xuất theo đúng cột người xem được phép thấy. |
| Q6 | **Thẻ kho:** bấm một dòng SKU thì mở các dòng sổ của SKU tại chi nhánh trong kỳ. Dòng đầu là tồn đầu kỳ; mỗi dòng có ngày, chứng từ (bấm để mở truy vết), nhập, xuất, tồn sau; dòng cuối là tồn cuối kỳ. In và xuất Excel được. Dùng lại sổ cái và API truy vết đã có. |
| Q7 | **Luôn hiện tất cả SKU**, kể cả SKU không tồn và không phát sinh. Có lọc theo danh mục, tìm theo tên/mã, phân trang, dòng tổng cộng. |
| Q8 | Màn **Tồn kho** có thêm tab **"Tồn theo kỳ"** cạnh tồn hiện tại, không tách màn riêng. Giá trị chỉ Owner thấy. |

### Chỉnh sau đánh giá kế toán và IT (19/11/2026)

| # | Quyết định |
| --- | --- |
| Q9 | **Hàng đang chuyển kho** (phiếu `InTransit`: chi nhánh đi đã ghi `OUT`, chi nhánh đến chưa ghi `IN`). Xem **"Tất cả"**: không tính chuyển đi/chuyển đến vào nhập/xuất (chuyển nội bộ không đổi tổng cửa hàng), thêm cột **"Hàng đang chuyển"** ở đầu kỳ và cuối kỳ (phiếu đã gửi chưa nhận tại mốc). Đối chiếu: Σ tồn chi nhánh + hàng đang chuyển = tổng cửa hàng. Xem **một chi nhánh**: giữ chuyển đi/chuyển đến trong nhập/xuất, thêm cột "Đang chuyển đến" để tham khảo. |
| Q11 | Xuất Excel từ **backend bằng ClosedXML** (MIT), ghi dạng luồng, dùng chung dịch vụ che giá. Không dùng gói `xlsx` (SheetJS) trên npm. Thay cho cách làm chưa nêu rõ ở Q5. |
| Q12 | **Phân trang SKU trước**, rồi mới tổng hợp sổ cái cho các SKU trong trang. Test đo với khoảng **100.000 dòng sổ**, mục tiêu **< 2 giây/trang**, ghi vào kiểm chứng NFR. Chưa làm bảng chốt số dư. |
| Q14 | Bản Staff (chỉ số lượng) gọi là **"Thẻ kho (S12-DN)"**; bản Owner (có giá trị) gọi là **"Sổ chi tiết hàng hóa (S10-DN)"**. Khi kỳ tính đến hiện tại, Owner thấy dòng **"Chênh lệch làm tròn"** nếu giá trị cuối kỳ theo sổ cái khác `OnHand × WAC`. |

## 3. Cách tính

| Cột | Số lượng | Giá trị (chỉ Owner) |
| --- | --- | --- |
| Đầu kỳ | `balance_after` dòng cuối trước đầu kỳ | Σ(IN × `unit_cost`) − Σ(OUT × `unit_cost`) với `created_at < đầu kỳ` |
| Nhập / Xuất | Σ `quantity` theo `type` và loại trong kỳ | Σ `quantity × unit_cost` trong kỳ |
| Cuối kỳ | `balance_after` dòng cuối trước cuối kỳ | Đầu kỳ + nhập − xuất |

- Không thêm bảng, không thêm cột: `inventory_transactions` đã có `unit_cost`, `balance_after` và index `ix_inventory_transactions_sku_history (tenant_id, branch_id, sku_id, created_at, id)`.
- Giá trị theo sổ cái có thể lệch vài đồng so với `OnHand × WAC` do làm tròn WAC (6 chữ số). Báo cáo kỳ lấy theo sổ cái để công thức đối chiếu luôn khớp.
- Tenant filter của `AppDbContext` áp như mọi truy vấn khác.

## 4. Ngoài phạm vi (hướng mở rộng)

- Tồn dự kiến (sắp về từ đơn mua, đang giữ chỗ). Hàng đang chuyển kho đã đưa vào phạm vi ở Q9.
- Tuổi tồn và hàng chậm bán đầy đủ (hiện có `inventory-velocity`).
- Báo cáo tổng hợp chênh lệch kiểm kê theo kỳ.
- Khóa kỳ / chốt sổ cuối tháng.
- Xem theo kho con.
- Bảng chốt số dư tháng (chỉ cần khi dữ liệu lớn).

## 5. Phiếu xuất kho bán hàng (chốt 19/11/2026)

Chuyển kho và kiểm kê đã có phiếu riêng, giữ nguyên. Phần này chỉ áp dụng cho **xuất bán** (POS, TMĐT, đơn Admin).

| # | Quyết định |
| --- | --- |
| P1 | ~~Mẫu in dựng từ dữ liệu có sẵn, không thêm bảng~~ — **đổi lại**: phiếu xuất kho là **chứng từ được lưu kèm dòng bút toán kho** để đối chiếu. Thêm bảng `goods_issues` (số phiếu, tenant, chi nhánh, kho, đơn hàng, hóa đơn bán, kênh, thời điểm xuất, người lập) và `goods_issue_items` (`inventory_transaction_id` **UNIQUE**, dòng đơn, SKU, đơn vị, SL, SL gốc, giá vốn chụp = `unit_cost` của dòng sổ). Phiếu được lập **trong cùng transaction** với dòng `OUT` khi POS checkout và khi xác nhận đơn (TMĐT, Admin). Cấu trúc `inventory_transactions` **không đổi**; dòng phiếu trỏ vào dòng sổ. Mỗi dòng `OUT` bán hàng có đúng một dòng phiếu và ngược lại. |
| P6 | Dữ liệu cũ: **không lập bù.** Khi triển khai, tạo lại DB từ schema và seed lại dữ liệu demo để mọi dòng `OUT` có phiếu từ đầu. Xóa volume local hoặc DB demo AWS do người dùng tự xác nhận và thực hiện (không tự chạy `docker compose down -v`). Migration vẫn phải có để thêm bảng cho DB đã tồn tại, nhưng **dừng và báo lỗi hướng dẫn reset** nếu DB đã có dòng `OUT` bán hàng (Q10). Vì vậy kiểm tra "dòng `OUT` bán hàng không có phiếu = 0" luôn đúng, không cần mốc nâng cấp. |
| P2 | **POS:** quầy giữ nguyên, chỉ in hóa đơn cho khách. Admin in lại phiếu xuất của một đơn khi cần, và in **"Bảng kê xuất bán POS"** theo ngày + chi nhánh: tổng SL theo SKU kèm danh sách hóa đơn. |
| P3 | **TMĐT và đơn Admin:** đơn đã xác nhận (đã trừ kho) có nút **"In phiếu xuất kho"**, dùng làm phiếu soạn hàng. Chọn nhiều đơn để **in hàng loạt** một lượt. Không tự bật hộp thoại in khi xác nhận. |
| P4 | Nội dung theo **mẫu 02-VT**: số phiếu, ngày xuất (thời điểm ghi sổ), chi nhánh/kho xuất, lý do (Bán POS/TMĐT/Admin + số đơn, số hóa đơn), dòng hàng (mã, tên, đơn vị, SL, SL quy đổi đơn vị gốc), chữ ký người lập, thủ kho, người nhận. **Owner** in bản có đơn giá vốn và thành tiền; **Staff** in bản không có giá; **Cashier** không in. |
| P5 | **Mỗi kho một phiếu.** Số phiếu `PXK-<số hóa đơn>` khi đơn xuất từ một kho; khi xuất từ nhiều kho thì `PXK-<số hóa đơn>-1`, `-2`... theo thứ tự kho cố định (sắp theo id kho), tên kho in trên phiếu (Q13: bảng `warehouses` không có mã kho duy nhất, không thêm). Số phiếu suy từ số hóa đơn nên không cần bộ đếm, không có điểm nghẽn khi bán đồng thời. |

In bằng CSS print như Gói 4. Bảng kê và phiếu dùng chung dịch vụ che giá của Upgrade 12.

**Rủi ro cao:** P1 chạm transaction POS checkout và xác nhận đơn (luồng giữ chỗ, khóa tồn, ghi sổ). Phiếu phải lập sau khi ghi dòng `OUT`, trong cùng transaction, không thêm khóa mới; retry idempotent trả lại phiếu cũ. Bắt buộc test trên PostgreSQL thật, gồm cả test đồng thời 50 request/1 SKU hiện có.

**Schema:** thêm 2 bảng (48 → 50): file migration mới trong `docker/postgres/migrations/`, cập nhật `oisms_schema.sql`, `DatabaseMigrationService.RequiredTables`, số bảng trong `init-oisms.sh`, EF configuration, tenant query filter trong `AppDbContext`, ràng buộc `UNIQUE (tenant_id, number)` và `UNIQUE (tenant_id, inventory_transaction_id)`.

**Đối chiếu:** màn thẻ kho và truy vết hiện số phiếu xuất cho mỗi dòng `OUT` bán hàng; có kiểm tra "dòng `OUT` bán hàng không có phiếu" (phải luôn bằng 0).

## 6. Kiểm thử và tài liệu

| Phần | Việc |
| --- | --- |
| Test | Với mọi SKU và chi nhánh: đầu kỳ + nhập − xuất = cuối kỳ (số lượng và giá trị). Cuối kỳ "hôm nay" khớp `inventory_balances.OnHand`. Cuối ngày D = đầu ngày D+1. Ranh giới UTC+7 (giao dịch 23:59 và 00:00). Tách đúng loại và kênh. Chế độ "Tất cả" có phiếu chuyển `InTransit` vắt qua mốc: tổng chi nhánh + hàng đang chuyển = tổng cửa hàng, chuyển nội bộ không vào nhập/xuất. Hiệu năng 100k dòng sổ < 2 giây/trang. Migration Upgrade 13 từ chối chạy trên DB đã có `OUT` bán hàng. Staff nhận `null` ở cột giá trị và trong file Excel. Tenant khác không thấy dữ liệu. Phiếu xuất kho: mọi dòng `OUT` bán hàng có đúng một dòng phiếu; checkout/confirm lỗi giữa chừng thì không còn phiếu mồ côi (rollback cùng sổ); retry cùng key không tạo phiếu thứ hai; SL và giá vốn khớp dòng sổ `OUT`; đơn nhiều kho ra đúng số phiếu; Staff không có giá; Cashier bị 403; bảng kê POS theo ngày khớp tổng xuất bán POS trong báo cáo kỳ cùng ngày. |
| API | Endpoint báo cáo kỳ và thẻ kho, cập nhật `docs/05-api-contracts/`. |
| Docs | Đặc tả báo cáo FR-REP, màn hình trong `docs/11-ui-screens/`, ma trận RBAC, RTM/checklist, báo cáo sau nâng cấp. |

## 7. Công việc backend và frontend (sơ bộ)

Đường dẫn tính từ `src/`.

### Backend

| # | Việc | File chính |
| --- | --- | --- |
| BE-13.1 | Service báo cáo NXT theo kỳ: đầu kỳ/cuối kỳ từ `balance_after`, nhập/xuất tách loại (theo cột nguồn của dòng sổ) và kênh (theo đơn hàng), giá trị Σ `quantity × unit_cost`; chế độ chi nhánh / "Tất cả" (bỏ chuyển nội bộ, cột hàng đang chuyển); phân trang SKU trước khi tổng hợp; mốc UTC+7 nửa mở | `Features/Reports/` (service + DTO mới), `ReportsController.cs` |
| BE-13.2 | Thẻ kho S12 (Staff, số lượng) / sổ chi tiết S10 (Owner, giá trị) của một SKU × chi nhánh × kỳ; dòng chênh lệch làm tròn khi kỳ tới hiện tại | như trên |
| BE-13.3 | Xuất Excel bằng ClosedXML qua interface ở Application, cài đặt ở Infrastructure; ghi dạng luồng; Staff không có cột giá trị | `Application/Common/Interfaces`, `OISM.Infrastructure` |
| BE-13.4 | Migration 2 bảng `goods_issues`, `goods_issue_items` (`inventory_transaction_id` UNIQUE); **dừng nếu DB đã có dòng `OUT` bán hàng**; entity, Configuration, query filter; `RequiredTables` 48 → 50; `init-oisms.sh` | `docker/postgres/migrations/`, `oisms_schema.sql`, `OISM.Domain`, `OISM.Infrastructure` |
| BE-13.5 | Lập phiếu xuất trong cùng transaction với dòng `OUT`: POS checkout và xác nhận đơn; số `PXK-<số hóa đơn>[-n]`; replay trả phiếu cũ; không thêm khóa | `PosCheckoutService.cs` (cạnh chỗ lập hóa đơn), `OrderService.cs` (cạnh chỗ lập hóa đơn) |
| BE-13.6 | API phiếu xuất: xem theo đơn, xem chi tiết, bảng kê xuất bán POS theo ngày + chi nhánh; Owner có giá vốn, Staff không, Cashier 403; sổ cái/truy vết trả số phiếu xuất | Controller mới, `InventoryService` (DTO sổ cái/truy vết) |

### Frontend Admin

| # | Việc | File chính |
| --- | --- | --- |
| FE-13.1 | Tab "Tồn theo kỳ" trong màn Tồn kho: bộ chọn kỳ, chi nhánh / Tất cả, bảng đầu kỳ–nhập–xuất–cuối kỳ (mở chi tiết loại/kênh), cột hàng đang chuyển, phân trang, tìm kiếm, lọc danh mục, dòng tổng; Excel và in | component mới, chèn tab tối thiểu vào `pages/InventoryPage.tsx` |
| FE-13.2 | Thẻ kho S12/S10 mở từ dòng SKU, bấm chứng từ để mở truy vết; in, Excel | component mới |
| FE-13.3 | In phiếu xuất kho 02-VT (một đơn, in hàng loạt), bảng kê xuất bán POS theo ngày; bản Staff không giá | component mới theo mẫu `components/orders/ShippingSlipPrintModal.tsx`, `pages/OrdersPage.tsx` |

## 8. Gói công việc

| Gói | Nội dung | Mức | Thứ tự |
| --- | --- | --- | --- |
| **Gói 2** | BE-13.1 → BE-13.3, FE-13.1, FE-13.2 (chỉ đọc, không schema) | CRITICAL (quyền xem giá trị) | Song song với Gói 1, 3 |
| **Gói 3** | BE-13.4 → BE-13.6, FE-13.3 | CRITICAL (schema, transaction checkout/confirm) | Song song với Gói 1, 2 |

Prompt giao Anti: [`skills/claude-to-antigravity/references/OISMS-66-lan2-prompts.md`](../../../skills/claude-to-antigravity/references/OISMS-66-lan2-prompts.md).
