# SKILL-04: THUẬT TOÁN KHỬ VÒNG LẶP CÂY DANH MỤC (DFS) & SINH MÃ VẠCH QUỐC TẾ EAN-13

> **Người thực hiện:** Nhựt Phùng Minh (Backend 2 Lead)  
> **Áp dụng tại:** Dự án OISMS — Phân hệ Quản lý Danh mục, Sản phẩm & Quầy POS  
> **Cơ sở đối chiếu lý thuyết:** Ch14 - Information Hiding & Business Logic Classes, FR-PROD-01 & FR-PROD-03.

---

### 1. THUẬT TOÁN 1: KHỬ VÒNG LẶP CÂY DANH MỤC (CYCLE DETECTION DFS)

#### 1.1. Bài toán thực tế
Trong mô hình cây danh mục đa cấp (Category Hierarchy), một danh mục con có thể chọn danh mục khác làm cha (`ParentId`).
* Người dùng vô tình hoặc cố ý chọn danh mục con làm cha của danh mục mẹ (ví dụ: Danh mục $A$ là cha của $B$, sau đó sửa $A$ có cha là $B$).
* Khi Frontend hoặc báo cáo duyệt cây danh mục, hệ thống sẽ rơi vào vòng lặp vô tận ($A \to B \to A \to B...$), dẫn đến tràn bộ nhớ (`StackOverflowException`) và sập server.

#### 1.2. Giải pháp thuật toán đã cài đặt (`CategoryService.cs`)
Áp dụng thuật toán **Tìm kiếm theo chiều sâu (Depth-First Search - DFS)** để duyệt ngược lên chuỗi tổ tiên:
1. Khi có yêu cầu cập nhật `ParentId = targetParentId` cho danh mục `categoryId`:
   - Kiểm tra nếu `categoryId == targetParentId` &rarr; Từ chối ngay.
   - Bắt đầu duyệt từ `targetParentId`: Lần ngược lên cha của nó (`parent.ParentId`), rồi ông nội, cụ tổ...
   - Nếu trong quá trình duyệt gặp lại `categoryId` &rarr; Phát hiện chu trình khép kín (Cycle Loop).
2. Kết hợp với **PostgreSQL Advisory Lock** theo `TenantId` để ngăn chặn Race Condition khi 2 người cùng bấm sửa chéo nhau trong 1 tích tắc.
3. Mã lỗi trả về: `400 Bad Request` với thông điệp: *"Không thể gán danh mục cha vì sẽ tạo thành vòng lặp chu trình"*.

---

### 2. THUẬT TOÁN 2: TỰ ĐỘNG SINH MÃ VẠCH QUỐC TẾ EAN-13 (MODULO 10)

#### 2.1. Bài toán thực tế
Khi một sản phẩm được nhập kho hoặc bán tại quầy POS, thu ngân sử dụng máy quét laser để đọc mã vạch.
* Mã vạch EAN-13 tiêu chuẩn gồm 13 chữ số, trong đó chữ số cuối cùng (chữ số thứ 13) là **Chữ số kiểm tra (Check Digit)** bắt buộc phải tuân theo công thức toán học của tổ chức GS1 quốc tế.
* Nếu chữ số thứ 13 tính sai, máy quét laser tại quầy POS sẽ không nhận diện được mã vạch.

#### 2.2. Giải pháp thuật toán đã cài đặt (`BarcodeGenerator.cs`)
Cài đặt thuật toán **Modulo 10** theo đúng tiêu chuẩn GS1:
1. Sinh chuỗi 12 chữ số đầu tiên (gồm mã quốc gia/doanh nghiệp và mã số định danh sản phẩm):
   $$d_1, d_2, d_3, \dots, d_{12}$$
2. Tính tổng trọng số:
   $$\text{Sum} = (d_1 + d_3 + d_5 + d_7 + d_9 + d_11) \times 1 + (d_2 + d_4 + d_6 + d_8 + d_{10} + d_{12}) \times 3$$
3. Tính số kiểm tra $d_{13}$:
   $$r = \text{Sum} \pmod{10}$$
   $$d_{13} = \begin{cases} 0 & \text{nếu } r = 0 \\ 10 - r & \text{nếu } r \ne 0 \end{cases}$$
4. Kết hợp cơ chế **Retry Pattern tối đa 10 lần** kiểm tra trùng lặp trong bảng `skus` theo `tenant_id` trước khi lưu vào CSDL.

---

### 3. BẰNG CHỨNG KIỂM CHỨNG TRONG MÃ NGUỒN
* Mã nguồn cài đặt: `src/backend/src/OISM.Application/Features/Categories/Services/CategoryService.cs` và `BarcodeGenerator.cs`.
* Bài kiểm thử tự động:
  - `TC_CAT_CycleDetection_Returns400_WhenCycleDetected`: Kiểm chứng phát hiện chu trình trả mã `400 Bad Request`.
  - `TC_PROD_GenerateBarcode_ReturnsValidEan13WithCheckDigit`: Kiểm chứng mã vạch 13 số sinh ra luôn khớp 100% công thức Modulo 10.
