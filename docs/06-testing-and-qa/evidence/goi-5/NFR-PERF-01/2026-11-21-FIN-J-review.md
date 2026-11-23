# Review FIN-J — nguyên nhân ca 20 SKU đồng thời 10 chậm (`NFR-PERF-01`)

- **Ngày:** 2026-11-21. Người chạy: nhóm rà soát (review FIN-J).
- **Commit:** `879a1ef` + thay đổi chưa commit của OISMS-66-FIN (gồm FIN-J).
- **Môi trường:** như [FIN-J](./2026-11-21-FIN-J.md): backend Debug cổng 55002, DB `oisms_perf` trên PostgreSQL 16 Docker local (`postgres:16-alpine`, `/dev/shm` mặc định 64 MB).

## Hiện tượng

FIN-J ghi 20 SKU `c=10` p95 5282 ms và cho rằng nguyên nhân là `EXISTS` trên `order_items`. Đo lại với 50 request mỗi ca ([JSON](./latency-fin-j-review-n50.json)):

| Ca | c | p50 (ms) | p95 (ms) | ok |
| --- | ---: | ---: | ---: | ---: |
| không lọc | 10 | 792.86 | 924.98 | 50/50 |
| 30 ngày | 10 | 780.92 | 1070.58 | 50/50 |
| 1 SKU | 10 | 87.29 | 165.21 | 50/50 |
| 20 SKU | 10 | 508.19 | **5096.56** | **49/50** |

Bắn 10 request 20 SKU cùng lúc, độ trễ rơi vào 3 nấc: ~0,6 s, ~1,7 s, ~4,8 s.

## Nguyên nhân

- `EXPLAIN ANALYZE` của truy vấn dòng hàng có `EXISTS` chỉ 44–55 ms (cả custom và generic plan); khi đang tải, truy vấn trong `pg_stat_activity` đều ≤ 150 ms. SQL không phải nút thắt.
- Log PostgreSQL có lỗi `could not resize shared memory segment "/PostgreSQL.…" … No space left on device`: 10 truy vấn cùng dùng parallel hash join làm hết `/dev/shm` 64 MB mặc định của container Docker.
- Lỗi này là lỗi tạm thời, `EnableRetryOnFailure(3, 5s)` trong `OISM.Infrastructure/DependencyInjection.cs` chạy lại truy vấn sau ~1 s rồi ~3 s, tạo ra các nấc 1,7 s và 4,8 s; một request hết lượt retry nên lỗi (49/50).

## Kiểm chứng

Tạm đặt `ALTER DATABASE oisms_perf SET max_parallel_workers_per_gather = 0`, khởi động lại backend, đo lại ([JSON](./latency-fin-j-review-n50-noparallel.json)), rồi `RESET` về mặc định (đã kiểm tra trả về `2`):

| Ca | c | p50 (ms) | p95 (ms) | ok |
| --- | ---: | ---: | ---: | ---: |
| không lọc | 1 | 377.28 | 406.10 | 50/50 |
| 30 ngày | 1 | 381.43 | 418.21 | 50/50 |
| 1 SKU | 1 | 54.25 | 66.38 | 50/50 |
| 20 SKU | 1 | 379.66 | 479.71 | 50/50 |
| không lọc | 10 | 801.77 | 1048.62 | 50/50 |
| 30 ngày | 10 | 765.30 | 1022.00 | 50/50 |
| 1 SKU | 10 | 76.55 | 145.48 | 50/50 |
| 20 SKU | 10 | 780.63 | **1009.59** | **50/50** |

## Kết luận

- Code FIN-J đúng và đạt: kết quả JSON trước/sau không đổi, 8/8 ca dưới 2 s khi PostgreSQL không bị thiếu `/dev/shm`.
- Ca 20 SKU `c=10` CHƯA ĐẠT là do cấu hình container PostgreSQL (`/dev/shm` 64 MB), không do code. Cách sửa: đặt `shm_size` cho service `postgres` trong `docker-compose.yml` (bản demo AWS dùng cùng file nên cùng bị ảnh hưởng), cần tạo lại container `postgres` (dữ liệu nằm ở volume, không mất). Chưa áp dụng, chờ nhóm đồng ý.
- **CHƯA ĐO TRÊN DEPLOY**.
