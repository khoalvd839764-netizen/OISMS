# Upgrade 12 — Đơn mua, ẩn giá với Staff và cấu hình giá bán khi nhập kho

Chốt ngày 19/11/2026 qua phiên grill-me, theo feedback:

1. Bỏ hóa đơn NCC vì trùng với đơn mua.
2. Ẩn giá tiền khi nhập kho đối với Staff. Giá mua cấu hình trên đơn mua.
3. Đơn mua mang theo giá bán lẻ và giá bán sỉ. Owner chọn cách đổi giá bán khi hàng nhập kho.

Trạng thái: **đã chốt nghiệp vụ. Gói 1 (BE-12.1→12.6, FE-12.1, FE-12.2) đã triển khai 19/11/2026** ([báo cáo](../../report-after-upgrade/don-mua-pending-approval-u12.md)); Gói 4 và Gói 5 chưa triển khai. Tài liệu này thay đổi yêu cầu U01-B ([Done/01](../Done/01-mua-hang-nhap-kho-gia-von.md), [báo cáo 01-B](../../report-after-upgrade/hoa-don-nha-cung-cap-u01b.md)) và ma trận RBAC ([02-rbac-matrix.md](../../01-users-and-rbac/02-rbac-matrix.md)).

Phần tồn kho theo kỳ (ngày, tháng, khoảng tùy chọn) chưa bàn, sẽ ghi ở tài liệu riêng.

## Tóm tắt luồng sau khi áp dụng

### A. Staff đề nghị mua

- Staff tạo đơn mua `Draft`: chi nhánh, SKU, đơn vị, số lượng. Không có NCC, không có giá.
- **Chỉ người tạo mới được sửa hoặc gửi duyệt đơn nháp của mình (Q11, Q27).** Người khác (kể cả Owner) xem đơn nháp ở chế độ chỉ đọc ("Staff đang soạn — chỉ xem"), không có nút Sửa/Gửi duyệt. Owner vẫn có quyền Hủy (`Cancel`) đơn nháp của bất kỳ ai (Q11).
- Người tạo bấm **Gửi** → `PendingApproval`. Từ đây Staff không sửa được nữa.
- Owner tự tạo đơn thì đơn vào thẳng `PendingApproval`. Đơn do Owner tạo nếu bị Trả về thành `Draft` thì Owner là người tạo nên sửa và gửi duyệt bình thường.

### B. Owner chốt đơn ở `PendingApproval` (lưu được nhiều lần & quy trình Thiết lập 4 bước Q28)

- Đơn ở `PendingApproval` có checklist điều kiện duyệt trên giao diện: NCC (✓/✗), Đơn giá mua đủ mọi dòng (✓/✗), Ngày đặt hợp lệ (✓/✗), Đã xem cấu hình giá bán (✓/✗). Màn chi tiết **không còn nút Duyệt**; bảng cấu hình giá bán ở màn chi tiết chỉ để xem. Duyệt chỉ ở bước cuối của trình hướng dẫn.
- Owner bấm **"Thiết lập đơn mua"** để mở trình hướng dẫn 4 bước (cập nhật 21/11/2026 theo góp ý người dùng):
  - **Bước 1 — Nhà cung cấp:** Chọn **NCC** hoặc **thêm NCC mới ngay tại chỗ** (`+ Thêm NCC`), chọn **ngày đặt hàng** (mặc định hôm nay theo giờ máy, không chọn được ngày tương lai). Bấm "Tiếp tục" (chưa lưu).
  - **Bước 2 — Giá mua:** nhập **đơn giá mua** từng dòng (gợi ý lần mua gần nhất và WAC hiện tại), phí vận chuyển, chiết khấu thương mại, VAT; xem giá vốn dự kiến sau phân bổ. Bấm "Lưu & tiếp tục" để lưu NCC, ngày đặt và giá (PUT có `version`). Cấu hình giá bán đã lưu **được giữ nguyên** khi sửa lại giá mua.
  - **Bước 3 — Giá bán:** cấu hình giá bán từng đơn vị bán (giá lẻ, giá sỉ) theo *Giữ giá cũ*, *WAC + lãi*, hoặc *Giá mới*; công tắc nhập theo **Lãi gộp trên giá bán** hoặc **Cộng thêm trên giá vốn** (Q29). Giá dự kiến tính theo giá vốn vừa lưu ở Bước 2. Bấm "Lưu & xem lại".
  - **Bước 4 — Xem lại & duyệt:** tóm tắt NCC, ngày đặt, từng dòng (giá mua, giá vốn dự kiến), tổng tiền, giá bán sẽ áp khi nhập kho; giá 0đ hoặc bán lỗ có ô tích xác nhận ngay trên màn; tích đủ mới bật **"Duyệt đơn mua"** → `Approved`, chốt giá vốn sau phân bổ.
- Ngoài ra, Owner có thể bấm:
  - **Trả về** (bắt buộc lý do) → `Draft` cho Staff sửa.
  - **Hủy** → `Cancelled`.

### C. Staff nhận hàng và nhập kho

- Staff thấy NCC và số lượng đặt, **không thấy giá**. Staff nhập số lượng thực nhận; được nhập nhiều đợt nhưng không vượt số lượng đặt.
- Xác nhận phiếu nhập chạy **trong một transaction**:
  1. Khóa đơn mua → khóa tồn SKU → khóa giá (advisory lock theo SKU + đơn vị + bảng giá).
  2. Cộng `OnHand`, ghi sổ `IN`. Giá nhập cho WAC = giá sau phân bổ ÷ hệ số.
  3. Tính lại **WAC** của chi nhánh nhận hàng.
  4. Nếu là **phiếu nhập đầu tiên** của dòng và dòng có cấu hình giá bán: sinh phiếu đổi giá (lý do "Theo đơn mua PO-xxx") và dòng giá mới, `valid_from = max(ngày Owner chọn, lúc nhập)`. Giá "WAC + lãi" tính theo WAC vừa cập nhật ở bước 3. Đánh dấu `price_applied_at` để không sinh giá lần nữa.
- Đơn mua → `PartiallyReceived`, nhận đủ thì → `Received`.
- NCC không giao đủ thì Owner bấm **Đóng** (bắt buộc lý do). Phần phí của hàng chưa nhận không vào giá vốn.

### D. Các tình huống đổi giá

- **NCC báo giá khác trước khi giao:** Owner đóng đơn cũ, tạo đơn mới. Đơn cũ không bị sửa.
- **NCC xuất hóa đơn khác giá sau khi giao:** giá đơn mua là giá cam kết, chênh lệch xử lý ngoài hệ thống.
- **Owner đổi giá bán tay** (khan hàng, sale...) qua phiếu đổi giá, bắt buộc chọn loại lý do. Nếu SKU đó đang có giá chờ áp từ đơn mua chưa nhập, hệ thống hỏi: giữ giá vừa đặt (dòng trên đơn mua chuyển về "Giữ giá cũ"), hay đến khi nhập kho vẫn cập nhật theo đơn mua.

### E. Ai thấy gì

| Thông tin | Owner | Staff |
| --- | --- | --- |
| SKU, số lượng, trạng thái đơn mua | ✓ | ✓ |
| NCC | ✓ | ✓ từ khi đơn được duyệt |
| Giá mua, phí, chiết khấu, VAT, giá bán đề xuất | ✓ | ✗ (`null`) |
| Giá vốn, WAC, giá trị tồn, lãi gộp (mọi màn) | ✓ | ✗ (`null`) |
| Giá bán niêm yết (đơn sàn, POS) | ✓ | ✓ |

Che giá qua một dịch vụ dùng chung, có test quét mọi endpoint bằng tài khoản Staff. Sự kiện SignalR không gửi giá.

### F. Thay đổi so với hiện tại

- **Bỏ:** màn hình và API hóa đơn NCC, bước ghi đè giá khi xác nhận nhập. Bảng cũ giữ để tra cứu.
- **Thêm:** trạng thái `PendingApproval`, ngày đặt hàng, phân bổ phí và chiết khấu vào giá vốn, cấu hình giá bán theo đơn mua, loại lý do của phiếu đổi giá.
- **Dữ liệu cũ:** đơn `Draft` đã có đủ NCC và giá chuyển sang `PendingApproval`; phiếu nhập nháp xác nhận theo giá đơn mua.

## 1. Luồng đơn mua

```
Staff:  Draft (SKU, đơn vị, số lượng; không NCC, không giá) ──Gửi──▶ PendingApproval
Owner tự tạo đơn ─────────────────────────────────────────────────▶ PendingApproval
Owner ở PendingApproval: chọn NCC, ngày đặt hàng, giá mua, VAT, chiết khấu, phí vận chuyển,
                         cấu hình giá bán; sửa số lượng hoặc bỏ dòng; lưu nhiều lần
  ├─ Duyệt  ──▶ Approved ──▶ PartiallyReceived / Received / Closed
  ├─ Trả về ──▶ Draft (bắt buộc lý do)
  └─ Hủy    ──▶ Cancelled
```

| # | Quyết định |
| --- | --- |
| Q1 | Gỡ API, màn hình và đoạn ghi đè giá lúc xác nhận phiếu nhập (`GoodsReceiptService.cs`, đoạn áp giá hóa đơn đã duyệt). Bảng hóa đơn NCC **vẫn giữ** để lưu dữ liệu cũ, không ghi thêm. Số bảng không đổi (48). |
| Q2 | Dùng chung bảng `purchase_orders`: `Draft` đóng vai trò đề nghị mua. `supplier_id` và `purchase_order_items.unit_price` được `NULL` khi đơn ở `Draft`/`PendingApproval`; CHECK bắt buộc có đủ khi chuyển sang `Approved` trở đi. |
| Q3 | Thêm trạng thái `PendingApproval`. Owner lưu nháp NCC và giá, kiểm tra xong mới bấm Duyệt (thao tác riêng). Staff không sửa được đơn ở trạng thái này. |
| Q4 | Owner được sửa số lượng và bỏ dòng khi đơn ở `PendingApproval`. |
| Q5 | Đơn do Owner tạo đi thẳng vào `PendingApproval`, nhập luôn NCC và giá. |
| Q11 | Staff: Gửi đề nghị và Hủy `Draft` của mình, không rút lại được đơn đã gửi. Owner: Duyệt, Trả về (kèm lý do), Hủy. |
| Q14 | Thêm **ngày đặt hàng với NCC** do Owner chọn, mặc định hôm nay, không được chọn ngày tương lai. Báo cáo mua hàng theo kỳ lọc theo ngày này. |
| Q27 | **Quyền đơn nháp (U12-G6):** Chỉ người tạo (`CreatedBy == userId`) được sửa hoặc gửi duyệt đơn nháp `Draft`. Người khác (kể cả Owner) cố tình sửa hoặc gửi duyệt bị từ chối `403 PURCHASE_ORDER_NOT_CREATOR`. Owner xem đơn `Draft` của người khác ở chế độ chỉ đọc ("Staff đang soạn — chỉ xem"), vẫn có quyền Hủy (`Cancel`) đơn nháp của bất kỳ ai (Q11). Đơn do Owner tạo nếu bị Trả về thành `Draft` thì Owner là người tạo nên sửa và gửi duyệt bình thường. |
| Q28 | **Thiết lập đơn mua trước khi duyệt (U12-G6):** Đơn `PendingApproval` có checklist điều kiện duyệt trên UI: NCC, giá mua đủ mọi dòng, ngày đặt hợp lệ, đã xem cấu hình giá bán. Nút "Duyệt đơn mua" ở màn chi tiết bị khóa cho tới khi `approvalReadiness.isReady`. Trình hướng dẫn 4 bước (cập nhật 21/11/2026): B1 Nhà cung cấp (chọn hoặc thêm NCC mới), B2 Giá mua (lưu PUT), B3 Giá bán (lưu cấu hình), B4 Xem lại & duyệt (ô tích xác nhận giá 0đ, bán lỗ trên màn). Màn chi tiết không có nút Duyệt, bảng giá bán chỉ xem. Sửa giá mua không làm mất cấu hình giá bán đã lưu. |

## 2. Giá mua

| # | Quyết định |
| --- | --- |
| Q6 | Giá gợi ý cho từng dòng = giá của đơn đã duyệt gần nhất cùng NCC, SKU và đơn vị, kèm WAC hiện tại quy theo đơn vị để so sánh. Không thêm bảng giá NCC. |
| Q7 | Giá trên đơn đã duyệt **không bao giờ đổi**. NCC đổi giá thì Owner đóng phần còn lại (thao tác Close đã có) rồi tạo đơn mới. |
| Q8 | ~~VAT, chiết khấu, phí vận chuyển chỉ lưu tham khảo~~ — thay bằng Q20–Q22 để đúng VAS 02. |
| Q20 | **Phân bổ theo VAS 02:** phí vận chuyển ở đầu đơn được cộng vào giá vốn, chiết khấu thương mại ở đầu đơn được trừ khỏi giá vốn, chia cho các dòng theo tỷ lệ giá trị dòng (`SL × đơn giá`). |
| Q21 | Phân bổ **tính sẵn lúc duyệt**: mỗi dòng lưu cố định `giá vốn đơn vị sau phân bổ` (theo đơn vị mua). Mỗi đợt nhập dùng `giá sau phân bổ / FactorSnapshot` làm giá nhập cho WAC, nhân với SL thực nhận. Đơn bị Đóng khi còn thiếu hàng thì phần phí và chiết khấu của hàng chưa nhận không vào giá vốn (ghi chú trên đơn). Công thức WAC không đổi. |
| Q22 | VAT đầu vào **không vào giá vốn**. Giả định cửa hàng là doanh nghiệp khấu trừ thuế; hộ kinh doanh nộp thuế trực tiếp (VAT vào giá vốn) là hướng mở rộng. VAT vẫn lưu ở đầu đơn để hiện tổng thanh toán. |
| Q23 | Giá trên đơn mua đã duyệt là **giá cam kết với NCC**, không đổi khi giao hàng hay khi NCC xuất hóa đơn. Không còn đối chiếu ba chiều với hóa đơn, không có chứng từ điều chỉnh giá vốn, không lưu số hóa đơn đầu vào. Chênh lệch hóa đơn xử lý ngoài hệ thống. |
| Q12 | Migration: đơn `Draft` cũ đã có đủ NCC và giá chuyển sang `PendingApproval`. Phiếu nhập `Draft` đang có hóa đơn NCC đã duyệt sẽ xác nhận theo giá đơn mua, hóa đơn cũ chỉ còn để xem. Ghi rõ trong báo cáo nâng cấp. |

## 3. Phân quyền xem giá

| # | Quyết định |
| --- | --- |
| Q9 | Staff thấy NCC kể từ khi đơn được duyệt (để biết ai giao hàng). Giá mua, tổng tiền, VAT, chiết khấu, phí, giá bán đề xuất **trả `null` cho Staff ở mọi trạng thái**, ẩn ngay ở dữ liệu trả về, không chỉ ẩn trên giao diện. |
| Q10 | Giá vốn, WAC, giá trị tồn, lãi gộp chỉ Owner xem, **ở mọi nơi**: chi tiết đơn hàng (`OrderItemDto.CostPrice`, `TotalCost` hiện đang trả cho Staff), truy vết chứng từ, thẻ kho, báo cáo tồn, phiếu nhập, phiếu trả hàng, tool của Trợ lý AI. Phải sửa ma trận RBAC. |

| Q24 | Che giá qua **một dịch vụ dùng chung** quyết định trường nhạy cảm theo vai trò; mọi service gọi trước khi trả DTO. Phạm vi gồm cả tìm kiếm toàn hệ thống (CS-04), in chứng từ (Gói 4), catalog webhook, tool Trợ lý AI. Sự kiện SignalR chỉ gửi id, không gửi giá. Có **test tích hợp quét endpoint**: đăng nhập Staff, gọi mọi GET liên quan kho, mua, đơn, báo cáo, khẳng định các trường giá mua/giá vốn/WAC/lãi gộp là `null` hoặc không có. |

Giá bán (niêm yết, trên đơn sàn và POS) Staff **vẫn thấy**. Staff xác nhận đơn TMĐT không cần giá vốn; `OrderItem.CostPrice` vẫn được hệ thống tự chụp lúc Confirm.

## 4. Cấu hình giá bán trên đơn mua

Ở `PendingApproval`, Owner cấu hình giá bán cho từng dòng, theo **từng đơn vị bán** và từng bảng giá (`retail`, `wholesale`). Có ba lựa chọn:

| Lựa chọn | Giá áp dụng |
| --- | --- |
| **Giữ giá cũ** (mặc định) | Không sinh dòng giá mới |
| **WAC + lãi** | `WAC ÷ (1 − %lãi gộp)`, làm tròn **lên** bội số 1.000đ (Q16). %lãi gộp mặc định bằng lãi gộp hiện tại của SKU và đơn vị đó, Owner sửa được. SKU chưa có giá bán hoặc WAC = 0 thì Owner bắt buộc nhập % (Q15). |
| **Giá mới** | Owner nhập, có nhãn nguồn: NCC đề xuất hoặc Owner tự đặt |

Thời điểm áp dụng: **ngay khi nhập kho** hoặc **từ ngày Owner chọn**.

| # | Quyết định |
| --- | --- |
| Q17 | Giá chỉ đổi **một lần**, ở phiếu nhập đầu tiên có dòng đó được xác nhận. Các đợt nhập sau không đổi giá. "WAC + lãi" dùng WAC của **chi nhánh nhận hàng** ngay sau bước cập nhật WAC. Giá bán áp cho toàn cửa hàng. |
| Q18 | `valid_from = max(ngày Owner chọn, thời điểm xác nhận nhập)`. Hàng chưa về thì giá cũ vẫn giữ. |
| Q19 | Khi Owner tạo hoặc duyệt phiếu đổi giá tay cho SKU, đơn vị và bảng giá đang có cấu hình giá trên một đơn mua chưa nhập, hệ thống hỏi: **giữ giá vừa đặt** (dòng đó trên đơn mua chuyển về "Giữ giá cũ") hay **đến khi nhập kho thì cập nhật theo đơn mua** (giá vừa đặt chỉ dùng tạm). |
| — | **Không làm giá theo lô.** Mỗi SKU và đơn vị có một giá tại một thời điểm, vì hàng cũ và mới chung tồn, và trên sàn mỗi SKU chỉ có một giá. |

| Q25 | Khóa đồng thời giữa đổi giá tay và sinh giá lúc nhập kho: `pg_advisory_xact_lock` theo **SKU + đơn vị + bảng giá**, lấy sau khi mở transaction. Thứ tự khóa: đơn mua → tồn SKU (sắp theo `SkuId`) → giá (sắp theo khóa giá). Luồng đến sau đọc lại trạng thái rồi mới quyết định. Có test đồng thời trên PostgreSQL thật. |
| Q29 | **% lãi gộp và chế độ nhập cộng thêm (U12-G6):** Giữ nguyên công thức Q16 `giá = WAC ÷ (1 - %lãi gộp)`, làm tròn lên bội 1.000đ. Backend và frontend bắt buộc `0 <= marginPercent < 100`, ngoài khoảng từ chối `400 VALIDATION_ERROR`. FE hỗ trợ công tắc "Nhập theo: Lãi gộp trên giá bán | Cộng thêm trên giá vốn": ở chế độ cộng thêm người dùng nhập k (k >= 0), FE quy đổi sang lãi gộp `m = k ÷ (100 + k) × 100` để lưu vào DB, không đổi schema hay công thức backend. Dưới ô nhập luôn hiển thị dòng quy đổi trực tiếp. |

Giá sinh từ đơn mua đi qua **phiếu đổi giá** (lý do "Theo đơn mua PO-xxx", gắn `SourceItemId`), được ghi trong **cùng transaction** xác nhận phiếu nhập, sau bước khóa tồn và cập nhật WAC, giữ đúng thứ tự khóa của luồng cũ.

### Mặc định tự chọn, chưa hỏi lại người dùng

- Cấu hình giá theo từng đơn vị bán; đơn vị không cấu hình thì giữ giá cũ, không tự tính theo hệ số.
- Staff xác nhận nhập kho vẫn kích hoạt đổi giá, vì Owner đã duyệt cấu hình trên đơn.
- Cảnh báo bán lỗ khi giá mới (hoặc giá dự kiến) thấp hơn WAC; Owner phải xác nhận mới duyệt được.
- API Duyệt gửi kèm phiên bản đơn để Owner duyệt đúng bản đã kiểm tra (tránh hai tab lưu đè nhau).
- Phiếu đổi giá tay thêm **loại lý do bắt buộc**: Khan hàng, Khuyến mãi/Sale, Theo giá nhập mới, Khác.

## 5. Đánh giá kế toán và IT (19/11/2026)

**Phù hợp:** tách người đề nghị và người duyệt; Staff nhận hàng mà không thấy giá (nhận hàng mù); WAC bình quân sau mỗi lần nhập theo chi nhánh (TT200/133 cho phép); giữ bảng hóa đơn cũ (lưu trữ chứng từ); chứng từ đã duyệt không sửa; đổi giá bán có phiếu và lý do; CHECK ở DB; sinh giá trong cùng transaction nhập kho; khóa phiên bản khi duyệt.

**Giả định và ngoài phạm vi (ghi rõ khi bảo vệ):**

- Cửa hàng khấu trừ VAT (Q22).
- Không đối chiếu ba chiều với hóa đơn NCC; giá đơn mua là giá cam kết (Q23).
- Không theo dõi công nợ phải trả NCC.
- Không có vai trò Kế toán; chỉ Owner xem giá vốn.
- Ngày đặt hàng (Q14) chỉ là ngày chứng từ của đơn mua. Ngày ghi sổ kho và tính WAC luôn là thời điểm xác nhận nhập thật.
- Giá "WAC + lãi" dùng WAC của chi nhánh nhận hàng nhưng áp cho toàn cửa hàng (Q17); phải ghi trong hướng dẫn sử dụng.

**Kỹ thuật bổ sung:** dòng đơn mua có cột kiểu `price_applied_at` để việc sinh giá idempotent (Q17); thu quyền `INSERT`/`UPDATE` của `oisms_app` trên bảng hóa đơn NCC; transaction nhập kho phình thêm khóa giá nên bắt buộc có test đồng thời nhập và bán cùng SKU.

**Triển khai:** một lần, không chia giai đoạn (Q26).

## 6. Phạm vi thay đổi dự kiến

**Rủi ro cao:** có migration, chạm luồng xác nhận phiếu nhập, WAC, bảng giá và transaction. Phải có integration test trên PostgreSQL thật.

| Phần | Việc |
| --- | --- |
| DB | Migration mới trong `docker/postgres/migrations/`: nullable + CHECK cho `supplier_id`/`unit_price`, trạng thái `PendingApproval`, cột ngày đặt hàng, VAT/chiết khấu/phí, giá vốn sau phân bổ theo dòng, `price_applied_at`, lý do trả về; bảng hoặc cột cấu hình giá bán theo dòng và đơn vị; loại lý do của phiếu đổi giá. Chuyển dữ liệu theo Q12. Cập nhật `oisms_schema.sql`, `RequiredTables`, `init-oisms.sh` nếu thêm bảng. |
| Backend | `PurchaseOrder` (trạng thái, Submit/Return), `PurchaseOrderService` (lưu giá ở PendingApproval, duyệt, giá gợi ý), `GoodsReceiptService` (bỏ ghi đè hóa đơn, sinh giá bán lúc nhập đầu tiên), `PriceChangeService` (lý do, cảnh báo Q19), gỡ `SupplierInvoiceService` và controller, che giá ở DTO theo vai trò (đơn mua, phiếu nhập, đơn hàng, truy vết, báo cáo, AI). |
| Frontend Admin | Màn đơn mua (luồng Staff/Owner, bảng tính giá bán), phiếu nhập không còn giá với Staff, gỡ panel hóa đơn NCC, hộp thoại Q19 ở phiếu đổi giá. |
| Test | Chuyển trạng thái và quyền; CHECK constraint; giá vốn lấy từ đơn mua; sinh giá đúng một lần, đúng `valid_from`, rollback cùng phiếu nhập; Staff nhận `null` ở mọi API có giá; migration chạy hai lần không lỗi. |
| Docs | Đặc tả mua hàng FR, `04-state-machines.md`, `02-rbac-matrix.md`, hợp đồng API và mã lỗi mới, U01-B ghi "hủy theo feedback", RTM/checklist, báo cáo sau nâng cấp. |

## 7. Công việc backend và frontend (sơ bộ)

Chia vào các gói giao Antigravity (xem [mục 8](#8-gói-công-việc)). Đường dẫn tính từ `src/`.

### Backend

| # | Việc | File chính |
| --- | --- | --- |
| BE-12.1 | Migration `PendingApproval`: `supplier_id`, `unit_price` nullable + CHECK theo trạng thái; `order_date`; `shipping_fee`, `discount_amount`, `vat_amount`; lý do/người/lúc trả về; `allocated_unit_cost` theo dòng; cột phiên bản để khóa lạc quan; chuyển `Draft` đủ NCC + giá sang `PendingApproval`; thu quyền ghi của `oisms_app` trên bảng hóa đơn NCC | `docker/postgres/migrations/`, `docs/database/oisms_schema.sql`, `DatabaseMigrationService.cs`, EF Configuration |
| BE-12.2 | Domain `PurchaseOrder`: `Submit`, `Return`, `Approve` (chỉ từ `PendingApproval`), `Cancel` theo vai trò | `OISM.Domain/Entities/PurchaseOrder.cs`, `PurchaseOrderStatus` |
| BE-12.3 | `PurchaseOrderService`: Staff tạo/sửa `Draft` không NCC, không giá (bỏ qua trường giá); Owner tạo vào thẳng `PendingApproval`, sửa NCC/giá/phí/SL; `Approve` kiểm tra đủ NCC + giá, xác nhận giá 0, khớp phiên bản, **phân bổ phí/chiết khấu** tính `allocated_unit_cost`; API gợi ý giá (lần mua gần nhất + WAC) | `Features/PurchaseOrders/`, `PurchaseOrdersController.cs` |
| BE-12.4 | `GoodsReceiptService`: giá nhập cho WAC = `allocated_unit_cost / FactorSnapshot`; **bỏ** đoạn áp giá hóa đơn NCC | `Features/Inventory/Services/GoodsReceiptService.cs` |
| BE-12.5 | Gỡ `SupplierInvoiceService`, `SupplierInvoicesController`, đăng ký DI và mã lỗi `SUPPLIER_INVOICE_*`; giữ entity/bảng để đọc dữ liệu cũ | `Features/SupplierInvoices/`, `WebApi/Controllers/SupplierInvoicesController.cs`, `ApiErrorTitles.cs` |
| BE-12.6 | Che giá trên DTO đơn mua và phiếu nhập: Staff nhận `null` cho giá, tổng tiền, phí, VAT, giá vốn phân bổ; NCC `null` khi đơn ở `Draft`/`PendingApproval` | DTO + service đơn mua, phiếu nhập |
| BE-12.7 | Cấu hình giá bán theo dòng × đơn vị bán × bảng giá (Giữ cũ / WAC + lãi / Giá mới, áp ngay / từ ngày); sinh phiếu đổi giá + dòng giá lúc **xác nhận phiếu nhập đầu tiên**, `price_applied_at`; làm tròn lên 1.000đ; cảnh báo bán lỗ | `GoodsReceiptService`, `Features/Pricing/`, bảng/cột cấu hình mới |
| BE-12.8 | Phiếu đổi giá: loại lý do bắt buộc; hộp thoại Q19 (API trả danh sách cấu hình giá đang chờ của SKU); khóa `pg_advisory_xact_lock` theo SKU + đơn vị + bảng giá, thứ tự đơn mua → tồn → giá | `PriceChangeService`, `SqlLockService`, `ISqlLockService` |
| BE-12.9 | Dịch vụ che giá dùng chung cho toàn hệ thống (đơn hàng `CostPrice`, truy vết, sổ cái, báo cáo, tìm kiếm, in, tool AI, SignalR) + test quét endpoint bằng Staff | `Application/Common`, các service trả DTO |

### Frontend Admin

| # | Việc | File chính |
| --- | --- | --- |
| FE-12.1 | Màn đơn mua theo vai trò: Staff tạo/sửa `Draft` không giá, nút Gửi; Owner nhập NCC, ngày đặt, giá (hiện giá gợi ý + WAC), phí/chiết khấu/VAT, xem giá vốn sau phân bổ, nút Lưu/Duyệt/Trả về/Hủy; hiện lý do trả về | `pages/PurchaseOrdersPage.tsx`, `components/purchase/*`, `services/purchaseOrderService.ts`, `types/purchaseOrder.ts` |
| FE-12.2 | Phiếu nhập: Staff không thấy cột giá/giá trị; gỡ panel và danh sách hóa đơn NCC | `pages/InventoryPage.tsx` (phần phiếu nhập), `components/GoodsReceiptList.tsx`, `components/supplierInvoices/*` |
| FE-12.3 | Bảng cấu hình giá bán trên đơn mua (3 lựa chọn × đơn vị bán × lẻ/sỉ, ngày áp dụng, cảnh báo bán lỗ) | `components/purchase/*` |
| FE-12.4 | Phiếu đổi giá: chọn loại lý do; hộp thoại Q19 | `components/pricing/CreatePriceChangeModal.tsx`, `PriceManagementModal.tsx` |
| FE-12.5 | Ẩn mọi cột giá vốn/WAC/lãi với Staff ở màn còn lại (đơn hàng, truy vết, báo cáo) | các trang liên quan |

## 8. Gói công việc

| Gói | Nội dung | Mức | Thứ tự |
| --- | --- | --- | --- |
| **Gói 1** | BE-12.1 → BE-12.6, FE-12.1, FE-12.2 | CRITICAL | Song song với Gói 2, 3 |
| Gói 4 | BE-12.7, BE-12.8, FE-12.3, FE-12.4 | CRITICAL | Sau Gói 1 |
| Gói 5 | BE-12.9, FE-12.5 (gộp các kiểm tra quyền cục bộ của Gói 1–3 về dịch vụ dùng chung) | CRITICAL | Sau Gói 1–3 |
| **Gói 6** | Q27, Q28, Q29: Siết quyền đơn nháp (chỉ người tạo gửi duyệt/sửa), Owner thiết lập đơn mua trước khi duyệt (checklist + wizard 4 bước), % lãi gộp và chế độ cộng thêm trên giá vốn | CRITICAL | Sau Gói 5 |

Prompt giao Anti: [`skills/claude-to-antigravity/references/OISMS-66-lan2-prompts.md`](../../../skills/claude-to-antigravity/references/OISMS-66-lan2-prompts.md).
