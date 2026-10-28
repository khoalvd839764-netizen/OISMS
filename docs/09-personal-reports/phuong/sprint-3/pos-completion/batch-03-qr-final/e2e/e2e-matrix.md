# Nghiệm thu B03 v1.3 — giữ AC gốc

Ngày 28/10/2026. B02 VERIFIED v1.8 là đầu vào; không còn gate chờ B02. Nguồn yêu cầu: brief.md mục3 và PLAN.md mục7/9. Bảng dưới không thay nghĩa/mã AC để khớp testcase.

| AC gốc | Bằng chứng và provider | Kết quả |
| --- | --- | --- |
| T04-AC-01 — Phân biệt các mức QR | qr-configuration.md: payload/QuickLink thanh toán, thu ngân xác nhận; chưa có đối soát ngân hàng tự động | PASS trong phạm vi Sprint3 |
| T04-AC-02 — Config, amount, identity | 8 test QR qua service/PaymentModal thật trong VM; browser case2/3: tenant/branch, close/open, reload, retry và đóng băng người nhận | PASS |
| T04-AC-03 — Xác nhận thủ công | UI cảnh báo thu ngân; module test08; ảnh QR không tự tạo checkout, chỉ nút checkout gửi POST | PASS |
| T04-AC-04 — Báo cáo minh bạch | handover.md, qr-configuration.md và báo cáo Sprint3 phân biệt fixture/provider/giới hạn | PASS |
| T04-AC-05 — Fixture/boundary/frontend | Module10/10 + regression QR8/8; fixture gắn nhãn, không chuyển tiền thật | PASS |
| T06-AC-01 — Browser + backend + PostgreSQL | browser-postgres-results.json:8 case, login/branch/barcode/cash/QR/receipt/reprint/shortage/loss/401/owners/Hub và các ca biên stock | PASS |
| T06-AC-02 — Mất response sau commit, retry UI | Case3: proxy nhận200 sau commit rồi ngắt body; UI unknown giữ2 dòng; reload/retry cùng payload; DB1 order +1 payment +2 OUT cho2 SKU | PASS |
| T06-AC-03 — Suites/build, provider đúng | npm test51/51 (baseline34 + QR8 + guard9); B02 24/24; QR module10/10; build tsc/Vite PASS. Source/VM không gọi là E2E thật | PASS |
| T06-AC-04 — Hồ sơ PR24-F01..F04 | T01/B01/B02 giữ VERIFIED; B03 giữ key, owner, receipt server và QR scoped; handover có mapping finding | PASS |
| T06-AC-05 — Develop và CI head cuối | Fetch develop ecc0c85; merge4197e28 không conflict, giữ docs PR25. CI source becc76d frontend/backend SUCCESS, evidence ci-source-head.json | PASS |
| T06-AC-06 — PR24 theo template | Dùng PR24 hiện có; mô tả theo template đã cập nhật trên GitHub, có scope/test/giới hạn và checklist trung thực | PASS |
| T06-AC-07 — Stock10→8 không F5 | Case1: Hub offline, mua2, UI8, replay vẫn8 + DB1 OUT; case5 reconnect actual event; case7 GET503/recovery; case8 responseA trễ không thêm hàng vàoB. B02 24 ca bổ sung kiểm tra cache/write ordering | PASS |

Lệnh từ root: node docs/09-personal-reports/phuong/sprint-3/pos-completion/batch-03-qr-final/e2e/test-runner-e2e-guard.mjs. Cũng có npm run test:b03:live tại pos-app. Môi trường cần backend5001→oisms_test_backend, PG5433→oisms_test_pg_check, DB oisms/user oisms_test_app/marker run_test_b02_v17. Guard bind container/ports/backend DB config/shared network/marker/readiness trước mọi mutation, không nhận cờ bypass. Chín negative/positive guard fixtures không kết nối DB.

Browser dùng Edge thật và build production; API nghiệp vụ đi backend thật, SQL chỉ SELECT. Proxy chỉ gây lỗi mạng/auth/Hub/GET có chủ đích; ảnh external VietQR được thay bằng SVG ghi TEST IMAGE ONLY để tránh phụ thuộc Internet. Đây không phải bằng chứng ảnh QR đã được app ngân hàng quét, tiền đã chuyển hoặc máy in vật lý đã in. Trace chứa phiên đăng nhập chỉ lưu tmp bị ignore; ảnh/JSON/log bàn giao không có token.
