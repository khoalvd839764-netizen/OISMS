# Bug Fix After — Tài Liệu Rà Soát & Khắc Phục Lỗi Hệ Thống

Đợt rà soát & khắc phục: **28–30/10/2026 · Asia/Saigon**  
Trạng thái tổng thể: ✅ **20/20 Lỗi Hệ Thống (BF-001 ➔ BF-020) Đã Khắc Phục & Kiểm Thử Thành Công (100% VERIFIED PASS)**

> **Cập nhật 02/11/2026, kết quả trên ghi theo đợt 28–30/10:**
> - Bản sửa cho Web Admin trước đó chỉ nằm trên máy local, nay đã lên nhánh `featch/FIx-and-Uprade` ở commit `97b3418`. Chưa chạy lại toàn bộ kiểm thử BF sau khi đưa lên.
> - POS riêng (`pos-app`) không đưa bản sửa cũ lên vì xung đột với bản mới của `develop`, nên đang giữ code `develop`. Bản này có cơ chế riêng cho thử lại, hết phiên và kiểm tra biên lai, nhưng chưa có phần khóa giỏ hàng và chi nhánh trong lúc checkout như bản sửa cũ. **BF-020 trên `pos-app` cần kiểm tra lại**; các lỗi BF-003, BF-015, BF-019 trên `pos-app` cũng cần kiểm chứng lại theo code mới.

---

## 1. Cấu Trúc Thư Mục & Tham Chiếu Tài Liệu

Thư mục `docs/bug-fixaffter/` được phân chia thành 2 khu vực chức năng chính nhằm tách biệt rõ ràng giữa **kết quả sửa lỗi hệ thống** và **các tính năng đang phát triển (Coming Soon)**:

```
docs/bug-fixaffter/
├── README.md                              # (Tài liệu này) Tổng quan, bản đồ tài liệu & trạng thái
├── Comming Soon function/
│   └── COMING-SOON.md                     # Danh sách tính năng chưa hoàn thiện, gắn badge Coming Soon
└── Report Bugfix/
    ├── report result affter fix.md        # ⭐ BÁO CÁO CHI TIẾT KẾT QUẢ KHẮC PHỤC 20 LỖI (Sau khi fix)
    ├── BUG-LIST.md                        # Danh mục chi tiết 20 lỗi ban đầu (Mô tả, nguyên nhân, vị trí code)
    └── TEST-RESULTS.md                    # Báo cáo kết quả kiểm thử tự động, phạm vi quét & đối chuẩn
```

---

## 2. Bảng Điều Hướng Tài Liệu (Navigation & Links)

| Thư mục / Tài liệu | Đường dẫn tham chiếu | Nội dung chính | Trạng thái |
| :--- | :--- | :--- | :---: |
| **Checklist Bugfix & Upgrade** | [CHECKLIST-BUGFIX-AND-UPGRADE.md](CHECKLIST-BUGFIX-AND-UPGRADE.md) | Checklist tổng hợp 20/20 bug đã fix (`BF-001..020`), Upgrade đã hoàn thành (`Upgrade 06`) và lộ trình 3 nhóm Sprint 4 (`OISMS-45..47`) | ✅ **CẬP NHẬT MỚI** |
| **Báo cáo kết quả sau fix** | [Report Bugfix/report result affter fix.md](Report%20Bugfix/report%20result%20affter%20fix.md) | Phân tích chi tiết phương pháp khắc phục 20 lỗi, diff logic, và bằng chứng kiểm thử đạt chuẩn | ✅ **HOÀN THÀNH** |
| **Danh sách lỗi ban đầu** | [Report Bugfix/BUG-LIST.md](Report%20Bugfix/BUG-LIST.md) | Đặc tả 20 lỗi hệ thống được ghi nhận (BF-001 đến BF-020): mức ưu tiên P1/P2, cách tái hiện, ảnh hưởng | ✅ **ĐÃ FIX 20/20** |
| **Kết quả kiểm thử nền** | [Report Bugfix/TEST-RESULTS.md](Report%20Bugfix/TEST-RESULTS.md) | Kết quả kiểm thử tự động backend/frontend nền tảng (Unit tests, Integration tests, Regression) | ✅ **PASS** |
| **Chức năng Coming Soon** | [Comming Soon function/COMING-SOON.md](Comming%20Soon%20function/COMING-SOON.md) | Thống kê các nút bấm/tính năng chưa có backend hoặc đang trong lộ trình phát triển (tách khỏi lỗi code) | ℹ️ **ĐANG THEO DÕI** |
| **Checklist tổng dự án** | [checklist.md](../../checklist.md) | Đồng bộ trạng thái nghiệm thu toàn dự án OISMS (mục 0A) | ✅ **ĐỒNG BỘ** |

---

## 3. Tổng Hợp Kết Quả Khắc Phục Lỗi (BF-001 đến BF-020)

Toàn bộ 20 lỗi trong [BUG-LIST.md](Report%20Bugfix/BUG-LIST.md) đã được khắc phục triệt để và kiểm chứng độc lập:

1. **Nhóm Toàn vẹn phiên & Realtime (P1):**
   * **BF-001**: Xử lý refresh token và mutation 401 gắn chặt với thế hệ phiên (`currentAuthGeneration`), chặn đứng ghi đè token chéo hoặc replay mutation của user A bằng token của user B.
   * **BF-002**: Quản lý vòng đời SignalR connection gắn với token phiên, giải phóng triệt để transport cũ khi đăng xuất và tạo mới cho người dùng kế tiếp.
2. **Nhóm Idempotency & Bảo toàn giao dịch (P1):**
   * **BF-003**: Cố định khóa `requestKey` qua các lần thử lại (retry) thanh toán POS (cả bản Admin và POS app độc lập).
   * **BF-004, BF-005**: Bảo toàn `requestKey` và chữ ký payload khi retry tạo phiếu chuyển kho hoặc đợt kiểm kho.
   * **BF-006, BF-007**: Sửa lỗi kiểm kho: bắt buộc ghi nhận số sổ sách lên database trước khi chốt kiểm kê, và hiển thị hộp thoại xác nhận khi lưu ô trống.
   * **BF-013**: Khóa form khi đang retry giữ chỗ kho và yêu cầu xác nhận trước khi hủy pending reservation.
   * **BF-019**: Từ chối phản hồi HTTP 200 thiếu mã định danh đơn hàng server, ngăn ngừa xác nhận thanh toán ảo.
   * **BF-020**: Khóa ngữ cảnh giỏ hàng và chi nhánh (`cartVersion`, `currentBranchIdRef`) trong POS độc lập, không để giao dịch cũ xóa giỏ hàng mới.
3. **Nhóm Dữ liệu kinh doanh, Múi giờ & Trải nghiệm (P1 & P2):**
   * **BF-008**: Reset khóa `actionLoading` khi chuyển đổi chứng từ trong Drawers chuyển kho/kiểm kê.
   * **BF-009, BF-010**: Ngăn phản hồi API báo cáo cũ ghi đè bộ lọc mới (generation guard), và dọn sạch dữ liệu cũ khi truy vấn bị lỗi.
   * **BF-011**: Chuẩn hóa múi giờ kinh doanh Việt Nam (Asia/Saigon UTC+07:00) cho "Hôm nay", và chuyển backend sang lọc khoảng nửa mở `CreatedAt < EndDateBoundary`.
   * **BF-012**: Dropdown chọn SKU trên form chuyển kho đọc số tồn thực tế theo kho nguồn đã chọn.
   * **BF-014**: Hiển thị banner lỗi API kèm nút "Thử lại" trên trang chi nhánh/kho, không nuốt lỗi thành dữ liệu rỗng.
   * **BF-015**: POS xử lý chuẩn lỗi 401 (xóa token, kích hoạt `pos-auth-expired`) và trích xuất đúng RFC 7807 `ProblemDetails`.
   * **BF-016**: Kết nối nhóm chi nhánh (`joinBranch`), cập nhật số tồn khi có biến động và khi phục hồi kết nối (`reconnected`).
   * **BF-017**: Hỗ trợ mở trực tiếp tab Cảnh báo tồn kho tối thiểu qua URL query parameter `?tab=alerts`.
   * **BF-018**: Bổ sung tùy chọn kênh bán hàng `Admin (Quản trị viên)` trong bộ lọc báo cáo.

---

## 4. Bằng Chứng & Kết Quả Kiểm Thử (Verification Suite)

* **Bộ kiểm tra chuyên sâu 20 lỗi (`scratch/test_bugfix_after_verification.cjs`)**: **20/20 kịch bản PASS**.
* **Bộ kiểm tra hồi quy Frontend (`scratch/test_regression_v12.cjs`)**: **13/13 tests PASS**.
* **Bộ kiểm tra đơn vị Backend C# (`src/backend/tests/OISM.UnitTests`)**: **158/158 tests PASS**.
* **Biên dịch Production**:
  * `src/frontend/admin-dashboard`: `tsc -b && vite build` ➔ **Thành công (0 lỗi)**.
  * `src/frontend/pos-app`: `tsc -b && vite build` ➔ **Thành công (0 lỗi)**.
  * `src/backend/OISM.sln`: `dotnet build` ➔ **Thành công (0 cảnh báo, 0 lỗi)**.

> [!NOTE]
> **Về thư mục `evidence/` cũ**: Thư mục tạm `evidence/` chứa các tệp log `.trx` và `.log` nặng (~10 MB) từ đợt chạy audit ban đầu đã được dọn dẹp để tinh gọn repository. Toàn bộ bằng chứng nghiệm thu, diff code và kịch bản kiểm thử tự động đã được tổng hợp đầy đủ tại [Report Bugfix/report result affter fix.md](Report%20Bugfix/report%20result%20affter%20fix.md).
