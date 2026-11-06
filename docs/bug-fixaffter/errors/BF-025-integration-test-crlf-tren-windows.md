# BF-025 — Một integration test fail trên Windows do CRLF

| Mục | Giá trị |
| :--- | :--- |
| **Trạng thái** | ❌ Chưa sửa |
| **Ưu tiên** | P2 (môi trường kiểm thử, không phải lỗi sản phẩm) |
| **Khu vực** | Backend / Integration test |
| **Phát hiện** | 04/11/2026, lần chạy integration test 364/365 ([báo cáo](../../report-after-upgrade/frontend-coming-soon-va-upgrade.md#lệnh-kiểm-chứng)) |

## Mô tả lỗi

`SalesInvoiceTraceIntegrationTests.AC01_UpgradeFromPreU02Schema_TwiceIdempotent_MatchesFreshSchemaExactly` so nguyên văn thân hàm SQL giữa database nâng cấp bằng migration và database cài mới từ `oisms_schema.sql`. Trên Windows, `core.autocrlf=true` đổi file sang CRLF nên hai bản khác nhau ở ký tự xuống dòng, test fail. Trên CI (Linux) không gặp.

**Vị trí:** `src/backend/tests/OISM.IntegrationTests/` (lớp `SalesInvoiceTraceIntegrationTests`), `.gitattributes`.

## Điều kiện đóng

- Chuẩn hóa xuống dòng khi so (bỏ `\r`) **hoặc** ép file `.sql` dùng LF bằng `.gitattributes` (`*.sql text eol=lf`). Không nới assertion so sánh nội dung.
- Chạy lại test trên Windows và CI đều PASS.
