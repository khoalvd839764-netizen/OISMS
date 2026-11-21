# Checkout POS đồng thời: cùng SKU và nhiều SKU (`NFR-PERF-01`, 21/11/2026)

- **Người chạy:** nhóm rà soát. **Commit:** `879a1ef` + thay đổi chưa commit của OISMS-66-FIN.
- **Môi trường:** như [đo lại sau `shm_size`](./2026-11-21-shm256.md): backend Debug cổng 55002, DB `oisms_perf` (PostgreSQL 16 Docker, `/dev/shm` 256 MB), AMD Ryzen 7 H 255 16 vCPU, 22 GiB RAM. Tồn khả dụng mỗi SKU PERF ≥ 95.700, không có ca hết hàng.
- **Thay đổi script:** `test-oisms/nfr/measure-latency.mjs` thêm nhóm `POST /api/pos/checkout (multi-SKU)`: request thứ `idx` bán 1 đơn vị SKU thứ `idx % 20`, nên các request đồng thời không tranh cùng khóa tồn kho. Nhóm cũ `POST /api/pos/checkout` giữ nguyên (mọi request bán cùng SKU đầu tiên).
- **Lệnh (chạy 2 lượt):** `node test-oisms/nfr/measure-latency.mjs --base-url http://127.0.0.1:55002 --only "checkout" --n-read 50 --out docs/06-testing-and-qa/evidence/goi-5/NFR-PERF-01/latency-checkout-multisku-run<N>.json`
- **Kết quả thô:** [lượt 1](./latency-checkout-multisku-run1.json), [lượt 2](./latency-checkout-multisku-run2.json). Mỗi lượt tạo thêm 200 đơn POS trong `oisms_perf`.

| Ca | c | Lượt | p50 (ms) | p95 (ms) | p99 (ms) | ok | Ngưỡng < 500 ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| Cùng 1 SKU | 1 | 1 | 43.62 | 49.51 | 756.38 | 50/50 | **ĐẠT** |
| Cùng 1 SKU | 1 | 2 | 38.11 | 42.72 | 46.60 | 50/50 | **ĐẠT** |
| Nhiều SKU | 1 | 1 | 38.68 | 49.59 | 51.31 | 50/50 | **ĐẠT** |
| Nhiều SKU | 1 | 2 | 32.79 | 38.74 | 48.78 | 50/50 | **ĐẠT** |
| Cùng 1 SKU | 10 | 1 | 356.86 | 577.48 | 610.30 | 50/50 | **CHƯA ĐẠT** |
| Cùng 1 SKU | 10 | 2 | 358.12 | 379.00 | 380.17 | 50/50 | **ĐẠT** |
| **Nhiều SKU** | **10** | 1 | 45.29 | **53.08** | 61.53 | 50/50 | **ĐẠT** |
| **Nhiều SKU** | **10** | 2 | 46.25 | **58.35** | 60.04 | 50/50 | **ĐẠT** |

- p99 756 ms ở lượt 1 (cùng SKU, c=1) là request đầu tiên sau khi khởi động backend.

## Kết luận

- **10 quầy bán các SKU khác nhau cùng lúc:** p95 53–58 ms, gần bằng khi chỉ 1 quầy → **ĐẠT** rõ ràng. Đây là tình huống thường gặp.
- **10 quầy cùng bán 1 SKU cùng lúc (ca xấu nhất):** p50 ~357 ms ổn định vì các giao dịch xếp hàng qua khóa tồn kho (`pg_advisory_xact_lock` + `FOR UPDATE`, thiết kế chống bán âm theo `NFR-PERF-02`); p95 dao động quanh ngưỡng (379–577 ms giữa 2 lượt). Ghi **giới hạn đã biết**: độ trễ tăng tuyến tính theo số quầy tranh cùng một SKU; đánh đổi có chủ đích để đảm bảo tồn không âm.
- **CHƯA ĐO TRÊN DEPLOY**.
