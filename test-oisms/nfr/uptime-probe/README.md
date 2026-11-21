# `uptime-probe` — Kiểm tra độ sẵn sàng (`NFR-USA-01`)

Script Node.js không phụ thuộc thư viện ngoài, thăm dò `/health/live` và trang POS theo chu kỳ, chỉ tính tỷ lệ uptime trong khung giờ cao điểm `07:00–22:00 Asia/Ho_Chi_Minh`, xuất CSV và file tổng hợp JSON.

## Cách chạy

```bash
# Chạy thử 1 giờ (mỗi 30 giây) trên local
node test-oisms/nfr/uptime-probe/uptime-probe.mjs \
  --health-url http://localhost:5000/health/live \
  --pos-url http://localhost:5174/ \
  --interval-sec 30 \
  --duration-sec 3600 \
  --out-dir docs/06-testing-and-qa/evidence/goi-5/NFR-USA-01

# Chạy smoke test nhanh 5 lần (mỗi 2 giây)
node test-oisms/nfr/uptime-probe/uptime-probe.mjs \
  --interval-sec 2 --max-probes 5
```
