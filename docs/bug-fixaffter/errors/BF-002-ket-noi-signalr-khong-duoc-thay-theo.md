# BF-002 — Kết nối SignalR không được thay theo phiên

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa |
| **Ưu tiên** | P1 |
| **Khu vực** | Realtime |
| **Phát hiện** | 28–29/10/2026 (Tái hiện có kiểm soát; PR26-F02) |
| **Sửa** | 30/10/2026, đợt sửa BF-001..020 |
| **Kiểm chứng** | `scratch/test_bugfix_after_verification.cjs` (30/10/2026, PASS) |

## Mô tả lỗi (lúc phát hiện)

**Tái hiện:** A kết nối SignalR → token/người dùng chuyển sang B → NotificationCenter gọi `start()` lại. Adapter thấy connection đang Connected và trả về ngay, giữ transport đã xác thực A. Listener của B vẫn nhận được sự kiện fixture từ transport A.

**Nguyên nhân:** singleton không lưu identity/generation của kết nối. Auth logout không gọi stop/dispose Hub; cleanup NotificationCenter chỉ bỏ listener. `stop()` cũng chưa reset đầy đủ instance, nhóm chi nhánh và latest event.

**Ảnh hưởng:** nguy cơ thông báo của phiên/tenant cũ xuất hiện trong phiên mới. Probe xác nhận đường client; chưa phát dữ liệu thật chéo tenant qua browser.

**Vị trí:** `services/realtimeAdapter.ts:start/stop/initSignalRConnection`, `components/NotificationCenter.tsx` effect, `context/AuthContext.tsx:logout`.

**Bằng chứng:** `realtime-owner`; đối chiếu báo cáo PR26-F02 cũ, không mặc định finding đã đóng vì happy path chạy được.

**Điều kiện đóng:** mỗi kết nối thuộc actor/token phù hợp; chặn callback/deferred start/reconnect cũ; logout đóng và reset; test fake transport và transport thật riêng trên DB test.

## Cách đã sửa

* **Thành phần:** `src/frontend/admin-dashboard/src/services/realtimeAdapter.ts`, `src/frontend/admin-dashboard/src/context/AuthContext.tsx`
* **Triệu chứng & Nguyên nhân:**
  * `realtimeAdapter` là singleton giữ connection instance. Khi user A đăng xuất và user B đăng nhập, `realtimeAdapter.start()` thấy kết nối đang ở trạng thái `Connected` (hoặc `isStarting` đang chạy) nên giữ nguyên kết nối xác thực bằng token của user A. Ngoài ra các callback `onreconnecting`/`onreconnected`/`onclose` của kết nối cũ có thể phát sự kiện đè lên phiên mới.
* **Giải pháp khắc phục:**
  * Thêm `currentConnectionToken` và bộ đếm `connectionGeneration` để mỗi lần `start()` với token mới đều vô hiệu hóa tiến trình khởi tạo cũ, chủ động dừng (`stop()`) kết nối cũ và tạo connection mới.
  * Bổ sung guard `if (connection !== hubConnection) return;` trong toàn bộ các lifecycle handlers (`onreconnecting`, `onreconnected`, `onclose`).
  * Bổ sung lệnh gọi `realtimeAdapter.stop()` ngay trong `logout` và khi hết hạn phiên trong `AuthContext.tsx`.
* **Kiểm thử xác thực:** Pass kịch bản `realtime-owner`: kết nối SignalR cũ bị ngắt và thay thế hoàn toàn bằng instance mới khi đổi tài khoản.
