# Bằng chứng trước fix — 2026-10-26

Reviewed branch: `feature/OISMS-42-sprint-3-backend-2` @ `d7fa4bc8738a1f22053d9b313f8e90c423b0483d`.

| Artifact | Nguồn / ý nghĩa |
| --- | --- |
| [build.log](./build.log) | Release build toàn backend, exit 0, 0 warning / 0 error |
| [unit.log](./unit.log) | Unit suite thực chạy, exit 0, 114 passed / 0 failed / 0 skipped |
| [merge-preview.log](./merge-preview.log) | Merge-tree preview với BE1 `4206cc4`, 5 conflict; không phải actual merge |
| [probe-results.json](./probe-results.json) | 7 quan sát tái hiện service bugs; provider **EF InMemory** |
| [reviewer-probe.cs](./reviewer-probe.cs) | Source probe độc lập dùng API hiện tại, không thay source nghiệp vụ |

## Lệnh đã chạy

```powershell
dotnet build src/backend/OISM.sln -c Release --no-restore -m:1 --verbosity minimal
dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj -c Release --no-build --no-restore --logger "trx;LogFileName=be2-unit-reviewed-head.trx" --results-directory tmp/codex-be2-review --verbosity minimal
git merge-tree --write-tree --name-only HEAD origin/feature/OISMS-40-loi-van-hanh-kho-va-don-hang
dotnet run --project tmp/codex-be2-review/probe/Probe.csproj -c Release --no-launch-profile
```

TRX đầy đủ được kiểm tra counters: total/executed/passed=114, failed/error/timeout/aborted=0; runner báo skipped=0. Raw TRX tại `tmp/codex-be2-review/be2-unit-reviewed-head.trx` là artifact cục bộ, nằm trong thư mục ignored; không coi đường dẫn đó là file đã xuất bản. Lượt chạy trong sandbox bị testhost connection timeout/aborted; các lượt chạy lại ngoài sandbox pass. Kết quả cuối dùng tên TRX riêng để tránh lượt timeout ghi đè; log thành công được lưu ở đây. Lỗi testhost của môi trường không được phân loại thành finding nghiệp vụ.

## Tái chạy probe từ bản sao tài liệu

Tại repo root, đúng reviewed commit và không có source fixes, tạo console project tạm rồi tham chiếu UnitTests project:

```powershell
dotnet new console --framework net8.0 --no-restore -o tmp/be2-repro
dotnet add tmp/be2-repro reference src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj
Copy-Item -LiteralPath docs/09-personal-reports/nhut/sprint-3/codex-antigravity/evidence/reviewer-probe.cs -Destination tmp/be2-repro/Program.cs
dotnet run --project tmp/be2-repro -c Release
```

Probe tạo database InMemory riêng mỗi nhóm, không kết nối PostgreSQL hoặc ghi dữ liệu làm việc. JSON là quan sát thực tế và kỳ vọng nghiệp vụ, không phải báo cáo 7 test pass. Ví dụ batch trả 50 success là bằng chứng lỗi, không phải nghiệm thu thành công.

Sau T01 thay đổi API, probe nguồn này có thể không compile: giữ nguyên làm evidence lịch sử, viết regression test theo contract mới. Các kiểm thử PostgreSQL, HTTP RBAC và Hangfire của nhánh hợp nhất là công việc phía trước trong T01–T06, chưa được xác nhận bằng các artifact này.
