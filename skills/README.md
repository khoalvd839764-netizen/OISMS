# HỒ SƠ MINH CHỨNG KỸ NĂNG KỸ THUẬT CHUYÊN MÔN (SKILLS REPOSITORY)
> **Dự án:** OISMS — Omnichannel Inventory & Sales Management System  
> **Học phần:** Công nghệ Phần mềm (122005) — Đại học Giao thông Vận tải TP.HCM (UTH)  
> **Mục tiêu thư mục `Skills/`:** Cung cấp minh chứng chi tiết về các năng lực kỹ thuật, giải pháp kiến trúc và thuật toán chuyên sâu được áp dụng trong mã nguồn thực tế của dự án theo đúng yêu cầu đánh giá chuẩn đầu ra (CLO) của Giảng viên.

---

## DANH MỤC 5 KỸ NĂNG KỸ THUẬT CỐT LÕI (TECHNICAL SKILLS)

| Mã Kỹ năng | Tên Kỹ năng Kỹ thuật | Thành viên phụ trách chính | Tập tin minh chứng | Vị trí mã nguồn thực tế |
| :---: | :--- | :--- | :--- | :--- |
| **SKILL-01** | **Kiến trúc phần mềm Clean Architecture & Cô lập Đa người thuê (Multi-Tenancy)** | Lê Võ Đăng Khoa & Nhựt Phùng Minh | [`SKILL_01_CLEAN_ARCHITECTURE_AND_MULTI_TENANCY.md`](file:///Users/bo/OISMS/Skills/SKILL_01_CLEAN_ARCHITECTURE_AND_MULTI_TENANCY.md) | `src/backend/src/OISM.Domain`, `OISM.Application`, `OISM.Infrastructure` |
| **SKILL-02** | **Kiểm soát Tranh chấp Đồng thời & Khóa CSDL chống Bán vượt Tồn kho (Concurrency Control)** | Lê Võ Đăng Khoa | [`SKILL_02_CONCURRENCY_CONTROL_AND_LOCKING.md`](file:///Users/bo/OISMS/Skills/SKILL_02_CONCURRENCY_CONTROL_AND_LOCKING.md) | `Pessimistic Row Lock (FOR UPDATE)`, `PostgreSQL Advisory Lock` |
| **SKILL-03** | **Bảo mật Xác thực JWT, Xoay vòng Refresh Token & Phân quyền RBAC 2 Lớp** | Nhựt Phùng Minh | [`SKILL_03_SECURITY_JWT_AND_TOKEN_ROTATION.md`](file:///Users/bo/OISMS/Skills/SKILL_03_SECURITY_JWT_AND_TOKEN_ROTATION.md) | `AuthController.cs`, `JwtTokenService.cs`, `ProtectedRoute.tsx` |
| **SKILL-04** | **Thuật toán Khử vòng lặp Cây danh mục (DFS) & Sinh mã vạch chuẩn quốc tế EAN-13** | Nhựt Phùng Minh | [`SKILL_04_ALGORITHMS_CYCLE_DETECTION_AND_BARCODE.md`](file:///Users/bo/OISMS/Skills/SKILL_04_ALGORITHMS_CYCLE_DETECTION_AND_BARCODE.md) | `CategoryService.cs`, `BarcodeGenerator.cs`, `SkuAttributeHelper.cs` |
| **SKILL-05** | **Kỹ thuật Kiểm thử Phần mềm Toàn diện (Unit & Integration Tests trên PostgreSQL thật)** | Nhựt Phùng Minh & Nam Nguyễn | [`SKILL_05_AUTOMATED_TESTING_AND_VERIFICATION.md`](file:///Users/bo/OISMS/Skills/SKILL_05_AUTOMATED_TESTING_AND_VERIFICATION.md) | `src/backend/tests/` (194 Tests Pass 100%, Code Coverage 94.83%) |

---

## Ý NGHĨA PHỤC VỤ CHẤM ĐIỂM
Mỗi tài liệu trong thư mục này đều trình bày theo cấu trúc 4 bước khoa học:
1. **Bài toán thực tế:** Nỗi đau và rủi ro nếu không áp dụng kỹ thuật.
2. **Cơ sở lý thuyết:** Trích dẫn các mô hình từ giáo trình (Wiegers, Pressman, Fowler).
3. **Hiện thực trong mã nguồn:** Đoạn code C#/SQL cụ thể kèm file nguồn.
4. **Bằng chứng kiểm chứng:** Test case tự động chứng minh kỹ năng đã chạy thành công.
