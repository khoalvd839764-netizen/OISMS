# Reviewer evidence — T01 v1.2 / R3

Ngày 2026-10-26, Asia/Saigon. Code `db889057940d4ca43fd400b6ce424db6401d4c4a`; head tài liệu `f9db45a1808f2d25ec8ea7bf7aae515491e14d50` không đổi backend source.

| Artifact | Nội dung |
| --- | --- |
| [build.log](./build.log) | Release độc lập: 0 warning / 0 error |
| [unit.log](./unit.log) | Unit độc lập: 143/143 PASS |
| [targeted.log](./targeted.log) | 41/41 PASS: 12 regression v1.2 + 22 webhook + 6 warehouse + B-01 gốc |
| [probes.log](./probes.log) | Sáu probe: 4 PASS / 2 FAIL, không aborted |
| [probe-results.json](./probe-results.json) | Counters và output từng test |
| [ReviewerProbes.cs](./ReviewerProbes.cs) | Source probes reviewer |
| [ReviewerProbes.csproj.txt](./ReviewerProbes.csproj.txt) | Định nghĩa project tạm |

Probe credential đã được đổi precondition đúng contract mới: batch thiếu cấu hình phải trả 404, không tạo shop/order/reservation; sau đó xác minh secret cố định trả 401 và credential riêng của integration fixture trả 201. Đây là case PASS thực sự, không nhận Expected Failure của assertion 200 cũ làm bằng chứng đóng finding.

Các positive probes còn lại: Process 200/Confirmed/stock đúng một lần; same-total changed-items trả đúng WEBHOOK_PAYLOAD_MISMATCH 409 với timestamp hợp lệ tường minh; default timestamp trả validation 400 ở cả hai đường.

Hai failure: adapter tự thêm timestamp và persist lần đầu cho payload thiếu timestamp, retry DTO mới trả 409 trong khi canonical missing trả 400; adapter ném NullReferenceException khi item null trong khi canonical trả 400.

## Tái chạy

Copy hai file probe vào `tmp/codex-be2-t01-review-r3/probe/`, đổi `.csproj.txt` thành `.csproj`, restore/build sau product build. Content root trong source là `D:/OISMS/src/backend/src/OISM.WebApi`; chỉnh nếu clone ở vị trí khác.

```powershell
dotnet build src/backend/OISM.sln -c Release --no-restore -m:1 --verbosity minimal
dotnet test tmp/codex-be2-t01-review-r3/probe/ReviewerProbes.csproj -c Release -p:BuildProjectReferences=false --logger trx --results-directory tmp/codex-be2-t01-review-r3/probe-results --verbosity minimal
dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj -c Release --no-build --no-restore --logger trx --results-directory tmp/codex-be2-t01-review-r3/unit
dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~OrderProcessAndSimulatorRegressionTests|FullyQualifiedName~Webhook|FullyQualifiedName~WarehouseBranchReassignment|FullyQualifiedName~Postgres_Warehouse_ChangeBranch" --logger trx --results-directory tmp/codex-be2-t01-review-r3/targeted
```

Fixture dùng PostgreSQL test theo RunId, target/ownership guards và cleanup riêng; không gọi deployed API hay database nghiệp vụ. Không có JWT/credential người dùng trong output. Raw TRX được giữ ở tmp ignored; artifact ở đây là bản sao bàn giao.
