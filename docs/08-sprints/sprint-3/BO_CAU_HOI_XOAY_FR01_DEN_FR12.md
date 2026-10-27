# BỘ CÂU HỎI "XOÁY" VẤN ĐÁP 12 PHÂN HỆ NGHIỆP VỤ (FR-01 ĐẾN FR-12)
## DỰ ÁN OISMS (OMNICHANNEL INVENTORY & SALES MANAGEMENT SYSTEM)

> **Dành riêng cho:** **Phùng Minh Nhựt (Backend 2 Lead - Sinh viên UTH)**  
> **Mục tiêu:** Trang bị toàn bộ câu hỏi bẫy, câu hỏi xoáy sâu kỹ thuật và nghiệp vụ từ Hội đồng chấm thi/Giảng viên cho trọn vẹn 12 Yêu cầu Chức năng (FR-01 $\rightarrow$ FR-12), kèm kịch bản trả lời "ăn điểm tuyệt đối".

---

## MỤC LỤC 12 PHÂN HỆ

- [FR-01: Xác thực, Phân quyền & Cô lập Đa tổ chức (Auth & Multi-tenant)](#fr-01-xác-thực-phân-quyền--cô-lập-đa-tổ-chức-auth--multi-tenant)
- [FR-02: Danh mục Đa tầng, Biến thể SKU, Mã vạch EAN-13 & Giá bán (Catalog & Barcode)](#fr-02-danh-mục-đa-tầng-biến-thể-sku-mã-vạch-ean-13--giá-bán-catalog--barcode)
- [FR-03: Sổ cái Kho Bất biến & Chứng từ Kho (Append-Only Inventory Ledger)](#fr-03-sổ-cái-kho-bất-biến--chứng-từ-kho-append-only-inventory-ledger)
- [FR-04: Tự động tính Giá vốn WAC & Chốt Snapshot (Cost WAC & Snapshot)](#fr-04-tự-động-tính-giá-vốn-wac--chốt-snapshot-cost-wac--snapshot)
- [FR-05: Giữ chỗ Tồn kho & Chống bán vượt Flash Sale (Stock Reservation & Concurrency)](#fr-05-giữ-chỗ-tồn-kho--chống-bán-vượt-flash-sale-stock-reservation--concurrency)
- [FR-06: Chuẩn hóa Đơn hàng Đa kênh & Máy trạng thái (Omnichannel Orders & State Machine)](#fr-06-chuẩn-hóa-đơn-hàng-đa-kênh--máy-trạng-thái-omnichannel-orders--state-machine)
- [FR-07: Bán lẻ Quầy POS Cảm ứng, Quét mã & In hóa đơn nhiệt (POS Checkout & Thermal Bill)](#fr-07-bán-lẻ-quầy-pos-cảm-ứng-quét-mã--in-hóa-đơn-nhiệt-pos-checkout--thermal-bill)
- [FR-08: Trình giả lập Webhook 3 Sàn TMĐT (Marketplace Webhook Simulator)](#fr-08-trình-giả-lập-webhook-3-sàn-tmđt-marketplace-webhook-simulator)
- [FR-09: Thông báo Thời gian thực Đơn hàng mới (SignalR Realtime Notification)](#fr-09-thông-báo-thời-gian-thực-đơn-hàng-mới-signalr-realtime-notification)
- [FR-10: Tác vụ Nền Dọn dẹp & Tổng hợp Báo cáo (Hangfire Background Jobs)](#fr-10-tác-vụ-nền-dọn-dẹp--tổng-hợp-báo-cáo-hangfire-background-jobs)
- [FR-11: Báo cáo Tài chính, Vận tốc kho & Cảnh báo tồn thấp (Financial Reports & Analytics)](#fr-11-báo-cáo-tài-chính-vận-tốc-kho--cảnh-báo-tồn-thấp-financial-reports--analytics)
- [FR-12: Trí tuệ Nhân tạo Dự báo Nhu cầu & Điểm Đặt hàng lại (AI Reorder Point Forecasting)](#fr-12-trí-tuệ-nhân-tạo-dự-báo-nhu-cầu--điểm-đặt-hàng-lại-ai-reorder-point-forecasting)

---

# FR-01: XÁC THỰC, PHÂN QUYỀN & CÔ LẬP ĐA TỔ CHỨC (AUTH & MULTI-TENANT)

### ❓ Câu hỏi xoáy 1:
*"Hệ thống của em cho nhiều cửa hàng (Tenants) dùng chung một Database. Nếu một lập trình viên viết câu query `SELECT * FROM orders` mà quên thêm `WHERE tenant_id = @id` thì dữ liệu có bị lộ sang cửa hàng khác không?"*
- **Ý đồ của Thầy:** Kiểm tra xem nhóm cô lập dữ liệu bằng cách thủ công (dễ lỗi) hay dùng cơ chế framework tự động.
- **Kịch bản trả lời:**  
  *"Dạ thưa Thầy, **tuyệt đối không bị lộ ạ**! Nhóm em áp dụng tính năng **Global Query Filter** của EF Core trong tầng Infrastructure (`ApplicationDbContext`). Mọi truy vấn vào các bảng nghiệp vụ đều được EF Core tự động gắn thêm điều kiện `WHERE tenant_id = CurrentTenantId` trước khi chuyển thành câu lệnh SQL gửi xuống PostgreSQL. Dù lập trình viên có quên viết `Where(x => x.TenantId == ...)` thì hệ thống vẫn tự động lọc đúng cửa hàng của người đang đăng nhập ạ!"*
- **Bằng chứng kỹ thuật:** Tệp `ApplicationDbContext.cs`, cấu hình `modelBuilder.Entity<T>().HasQueryFilter(e => e.TenantId == _tenantContext.TenantId)`.

### ❓ Câu hỏi xoáy 2:
*"Token JWT của em có thời hạn bao lâu? Nếu bị lộ Access Token thì kẻ gian có chiếm quyền vĩnh viễn không?"*
- **Kịch bản trả lời:**  
  *"Dạ thưa Thầy, Access Token chỉ có thời hạn ngắn là **60 phút**. Nhóm áp dụng cơ chế **JWT Token Rotation** kèm Refresh Token lưu trong Database (hạn 7 ngày). Khi Refresh Token được sử dụng để lấy Access Token mới, Refresh Token cũ sẽ bị vô hiệu hóa ngay lập tức. Nếu kẻ gian cố dùng lại Refresh Token cũ, hệ thống phát hiện hành vi tấn công Replay Attack và sẽ hủy toàn bộ phiên đăng nhập của tài khoản đó ạ!"*

---

# FR-02: DANH MỤC ĐA TẦNG, BIẾN THỂ SKU, MÃ VẠCH EAN-13 & GIÁ BÁN (CATALOG & BARCODE)

### ❓ Câu hỏi xoáy 3:
*"Danh mục sản phẩm của em cho phép phân cấp cha - con nhiều tầng. Nếu người dùng chỉnh sửa danh mục A có cha là B, rồi lại sửa danh mục B có cha là A thì có bị treo đệ quy vô tận (vòng lặp chu trình) không?"*
- **Ý đồ của Thầy:** Bẫy cấu trúc dữ liệu cây (Tree Cycle Detection).
- **Kịch bản trả lời:**  
  *"Dạ thưa Thầy, hệ thống **chặn đứng 100%** lỗi này ở cả tầng Database lẫn tầng Code:  
  1. Tầng DB có Check Constraint: `CHECK (parent_id IS NULL OR parent_id <> id)` chống tự làm cha của chính mình.  
  2. Tầng Service (`CategoryService`), trước khi cập nhật `parent_id`, hệ thống dùng thuật toán duyệt ngược lên các nút tổ tiên (Ancestor Traversal) kèm `PostgreSQL Advisory Lock`. Nếu phát hiện nút cha mới chính là con cháu của nút hiện tại $\rightarrow$ API ném ngay lỗi `400 Bad Request: Phát hiện vòng lặp chu trình trong cây danh mục` ạ!"*

### ❓ Câu hỏi xoáy 4:
*"Mã vạch EAN-13 có 13 số, hệ thống tự sinh số cuối cùng (Checksum) thế nào? Hai cửa hàng khác nhau có được dùng trùng mã SKU hoặc Barcode không?"*
- **Kịch bản trả lời:**  
  *"Dạ thưa Thầy:  
  1. Số thứ 13 là **Số kiểm tra Checksum chuẩn quốc tế**: Tính bằng tổng các chữ số ở vị trí lẻ nhân 1 cộng tổng các chữ số ở vị trí chẵn nhân 3, lấy phần dư theo modulo 10 để ra số kiểm tra. Nhóm dùng tiền tố `893` (Mã quốc gia Việt Nam).  
  2. Về trùng mã: Bảng `skus` có ràng buộc Unique Index tổ hợp kèm `tenant_id`: `UNIQUE(tenant_id, code)` và `UNIQUE(tenant_id, barcode)`. Nghĩa là **trong cùng một cửa hàng thì cấm trùng**, nhưng **hai cửa hàng khác nhau thì hoàn toàn được phép trùng mã SKU/Barcode**, rất thực tế vì các shop có thể cùng bán 1 sản phẩm chính hãng ạ!"*

---

# FR-03: SỔ CÁI KHO BẤT BIẾN & CHỨNG TỪ KHO (APPEND-ONLY INVENTORY LEDGER)

### ❓ Câu hỏi xoáy 5:
*"Tại sao nhóm không lưu luôn số lượng tồn kho vào 1 cột `quantity` rồi mỗi lần bán thì chạy câu lệnh `UPDATE products SET quantity = quantity - 1` cho tiện mà phải bày ra Sổ cái làm gì?"*
- **Ý đồ của Thầy:** Kiểm tra hiểu biết về Kiểm toán kho (Audit Trail) và Tính toàn vẹn dữ liệu.
- **Kịch bản trả lời:**  
  *"Dạ thưa Thầy, cập nhật đè (In-place UPDATE) là cách làm rất nguy hiểm của các phần mềm cũ:  
  - Nó làm mất sạch lịch sử kiểm toán: Không thể biết hàng mất lúc nào, ai xuất, nhập từ đâu, và tại sao số tồn bị giảm.  
  - Dễ gây xung đột và sai lệch số liệu khi có nhiều luồng cùng ghi.  
  Nhóm em áp dụng **Sổ cái bất biến (Append-Only Ledger)** như sổ ngân hàng: Mọi biến động đều `INSERT` một dòng `inventory_transactions` có loại phiếu, người làm và số dư tức thời `BalanceAfter`. Tầng DB có trigger `ledger_append_only` ném lỗi `23514` cấm tiệt lệnh `UPDATE` hay `DELETE` ạ!"*

### ❓ Câu hỏi xoáy 6:
*"Quy trình Chuyển kho 2 bước (Transfer) giữa 2 chi nhánh diễn ra thế nào? Hàng đang trên đường đi thì tính vào tồn kho của ai?"*
- **Kịch bản trả lời:**  
  *"Dạ thưa Thầy:  
  - **Bước 1 (Xuất chuyển - In-Transit):** Chi nhánh xuất bấm gửi hàng $\rightarrow$ Sổ cái chi nhánh xuất ghi một dòng xuất kho (`OUT`), tồn kho thực tế giảm ngay. Hàng lúc này rơi vào trạng thái `InTransit` (Đang vận chuyển), không thuộc tồn khả dụng của cả kho gửi lẫn kho nhận để tránh bán nhầm.  
  - **Bước 2 (Xác nhận nhập - Received):** Khi chi nhánh nhận nhận được kiện hàng và kiểm đếm $\rightarrow$ Bấm xác nhận $\rightarrow$ Sổ cái chi nhánh nhận mới ghi dòng nhập kho (`IN`), lúc này hàng mới chính thức tăng vào kho nhận ạ!"*

---

# FR-04: TỰ ĐỘNG TÍNH GIÁ VỐN WAC & CHỐT SNAPSHOT (COST WAC & SNAPSHOT)

### ❓ Câu hỏi xoáy 7:
*"Công thức WAC tính thế nào? Nếu một mặt hàng đang hết sạch tồn kho (OnHand = 0) mà nhập một lô mới thì công thức xử lý ra sao?"*
- **Kịch bản trả lời:**  
  *"Dạ thưa Thầy, công thức WAC (Bình quân gia quyền) là:  
  $$AvgCost_{mới} = \frac{(OnHand_{cũ} \times AvgCost_{cũ}) + (ImportQty \times ImportPrice)}{OnHand_{cũ} + ImportQty}$$  
  Trường hợp đặc biệt: Nếu tồn kho cũ bằng 0 ($OnHand_{cũ} \le 0$), hệ thống nhận diện mẫu số ban đầu bằng 0 nên gán thẳng: $AvgCost_{mới} = ImportPrice$ (Lấy luôn giá của lô hàng mới nhập làm giá vốn mới), tránh hoàn toàn lỗi chia cho 0 (Divide by Zero) ạ!"*

### ❓ Câu hỏi xoáy 8:
*"Snapshot Giá vốn (Cost Snapshot) vào đơn hàng là gì? Tại sao bắt buộc phải có?"*
- **Kịch bản trả lời:**  
  *"Dạ thưa Thầy, khi một đơn hàng được xác nhận hoặc checkout tại POS, hệ thống lấy giá vốn WAC của sản phẩm tại **đúng giây phút đó** và lưu cứng vào cột `order_items.cost_price`.  
  Bắt buộc phải có vì sau này cửa hàng nhập thêm nhiều lô hàng mới với giá biến động làm thay đổi giá vốn kho, thì giá vốn của các đơn hàng trong quá khứ vẫn được bảo toàn nguyên vẹn. Nhờ đó, báo cáo Lợi nhuận gộp $GrossProfit = Revenue - Cost$ của năm trước hay tháng trước luôn đúng tuyệt đối 100% ạ!"*

---

# FR-05: GIỮ CHỖ TỒN KHO & CHỐNG BÁN VƯỢT FLASH SALE (STOCK RESERVATION & CONCURRENCY)

### ❓ Câu hỏi xoáy 9:
*"Trong đợt Flash Sale, kho còn đúng 1 cái áo. Có 50 khách cùng bấm mua trong cùng 1 tích tắc. Hệ thống của em xử lý thế nào để không bị âm kho và không sập DB?"*
- **Ý đồ của Thầy:** Kiểm tra giải pháp chịu tải cao và chống Race Condition (`NFR-PERF-02`).
- **Kịch bản trả lời:**  
  *"Dạ thưa Thầy:  
  1. Hệ thống dùng công thức tồn: $\mathbf{Available} = \mathbf{OnHand} - \mathbf{Reserved}$.  
  2. Khi có request, tầng Service áp dụng cơ chế khóa bi quan **Pessimistic Locking**: `SELECT ... FROM inventory_balances WHERE ... FOR UPDATE`.  
  3. Lệnh này khóa đúng dòng SKU đó trong vài mili-giây. Request đầu tiên vào kiểm tra `Available = 1 >= 1` $\rightarrow$ tăng `Reserved = 1` $\rightarrow$ thành công (`200 OK`).  
  4. 49 requests tiếp theo khi đọc được dòng dữ liệu thì thấy `Available = 0` $\rightarrow$ lập tức trả về mã lỗi nghiệp vụ `409 Conflict: Không đủ tồn kho khả dụng`.  
  5. Không có deadlock, không bị lỗi 500, và sau test số lượng tồn tuyệt đối không bị âm ạ!"*

### ❓ Câu hỏi xoáy 10:
*"Nếu khách đặt hàng giữ chỗ xong nhưng không thanh toán thì cái áo đó bị giam trong kho mãi mãi à?"*
- **Kịch bản trả lời:**  
  *"Dạ không thưa Thầy! Bản ghi `stock_reservations` có trường `expires_at` (thường là 15 đến 30 phút). Nhóm cấu hình **Hangfire Background Job** quét định kỳ mỗi phút: Nếu đơn nào hết hạn giữ chỗ mà chưa thanh toán, Hangfire sẽ tự động gọi hàm `ReleaseStock` để giảm `Reserved`, trả lại số lượng tồn khả dụng cho khách khác mua ngay ạ!"*

---

# FR-06: CHUẨN HÓA ĐƠN HÀNG ĐA KÊNH & MÁY TRẠNG THÁI (OMNICHANNEL ORDERS & STATE MACHINE)

### ❓ Câu hỏi xoáy 11:
*"Mỗi sàn Shopee, TikTok, POS có cấu trúc đơn hàng khác nhau. Em làm cách nào để lưu vào chung 1 bảng `orders`?"*
- **Kịch bản trả lời:**  
  *"Dạ thưa Thầy, em sử dụng mô hình **Canonical Order Model** (Mô hình Đơn hàng Chuẩn hóa):  
  - Dù đơn đến từ kênh nào (Shopee, TikTok, Lazada, Web hay POS), hệ thống đều đi qua một Adapter chuyển đổi về cấu trúc chuẩn thống nhất: gồm Mã đơn chuẩn, Danh sách SKU, Số lượng, Đơn giá, Thông tin người nhận, và Kênh bán (`Channel = POS | SHOPEE | TIKTOK | LAZADA`).  
  - Nhờ vậy, toàn bộ nghiệp vụ xử lý kho, trừ tồn, tính lãi và xuất hóa đơn chỉ cần viết đúng 1 lần cho Canonical Order Model mà không phải viết riêng cho từng sàn ạ!"*

### ❓ Câu hỏi xoáy 12:
*"Máy trạng thái đơn hàng (Order State Machine) của em hoạt động thế nào? Một đơn đã 'Completed' hoặc 'Cancelled' có được phép chuyển trạng thái nữa không?"*
- **Kịch bản trả lời:**  
  *"Dạ thưa Thầy, vòng đời đơn hàng đi qua 5 trạng thái: `Draft` $\rightarrow$ `Pending` $\rightarrow$ `Confirmed` $\rightarrow$ `Processing` $\rightarrow$ `Completed`.  
  - Tầng Service kiểm soát ma trận chuyển đổi hợp lệ: Chỉ được hủy (`Cancelled`) khi đơn ở trạng thái `Pending` hoặc `Confirmed` (chưa xuất kho).  
  - Khi đơn đã vào trạng thái cuối cùng là `Completed` (Đã giao thành công) hoặc `Cancelled` (Đã hủy), State Machine **khóa cứng đơn hàng**. Mọi yêu cầu chuyển trạng thái tiếp theo đều bị chặn và trả về lỗi `400 Bad Request: Đơn hàng đã ở trạng thái đóng, không thể thay đổi` ạ!"*

---

# FR-07: BÁN LẺ QUẦY POS CẢM ỨNG, QUÉT MÃ & IN HÓA ĐƠN NHIỆT (POS CHECKOUT & THERMAL BILL)

### ❓ Câu hỏi xoáy 13:
*"Giỏ hàng tại quầy POS có 5 sản phẩm. Khách bấm Thanh toán, nhưng sản phẩm thứ 5 vừa bị khách trên mạng mua mất. Hệ thống xử lý giỏ hàng này ra sao?"*
- **Ý đồ của Thầy:** Kiểm tra tính nguyên tử (Atomicity) của giao dịch bán lẻ.
- **Kịch bản trả lời:**  
  *"Dạ thưa Thầy, hệ thống áp dụng nguyên lý **Nguyên tử (All-or-Nothing)** thông qua Database Transaction (`NpgsqlTransaction`):  
  - Khi thu ngân bấm thanh toán, toàn bộ 5 sản phẩm được kiểm tra và giữ chỗ đồng thời trong một giao dịch duy nhất.  
  - Nếu chỉ cần 1 sản phẩm bị thiếu tồn kho, hệ thống lập tức **ROLLBACK toàn bộ**, không trừ bất kỳ món nào và trả về thông báo lỗi chỉ đích danh sản phẩm đã hết hàng. Tuyệt đối không có chuyện 4 món đầu thanh toán được còn món thứ 5 bị lỗi dở dang ạ!"*

### ❓ Câu hỏi xoáy 14:
*"Làm sao ứng dụng React POS in được hóa đơn ra máy in nhiệt 80mm tại quầy?"*
- **Kịch bản trả lời:**  
  *"Dạ thưa Thầy, ứng dụng Web POS PWA sử dụng mẫu in chuẩn HTML/CSS `@media print` được định dạng chiều rộng cố định đúng `80mm` (Font chữ Monospace, tự động ngắt trang, căn lề 0mm). Khi bấm Thanh toán thành công, hệ thống tự động kích hoạt `window.print()` gửi lệnh thẳng tới driver máy in nhiệt ESC/POS kết nối qua cổng USB hoặc mạng LAN ạ!"*

---

# FR-08: TRÌNH GIẢ LẬP WEBHOOK 3 SÀN TMĐT (MARKETPLACE WEBHOOK SIMULATOR)

### ❓ Câu hỏi xoáy 15:
*"Tại sao nhóm phải làm Trình giả lập Webhook (Simulator) làm gì? Sao không kết nối thẳng vào API thật của Shopee?"*
- **Kịch bản trả lời:**  
  *"Dạ thưa Thầy, có 2 lý do kỹ thuật mang tính thực tế rất cao:  
  1. **Điều kiện pháp lý:** Để kết nối API thật của Shopee/TikTok Shop Partner, cần phải có Giấy phép Đăng ký Doanh nghiệp thật, ký hợp đồng và được sàn duyệt sandbox mất nhiều tuần.  
  2. **Phục vụ Kiểm thử Chịu tải (Stress Test) và Chấm điểm:** Simulator cho phép nhóm chủ động bắn đơn mẫu bất cứ lúc nào, hỗ trợ chế độ bắn đơn hàng loạt (`/api/simulator/bulk-orders`) để biểu diễn cho Thầy Cô thấy luồng webhook đổ về và cơ chế chống bán vượt hoạt động chuẩn xác 100% ạ!"*

### ❓ Câu hỏi xoáy 16:
*"Nếu sàn TMĐT bị chập chờn mạng và bắn trùng 1 đơn hàng 3 lần liên tiếp thì hệ thống của em có bị trừ kho 3 lần không?"*
- **Kịch bản trả lời:**  
  *"Dạ **tuyệt đối không**! Hệ thống áp dụng cơ chế **Xử lý Bất biến (Idempotent Webhook Consumer)**: Bảng `orders` có trường `external_order_id` (Mã đơn của sàn Shopee/TikTok) kèm Unique Index. Khi đơn đầu tiên vào, hệ thống xử lý bình thường. Hai lần bắn lặp tiếp theo, hệ thống kiểm tra thấy mã đơn sàn đã tồn tại thì lập tức trả về `200 OK` xác nhận cho sàn mà không trừ kho lần thứ hai ạ!"*

---

# FR-09: THÔNG BÁO THỜI GIAN THỰC ĐƠN HÀNG MỚI (SIGNALR REALTIME NOTIFICATION)

### ❓ Câu hỏi xoáy 17:
*"SignalR hoạt động thế nào? Tại sao không dùng polling (cứ 3 giây gọi API 1 lần) cho dễ?"*
- **Kịch bản trả lời:**  
  *"Dạ thưa Thầy:  
  - **Polling** gửi request liên tục làm lãng phí băng thông, gây nghẽn CPU máy chủ và có độ trễ cao (phải chờ hết chu kỳ poll).  
  - Nhóm em dùng **SignalR (WebSockets)** tạo một đường ống kết nối 2 chiều duy nhất. Khi có đơn hàng mới từ Webhook sàn hoặc POS, Backend chủ động 'đẩy' (Push) một gói tin nhẹ dạng JSON xuống Client. Trình duyệt nhận được gói tin lập tức reo chuông ting ting và hiển thị popup thông báo trong vòng **dưới 100ms** ạ!"*

### ❓ Câu hỏi xoáy 18:
*"Cửa hàng A có đơn hàng mới, chuông thông báo có bị reo nhầm trên màn hình của Cửa hàng B không?"*
- **Kịch bản trả lời:**  
  *"Dạ không thưa Thầy! Nhóm em sử dụng tính năng **Group của SignalR Hub**: Khi client kết nối WebSocket, Middleware xác thực JWT trích xuất `TenantId` và đưa kết nối vào Group riêng `Groups.AddToGroupAsync(Context.ConnectionId, tenantId)`. Khi bắn thông báo đơn mới, server chỉ gửi đến đúng Group của Tenant đó: `Clients.Group(tenantId).SendAsync(...)` nên dữ liệu hoàn toàn cô lập ạ!"*

---

# FR-10: TÁC VỤ NỀN DỌN DẸP & TỔNG HỢP BÁO CÁO (HANGFIRE BACKGROUND JOBS)

### ❓ Câu hỏi xoáy 19:
*"Hangfire chạy ngầm thế nào? Nếu máy chủ Backend bị khởi động lại (restart) thì các tác vụ ngầm đang chạy dở có bị mất không?"*
- **Kịch bản trả lời:**  
  *"Dạ thưa Thầy, Hangfire lưu toàn bộ trạng thái Job và Lịch trình chạy vào **Database PostgreSQL thật** (chứ không lưu trong RAM). Do đó, nếu máy chủ bị restart đột ngột, khi khởi động lại Hangfire sẽ tự động đọc từ CSDL lên và tiếp tục thực thi các Job còn dở dang hoặc bị trễ mà không hề bị mất dữ liệu ạ!"*

### ❓ Câu hỏi xoáy 20:
*"Bảng `daily_sales_summaries` sinh ra để làm gì? Sao không tính toán trực tiếp từ bảng `orders` mỗi khi người dùng xem báo cáo?"*
- **Kịch bản trả lời:**  
  *"Dạ thưa Thầy, để phục vụ yêu cầu hiệu năng **NFR-PERF-01 (Thời gian phản hồi báo cáo < 300ms)**:  
  Nếu chuỗi cửa hàng hoạt động lâu năm có hàng triệu dòng đơn hàng, mỗi lần mở Dashboard mà quét `SUM/COUNT` trên bảng `orders` thì database sẽ bị quá tải và phản hồi mất cả chục giây.  
  Vì vậy, cứ đúng 00:00 mỗi đêm, Hangfire chạy ngầm tổng hợp sẵn doanh thu, giá vốn của ngày hôm đó vào bảng `daily_sales_summaries`. Khi người dùng xem báo cáo năm, API chỉ cần đọc vài trăm dòng đã tổng hợp sẵn, tốc độ phản hồi cực nhanh dưới **50ms** ạ!"*

---

# FR-11: BÁO CÁO TÀI CHÍNH, VẬN TỐC KHO & CẢNH BÁO TỒN THẤP (FINANCIAL REPORTS & ANALYTICS)

### ❓ Câu hỏi xoáy 21:
*"Công thức tính Lợi nhuận gộp (Gross Profit) là gì? Một nhân viên thu ngân (Staff) đăng nhập vào có xem được số tiền lãi của cửa hàng không?"*
- **Kịch bản trả lời:**  
  *"Dạ thưa Thầy:  
  1. Công thức:  
     $$\mathbf{GrossProfit} = \mathbf{NetRevenue} - \mathbf{COGS}$$  
     *(Doanh thu thuần trừ Tổng giá vốn hàng bán dựa trên Snapshot Giá vốn WAC).*  
  2. Về phân quyền: **Tuyệt đối không!** Endpoint `/api/reports/revenue-profit` được gắn thuộc tính `[Authorize(Roles = "Owner")]`. Nhân viên quầy (Staff) nếu cố tình gọi API này sẽ nhận mã lỗi **`403 Forbidden`**. Chỉ có Chủ shop (Owner) mới có quyền xem thông tin tài chính mật này ạ!"*

### ❓ Câu hỏi xoáy 22:
*"Báo cáo Vận tốc kho (Inventory Velocity) phân loại hàng Bán chạy (Fast-moving) và Hàng ế (Slow-moving) dựa trên tiêu chí nào?"*
- **Kịch bản trả lời:**  
  *"Dạ thưa Thầy, hệ thống dựa trên **Tốc độ bán hàng trung bình ngày (Daily Sales Rate - DSR)** và **Số ngày tồn kho (Days of Inventory On Hand - DIO)** trong khoảng thời gian phân tích (30 ngày gần nhất):  
  - **Fast-moving:** Những SKU có số lượng bán ra cao, tốc độ quay vòng nhanh và số ngày dự kiến hết hàng thấp.  
  - **Slow-moving:** Những SKU có số lượng tồn kho lớn nhưng trong 30 ngày qua không phát sinh giao dịch bán hoặc tốc độ bán gần bằng 0. Giúp chủ shop lên kế hoạch xả hàng cắt lỗ ạ!"*

---

# FR-12: TRÍ TUỆ NHÂN TẠO DỰ BÁO NHU CẦU & ĐIỂM ĐẶT HÀNG LẠI (AI REORDER POINT FORECASTING)

### ❓ Câu hỏi xoáy 23:
*"Tính năng AI trong hệ thống của em dùng thuật toán gì? Dữ liệu đầu vào lấy từ đâu để gợi ý nhập hàng?"*
- **Kịch bản trả lời:**  
  *"Dạ thưa Thầy, hệ thống sử dụng mô hình **Dự báo Điểm Đặt Hàng Lại Thông Minh (Smart Reorder Point - ROP)**:  
  $$\mathbf{ROP} = (\mathbf{DSR} \times \mathbf{LeadTime}) + \mathbf{SafetyStock}$$  
  - Dữ liệu đầu vào lấy từ 2 nguồn: Lịch sử xuất bán thực tế trong Sổ cái kho (`InventoryTransaction`) và Thời gian nhà cung cấp giao hàng (`LeadTime`).  
  - Hệ thống tính toán độ lệch chuẩn của nhu cầu để xác định lượng Tồn kho an toàn (`SafetyStock`).  
  - Khi tồn khả dụng rơi xuống dưới ngưỡng $ROP$, hệ thống tự động tính ra số lượng đề xuất cần nhập thêm: $SuggestedQty = MaxStock - AvailableStock$ để chủ cửa hàng chỉ cần 1 click là tạo được phiếu đặt hàng nhà cung cấp ạ!"*

### ❓ Câu hỏi xoáy 24:
*"Nếu một mặt hàng mới nhập về bán được 2 ngày, dữ liệu quá ít thì mô hình AI dự báo có bị sai lệch không?"*
- **Kịch bản trả lời:**  
  *"Dạ thưa Thầy, đây là bài toán **Cold Start (Khởi đầu lạnh)** mà nhóm em đã xử lý:  
  Nếu dữ liệu lịch sử bán hàng dưới 7 ngày, hệ thống sẽ tự động chuyển sang chế độ **Fallback an toàn**: Sử dụng ngưỡng tồn tối thiểu do người dùng thiết lập thủ công (`Default Reorder Point`) kết hợp với số lượng bán thực tế của những ngày đầu. Khi tích lũy đủ dữ liệu từ 14 đến 30 ngày, hệ thống mới tự động kích hoạt trọng số tính toán theo mô hình thống kê đầy đủ ạ!"*

---

> [!TIP]
> **CHIẾN THUẬT VÀNG KHI ĐỨNG TRƯỚC HỘI ĐỒNG:**  
> - Luôn trả lời dõng dạc: **"Dạ thưa Thầy/Cô..."**  
> - Bắt đầu bằng câu khẳng định chắc nịch (**"Dạ có/Dạ không"**), sau đó mới giải thích logic.  
> - Nhắc đến các công nghệ và mã lỗi chuẩn: `EF Core Global Query Filter`, `Pessimistic Locking`, `Trigger 23514`, `HTTP 409 Conflict`, `RFC 7807`.  
> - Tự tin làm chủ mã nguồn: Bạn đã có **115/115 Unit Tests pass 100%**, không có gì phải lo sợ!
