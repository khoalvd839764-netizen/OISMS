# 02. Hướng Dẫn Tích Hợp & Kích Hoạt AI Skills Trong Antigravity

| Thông tin kiểm soát tài liệu | Chi tiết |
| :--- | :--- |
| **Mã tài liệu (Document ID)** | `DOC-SKL-02` |
| **Phiên bản (Version)** | `v1.0.0` |
| **Ngày hiệu lực (Effective Date)** | `2026-10-23` |
| **Nền tảng hỗ trợ** | Antigravity AI Agent (IDE, CLI & WebPairing) |

---

## 1. Cơ Chế Discovery Mặc Định Của Antigravity

Hệ thống Agentic của Antigravity tự động phát hiện và nạp (discover) các kỹ năng theo thứ tự ưu tiên từ cao xuống thấp:

1. **Workspace Project Customizations (Cấp dự án):**
   - Thư mục được Agent tự động quét tại gốc dự án là: `.agent/skills/` hoặc `.agents/skills/`.
   - Mỗi skill nằm trong thư mục con tương ứng và bắt buộc có file định nghĩa `SKILL.md`.
2. **Global Customizations (Cấp máy trạm):**
   - Thư mục cấu hình dùng chung cho toàn máy: `~/.gemini/config/skills/` hoặc `%USERPROFILE%\.gemini\antigravity\skills\`.
3. **Built-in System Skills (Kỹ năng mặc định tích hợp sẵn):**
   - Các công cụ lõi của Agent như `agy-customizations`, `codegraph`, `generative_ui`.

---

## 2. Thiết Lập Liên Kết Không Nhân Bản Dữ Liệu (Zero Duplication)

Theo yêu cầu chuẩn hóa của dự án OISMS, tất cả kỹ năng phải được lưu trữ vật lý duy nhất tại:
```text
D:\OISMS\skills\
├── csharp-pro\
├── architecture-patterns\
└── ...
```

Để Antigravity tự động nhận diện toàn bộ 12 skills này vào danh mục runtime của Agent mà **không nhân bản tệp tin (zero duplication)**, giải pháp khuyến nghị là tạo **NTFS Junction** (Directory Junction) trong môi trường Windows.

### Lệnh Khởi Tạo Junction (Chạy trên PowerShell / CMD)

```powershell
# Chuyển đến thư mục gốc của dự án OISMS
cd D:\OISMS

# Tạo thư mục ẩn .agent nếu chưa có
if (!(Test-Path ".agent")) { New-Item -ItemType Directory -Path ".agent" }

# Tạo NTFS Junction liên kết từ .agent\skills sang D:\OISMS\skills
cmd /c mklink /J "D:\OISMS\.agent\skills" "D:\OISMS\skills"
```

> [!NOTE]
> **Ưu điểm của NTFS Junction:**
> - Không yêu cầu quyền Administrator khi chạy lệnh.
> - Tiết kiệm hoàn toàn dung lượng ổ cứng vì mọi thao tác đọc/ghi từ `.agent\skills` đều trỏ trực tiếp đến `D:\OISMS\skills`.
> - Tương thích hoàn hảo với cơ chế duyệt tệp tin của Git (Git sẽ chỉ theo dõi `skills/` và bỏ qua liên kết nếu thêm `.agent/` vào `.gitignore`).

---

## 3. Cách Thức Hoạt Động & Kích Hoạt Skills

Antigravity áp dụng cơ chế **Tiết lộ tăng dần (Progressive Disclosure)** để tối ưu hóa context window:

1. **Khởi tạo ngữ cảnh (Context Initialization):**
   - Ở đầu mỗi phiên làm việc, Agent chỉ nạp danh sách tên (`name`) và phần mô tả tóm tắt (`description`) trong phần YAML Frontmatter của từng `SKILL.md`.
   - Toàn bộ nội dung chi tiết không bị nạp ồ ạt, giúp tiết kiệm đáng kể token và giữ cho Agent luôn có dung lượng ngữ cảnh lớn nhất.

2. **Kích hoạt tự động theo ngữ cảnh (Model-Driven Activation):**
   - Khi bạn đưa ra một câu hỏi hoặc yêu cầu lập trình, Agent đối chiếu ngữ cảnh với `description` của các skills đã được setup.
   - *Ví dụ:* Khi bạn yêu cầu *"Tối ưu hóa câu truy vấn kiểm tra số dư tồn kho tức thời khi chịu tải cao"*, Agent sẽ tự động kích hoạt và đọc `sql-optimization-patterns` cùng `postgresql`.

3. **Chỉ định trực tiếp bởi Lập trình viên (Proactive Activation):**
   - Bạn có thể chỉ thị thẳng cho Agent áp dụng skill mong muốn trong prompt:
   - *Ví dụ 1:* *"Áp dụng skill **architecture-patterns** để kiểm tra cấu trúc của tầng `OISM.Application` xem có vi phạm Clean Architecture không."*
   - *Ví dụ 2:* *"Dùng skill **unit-testing-test-generate** để viết bộ test cho chức năng tính giá vốn WAC trong `GoodsReceiptService.cs`."*
   - *Ví dụ 3:* *"Dùng skill **security-auditor** để rà soát cơ chế phân lập TenantContext trong middleware."*

---

## 4. Kiểm Thử Đọc Trực Tiếp Không Qua Runtime Discovery

Trong trường hợp phiên làm việc của Agent chưa được khởi động lại hoặc không tạo Junction, lập trình viên vẫn hoàn toàn có thể yêu cầu Agent đọc trực tiếp từ thư mục `D:\OISMS\skills\`:

```text
Prompt mẫu:
"Hãy đọc tài liệu hướng dẫn tại file D:\OISMS\skills\csharp-pro\SKILL.md và áp dụng các tiêu chuẩn C# mới nhất để refactor class OrderProcessingService."
```

Trợ lý AI sẽ đọc trực tiếp nội dung `SKILL.md` và các tài liệu bổ trợ trong thư mục `resources/` để cung cấp giải pháp chính xác nhất theo tiêu chuẩn của OISMS.
