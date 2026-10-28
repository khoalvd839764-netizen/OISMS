# Handover — S3-POS-B02 / v1.8 — VERIFIED

- Ngày nghiệm thu: 28/10/2026, Asia/Saigon.
- Trạng thái: **VERIFIED v1.8**. B02-F01..F06 đều CLOSED trong phạm vi B02 đã giao.
- Nhánh: `feature/OISMS-44-pos-pwa-checkout-va-in-hoa-don`.
- HEAD nền: `a20ebe9d8580a3426e764497ac306fe620942498` + working tree; chưa commit/push.
- Antigravity triển khai tới v1.7; Codex trực tiếp sửa và kiểm chứng phần F06 cuối theo yêu cầu người dùng.
- Giữ nguyên T01/B01 đã nghiệm thu và phần ứng dụng B02 đã đạt. Đợt v1.8 chỉ sửa runner/fixture hồi quy F06 và hồ sơ bàn giao.

## Kết quả sửa F06 cuối

R8 đã tái hiện nhánh live báo PASS dù client nhận tồn 123 trong khi server trả 40. Nguyên nhân là nhánh live chỉ kiểm tra tồn không âm, còn phép so sánh chính xác chỉ chạy trong fixture.

v1.8 dùng cùng điều kiện đúng cho cả live và preflight:

1. Đọc tồn ban đầu trực tiếp từ backend test, đúng SKU/branch, ngoài đường proxy/client và không dùng cache.
2. Xác nhận nghiệp vụ online làm giảm đúng tồn khả dụng; xác nhận checkout lúc offline tiếp tục giảm đúng tồn server.
3. Sau ngắt socket thật và SDK tự reconnect/rejoin/refetch, đối chiếu cache với `tồn đầu − lượng online − lượng offline`.
4. Hai negative control chạy đúng nhánh live cố ý trả tồn sai và tồn cũ cho client; cả hai phải bị từ chối bởi phép so sánh chính xác, không được báo recovery PASS.

Không sửa API/backend engine hoặc logic ứng dụng để làm xanh kiểm thử. Môi trường, proxy và các phần đạt trước đó được giữ nguyên.

## Bằng chứng v1.8

| Kiểm chứng | Kết quả | Provider / bằng chứng |
| --- | --- | --- |
| Regression trước sửa | FAIL đúng lỗi: 2 ca âm bị runner cũ chấp nhận | [before-live-stock-regression.log](./evidence/v1.8/before-live-stock-regression.log). Server47/client123 và server54/client55 đều bị báo PASS sai. |
| Regression sau sửa | PASS: cả 2 ca âm bị runner từ chối | [after-live-stock-regression.log](./evidence/v1.8/after-live-stock-regression.log). Server61/client123 và server68/client69 gây đúng assertion thất bại; bộ kiểm tra báo3/3 gồm parent,0 skip. |
| Luồng live đúng | PASS,1/1,0 skip | [live-positive.log](./evidence/v1.8/live-positive.log). PostgreSQL + SDK + socket proxy thật; tồn78 − online2 − offline1 =75; cache75, không stale. |
| Preflight | PASS8/8 | [preflight-runner.log](./evidence/v1.8/preflight-runner.log), [results](./evidence/v1.8/preflight-runner-results.json). Đây là fixture offline, không gọi là bằng chứng PostgreSQL. |
| Build và24 test B02 | PASS, giữ kết quả R8 | Runtime ứng dụng không đổi trong v1.8; log v1.7 và kết quả R8 đã đối chiếu. |
| Baseline34 test, stock ordering/UI, PWA, browser, latency | Giữ nghiệm thu các vòng trước | Không chạy lại phần đã đạt khi source/rủi ro liên quan không thay đổi. |

[Kết luận và giới hạn kiểm chứng](./evidence/v1.8/verification-summary.json), [SHA-256 source đã kiểm](./evidence/v1.8/working-tree-hashes.json).

## Trạng thái finding và AC

| Finding / phạm vi AC | Trạng thái |
| --- | --- |
| B02-F01/F02/F03/F05 | CLOSED, giữ kết quả các vòng trước |
| B02-F04 — stock read ordering, stale/error, cache/cart không cần F5 | CLOSED từ R6, source giữ nguyên |
| B02-F06 — live event, rớt mạng thật, auto reconnect/rejoin/GET và tồn cuối chính xác | CLOSED với bằng chứng v1.8 |
| T03-AC-01..07 — realtime và cập nhật tồn | VERIFIED trong phạm vi B02 đã giao, kết hợp nghiệm thu trước và live v1.8 |
| T05-AC-01/03/04/06 — thao tác quầy, manifest, offline shell và latency | Giữ VERIFIED từ các vòng trước; không mở rộng sang B03 |

## Môi trường và chạy lại

Backend test `oisms_test_backend:5001` nối trực tiếp PostgreSQL `oisms_test_pg_check:5433`, database `oisms`, role `oisms_test_app`; marker `_oisms_test_fixture_marker=run_test_b02_v17`. Reviewer đã xác minh host cấu hình, marker và volume riêng với demo trước khi chạy mutation. Health/role riêng lẻ không thay thế việc xác minh cấu hình này khi đổi môi trường.

Từ repo root, sau khi xác minh backend5001 vẫn dùng đúng database test trên:

```powershell
$env:POS_LIVE_TEST_ENV='1'
$env:POS_TEST_BACKEND_URL='http://localhost:5001'
$env:POS_PREFLIGHT_HARNESS='0'
node --test src/frontend/pos-app/test-b02-hub-transport.mjs
node --test src/frontend/pos-app/test-b02-live-stock-regression.mjs
node src/frontend/pos-app/test-b02-hub-preflight.cjs
```

Các lệnh live tạo chứng từ test qua nghiệp vụ trong database test riêng; giữ dữ liệu test theo cách runner hiện tại, không xóa database. Demo5000 chỉ chạy authentication/hub smoke, không có mutation nghiệp vụ.

Hồ sơ bàn giao v1.7 trước sửa được giữ tại [handover-v1.7-before-fix.md](./evidence/v1.8/handover-v1.7-before-fix.md); các claim cũ không thay thế bằng chứng v1.8. Không thay README/PLAN chung, Admin/B03 hoặc evidence cũ.
