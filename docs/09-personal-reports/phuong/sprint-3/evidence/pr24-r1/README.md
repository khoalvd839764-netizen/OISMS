# Evidence review PR #24 — R1

- Head: `44317f3b5188c276fa3ef25c8faf30323e503079`.
- Base/merge-base: `9e41eb11f751dfeac64d2eef3b360a02810113be`.
- Ngày: 27/10/2026, Asia/Saigon.

| Artifact | Nội dung |
| --- | --- |
| [build.log](./build.log) | `npm run build` trên snapshot PR: PASS |
| [tests.log](./tests.log) | Suite hiện có: 13/13 PASS; không phải browser E2E |
| [probes.cjs](./probes.cjs) | Thực thi adapter API, PaymentModal handler và App listener từ snapshot; transpile bằng TypeScript |
| [probe-results.json](./probe-results.json) | Kết quả head: key đổi sau response mất, receipt item không map, 409 mất detail, 401 xóa giỏ |
| [baseline-probe-results.json](./baseline-probe-results.json) | Đối chứng cùng probe trên base để phân loại regression/khoảng trống có sẵn |

Probe dùng React hooks/JSX runtime, fetch, localStorage/events và persistence model được stub; không thay logic của hàm production. Snapshot source xuất bằng `git archive` đúng SHA, không lấy source working tree. Các UUID và token trong probe chỉ là fixture, không phải thông tin đăng nhập thật.

Positive control: header hai loại khớp body ở head; metadata branch được map đúng từ server. F02 có khác biệt base/head: 401 cart 1→1 ở base, 1→0 ở head. Key retry và item receipt có vấn đề trên cả hai baseline; được phân loại rõ trong review, không nhận nhầm là regression mới.

## Tái chạy

Từ repo root, xuất frontend của head và base vào `tmp/pr24-review/head/src/frontend/pos-app` và `tmp/pr24-review/base/src/frontend/pos-app`, copy `probes.cjs` vào `tmp/pr24-review/`. Probe dùng TypeScript đã cài ở `src/frontend/pos-app/node_modules/typescript`.

```powershell
node tmp/pr24-review/probes.cjs
node tmp/pr24-review/probes.cjs --baseline
```

Build/test chạy tại snapshot head sau khi có dependencies đúng package lock:

```powershell
npm run build
npm test
```

Lượt review dùng junction từ node_modules hiện có; không cài/nâng dependency hoặc sửa source. Build là Vite 8.3.4 theo lock/dependencies đã có. CI [40465015227](https://github.com/khoalvd839764-netizen/OISMS/actions/runs/40465015227) được quan sát Success cho backend/frontend; không có raw CI log trong evidence.
