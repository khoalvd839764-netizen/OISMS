# Báo cáo sau nâng cấp — Siết quyền đơn nháp, Owner thiết lập đơn mua trước khi duyệt và % lãi gộp (Upgrade 12 — Gói 6)

| Thông tin | Chi tiết |
| :--- | :--- |
| **Mã gói công việc** | `OISMS-66-L2-G6` |
| **Ngày thực hiện** | `2026-11-21` |
| **Mức độ ưu tiên** | `CRITICAL` (RBAC + Chuyển trạng thái) |
| **Tài liệu nguồn** | [`docs/Upgrade-recommended/Pedding/12-don-mua-va-cau-hinh-gia.md`](../Upgrade-recommended/Pedding/12-don-mua-va-cau-hinh-gia.md) (Q3, Q4, Q5, Q11, Q15, Q16, Q27, Q28, Q29; mục 7 FE-12.1, FE-12.3); [`docs/04-architecture-and-design/04-state-machines.md`](../04-architecture-and-design/04-state-machines.md); [`docs/01-users-and-rbac/02-rbac-matrix.md`](../01-users-and-rbac/02-rbac-matrix.md) |
| **Migration SQL** | Không có (Gói này không thay đổi cấu trúc DB / schema) |
| **Baseline commit** | `ccae87c` |

---

## 1. Tóm tắt thay đổi nghiệp vụ và kỹ thuật

### 1.1. Phân quyền đơn nháp (AC-01, AC-02, Q11, Q27)
- **Gửi duyệt (`SubmitAsync`):** Chỉ người tạo đơn (`po.CreatedBy == userId`) mới có quyền gửi duyệt đơn nháp `Draft` sang `PendingApproval`. Người khác (kể cả vai trò `Owner`) cố tình gọi API gửi duyệt sẽ bị từ chối với HTTP 403 Forbidden và mã lỗi `PURCHASE_ORDER_NOT_CREATOR`.
- **Cập nhật đơn nháp (`UpdateDraftAsync`):** Đối với đơn hàng đang ở trạng thái `Draft`, chỉ người tạo (`po.CreatedBy == userId`) mới được phép chỉnh sửa; người khác (kể cả `Owner`) nhận HTTP 403 Forbidden và mã lỗi `PURCHASE_ORDER_NOT_CREATOR`.
- **Đặc quyền của Owner:**
  - `Owner` vẫn toàn quyền sửa đơn hàng ở trạng thái `PendingApproval` (Q4).
  - `Owner` vẫn có quyền Hủy (`Cancel`) đơn nháp `Draft` của bất kỳ ai (Q11).
  - Đơn do `Owner` tự tạo nếu bị Trả về (`Return`) thành `Draft` thì `Owner` chính là người tạo (`CreatedBy == userId`), do đó `Owner` vẫn sửa và gửi duyệt bình thường.
- **Giao diện (Frontend):**
  - Khi xem đơn `Draft` do người khác tạo: `Owner` thấy nhãn `"Staff đang soạn — chỉ xem"`, ẩn hoàn toàn các nút *Sửa đề nghị nháp* và *Gửi duyệt*, chỉ giữ lại nút *Hủy đơn* và *Đóng*.
  - Người tạo đơn thấy đầy đủ các nút *Sửa đề nghị nháp*, *Gửi duyệt*, *Hủy đơn*.

### 1.2. Đánh giá điều kiện duyệt đơn mua (AC-03, Q28)
- **Hàm dùng chung `EvaluateApprovalReadinessAsync`:** Tách toàn bộ logic kiểm tra tính sẵn sàng để duyệt thành hàm dùng chung giữa `ApproveAsync` và `MapToResponseDtoAsync`. Đảm bảo backend và frontend áp dụng thống nhất cùng một luật nghiệp vụ, không lệch chuẩn.
- **Trường `approvalReadiness` trong `PurchaseOrderResponseDto`:**
  - Đối với `Owner`: trả về object `{ isReady, missing, hasZeroPrice, hasLossPrice }`.
    - `isReady`: `true` khi đã có Nhà cung cấp (`supplierId != null`), ngày đặt hợp lệ (`orderDate <= today`), có ít nhất 1 dòng hàng và tất cả các dòng đều đã có đơn giá mua (`unitPrice != null`).
    - `missing`: danh sách mã lỗi thành phần thiếu (`["ITEMS", "SUPPLIER", "UNIT_PRICE", "ORDER_DATE"]`).
    - `hasZeroPrice`: `true` nếu có ít nhất 1 dòng mua có đơn giá bằng 0đ (hàng khuyến mãi/tặng).
    - `hasLossPrice`: `true` nếu có bất kỳ cấu hình giá bán nào có giá dự kiến nhỏ hơn giá vốn WAC dự kiến.
  - Đối với `Staff`: trường này luôn trả về `null`.

### 1.3. Trình hướng dẫn thiết lập 3 bước trước khi duyệt (AC-04, AC-05, AC-06, Q28)
- **Checklist tại màn hình chi tiết:** Khi đơn ở trạng thái `PendingApproval`, giao diện của Owner hiển thị checklist trực quan ✓/✗ ở đầu modal:
  1. Nhà cung cấp (✓ Đã chọn / ✗ Chưa chọn)
  2. Đơn giá mua đủ mọi dòng (✓ Đã nhập đủ / ✗ Chưa đủ đơn giá mua)
  3. Ngày đặt hợp lệ (✓ Hợp lệ / ✗ Chưa hợp lệ)
  4. Đã xem cấu hình giá bán (✓ Đã xem / ✗ Chưa xem — lưu trạng thái session FE)
- **Khóa nút Duyệt ngoài màn chi tiết:** Nút "Duyệt đơn mua" ở footer chi tiết bị khóa (disabled) kèm dòng lý do giải thích màu đỏ cho tới khi `approvalReadiness.isReady` và đã xem cấu hình giá bán. Nút chính chuyển thành **"Thiết lập đơn mua"**.
- **Wizard 3 bước (`PurchaseOrderSetupWizardModal`):**
  - **Bước 1 — NCC & Đơn giá mua:** Cho phép chọn NCC, ngày đặt hàng, nhập phí VC, chiết khấu, VAT, nhập đơn giá mua cho từng dòng (hiển thị giá gợi ý lần mua gần nhất và WAC chi nhánh, có thể bấm để điền nhanh). Bấm "Tiếp" sẽ gọi API PUT cập nhật đơn mua (gửi kèm `version`), lưu thành công vào CSDL mới chuyển sang Bước 2.
  - **Bước 2 — Cấu hình giá bán:** Nhúng `SalePriceConfigTable` cho phép thiết lập giá lẻ/sỉ cho từng đơn vị bán. Bấm "Tiếp" sẽ gọi API PUT cập nhật cấu hình giá bán vào CSDL và đánh dấu cờ đã xem cấu hình giá bán trong phiên.
  - **Bước 3 — Xác nhận & Duyệt:** Tóm tắt tài chính và bảng giá vốn sau phân bổ từng dòng.
    - Nếu có dòng giá 0đ: hiển thị ô checkbox xác nhận trực tiếp trên giao diện (`confirmZeroPrice`).
    - Nếu có cấu hình bán lỗ: hiển thị ô checkbox xác nhận trực tiếp trên giao diện (`confirmLossPrice`).
    - Bỏ hoàn toàn việc dùng `window.confirm` và popup modal bật ra sau khi duyệt.
    - Nút "Duyệt đơn mua" chỉ bật (enabled) khi đã tích đủ các ô xác nhận bắt buộc. Khi bấm Duyệt sẽ gọi API `approvePurchaseOrder` kèm `version` và các cờ xác nhận.
- **Bảo toàn dữ liệu & Kiểm soát xung đột phiên bản (409 Concurrency):**
  - Đóng modal giữa chừng không bị mất dữ liệu đã lưu ở các bước trước vì mỗi bước đã thực hiện lưu bền vững vào CSDL.
  - Khi gặp lỗi xung đột phiên bản (HTTP 409), hiển thị thông báo rõ ràng: `"Đơn vừa được sửa ở nơi khác, tải lại..."` và tự động tải lại dữ liệu mới nhất.
- **Chuẩn hóa thông báo lỗi (AC-06):** Di chuyển toàn bộ thông báo lỗi xuống ngay trên thanh nút hành động. Sửa các thông báo nhắc thao tác khớp với tên nút thật ("Thiết lập đơn mua").

### 1.4. % Lãi gộp và chế độ nhập cộng thêm trên giá vốn (AC-07, AC-08, Q16, Q29)
- **Giữ nguyên công thức Q16:**
  $$\text{Giá bán dự kiến} = \frac{\text{WAC}_{\text{dự kiến}}}{1 - \frac{m}{100}}$$
  và làm tròn LÊN bội số 1.000đ.
- **Kiểm soát miền giá trị tại Backend (AC-07):**
  - Bắt buộc $0 \le \text{marginPercent} < 100$.
  - Nếu $\text{marginPercent} < 0$ hoặc $\ge 100$, trả về HTTP 400 Bad Request với thông điệp: `"Lãi gộp tính trên giá bán, phải từ 0% đến dưới 100%."`.
- **Chế độ nhập linh hoạt tại Frontend (AC-08):**
  - Ô cấu hình WAC + lãi có công tắc chuyển đổi: **"Nhập theo: Lãi gộp trên giá bán | Cộng thêm trên giá vốn"**.
  - Khi chọn chế độ *Cộng thêm trên giá vốn*, người dùng có thể nhập tỷ lệ $k \ge 0$ (cho phép $k \ge 100\%$, ví dụ nhập $100\%$ nghĩa là bán gấp đôi giá vốn). Frontend tự động quy đổi sang lãi gộp theo công thức:
    $$m = \frac{k}{100 + k} \times 100$$
    để lưu vào backend, tuyệt đối không thay đổi công thức backend hay schema CSDL.
  - Kiểm tra tính hợp lệ tức thời: nếu $m < 0$ hoặc $m \ge 100$, ô nhập hiển thị viền đỏ, hiển thị câu cảnh báo lỗi bên dưới, giá dự kiến chuyển thành `"—"` và khóa nút Lưu.
  - Phía dưới ô nhập luôn hiển thị dòng chú thích quy đổi trực quan theo thời gian thực:
    `Lãi gộp m% = cộng thêm k% trên giá vốn`.

---

## 2. Kết quả kiểm thử (Test Execution)

### 2.1. Backend Unit Tests
- Lệnh: `dotnet test test-oisms/backend/OISM.UnitTests/OISM.UnitTests.csproj`
- Kết quả: **PASS 428 / 428 tests** (0 failed, 0 skipped).

### 2.2. Backend Integration Tests (PostgreSQL thật)
- Viết mới test suite `PostgresPurchaseOrderG6IntegrationTests.cs` kiểm thử toàn diện mọi tình huống:
  1. `Gate01_02_DraftRbac_CreatorOnlySubmitAndEdit_OwnerCancelOk_OwnerReturnedDraftOk`:
     - Staff A tạo đề nghị mua `Draft`.
     - Owner gửi duyệt hoặc sửa đơn của Staff A $\rightarrow$ bị chặn 403 `PURCHASE_ORDER_NOT_CREATOR`.
     - Staff B gửi duyệt hoặc sửa đơn của Staff A $\rightarrow$ bị chặn 403 `PURCHASE_ORDER_NOT_CREATOR`.
     - Owner hủy đơn nháp của Staff A $\rightarrow$ Thành công (Q11).
     - Staff A tự sửa và gửi duyệt đơn của mình $\rightarrow$ Thành công.
     - Owner trả về đơn do chính Owner tạo $\rightarrow$ Owner sửa và gửi duyệt lại thành công.
  2. `Gate03_04_ApprovalReadiness_And_ApproveBlockWhenNotReady`:
     - Đơn thiếu NCC hoặc đơn giá $\rightarrow$ `approvalReadiness.isReady = false`, `missing` có `SUPPLIER` và `UNIT_PRICE`.
     - Staff xem đơn $\rightarrow$ `approvalReadiness = null`.
     - Owner gọi Approve khi chưa ready $\rightarrow$ bị chặn 422 `PURCHASE_ORDER_INCOMPLETE_FOR_APPROVAL`.
     - Sau khi cập nhật đủ NCC, đơn giá, ngày đặt $\rightarrow$ `approvalReadiness.isReady = true`.
  3. `Gate05_MarginValidation_And_CeilFormula`:
     - Kiểm tra margin âm ($< 0$) $\rightarrow$ bị chặn 400.
     - Kiểm tra margin $\ge 100$ ($100\%, 120\%$) $\rightarrow$ bị chặn 400.
     - Kiểm tra margin hợp lệ ($20\%$, $50\%$) $\rightarrow$ lưu thành công, tính đúng công thức chia $(1 - m)$ và làm tròn trần lên bội 1.000đ.
- Kết quả chạy test G6: **PASS 3 / 3 tests** (100%).

### 2.3. Frontend Build Verification
- Lệnh: `cd src/frontend/admin-dashboard && npm run build`
- Kết quả: **Thành công** (`tsc -b && vite build` không có lỗi TypeScript hay cảnh báo compile).

---

## 3. Danh mục tài liệu đã cập nhật

1. `docs/Upgrade-recommended/Pedding/12-don-mua-va-cau-hinh-gia.md`: Cập nhật luồng A, B; bổ sung quyết định Q27, Q28, Q29; bổ sung Gói 6 vào bảng phân kỳ.
2. `docs/04-architecture-and-design/04-state-machines.md`: Cập nhật chuyển trạng thái `Submit` và `UpdateDraft` chỉ cho người tạo; cập nhật luồng duyệt có kiểm tra `approvalReadiness` và wizard 3 bước.
3. `docs/01-users-and-rbac/02-rbac-matrix.md` (FILE CHUNG): Cập nhật ma trận phân quyền đơn mua cho Owner và Staff (phiên bản `v1.11.0`).
4. `docs/05-api-contracts/03-inventory-endpoints.md`: Cập nhật hợp đồng API `/api/purchase-orders` (trường `approvalReadiness`, mã lỗi 403 `PURCHASE_ORDER_NOT_CREATOR`, validation margin).
5. `docs/12-error-and-status-codes/02-error-codes-catalog.md` (FILE CHUNG): Bổ sung mã lỗi `PURCHASE_ORDER_NOT_CREATOR`.
6. `docs/11-ui-screens/04-inventory-screens.md`: Cập nhật đặc tả giao diện màn hình Đơn mua hàng (checklist Owner, wizard 3 bước thiết lập, công tắc chế độ nhập lãi gộp/cộng thêm).
7. `docs/report-after-upgrade/thiet-lap-duyet-don-mua-u12-g6.md`: Báo cáo chi tiết gói công việc G6.

## Cải tiến và sửa sau nghiệm thu (21/11/2026)

Theo góp ý người dùng khi thử trên docker: Owner thêm được NCC ngay trong thiết lập, đưa cấu hình giá bán vào luồng thiết lập, đi theo thứ tự chọn NCC → nhập giá → xem lại → duyệt.

| # | Nội dung | Thay đổi |
| --- | --- | --- |
| 1 | Luồng thiết lập | Wizard từ 3 thành **4 bước**: Nhà cung cấp (có `+ Thêm NCC`) → Giá mua → Giá bán → Xem lại & duyệt. Màn chi tiết bỏ nút Duyệt và bảng giá bán chỉ để xem; duyệt chỉ ở bước cuối. Bước xem lại có bảng giá bán sẽ áp và giá vốn dự kiến từng dòng |
| 2 | Lỗi: lưu một bước làm đóng cả wizard và hộp thoại chi tiết (trang cha đóng chi tiết khi làm mới) | Giữa các bước chỉ ghi nhận đã lưu; làm mới danh sách khi đóng wizard |
| 3 | Lỗi: cờ hợp lệ của bảng giá bán bị đảo (`onValidationChange` trả "có lỗi" nhưng gán vào "hợp lệ") — cấu hình đúng thì khóa nút, cấu hình sai (lãi 100%) lại cho lưu | Đảo lại đúng nghĩa |
| 4 | Lỗi dữ liệu: lưu lại giá mua (PUT đơn) **xóa mất cấu hình giá bán** đã lưu, vì backend xóa rồi tạo lại dòng hàng | Backend `UpdateDraftAsync`: request không gửi `salePriceConfigs` cho dòng thì giữ cấu hình cũ cùng SKU + đơn vị (gửi mảng rỗng = chủ động bỏ). Wizard gửi kèm cấu hình hiện có. Test `Gate05_...` bước 7–8 |
| 5 | Ngày đặt mặc định lấy theo UTC (sau 0h–7h sáng giờ VN ra ngày hôm trước) | Lấy theo ngày giờ máy |

