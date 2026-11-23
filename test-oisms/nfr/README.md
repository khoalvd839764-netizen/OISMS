# Bộ công cụ đo NFR local (`test-oisms/nfr/`)

- `seed-perf.sql`: Chèn dữ liệu bulk ~100.000 dòng `order_items` + `inventory_transactions` cho tenant `PERF` trên DB đo riêng `oisms_perf` (từ chối chạy nếu DB là `oisms` hoặc `postgres`).
- `measure-latency.mjs`: Script Node.js (không thư viện ngoài) đo `p50`/`p95`/`p99` với đồng thời `1` và `10`. Tùy chọn: `--only "<chuỗi trong nhãn>"` chỉ đo nhóm khớp; `--n-read`, `--n-report` đổi số request. Có 2 nhóm checkout POS: `POST /api/pos/checkout` (mọi request bán cùng 1 SKU, ca xấu nhất tranh khóa) và `POST /api/pos/checkout (multi-SKU)` (request thứ `i` bán SKU thứ `i % 20`).
- `explain-tenant-02.sql`: Kiểm tra kích thước bảng và `EXPLAIN (ANALYZE, BUFFERS)` cho 3 truy vấn đại diện `NFR-TENANT-02`.
- `uptime-probe/`: Script thăm dò uptime `NFR-USA-01`.
