# BF-008 — Drawer chứng từ bị kẹt loading của thao tác cũ

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ✅ Đã sửa |
| **Ưu tiên** | P2 |
| **Khu vực** | Chuyển/kiểm kê |
| **Phát hiện** | 28–29/10/2026 (Tái hiện có kiểm soát) |
| **Sửa** | 30/10/2026, đợt sửa BF-001..020 |
| **Kiểm chứng** | `scratch/test_bugfix_after_verification.cjs` (30/10/2026, PASS) |

## Mô tả lỗi (lúc phát hiện)

**Tái hiện:** A đang Lưu kiểm kê hoặc Xuất gửi chuyển kho → đóng/chuyển sang chứng từ B → request A hoàn tất.

**Thực tế:** generation guard bỏ kết quả A, nhưng `actionLoading` không được reset khi đổi ID. `finally` của A cũng không được reset do generation đã khác. Nút thao tác của B vẫn disabled.

**Vị trí:** effect theo ID và các `finally` trong `StockCountDetailDrawer.tsx`, `TransferDetailDrawer.tsx`.

**Bằng chứng:** `count-action-lock`, `transfer-action-lock`; cả hai đều đã chạy component React thật.

**Điều kiện đóng:** trạng thái action thuộc ID/generation; B không kế thừa busy của A, nhưng A vẫn được theo dõi/đối soát an toàn. Test chuyển ID và close/reopen khi request pending.

## Cách đã sửa

* **Thành phần:** `src/frontend/admin-dashboard/src/components/stockCounts/StockCountDetailDrawer.tsx`, `src/frontend/admin-dashboard/src/components/transfers/TransferDetailDrawer.tsx`
* **Triệu chứng & Nguyên nhân:**
  * Người dùng đang thực hiện lưu kiểm kê hoặc chuyển kho ở chứng từ A, sau đó chuyển nhanh sang chứng từ B trong khi request A vẫn đang chạy. Generation guard ngăn kết quả A ghi đè B, nhưng cờ `actionLoading` bị kẹt ở trạng thái `true` vì khối `finally` của A bị bỏ qua.
* **Giải pháp khắc phục:**
  * Quản lý trạng thái bận theo từng ID chứng từ thông qua `busyIdsRef = useRef<Set<string>>(new Set())` kết hợp với `currentCountIdRef` / `currentTransferIdRef`.
  * Khi chuyển từ chứng từ A đang xử lý sang chứng từ B, nút thao tác của B lập tức ở trạng thái sẵn sàng (`actionLoading = false`); nếu quay lại chứng từ A khi request của A vẫn chưa xong thì A vẫn được bảo vệ khóa thao tác cho tới khi request A hoàn tất.
* **Kiểm thử xác thực:** Pass 2 kịch bản `count-action-lock` và `transfer-action-lock`: nút hành động của chứng từ mới luôn ở trạng thái sẵn sàng thao tác (`disabled: false`).
