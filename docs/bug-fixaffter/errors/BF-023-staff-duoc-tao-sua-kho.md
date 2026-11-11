# BF-023 — Staff được tạo, sửa, bật/tắt kho

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ❌ Chưa sửa (cần nhóm quyết định) |
| **Ưu tiên** | P2 |
| **Khu vực** | Backend / Tổ chức / Phân quyền |
| **Phát hiện** | 06/11/2026, đối chiếu code với ma trận RBAC |

## Mô tả lỗi

**Yêu cầu:** `FR-AUTH-04` và ma trận RBAC: chỉ `Owner` tạo, cập nhật, bật/tắt chi nhánh và kho; `Staff` chỉ xem.

**Thực tế:** `WarehousesController` cho `Owner,Staff` gọi `POST /api/warehouses`, `PUT /api/warehouses/{id}`, `PUT /api/warehouses/{id}/toggle-status`. Chi nhánh thì đúng (`BranchController` chỉ `Owner`).

**Ảnh hưởng:** Staff có thể tắt kho đang dùng hoặc đổi chi nhánh của kho (backend vẫn chặn đổi khi kho đã có tồn/sổ cái).

**Vị trí:** `src/backend/src/OISM.WebApi/Controllers/WarehousesController.cs:65-66`, `:90-91`, `:125-126`.

## Điều kiện đóng

- Nhóm chốt: sửa code về `Owner` hay sửa yêu cầu cho phép `Staff` quản lý kho.
- Nếu sửa code: đổi `Roles`, ẩn nút tương ứng trên `OrganizationPage.tsx` cho Staff, thêm test 403.
