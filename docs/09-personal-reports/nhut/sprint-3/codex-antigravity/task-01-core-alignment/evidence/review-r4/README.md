# Reviewer evidence — T01 v1.3 / R4

Ngày 2026-10-26, Asia/Saigon. Tested SHA: `ca6b5ce4d9ebe38836e9fbedef12e185c492a409`, nhánh `feature/OISMS-42-sprint-3-backend-2`.

| Artifact | Nội dung |
| --- | --- |
| [build.log](./build.log) | Release: 0 warning / 0 error |
| [unit.log](./unit.log) | 146/146 PASS; TRX executed=146, failed=0, aborted=0 |
| [targeted.log](./targeted.log) | 44/44 PASS; TRX executed=44, failed=0, aborted=0 |
| [probes.log](./probes.log) | Sáu reviewer probes PostgreSQL: 6/6 PASS |
| [probe-results.json](./probe-results.json) | Counters và output từng probe, không aborted |
| [ReviewerProbes.cs](./ReviewerProbes.cs) | Bản sao nguyên source reviewer R3, chạy lại trên code v1.3 |
| [ReviewerProbes.csproj.txt](./ReviewerProbes.csproj.txt) | Project tạm để tái chạy |

Probe R3 giữ nguyên assertion; không dùng bản tmp do implementer chỉnh sửa để thay bằng chứng gốc. Output missing timestamp chứng minh cả retry trả VALIDATION_ERROR/400, payload không bị inject timestamp và DB có 0 order. Assertion retry/Reserved bổ sung được kiểm chứng trong regression PostgreSQL mới của sản phẩm. Valid replay dùng hai DTO mới cũng được kiểm tra trong regression suite.

Nhóm 44 gồm 15 process/simulator regression, 14 WebhookIntegrationTests, 8 WebhookSimulatorControllerTests, 6 warehouse reassignment và 1 B-01 gốc. Không skipped/aborted.

## Tái chạy

Copy hai file probe vào `tmp/codex-be2-t01-review-r4/probe/`, đổi `.csproj.txt` thành `.csproj`. Content root trong source là `D:/OISMS/src/backend/src/OISM.WebApi`; chỉnh nếu clone ở vị trí khác. Hoàn tất build trước khi test để tránh DLL lock trên Windows.

```powershell
dotnet build src/backend/OISM.sln -c Release --no-restore -m:1 --verbosity minimal
dotnet test tmp/codex-be2-t01-review-r4/probe/ReviewerProbes.csproj -c Release -p:BuildProjectReferences=false --logger trx --results-directory tmp/codex-be2-t01-review-r4/probe-results --verbosity minimal
dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj -c Release --no-build --no-restore --logger trx --results-directory tmp/codex-be2-t01-review-r4/unit
dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~OrderProcessAndSimulatorRegressionTests|FullyQualifiedName~Webhook|FullyQualifiedName~WarehouseBranchReassignment|FullyQualifiedName~Postgres_Warehouse_ChangeBranch" --logger trx --results-directory tmp/codex-be2-t01-review-r4/targeted
```

Fixture dùng PostgreSQL test theo RunId, ownership guards và cleanup riêng; không gọi deployed API/database nghiệp vụ. Raw TRX ở tmp ignored; file tại đây là bản sao bàn giao. Không có JWT/credential người dùng trong output.

## CI

Reviewer quan sát [run 40347045096](https://github.com/khoalvd839764-netizen/OISMS/actions/runs/40347045096) của `ca6b5ce`: **Success**, backend 8m28s, frontend 17s, toàn workflow 8m32s. Backend [job 121656631902](https://github.com/khoalvd839764-netizen/OISMS/actions/runs/40347045096/job/121656631902); frontend [job 121656631915](https://github.com/khoalvd839764-netizen/OISMS/actions/runs/40347045096/job/121656631915).

Không có raw CI log trong evidence do GitHub yêu cầu đăng nhập; chỉ xác minh check status. Handover Anti ghi 282 full integration / tổng 428; reviewer không trình bày các số đó như bộ đếm CI tự tải. Hai warning và hai notice của workflow là Node Actions runtime/Ubuntu runner, không phải test failure.
