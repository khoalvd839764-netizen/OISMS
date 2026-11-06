# Bugfix và Upgrade — theo dõi lỗi

Nơi theo dõi lỗi của OISMS: lỗi nào đã sửa, lỗi nào chưa, kèm trạng thái các chức năng Coming Soon và các gói Upgrade.

- Xem nhanh: [**CHECKLIST.md**](./CHECKLIST.md)
- Chi tiết từng lỗi: thư mục [`errors/`](./errors/), mỗi lỗi một file

Cập nhật ngày **06/11/2026**. Trong **35 lỗi**, có 21 lỗi đã sửa, 7 lỗi cần kiểm chứng hoặc mới sửa một phần và 7 lỗi chưa sửa. Đợt 1 và 2 của Clean Architecture đã qua CI.

## Cấu trúc

```text
docs/bug-fixaffter/
├── README.md        # Tài liệu này: cấu trúc, quy ước, lịch sử các đợt
├── CHECKLIST.md     # Bảng tổng: lỗi, Coming Soon, Upgrade, việc cần quyết định
├── errors/          # Mỗi lỗi một file: BF-<số>-<mô-tả>.md
│   ├── BF-001-...md
│   └── ...
└── evidence/        # Log và dữ liệu thô của các đợt kiểm thử
```

## Quy ước

**Trạng thái** (ghi ở đầu mỗi file lỗi và trong checklist):

| Ký hiệu | Trạng thái | Nghĩa |
| :---: | :--- | :--- |
| ✅ `[x]` | Đã sửa | Có bản sửa trên nhánh hiện tại và đã có kiểm chứng |
| 🟡 `[~]` | Đã sửa, cần kiểm chứng lại / Cần kiểm chứng lại | Đã sửa ở một nơi, hoặc code đã thay đổi sau khi sửa nên kết quả cũ không còn chắc |
| ❌ `[ ]` | Chưa sửa | Lỗi còn mở |

**Ưu tiên:** `P1` sai phiên/quyền, giao dịch lặp, số liệu sai hoặc lộ dữ liệu · `P2` chức năng sai hoặc kẹt trong tình huống cụ thể · `P3` ảnh hưởng nhỏ hoặc chỉ ở môi trường đặc biệt.

**Mỗi file lỗi gồm:** bảng thông tin (trạng thái, ưu tiên, khu vực, ngày phát hiện, ngày sửa, kiểm chứng), mô tả và cách tái hiện, vị trí trong code, điều kiện đóng, và cách đã sửa nếu có.

## Thêm hoặc đóng một lỗi

1. Tạo `errors/BF-<số tiếp theo>-<mô-tả-ngắn-không-dấu>.md` theo mẫu của file có sẵn (ví dụ [BF-021](./errors/BF-021-bao-cao-lai-gop-lo-cho-staff.md)).
2. Thêm một dòng vào bảng tương ứng trong [CHECKLIST.md](./CHECKLIST.md) và cập nhật bảng tóm tắt.
3. Khi sửa xong: thêm mục "Cách đã sửa", ghi lệnh kiểm chứng đã chạy (PASS/FAIL/NOT RUN), rồi đổi trạng thái ở cả file lỗi và checklist. Lỗi chạm auth, tenant, transaction, ledger, WAC, giữ chỗ, idempotency hoặc concurrency phải có bằng chứng trên PostgreSQL thật.
4. Không xóa file lỗi đã đóng; giữ lại làm lịch sử.

## Lịch sử các đợt

| Đợt | Nội dung | Kết quả lúc đó |
| :--- | :--- | :--- |
| 28–29/10/2026 | Rà soát, ghi nhận BF-001..020 và 8 chức năng Coming Soon (CS-01..08) trên baseline `e3813da` | Backend 158 unit + 285 integration PASS; Admin regression 13 PASS; các kịch bản lỗi tái hiện được bằng probe (`audit-probes.cjs`), không ghi vào DB đang dùng |
| 29/10/2026 | Tích hợp `hotfix/sprint-3-core-release` vào `develop` (Sprint 3). Backend và Admin lấy từ `e3813da`; POS riêng giữ code `develop` (PR #24) | Build Admin/POS PASS; POS mới 51 + 24 + 10 = 85 test PASS (log trong `evidence/release-integration/`). Chưa phát hành production |
| 30/10/2026 | Sửa BF-001..020 | 23/23 probe PASS (`scratch/test_bugfix_after_verification.cjs`), 13/13 regression, 159/159 unit test |
| 02/11/2026 | Bản sửa Admin đưa lên nhánh `featch/FIx-and-Uprade` (`97b3418`). Bản sửa POS riêng không đưa lên vì xung đột với POS mới | BF-003, BF-015, BF-019, BF-020 trên `pos-app` chuyển sang cần kiểm chứng lại |
| 02–04/11/2026 | Làm Upgrade 05, 04, 01-A, 02, 03 và phần lớn Coming Soon | Xem [báo cáo nghiệm thu](../report-after-upgrade/frontend-coming-soon-va-upgrade.md) |
| 04–06/11/2026 | Ghi thêm BF-021..027 khi nghiệm thu và đối chiếu code với ma trận RBAC, cách deploy | Unit test 221/221 PASS (06/11/2026); 7 lỗi mới chưa sửa |
| 06/11/2026 | Rà soát lại Clean Architecture sau Upgrade 06, ghi BF-028..035 và kế hoạch 4 đợt ([CHECKLIST mục E](./CHECKLIST.md#e-kế-hoạch-hoàn-thiện-clean-architecture-bf-028035)) | Chưa sửa code |

Các file cũ (`Report Bugfix/BUG-LIST.md`, `report result affter fix.md`, `TEST-RESULTS.md`, `Comming Soon function/COMING-SOON.md`, `SPRINT-3-RELEASE.md`, `CHECKLIST-BUGFIX-AND-UPGRADE.md`) đã được gộp vào `errors/`, `CHECKLIST.md` và bảng trên ngày 06/11/2026; bản gốc còn trong lịch sử git.

## Bằng chứng

- `evidence/release-integration/`: log build và test POS lúc tích hợp Sprint 3.
- `evidence/probe-results.partial.json`: kết quả probe một phần của đợt rà soát.
- Log `.trx` và ảnh của đợt 28–29/10 đã được dọn khỏi repo; tên file vẫn ghi trong từng lỗi.
- Script probe và regression nằm trong `scratch/` (không có trong git).

## Liên quan

- [Ma trận RBAC và bảng lệch với code](../01-users-and-rbac/02-rbac-matrix.md)
- [Hiện trạng triển khai theo yêu cầu chức năng](../02-functional-requirements/README.md)
- [Upgrade-recommended](../Upgrade-recommended/README.md) · [report-after-upgrade](../report-after-upgrade/frontend-coming-soon-va-upgrade.md)
- [Checklist tổng dự án](../../checklist.md)
