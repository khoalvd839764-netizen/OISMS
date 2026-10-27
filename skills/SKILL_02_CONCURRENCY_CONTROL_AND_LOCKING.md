# SKILL-02: KIỂM SOÁT TRANH CHẤP ĐỒNG THỜI & KHÓA CSDL CHỐNG BÁN VƯỢT TỒN KHO

> **Người thực hiện:** Lê Võ Đăng Khoa  
> **Áp dụng tại:** Dự án OISMS — Hệ thống Quản lý Bán hàng & Tồn kho Đa kênh  
> **Cơ sở đối chiếu lý thuyết:** Ch16 - Software Architectural Transaction Patterns (ACID, Pessimistic Locking, Concurrency Control) & NFR-PERF-02.

---

### 1. BÀI TOÁN THỰC TẾ & NỖI ĐAU HỆ THỐNG
Trong giờ cao điểm (Flash Sale, Livestream bán hàng), hàng trăm người mua cùng bấm nút "Đặt mua" cho 1 sản phẩm còn lại cuối cùng trong kho.
* Nếu chỉ đọc số lượng tồn kho thông thường (`SELECT on_hand`), nhiều luồng xử lý đồng thời đều thấy `on_hand = 1` và cùng tạo đơn thành công.
* Hậu quả: Tồn kho bị âm, cửa hàng không có hàng giao, bị sàn TMĐT đánh điểm phạt vận hành (sao quả tạ) và mất khách hàng.

---

### 2. GIẢI PHÁP KỸ THUẬT ĐÃ ÁP DỤNG

#### 2.1. Khóa cấp hàng PostgreSQL (Pessimistic Row-Level Lock - `FOR UPDATE`)
Hệ thống sử dụng cơ chế khóa bi quan ở mức CSDL trong một transaction nguyên tử:
```sql
SELECT on_hand, reserved 
FROM inventory_levels 
WHERE tenant_id = @TenantId AND branch_id = @BranchId AND sku_id = @SkuId 
FOR UPDATE;
```
* **Nguyên lý hoạt động:**
  1. Luồng (Thread/Transaction) đầu tiên chạm vào dòng dữ liệu sẽ giữ khóa `EXCLUSIVE LOCK` trên hàng đó.
  2. Hệ thống kiểm tra công thức tồn khả dụng:
     $$\text{Available} = \text{OnHand} - \text{Reserved}$$
  3. Nếu $\text{Available} \ge \text{QuantityToBuy}$: Cập nhật $\text{Reserved} = \text{Reserved} + \text{QuantityToBuy}$, cam kết giao dịch (Commit) và nhả khóa.
  4. Các luồng đến sau phải xếp hàng đợi; khi vào đọc lại dữ liệu đã cam kết, chúng thấy $\text{Available} = 0$ và bị từ chối với mã lỗi `409 Conflict`.

#### 2.2. PostgreSQL Advisory Lock & Chống gửi trùng lặp (Idempotency)
* Khi tạo đơn hàng từ webhook giả lập Shopee/TikTok, hệ thống tạo một khóa ứng dụng (**PostgreSQL Advisory Lock**) dựa trên mã băm của `(TenantId, RequestKey)`:
  ```csharp
  await _dbContext.Database.ExecuteSqlRawAsync(
      "SELECT pg_advisory_xact_lock(hashtext({0}))", lockKey);
  ```
* Ngăn chặn hoàn toàn việc xử lý trùng lặp khi mạng bị giật và kênh bán hàng gửi lại webhook (Retry webhook).

---

### 3. BẰNG CHỨNG KIỂM CHỨNG TRONG MÃ NGUỒN
* Mã nguồn cài đặt: `InventoryReservationService.cs`
* Bài kiểm thử chịu tải đồng thời: `ConcurrencyReservationTests.cs` giả lập 50 luồng Task song song cùng tranh 1 đơn vị tồn kho cuối cùng: Kết quả duy nhất 1 request thành công, 49 request còn lại nhận `409 Conflict`, số lượng tồn kho khả dụng không bao giờ bị âm.
