# Evidence — S3-POS-T01 v1.0 / R1

Ngày 27/10/2026. HEAD `44317f3` cộng working tree bàn giao; source được xác định thêm bằng hash, chưa có commit mới của implementation.

| Artifact | Kết quả / nội dung |
| --- | --- |
| [build.log](./build.log) | Build POS PASS |
| [tests.log](./tests.log) | Suite 20/20 PASS, 0 skipped/cancelled |
| [reviewer-probes.cjs](./reviewer-probes.cjs) | Các handler/module thực được transpile trong Node VM; React hooks/fetch/storage/events/server model stub |
| [probes.log](./probes.log) | Output sáu scenario |
| [probe-results.json](./probe-results.json) | Positive retry giữ key; các counterexamples owner/reset/500/reload/invalid 2xx |
| [source-hashes-start.json](./source-hashes-start.json) | Hash source đầu review |
| [source-hashes-end.json](./source-hashes-end.json) | Hash source cuối review để phát hiện thay đổi đồng thời |

Không có browser/HTTP/PostgreSQL E2E hoặc DB mutation thật. Token/user/tenant trong source là fixture. Positive control normal retry cho một đơn trong mô hình; không có con số “PostgreSQL đã tạo hai đơn” trong kết luận.

## Tái chạy

Copy `reviewer-probes.cjs` vào `tmp/pos-t01-review-r1/` từ repo root; source code T01 phải đúng phiên bản bàn giao đang review. Probe đọc frontend hiện tại và dùng TypeScript trong node_modules POS:

```powershell
node tmp/pos-t01-review-r1/reviewer-probes.cjs
```

Tại `src/frontend/pos-app`:

```powershell
npm run build
npm test
```

Không gọi 20 tests hoặc probes Node VM là browser E2E. Các case bảo vệ owner phải dùng pending được modal production tạo; fixture tự điền userId/tenantId không chứng minh wiring của modal.
