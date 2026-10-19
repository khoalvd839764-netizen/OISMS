# OISMS — Thiết kế database v0.5

Ngày 16/10/2026. Thay thế v0.4. Đối chiếu đề gốc và cập nhật theo lựa chọn của Khoa: WAC theo chi nhánh, tồn riêng từng kho, thuộc tính biến thể tùy ý, đăng nhập email hoặc SĐT, QR chưa chốt.

[SQL](database/oisms_schema.sql) · [Tài liệu bàn giao](database/README.md) · [Yêu cầu dự án](DE_BAI_OISM.md)

## 1. Quyết định và phạm vi

| Nội dung | Thiết kế hiện tại | Căn cứ |
|---|---|---|
| Tồn khả dụng chi nhánh | Tổng OnHand trừ tổng Reserved các kho thuộc chi nhánh | FR-RSE-01 |
| Tồn từng kho | WarehouseInventoryBalances theo tenant/kho/SKU | Khoa yêu cầu bổ sung |
| WAC | InventoryBalances.AverageCost theo tenant/chi nhánh/SKU | Khoa chọn phạm vi; đề yêu cầu công thức WAC |
| Biến thể tùy ý | Skus.Attributes JSONB object thay Color/Size cố định | Khoa yêu cầu; đề có Color, Size, etc. |
| Đăng nhập | Email hoặc phone; ít nhất một giá trị, không bắt buộc cả hai | Đề và Khoa xác nhận |
| QR | Payments có BankTransfer; cấu hình QR và xác nhận tiền chưa chốt | Đề yêu cầu QR transfer; chưa chọn tích hợp |

Email/SĐT hiện unique toàn hệ thống và mỗi user thuộc một tenant. Đây vẫn là lựa chọn kỹ thuật để xác định tài khoản khi đăng nhập, không suy diễn rằng Khoa đã chốt tài khoản nhiều tenant. Email lưu chữ thường; SĐT chuẩn hóa ngay trên cột chính. Không có cột normalized phụ.

## 2. Hai cấp tồn và WAC

- `warehouse_inventory_balances`: một dòng mỗi tenant/kho/SKU; OnHand, Reserved, LastSequence. Không lưu WAC riêng kho.
- `inventory_balances`: một dòng mỗi tenant/chi nhánh/SKU; tổng OnHand/Reserved, AverageCost, ReorderPoint và LastSequence.
- Tổng OnHand/Reserved của các kho phải bằng các giá trị tương ứng ở chi nhánh. Hai cấp là dữ liệu dẫn xuất từ nghiệp vụ kho, được cập nhật cùng transaction dưới khóa; Constraint trigger v0.5 từ chối COMMIT nếu hai cấp số dư, ledger hoặc reservations không khớp. Backend vẫn phải ghi đầy đủ trong transaction; database không tự cộng/trừ thay service.
- Available chi nhánh = tổng Available các kho. Hàng đang vận chuyển được theo dõi từ phiếu chuyển, không tính vào Available tại kho nguồn hoặc đích.
- Nhập từ nhà cung cấp: WAC mới = (lượng thuộc phạm vi tính giá trước nhập × WAC cũ + lượng nhập × đơn giá nhập) / (lượng trước nhập + lượng nhập). Khi không có hàng vận chuyển nội bộ, lượng trước nhập là OnHand chi nhánh.
- WAC dùng chung cho mọi kho thuộc chi nhánh. Khi Confirmed/POS, chụp vào OrderItems.CostPrice; không sửa giá vốn đơn cũ theo WAC mới.
- Chuyển cùng chi nhánh không tự làm thay đổi WAC. Nếu đang có hàng vận chuyển nội bộ và phát sinh nhập mới, cơ chế tính giá phải bao gồm lượng/giá trị nội bộ đang vận chuyển trong phạm vi giá vốn chi nhánh, tránh coi là mua mới khi nhận. Quy tắc và kiểm thử định giá vận chuyển phải được triển khai trước nghiệm thu.
- Chuyển khác chi nhánh: chụp chi phí khi xuất, nhập đích tính WAC theo chi phí chuyển đó. Lượng/giá trị đang vận chuyển trình bày riêng; không ghi mất hàng.

## 3. Chứng từ, giữ chỗ và ledger

Phiếu nhập, kiểm kê, điều chỉnh và trả hàng bắt buộc chỉ ra kho và chi nhánh. FK ghép bảo đảm kho thuộc đúng chi nhánh và tenant. Một phiếu trong các loại này xử lý một kho; nhiều kho dùng nhiều phiếu.

StockTransfers có FromBranchId/ToBranchId và FromWarehouseId/ToWarehouseId. Hai kho phải khác nhau và thuộc đúng chi nhánh. Chuyển khác chi nhánh thực hiện yêu cầu đề; chuyển cùng chi nhánh hỗ trợ quản lý vị trí theo thiết kế bổ sung. Luồng xuất → InTransit → nhận; chưa đặc tả nhận thiếu/nhiều đợt.

Orders vẫn gắn chi nhánh, không bắt buộc một kho cho cả đơn. Reservations phân bổ từng dòng hàng xuống một hoặc nhiều kho của chi nhánh đơn. Tổng lượng phân bổ phải khớp dòng đơn và không vượt tồn khả dụng tại từng kho. Chính sách chọn kho do service thực hiện; chưa tự chọn FIFO hoặc kho ưu tiên.

Ledger giữ các trường đề yêu cầu và thêm WarehouseId để truy vết. BalanceAfter/SequenceNo ở cấp chi nhánh/SKU. LastSequence của số dư kho ghi mốc sequence chi nhánh gần nhất tác động vào kho đó. Kiểm kê so với số dư và LastSequence của kho, không so một kho với tổng chi nhánh.

Ledger có đúng một dòng nguồn. ReferenceId được sinh từ FK nguồn. Dòng bán có thể phát sinh một OUT tại mỗi kho phân bổ; unique theo dòng bán/kho chống ghi lặp. Các nguồn còn lại giữ một bút toán mỗi dòng; chuyển có một OUT và một IN. Service phải kiểm tra SKU, vị trí, lượng nguồn và tổng phân bổ: FK không tự chứng minh tất cả quan hệ nghiệp vụ này.

Mọi thao tác tồn/giữ chỗ/WAC/ledger/trạng thái phải nguyên tử, chống retry và cạnh tranh. Ledger cấm UPDATE/DELETE, sửa sai bằng bút toán bù. v0.5 đã có append-only trigger, constraint kiểm tra tồn ở COMMIT, khóa giao dịch và quyền runtime tích hợp trong SQL bàn giao. Posting service/WAC vẫn thuộc phần triển khai backend. Xem [hợp đồng tích hợp](database/README.md).

## 4. Biến thể tùy ý

Ví dụ `Skus.Attributes`:

```json
{"màu":"đen","size":"L","chất liệu":"cotton"}
```

Sản phẩm khác có thể dùng `{"dung lượng":"256GB","bảo hành":"24 tháng"}`. Sản phẩm không có biến thể dùng `{}`. Không thêm cột SQL mỗi khi có thuộc tính mới.

Database kiểm tra đây là JSON object. Backend phải chuẩn hóa tên/giá trị, giới hạn kích thước, từ chối thuộc tính rỗng/sai định dạng và kiểm tra tổ hợp thuộc tính trùng trong cùng product dưới khóa. DDL chưa bảo đảm uniqueness theo tổ hợp; SKU code vẫn unique theo tenant. Thiết kế chưa có danh mục thuộc tính/giá trị dùng chung có quản trị riêng, vì yêu cầu hiện tại là thêm thuộc tính tùy ý.

## 5. Danh mục 29 bảng

Thêm đúng một bảng `warehouse_inventory_balances` so với v0.3. Thuộc tính linh hoạt nằm trên SKU nên không tăng số bảng cho phần này.

| STT | Bảng |
|---|---|
| 1 | `tenants` |
| 2 | `users` |
| 3 | `refresh_tokens` |
| 4 | `branches` |
| 5 | `warehouses` |
| 6 | `categories` |
| 7 | `brands` |
| 8 | `products` |
| 9 | `skus` |
| 10 | `suppliers` |
| 11 | `goods_receipts` |
| 12 | `goods_receipt_items` |
| 13 | `inventory_balances` |
| 14 | `warehouse_inventory_balances` |
| 15 | `stock_transfers` |
| 16 | `stock_transfer_items` |
| 17 | `stock_counts` |
| 18 | `stock_count_items` |
| 19 | `inventory_adjustments` |
| 20 | `inventory_adjustment_items` |
| 21 | `orders` |
| 22 | `order_items` |
| 23 | `reservations` |
| 24 | `payments` |
| 25 | `sales_returns` |
| 26 | `sales_return_items` |
| 27 | `inventory_transactions` |
| 28 | `daily_sales_summaries` |
| 29 | `replenishment_suggestions` |

## 6. Đối chiếu chức năng

| Nhóm yêu cầu | Bảng hỗ trợ |
|---|---|
| Tenant, auth, vai trò, chi nhánh/kho | Tenants, Users, RefreshTokens, Branches, Warehouses |
| Sản phẩm, biến thể, barcode, giá | Categories, Brands, Products, Skus |
| Nhập và WAC | Suppliers, GoodsReceipts/Items, hai cấp số dư, ledger |
| Chuyển, kiểm kê, điều chỉnh | StockTransfers/Items, StockCounts/Items, InventoryAdjustments/Items |
| Đơn đa kênh, giữ chỗ, POS, thanh toán | Orders/Items, Reservations, Payments |
| Return trong ledger và báo cáo thuần | SalesReturns/Items |
| Báo cáo, cảnh báo tồn thấp | DailySalesSummaries, Orders/Items, ledger, ReorderPoint |
| Simulator, SignalR, Hangfire | Mã đơn ngoài, thời hạn Reservations, tổng hợp ngày; cần triển khai service/job |
| AI gợi ý nhập | ReplenishmentSuggestions và lịch sử bán/tồn; cần mô hình và đánh giá |

Tên và số bảng là thiết kế triển khai, không phải danh sách được đề liệt kê. InventoryTransactions là log kho bắt buộc; chưa có audit log toàn hệ thống vì đề không yêu cầu bảng riêng.

## 7. Các điểm còn mở và giới hạn

- QR: chưa chọn cấu hình tài khoản nhận tiền, sinh QR, xác nhận thủ công hay callback.
- Hủy sau Confirmed/Completed: chưa chốt transition matrix và xử lý bù/hoàn tiền. SQL cho giữ lịch sử ConfirmedAt khi Cancelled không có nghĩa API được hủy tùy ý.
- Nhận chuyển thiếu/từng phần và split payment chưa được đặc tả. Hiện hai bước chuyển cơ bản, một payment mỗi đơn.
- Thuật toán AI, ngày ghi nhận doanh thu và cách hiểu Separate Schema/TenantId trong đề cần thống nhất; DDL dùng shared tables + TenantId.
- Tenant FK ngăn tham chiếu chéo; đọc/ghi vẫn cần query filters, write guards và RBAC. Không cascade xóa chứng từ.
- SQL bàn giao tạo database mới. Database đã chạy cần migration riêng được review; không chạy lại file tạo mới. Nếu đã có dữ liệu v0.3, phải phân bổ số dư chi nhánh về kho thực tế và chuyển Color/Size sang Attributes; không tự đoán phân bổ.
- Kiểm tra SQL/DBML/ràng buộc không thay test WAC, rollback, 50 yêu cầu tranh một đơn vị, hiệu năng, bảo mật và coverage theo đề.

Nguồn cấu trúc hiện hành: `docs/database/oisms_schema.sql`; quy ước và từ điển dữ liệu: `docs/database/README.md`. Chưa áp dụng thay đổi vào database đang chạy.

## 8. Kiểm tra và triển khai v0.5

Đã bổ sung index Orders(TenantId, CreatedAt), index phục vụ đối soát tồn, bảo vệ mốc xác nhận/giá vốn dòng đơn, script kiểm thử PostgreSQL nhúng với dữ liệu thử tạo trong phiên. File `oisms_schema.sql` chứa đầy đủ trigger; Các file generator, DBML và seed riêng đã bỏ theo yêu cầu; chỉ giữ SQL bàn giao và script test. Quyền runtime đã tích hợp trong SQL bàn giao; DBA cấp group cho LOGIN backend riêng. Chưa áp dụng vào database thật.
