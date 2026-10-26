# Reviewer evidence — T01 R1

Ngày: 2026-10-26. Code: `10d9abf7e7dfe8c0171bf22c6988bc9b7d55001a`, backend source giống head tài liệu `ceaff0c`.

| Artifact | Nội dung |
| --- | --- |
| [build.log](./build.log) | Build Release độc lập: 0 warning / 0 error |
| [unit.log](./unit.log) | 141/141 unit PASS, 0 skipped |
| [targeted.log](./targeted.log) | 29/29 PostgreSQL/HTTP tests PASS: 22 webhook + 6 reassignment + B-01 gốc |
| [probes.log](./probes.log) | Hai reviewer regression probes FAIL sau khi thực thi PostgreSQL/HTTP |
| [probe-results.json](./probe-results.json) | Snapshot counters và output của đúng hai probe đã thực chạy |
| [ReviewerProbes.cs](./ReviewerProbes.cs) | Source reviewer probes, tách khỏi source sản phẩm |
| [ReviewerProbes.csproj.txt](./ReviewerProbes.csproj.txt) | Project tạm tham chiếu test fixture/backend hiện có |

Hai failure quan sát được:

- `/process`: HTTP 500, `ck_orders_status_values` (23514); DB sau lỗi order Confirmed, OnHand=8, Reserved=0, một ledger OUT sau khi ban đầu có 10 đơn vị và đặt 2.
- Adapter same-total replay: shop chưa cấu hình vẫn tạo đơn; đổi 1×100 thành 2×50 trả cùng order cũ, quantity=1 / price=100; không có payload mismatch exception.

## Lệnh reviewer đã chạy

```powershell
dotnet build src/backend/OISM.sln -c Release --no-restore -m:1 --verbosity minimal
dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj -c Release --no-build --no-restore --logger trx --results-directory tmp/codex-be2-t01-review/unit
dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Webhook|FullyQualifiedName~WarehouseBranchReassignment|FullyQualifiedName~Postgres_Warehouse_ChangeBranch" --logger trx --results-directory tmp/codex-be2-t01-review/targeted
dotnet test tmp/codex-be2-t01-review/probe/ReviewerProbes.csproj -c Release --no-restore -p:BuildProjectReferences=false --logger trx --results-directory tmp/codex-be2-t01-review/probe-results --verbosity minimal
```

Để tái tạo project, copy hai file probe trong evidence vào `tmp/codex-be2-t01-review/probe/`, đổi tên file `.csproj.txt` thành `ReviewerProbes.csproj`, restore/build project rồi chạy tests. Đường dẫn content root trong source là `D:/OISMS/src/backend/src/OISM.WebApi`; chỉnh theo workspace nếu clone ở vị trí khác. Project references tương đối dự kiến chạy từ vị trí tmp trên.

Fixture sử dụng database test riêng theo RunId, safe-target guard/ownership marker và cleanup thuộc chính run; không kết nối database nghiệp vụ. Hai test chạy nối tiếp và dùng `WebApplicationFactoryWithPostgres`/runtime context PostgreSQL.

Các lỗi thiết lập project probe ban đầu (thiếu SetTenant/testhost dependency) và file lock do build trùng lúc suite targeted đang chạy đã được sửa trước lần chạy cuối, không tính là lỗi sản phẩm. Log/TRX cuối có executed=2, failed=2, aborted=0 và kết quả persisted/HTTP ở output từng test. Các lượt thiết lập trước không dùng làm evidence findings.

Raw TRX đầy đủ nằm trong `tmp/codex-be2-t01-review/{unit,targeted,probe-results}/`, là artifact ignored cục bộ; JSON/log/source ở thư mục này là bản sao để bàn giao. Reviewer không chạy lại full suite vì CI hiện có đã xanh và negative probes đủ xác nhận hai lỗi chặn; cần chạy full suite lại sau fix thay đổi mutation.
