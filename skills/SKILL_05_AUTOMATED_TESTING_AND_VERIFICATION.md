# SKILL-05: KỸ THUẬT KIỂM THỬ PHẦN MỀM TOÀN DIỆN (UNIT & INTEGRATION TESTING TRÊN POSTGRESQL THẬT)

> **Người thực hiện:** Nhựt Phùng Minh (Backend 2 Lead) & Nam Nguyễn (QA Lead)  
> **Áp dụng tại:** Dự án OISMS — Hệ thống Quản lý Bán hàng & Tồn kho Đa kênh  
> **Cơ sở đối chiếu lý thuyết:** Chương 5 Kiểm thử phần mềm (Slide 2 - 44: Black-box, White-box, Unit Testing, Integration Testing, Test Cases).

---

### 1. BÀI TOÁN THỰC TẾ & NỖI ĐAU HỆ THỐNG
Trong các dự án sinh viên thông thường:
* Kiểm thử thường chỉ làm thủ công bằng tay (Manual Click trên Web), không có bằng chứng định lượng chứng minh phần mềm chạy ổn định.
* Khi dùng CSDL giả lập trong bộ nhớ (In-Memory Database), các câu lệnh SQL nâng cao như `FOR UPDATE`, `jsonb`, `Advisory Lock` hoặc các ràng buộc Foreign Key không hoạt động giống như môi trường thực tế, dẫn đến việc khi deploy lên server thật thì phát sinh hàng loạt lỗi nghiêm trọng.

---

### 2. PHƯƠNG PHÁP KIỂM THỬ ĐÃ ÁP DỤNG TRONG OISMS

#### 2.1. Kiểm thử Hộp đen (Black-box Testing - Slide 4)
* **Phân tích giá trị biên (Boundary Value Analysis):**
  - Kiểm thử số lượng nhập kho: Số lượng âm ($-1$), bằng $0$ &rarr; Trả về `400 Bad Request`.
  - Kiểm thử giá bán SKU: Giá âm &rarr; Chặn bởi FluentValidation.
* **Đoán lỗi (Error Guessing):**
  - Kiểm thử số điện thoại không hợp lệ, email sai định dạng, tên đăng nhập chứa ký tự đặc biệt &rarr; Chuẩn hóa bằng `PhoneHelper` và trả về thông báo lỗi thân thiện.

#### 2.2. Kiểm thử Hộp trắng (White-box Testing - Slide 6, 11)
* **Độ bao phủ nhánh (Branch Coverage):**
  - Kiểm thử toàn bộ các nhánh trong cấu trúc điều khiển của State Machine (Đơn hàng từ `Draft` &rarr; `Reserved` &rarr; `Confirmed` &rarr; `Completed`). Kiểm chứng rằng nếu chuyển cóc từ `Draft` sang thẳng `Completed` thì hệ thống lập tức ném ngoại lệ và trả mã `400 Bad Request`.
  - Phủ 100% các nhánh `switch-case` trong Factory và Strategy tính tiền thưởng.

#### 2.3. Kiểm thử Tích hợp trên PostgreSQL thật (Integration Testing on Real Database)
* Toàn bộ 91 bài Integration Tests của nhóm **chạy trực tiếp trên CSDL PostgreSQL 16+ thật** (Database: `oisms_test`, tài khoản: `oisms_test_app`, Schema v0.5 gồm 29 bảng).
* Kiểm chứng thực tế các tính năng phụ thuộc chặt chẽ vào engine của PostgreSQL:
  - Khóa hàng `FOR UPDATE`.
  - Truy vấn cột động `jsonb` và chỉ mục `GIN Index`.
  - Kiểm tra toàn vẹn khóa ngoại (Foreign Key Constraints - lỗi 23503).

---

### 3. KẾT QUẢ VÀ CHỈ SỐ NGHIỆM THU ĐẠT ĐƯỢC
* **Tổng số bài test tự động:** **194 / 194 Tests Pass 100%**.
  - Unit Tests nghiệp vụ: **103 bài**.
  - Integration Tests trên PostgreSQL thật: **91 bài**.
* **Thời gian thực thi:** Toàn bộ 194 bài test hoàn thành trong chưa đầy **12 giây**.
* **Code Coverage:** Đạt **94.83%** trên các thư viện cốt lõi `OISM.Application` và `OISM.Domain`.
* **Minh chứng báo cáo chi tiết:** [`docs/TAI_LIEU_DAC_TA_API_VA_LOGIC_NGHIEP_VU_BACKEND_SPRINT_2.pdf`](file:///Users/bo/OISMS/docs/TAI_LIEU_DAC_TA_API_VA_LOGIC_NGHIEP_VU_BACKEND_SPRINT_2.pdf) (19 trang đặc tả toàn diện 10 controllers, 46 endpoints).
