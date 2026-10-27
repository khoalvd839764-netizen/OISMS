# BỘ PHAO VẤN ĐÁP CHUYÊN SÂU: 12 PHÂN HỆ NGHIỆP VỤ & KỊCH BẢN BẢO VỆ BACKEND 2 (DỰ ÁN OISMS)

> **Dành cho:** Nhựt Phùng Minh (TV2 - Backend 2 Lead)  
> **Mục tiêu:** Nắm chắc 100% bản chất vận hành chuỗi bán lẻ, giải mã toàn bộ câu hỏi "xoáy bẫy" của Giảng viên và tự tin bảo vệ phần việc Sprint 3 (`OISMS-41`).

---

# PHẦN 1: 12 PHÂN HỆ NGHIỆP VỤ & CÁC CÂU HỎI "XOÁY TỬ HUYỆT" CỦA THẦY

---

### FR-01: Xác thực, Quản trị Cửa hàng & Đa người thuê (Auth & Multi-Tenancy)
* **Bản chất đời thường:** Giống như một tòa nhà chung cư có nhiều căn hộ dùng chung điện nước. Cửa hàng A (thời trang) và Cửa hàng B (giày dép) dùng chung một phần mềm OISMS, nhưng dữ liệu của hai bên hoàn toàn cách ly. Nhân viên shop A không bao giờ xem được doanh thu, kho hàng hay giá nhập của shop B.
* **Cơ chế kỹ thuật cốt lõi:**
  - Mô hình **Shared Database (Dùng chung CSDL)**: Mọi bảng vận hành đều có cột bắt buộc `TenantId`.
  - **EF Core Global Query Filter**: Tự động chèn `WHERE tenant_id = @CurrentTenantId` vào 100% câu truy vấn đọc/ghi. Lập trình viên dù quên viết điều kiện thì CSDL vẫn tự động lọc, cam kết **0% rò rỉ dữ liệu**.
  - **JWT Token Rotation**: Access Token sống 60 phút, Refresh Token dùng 1 lần (dùng xong thu hồi ngay và cấp mã mới).
* **Câu hỏi Thầy sẽ xoáy:** *"Nếu hacker sửa mã TenantId trên URL hoặc sửa body gói tin gửi lên thì sao?"*
  - **Cách trả lời ăn điểm:** *"Dạ thưa Thầy, hệ thống của tụi em **tuyệt đối không lấy TenantId từ Client gửi lên**. `TenantId` được máy chủ bóc tách trực tiếp từ chữ ký số bảo mật của mã JWT Token sau khi đã giải mã an toàn qua Middleware, người dùng không thể can thiệp giả mạo được ạ."*

---

### FR-02: Danh mục Sản phẩm, Biến thể SKU & Mã vạch EAN-13 (Catalog & Barcode)
* **Bản chất đời thường:** Một chiếc áo sơ mi có thể có 3 màu (Trắng, Đen, Xanh) và 3 kích cỡ (S, M, L) $\rightarrow$ sinh ra 9 món hàng riêng biệt trên kệ. Mỗi món hàng phải có một mã định danh (**SKU**) và một **mã vạch 13 số** riêng để dán tem, khi khách mang ra quầy thì thu ngân cầm máy quét tít một cái là hiện đúng giá tiền.
* **Cơ chế kỹ thuật cốt lõi:**
  - Phân tách rõ: `Product` (Sản phẩm gốc mô tả chung) và `ProductVariant / SKU` (Đơn vị lưu kho thực tế).
  - Mã `SkuCode` là duy nhất trong cùng một Tenant. Thuộc tính động lưu dạng `jsonb`.
  - **Thuật toán sinh mã vạch EAN-13**: Dùng tiền tố quốc gia Việt Nam (893) + Mã doanh nghiệp + Mã sản phẩm + **Chữ số kiểm tra thứ 13 (Check Digit)** tính tự động theo thuật toán **Modulo 10** chuẩn quốc tế.
* **Câu hỏi Thầy sẽ xoáy:** *"Tại sao không lưu thẳng số lượng tồn kho vào bảng Product mà phải tách ra SKU?"*
  - **Cách trả lời ăn điểm:** *"Dạ thưa Thầy, khách hàng mua là mua chiếc áo sơ mi size M màu Trắng chứ không ai mua 'áo sơ mi chung chung'. Nếu không tách ra SKU thì kho không thể biết được trong tiệm đang còn bao nhiêu cái size M, bao nhiêu cái size L, dẫn đến việc trên phần mềm báo còn hàng mà thực tế trong kho lại hết đúng kích cỡ khách cần."*

---

### FR-03: Sổ cái Kho Bất biến Chỉ Ghi Thêm (Append-Only Inventory Ledger)
* **Bản chất đời thường:** Giống hệt như **sao kê tài khoản ngân hàng**. Tiền tăng hay giảm đều phải có một dòng lịch sử ghi rõ ai chuyển, lúc mấy giờ, số dư sau đó là bao nhiêu. Nếu nhân viên lỡ tay gõ nhầm số lượng, **tuyệt đối không được vào ô số lượng để sửa đè hay xóa đi**, mà bắt buộc phải lập một phiếu điều chỉnh bù trừ mới để ghi nhận sai sót.
* **Cơ chế kỹ thuật cốt lõi:**
  - Bảng `InventoryTransaction` hoạt động theo nguyên tắc **Append-Only (Chỉ INSERT, cấm tuyệt đối UPDATE và DELETE)**.
  - Mọi nghiệp vụ: Nhập kho NCC, Bán tại POS, Khách trả hàng, Chuyển kho 2 bước, hay Kiểm kê cân bằng kho đều phải sinh 1 dòng giao dịch lưu trữ: `TenantId`, `BranchId`, `SkuId`, `Type (IN/OUT)`, `Quantity`, `BalanceAfter` (Số dư tồn kho ngay sau giao dịch) và `ReferenceId` (Mã chứng từ gốc).
* **Câu hỏi Thầy sẽ xoáy:** *"Nếu thủ kho kiểm kê đếm thấy thực tế bị mất 2 cái áo thì xử lý thế nào khi phần mềm không cho sửa số tồn?"*
  - **Cách trả lời ăn điểm:** *"Dạ thưa Thầy, thủ kho sẽ lập một Phiên kiểm kê (Stocktake). Hệ thống sẽ tự động sinh một **bút toán bù trừ (Compensating Ledger Transaction)** ghi nhận chiều xuất `OUT` với số lượng là 2, lý do là 'Lệch kiểm kê', và cập nhật `BalanceAfter` mới. Nhờ vậy, lịch sử kho luôn minh bạch 100%, quản lý mở ra là biết ngày nào mất hàng và ai là người chịu trách nhiệm kiểm đếm."*

---

### FR-04: Tính Giá vốn WAC & Snapshot Lịch sử (Weighted Average Cost)
* **Bản chất đời thường:** Đợt 1 nhập 10 cái áo giá 100k/cái. Đợt 2 giá xăng tăng, nhập thêm 10 cái áo đó với giá 120k/cái. Lúc này trong kho có 20 cái áo, giá vốn trung bình của mỗi cái là 110k. Khi bán cho khách giá 150k, hệ thống phải biết giá vốn lúc này là 110k để tính ra bạn lãi đúng 40k/cái.
* **Cơ chế kỹ thuật cốt lõi:**
  - **Công thức tính WAC khi xác nhận Phiếu nhập kho:**
    $$\text{Giá vốn mới} = \frac{(\text{Tồn cũ} \times \text{Giá vốn cũ}) + (\text{Số lượng nhập} \times \text{Giá nhập})}{\text{Tồn cũ} + \text{Số lượng nhập}}$$
  - **Cơ chế Snapshot Giá vốn:** Ngay khi đơn hàng chuyển sang `Confirmed` hoặc thanh toán tại quầy POS, hệ thống lập tức "đóng băng" (snapshot) đơn giá vốn hiện hành vào trường `OrderItem.CostPrice`.
* **Câu hỏi Thầy sẽ xoáy:** *"Tại sao khi tính lợi nhuận của tháng trước, không lấy giá vốn hiện tại nhân với số lượng đã bán mà phải snapshot?"*
  - **Cách trả lời ăn điểm:** *"Dạ thưa Thầy, nếu sang tháng sau giá nhập hàng tăng vọt lên 200k, mà ta lấy giá vốn 200k đó áp vào các đơn hàng đã bán từ tháng trước thì toàn bộ báo cáo tài chính của tháng trước sẽ bị méo mó, biến đơn đang có lãi thành lỗ. Snapshot giá vốn giúp bảo toàn vĩnh viễn tính chính xác của lịch sử kế toán."*

---

### FR-05: Động cơ Giữ chỗ Tồn kho & Chống bán vượt (Stock Reservation & Anti-Oversell)
* **Bản chất đời thường:** Trong đợt Flash Sale ngày 11/11, trong kho chỉ còn đúng 1 đôi giày duy nhất. Có 50 người trên mạng cùng bấm nút "Mua hàng" trong cùng một giây. Nếu không giữ chỗ, cả 50 người đều bấm mua thành công $\rightarrow$ shop không có 49 đôi giày còn lại để giao $\rightarrow$ bị sàn Shopee/TikTok phạt tiền và đánh sập gian hàng.
* **Cơ chế kỹ thuật cốt lõi:**
  - Công thức tồn kho khả dụng:
    $$\text{Available (Bán được)} = \text{OnHand (Hàng trong kho)} - \text{Reserved (Hàng khách đang giữ chỗ)}$$
  - Áp dụng **Khóa bi quan CSDL (Pessimistic Locking - `SELECT ... FOR UPDATE`)**: Khi có yêu cầu mua, hệ thống khóa dòng dữ liệu của SKU đó lại trong tích tắc. Người thứ nhất vào thấy `Available = 1`, hệ thống tăng `Reserved = 1` rồi nhả khóa. 49 người tiếp theo vào thấy `Available = 0` sẽ lập tức bị báo "Hết hàng".
* **Câu hỏi Thầy sẽ xoáy:** *"Nếu khách bấm đặt hàng giữ chỗ xong rồi... đi ngủ, không chịu thanh toán thì hàng bị giam mãi mãi sao?"*
  - **Cách trả lời ăn điểm:** *"Dạ không ạ! Hệ thống của tụi em có tác vụ ngầm Hangfire (FR-10). Nếu sau 15 phút mà đơn giữ chỗ không được thanh toán hoặc xác nhận, robot sẽ tự động hủy đơn và hoàn trả số lượng `Reserved` đó về lại quỹ `Available` cho khách khác mua ngay lập tức ạ."*

---

### FR-06: Tiếp nhận Đơn hàng Đa kênh & Máy trạng thái (Omnichannel Order Hub)
* **Bản chất đời thường:** Khách mua ở quầy POS thì có hóa đơn bán lẻ; khách mua trên Shopee thì có mã đơn Shopee; khách mua TikTok thì có mã đơn TikTok. Nếu để rời rạc thì nhân viên phải mở 3 màn hình khác nhau để xử lý. Hệ thống gom tất cả về một định dạng chuẩn (**Canonical Order Model**) để nhân viên chỉ cần đứng tại một màn hình đóng gói.
* **Cơ chế kỹ thuật cốt lõi:**
  - Chuẩn hóa mọi nguồn đơn (POS, Admin, Webhook sàn) về một thực thể `Order` duy nhất.
  - Vận hành theo **Máy trạng thái nghiêm ngặt (Order State Machine)**:
    $$\text{Draft/Pending} \longrightarrow \text{Reserved (Giữ hàng)} \longrightarrow \text{Confirmed (Duyệt/Trừ kho)} \longrightarrow \text{Processing (Đóng gói)} \longrightarrow \text{Completed (Hoàn tất)}$$
    *(Hoặc rẽ nhánh sang `Cancelled` để nhả tồn giữ chỗ).*
  - **Tuyệt đối cấm nhảy cóc trạng thái** (ví dụ từ `Draft` không thể nhảy thẳng sang `Completed` mà chưa qua trừ tồn kho).
* **Câu hỏi Thầy sẽ xoáy:** *"Sự khác biệt giữa việc Hủy đơn ở trạng thái Reserved và Hủy đơn ở trạng thái Confirmed là gì?"*
  - **Cách trả lời ăn điểm:** *"Dạ thưa Thầy, ở trạng thái `Reserved`, hàng mới chỉ bị giữ chỗ chứ chưa xuất ra khỏi kho, nên khi Hủy đơn hệ thống chỉ cần trừ `Reserved` để trả lại `Available`, không ghi sổ cái. Còn nếu đơn đã sang `Confirmed` (tức là hàng đã chính thức xuất kho và đã ghi dòng OUT vào sổ cái), thì muốn hủy bắt buộc phải thực hiện nghiệp vụ **Nhập trả hàng (Return)** để sinh một dòng IN vào sổ cái nhằm cân bằng kho."*

---

### FR-07: Bán hàng Tốc độ cao tại Quầy (Web POS PWA Checkout)
* **Bản chất đời thường:** Giờ cao điểm tại cửa hàng, khách xếp hàng dài chờ thanh toán. Thu ngân cần một màn hình cảm ứng siêu nhạy, hỗ trợ phím tắt, tìm kiếm sản phẩm cực nhanh, thanh toán tiền mặt/quét mã VietQR và máy in tự động thò hóa đơn ra dưới 3 giây.
* **Cơ chế kỹ thuật cốt lõi:**
  - Xây dựng dạng ứng dụng **PWA (Progressive Web App)** chạy mượt mà trên trình duyệt máy tính và máy tính bảng.
  - Tối ưu tốc độ tìm kiếm/quét mã vạch: thời gian phản hồi đạt tiêu chuẩn **p95 < 200ms**.
  - **Giao dịch thanh toán nguyên tử (Fast Atomic Checkout)**: Thực hiện chuỗi `Giữ chỗ -> Xác nhận -> Trừ tồn kho -> Chốt giá vốn` chỉ trong **đúng 1 Transaction CSDL duy nhất**.
  - Tích hợp in hóa đơn nhiệt khổ 80mm/58mm qua Browser Print API.
* **Câu hỏi Thầy sẽ xoáy:** *"Nếu đang quét mã tính tiền cho khách tại quầy mà đường truyền mạng bị chập chờn thì sao?"*
  - **Cách trả lời ăn điểm:** *"Dạ thưa Thầy, hệ thống POS của tụi em được thiết kế theo chuẩn PWA có hỗ trợ lưu giỏ hàng ngoại tuyến. Tuy nhiên, về mặt nghiệp vụ kho, hệ thống bắt buộc phải kiểm tra tồn khả dụng `Available` trước khi trừ tồn. Nếu mất mạng, hệ thống sẽ lưu tạm hóa đơn chờ và lập tức đồng bộ xử lý nguyên tử ngay khi có mạng trở lại để đảm bảo không bị bán âm kho."*

---

### FR-08: Giả lập Webhook Sàn TMĐT (Marketplace Webhook Simulator)
* **Bản chất đời thường:** Để kết nối trực tiếp với Shopee hay TikTok Shop thật, doanh nghiệp bắt buộc phải có giấy phép kinh doanh lớn và ký hợp đồng đối tác Enterprise với sàn (sinh viên làm đồ án không thể có tài khoản thật này). Vì vậy nhóm tự xây dựng một "Đài giả lập" đóng vai Shopee/TikTok để bắn đơn thử nghiệm về hệ thống.
* **Cơ chế kỹ thuật cốt lõi:**
  - Cung cấp API tiếp nhận Webhook: `POST /api/webhooks/simulator/{channel}` hỗ trợ các kênh `shopee`, `tiktok`, `lazada`.
  - Hỗ trợ bắn đơn đơn lẻ và **bắn đơn hàng loạt (Batch Simulation)** để thử tải hệ thống.
  - **Cơ chế chống xử lý trùng (Idempotency Key)**: Dựa vào mã `RequestKey / ExternalOrderId`. Nếu sàn gửi lại đơn hàng cũ lần thứ 2, hệ thống nhận diện được ngay và trả về kết quả cũ, tuyệt đối không tạo thêm đơn và không trừ kho lần 2.
* **Câu hỏi Thầy sẽ xoáy:** *"Simulator của các em khác gì so với API sàn thật ngoài đời?"*
  - **Cách trả lời ăn điểm:** *"Dạ thưa Thầy, Simulator của tụi em mô phỏng chính xác 100% cấu trúc gói tin JSON (Payload) mà Shopee và TikTok Shop gửi qua Webhook ngoài đời (gồm mã đơn sàn, danh sách SKU, phí ship, chiết khấu). Điểm khác biệt duy nhất là tụi em chạy trong môi trường kiểm thử nội bộ để chủ động kiểm soát các kịch bản thử tải cao điểm như Flash Sale mà không bị phụ thuộc vào máy chủ của bên thứ ba."*

---

### FR-09: Cảnh báo Thời gian thực (Real-time SignalR)
* **Bản chất đời thường:** Khi bạn đang đứng bán hàng ở quầy hoặc đang ngồi duyệt đơn trên máy tính, bỗng nhiên có một đơn hàng mới từ Shopee nổ về. Ngay lập tức màn hình của bạn phát ra tiếng chuông "Ting ting" và hiện một ô thông báo màu xanh trên góc phải mà bạn **không cần phải bấm F5 để tải lại trang**.
* **Cơ chế kỹ thuật cốt lõi:**
  - Sử dụng công nghệ **WebSockets qua thư viện ASP.NET Core SignalR**.
  - Thiết lập kênh kết nối liên tục 2 chiều giữa Backend và Frontend (Admin & POS).
  - Khi có sự kiện: Khách đặt đơn mới, Giữ chỗ thành công, hoặc Tồn kho thay đổi $\rightarrow$ Máy chủ tự động "bắn" thông điệp (Push event) tới đúng Client của Tenant đó.
* **Câu hỏi Thầy sẽ xoáy:** *"Tại sao không cho Frontend cứ 2 giây gửi 1 request lên hỏi server xem có đơn mới chưa (Polling) mà phải dùng SignalR?"*
  - **Cách trả lời ăn điểm:** *"Dạ thưa Thầy, nếu dùng cơ chế Polling (cứ 2 giây hỏi 1 lần), khi hệ thống có hàng trăm thu ngân và quản lý cùng mở máy, máy chủ sẽ bị nghẽn mạng và quá tải CPU vì phải xử lý hàng ngàn request rác vô nghĩa. Dùng SignalR giúp mở một đường ống nhẹ duy nhất, server chỉ phát tín hiệu khi thật sự có đơn mới, vừa tiết kiệm tài nguyên vừa đạt độ trễ thời gian thực tức thì dưới 100ms."*

---

### FR-10: Tác vụ Ngầm Tự động (Hangfire Background Jobs)
* **Bản chất đời thường:** Trong siêu thị luôn có những việc định kỳ không cần người ngồi bấm: cứ đúng 15 phút đi dọn các giỏ hàng khách bỏ quên trên kệ để trả về quầy; cứ đúng 00:00 nửa đêm thì khóa sổ chốt doanh thu ngày. Hangfire chính là "người phục vụ vô hình" làm các việc định kỳ đó.
* **Cơ chế kỹ thuật cốt lõi:**
  - Tích hợp thư viện xử lý tiến trình ngầm **Hangfire** chạy độc lập với luồng xử lý HTTP Request của người dùng.
  - **Job 1 (Quét hủy đơn quá hạn)**: Chạy định kỳ mỗi 5 phút (Cron: `*/5 * * * *`), tìm các đơn `Reserved` quá 15 phút chưa thanh toán $\rightarrow$ chuyển trạng thái sang `Cancelled` $\rightarrow$ giải phóng số lượng giữ chỗ về tồn khả dụng.
  - **Job 2 (Dọn dẹp bảo mật)**: Chạy lúc nửa đêm (00:00) quét dọn các Refresh Token hết hạn hoặc bị thu hồi trong CSDL.
* **Câu hỏi Thầy sẽ xoáy:** *"Nếu tiến trình ngầm đang chạy mà server bị sập nguồn thì các công việc dở dang có bị mất không?"*
  - **Cách trả lời ăn điểm:** *"Dạ thưa Thầy, Hangfire lưu toàn bộ trạng thái công việc (Job State) vào CSDL PostgreSQL chứ không lưu trên RAM. Khi máy chủ khởi động lại, Hangfire sẽ tự động đọc lại CSDL và thực thi tiếp các tác vụ bị gián đoạn, đảm bảo không bao giờ bị sót đơn cần giải phóng tồn kho ạ."*

---

### FR-11: Báo cáo Tài chính & Tồn kho (Financial & Inventory Analytics)
* **Bản chất đời thường:** Cuối ngày hoặc cuối tháng, chủ cửa hàng mở phần mềm lên cần biết ngay: Hôm nay tiệm thu về bao nhiêu tiền (**Doanh thu**)? Tiền gốc bỏ ra nhập đống hàng đó là bao nhiêu (**Giá vốn COGS**)? Bỏ túi lãi ròng được bao nhiêu (**Lợi nhuận gộp**)? Món nào đang bán chạy nhất và món nào sắp cháy hàng?
* **Cơ chế kỹ thuật cốt lõi:**
  - **Công thức tính Lợi nhuận gộp chuẩn kế toán:**
    $$\text{Doanh thu thuần (Net Sales)} = \text{Tổng tiền bán} - \text{Chiết khấu} - \text{Giá trị trả hàng}$$
    $$\text{Tổng Giá vốn (Total COGS)} = \sum (\text{Số lượng bán} \times \text{Đơn giá vốn snapshot trong OrderItem})$$
    $$\text{Lợi nhuận gộp (Gross Profit)} = \text{Doanh thu thuần} - \text{Tổng Giá vốn}$$
  - **Phân tích Vận tốc kho (Velocity)**: Tính số lượng tiêu thụ trung bình mỗi ngày để phân loại hàng bán chạy (Best-sellers) và hàng tồn ứ đọng (Slow-moving).
  - **Cảnh báo tồn kho tối thiểu (Low-stock Alert)**: Báo động đỏ khi $\text{Available} \le \text{MinAlertLevel}$.
* **Câu hỏi Thầy sẽ xoáy:** *"Thu ngân hay nhân viên kho có được phép xem màn hình Báo cáo Lợi nhuận gộp này không?"*
  - **Cách trả lời ăn điểm:** *"Dạ TUYỆT ĐỐI KHÔNG ạ! Lợi nhuận gộp và giá vốn là bí mật kinh doanh sống còn của chủ cửa hàng. Hệ thống của tụi em đã thiết lập phân quyền RBAC 2 lớp: ở Frontend ẩn hoàn toàn trang Báo cáo đối với Staff và Cashier; ở Backend API gắn quyền `[Authorize(Roles = "Owner")]`. Nếu nhân viên cố tình gọi API thì hệ thống lập tức chặn lại và trả về mã lỗi HTTP `403 Forbidden`."*

---

### FR-12: Trí tuệ Nhân tạo Dự báo Nhập hàng (AI Stock Forecasting)
* **Bản chất đời thường:** Một chủ shop thông minh không đợi đến khi trong tiệm hết sạch hàng mới cuống cuồng đi gọi nhà cung cấp, vì nhà cung cấp phải mất 3 ngày mới giao tới nơi. AI sẽ phân tích tốc độ bán của tuần qua và tính toán trước: *"Với tốc độ bán này, 2 ngày nữa món này sẽ hết sạch, hôm nay bạn phải đặt mua thêm 50 cái ngay!"*.
* **Cơ chế kỹ thuật cốt lõi:**
  - Dựa trên thuật toán thống kê chuỗi thời gian: Tốc độ bán trung bình ngày (Daily Sales Rate - DSR), Thời gian nhà cung cấp giao hàng (Lead Time) và Lượng tồn an toàn (Safety Stock).
  - Công thức tính điểm đặt hàng lại (Reorder Point - ROP):
    $$\text{ROP} = (\text{DSR} \times \text{Lead Time}) + \text{Safety Stock}$$
  - Tự động sinh bản thảo Phiếu đề xuất nhập hàng (Draft Purchase Order) để chủ shop chỉ cần bấm 1 click là duyệt gửi nhà cung cấp.
* **Câu hỏi Thầy sẽ xoáy:** *"AI này có tự động xuất tiền đi đặt mua hàng luôn không?"*
  - **Cách trả lời ăn điểm:** *"Dạ không ạ! Theo quy tắc đặc tả trong hệ thống, AI chỉ đóng vai trò là một 'Trợ lý thông minh hỗ trợ ra quyết định'. Nó chỉ tạo bản thảo gợi ý số lượng tối ưu. Người có thẩm quyền cao nhất là Chủ cửa hàng (Owner) bắt buộc phải kiểm tra và bấm phê duyệt thì phiếu nhập kho mới được kích hoạt, tránh rủi ro đặt nhầm hàng ạ."*

---

# PHẦN 2: KỊCH BẢN BẢO VỆ CHUYÊN SÂU BACKEND 2 (NHỰT - OISMS-41)

Khi Thầy hoặc Nhóm trưởng Khoa chỉ định:  
👉 **"Nhựt phụ trách những gì trong Sprint 3 và kết quả thế nào? Trình bày chi tiết xem nào!"**

---

### Lời mở đầu tổng quan (Báo cáo thành tích dứt khoát):
> *"Dạ kính thưa Thầy và cả nhóm, trong Sprint 3, em đảm nhiệm vai trò **Backend 2 Lead (TV2)**, phụ trách task **`OISMS-41`**. Em đã hiện thực hóa trọn vẹn **3 phân hệ nghiệp vụ xương sống** của hệ thống:
> 1. **Phân hệ Quản lý Đơn hàng đa kênh (FR-06)**.
> 2. **Phân hệ Báo cáo Tài chính & Giám sát Tồn kho (FR-11)**.
> 3. **Phân hệ Giả lập Webhook Sàn TMĐT Shopee/TikTok/Lazada (FR-08)**.
> 
> Toàn bộ mã nguồn do em phụ trách đã hoàn thành 100%, vượt qua **115/115 bài Unit Test** và **24/24 bài Integration Test (đạt tỷ lệ Pass tuyệt đối)**. Em đã đóng gói mã nguồn trên nhánh `feature/OISMS-42-sprint-3-backend-2` và mở **Pull Request #20** trên GitHub đúng chuẩn quy ước của nhóm."*

---

### Đi sâu vào từng phân hệ để chứng minh bạn nắm chắc nghiệp vụ:

#### 1. Về Phân hệ Đơn hàng đa kênh (FR-06):
> *"Ở phân hệ này, em xây dựng cụm API trong `OrdersController` và `OrderService`:
> - API `GET /api/orders`: Hỗ trợ phân trang chuẩn, lọc linh hoạt theo Chi nhánh, Kênh bán (POS, Web, Shopee, TikTok, Lazada), Khoảng thời gian và Trạng thái đơn hàng.
> - API `GET /api/orders/{id}`: Trả về thông tin chi tiết người mua, danh sách SKU, số lượng, đơn giá, snapshot giá vốn, trạng thái thanh toán và lịch sử chuyển trạng thái.
> - **Máy trạng thái đơn hàng (Order State Machine)**: Em xây dựng đầy đủ 4 thao tác vòng đời:
>   - `POST /api/orders/{id}/confirm`: Duyệt đơn (Draft/Reserved $\rightarrow$ Confirmed), trừ tồn OnHand, giảm Reserved, ghi sổ cái xuất OUT và chốt giá vốn WAC.
>   - `POST /api/orders/{id}/process`: Chuyển sang đóng gói (Confirmed $\rightarrow$ Processing).
>   - `POST /api/orders/{id}/complete`: Hoàn tất giao hàng (Processing $\rightarrow$ Completed).
>   - `POST /api/orders/{id}/cancel`: Hủy đơn (Draft/Reserved $\rightarrow$ Cancelled) và giải phóng tồn giữ chỗ về lại kho khả dụng."*

#### 2. Về Phân hệ Báo cáo Tài chính & Lợi nhuận gộp (FR-11):
> *"Em xây dựng 3 endpoint báo cáo trọng yếu trong `ReportsController`:
> - `GET /api/reports/revenue-profit`: Tính toán Doanh thu thuần (Net Revenue), Tổng giá vốn hàng bán (COGS) dựa trên WAC snapshot của từng đơn hàng, và **Lợi nhuận gộp (`Gross Profit = Net Revenue - COGS`)**. Hỗ trợ lọc theo Chi nhánh, Kênh bán và Khoảng ngày.
> - `GET /api/reports/inventory-velocity`: Báo cáo Tổng giá trị tồn kho theo giá vốn WAC từng SKU, phân tích Top mặt hàng bán chạy (Best-sellers) và Top mặt hàng tồn chậm (Slow-moving).
> - `GET /api/reports/stock-alerts`: Báo cáo Cảnh báo tồn kho dưới ngưỡng an toàn (`Available <= ReorderPoint`).
> - **Về mặt bảo mật:** Toàn bộ API báo cáo tài chính đều được em phân quyền nghiêm ngặt `[Authorize(Roles = "Owner")]` để ngăn chặn nhân viên xem trộm lợi nhuận của chủ cửa hàng."*

#### 3. Về Phân hệ Giả lập Webhook sàn TMĐT (FR-08):
> *"Vì đồ án không thể kết nối trực tiếp với tài khoản doanh nghiệp thật của sàn, em đã xây dựng `SimulatorController` và `SimulatorService`:
> - `POST /api/webhooks/simulator/{channel}`: Giả lập tiếp nhận Webhook đơn hàng từ Shopee, TikTok Shop, Lazada; tự động chuẩn hóa về `Canonical Order Model` và kích hoạt giữ chỗ tồn kho.
> - `POST /api/webhooks/simulator/batch`: Chạy mô phỏng bắn đồng thời hàng loạt đơn hàng thử nghiệm để kiểm tra cơ chế khóa giữ chỗ và chống bán vượt (Anti-Oversell) của hệ thống.
> - Áp dụng cơ chế **Idempotency** qua `RequestKey / ExternalOrderId`: Nếu sàn gửi lại đơn cũ, hệ thống nhận diện được ngay và trả về kết quả cũ, tuyệt đối không trừ kho 2 lần."*

---

### Lời kết chốt hạ đầy tự tin:
> *"Tất cả các API trên em đều tuân thủ nghiêm ngặt chuẩn kiến trúc Clean Architecture, đảm bảo mọi câu lệnh truy vấn đều đi qua bộ lọc `TenantId` tự động để cô lập dữ liệu cửa hàng, và đã có bộ test tự động 115 tests kiểm chứng toàn diện. Em sẵn sàng mở mã nguồn hoặc chạy thử nghiệm trực tiếp trên Swagger để Thầy kiểm tra ạ!"*

---

# PHẦN 3: BẢNG TRA CỨU NHANH CÔNG THỨC & QUY TẮC CỐT LÕI

| Chỉ số / Cơ chế | Công thức toán học / Quy tắc kỹ thuật | Ý nghĩa thực tế |
| :--- | :--- | :--- |
| **Tồn khả dụng (Available)** | $\text{Available} = \text{OnHand} - \text{Reserved}$ | Số lượng thực sự còn được phép bán cho khách. |
| **Giá vốn bình quân (WAC)** | $\text{NewCost} = \frac{(\text{OldStock} \times \text{OldCost}) + (\text{ImportQty} \times \text{ImportPrice})}{\text{OldStock} + \text{ImportQty}}$ | Tự động tính lại đơn giá vốn trung bình ngay khi xác nhận nhập kho. |
| **Giá vốn hàng bán (COGS)** | $\text{COGS} = \sum (\text{Quantity} \times \text{Snapshot UnitCost})$ | Tổng tiền gốc của các mặt hàng đã xuất bán. |
| **Lợi nhuận gộp (Gross Profit)** | $\text{Gross Profit} = \text{Doanh thu thuần} - \text{COGS}$ | Số tiền lãi thực sự thu về sau khi trừ tiền vốn nhập hàng. |
| **Biên lợi nhuận gộp (%)** | $\text{Gross Margin} = \left(\frac{\text{Gross Profit}}{\text{Doanh thu thuần}}\right) \times 100\%$ | Tỷ suất sinh lời trên mỗi đồng doanh thu. |
| **Vận tốc bán (Velocity)** | $\text{Velocity} = \frac{\text{Tổng SL bán trong kỳ}}{\text{Số ngày theo dõi}}$ | Số lượng tiêu thụ trung bình mỗi ngày để phân loại hàng bán chạy/chậm. |
| **Cảnh báo tồn kho an toàn** | $\text{Available} \le \text{MinAlertLevel}$ | Báo động đỏ trên Dashboard nhắc chủ shop nhập thêm hàng. |
| **Sổ cái kho bất biến** | Chỉ `INSERT`, cấm `UPDATE/DELETE` | Giống sao kê ngân hàng, minh bạch 100% lịch sử kiểm toán kho. |
