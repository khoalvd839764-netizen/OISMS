# Reviewer evidence — T01 v1.1 / R2

Ngày 2026-10-26, Asia/Saigon. Code `dc20e8887a56e8e190b31f62b1273a5580138e68`; documentation head `88898417d438344c3d40aa14716e1cb3feff1d8a` có cùng backend source.

| Artifact | Kết quả |
| --- | --- |
| [build.log](./build.log) | Release: 0 warning / 0 error |
| [unit.log](./unit.log) | Unit độc lập: 143/143 PASS |
| [targeted.log](./targeted.log) | PostgreSQL: 36/36 PASS, gồm 7 regression v1.1 + 22 webhook + 6 warehouse + B-01 gốc |
| [probes.log](./probes.log) | Bốn probe: 2 PASS, 2 FAIL; không aborted |
| [probe-results.json](./probe-results.json) | Counters và output từng probe |
| [ReviewerProbes.cs](./ReviewerProbes.cs) | Source reviewer probes, tách khỏi source sản phẩm |
| [ReviewerProbes.csproj.txt](./ReviewerProbes.csproj.txt) | Định nghĩa project tạm |

Hai case PASS: process HTTP 200/Confirmed/OnHand 8/Reserved 0/một OUT; same-total changed-items trả đúng WEBHOOK_PAYLOAD_MISMATCH 409. Hai case FAIL: batch cấp credential biết trước được request không JWT dùng để tạo đơn qua HTTP 201; adapter nhận default timestamp và persist order trong khi canonical ingress trả 400.

Probe same-total dùng source R1 với fixture ShopIntegration hợp lệ và assertion error code/status chặt hơn. Evidence R1 gốc được giữ nguyên. Thiếu cấu hình không được tính nhầm là payload mismatch đã đạt.

## Tái chạy

Copy hai file probe vào `tmp/codex-be2-t01-review-r2/probe/`, đổi `.csproj.txt` thành `.csproj`, restore/build sau khi build sản phẩm. Source content root là `D:/OISMS/src/backend/src/OISM.WebApi`; chỉnh nếu clone ở vị trí khác.

```powershell
dotnet build src/backend/OISM.sln -c Release --no-restore -m:1 --verbosity minimal
dotnet test tmp/codex-be2-t01-review-r2/probe/ReviewerProbes.csproj -c Release -p:BuildProjectReferences=false --logger trx --results-directory tmp/codex-be2-t01-review-r2/probe-results --verbosity minimal
dotnet test src/backend/tests/OISM.UnitTests/OISM.UnitTests.csproj -c Release --no-build --no-restore --logger trx --results-directory tmp/codex-be2-t01-review-r2/unit
dotnet test src/backend/tests/OISM.IntegrationTests/OISM.IntegrationTests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~OrderProcessAndSimulatorRegressionTests|FullyQualifiedName~Webhook|FullyQualifiedName~WarehouseBranchReassignment|FullyQualifiedName~Postgres_Warehouse_ChangeBranch" --logger trx --results-directory tmp/codex-be2-t01-review-r2/targeted
```

Mọi DB write/HTTP trong probe chỉ nhắm PostgreSQL test theo RunId, có target/ownership guard và cleanup thuộc run. Không gọi service triển khai bên ngoài. Secret literal đã có trong source sản phẩm chỉ dùng đối chứng trên DB test cục bộ; artifact output không chứa JWT/credential của người dùng.

Raw TRX nằm trong thư mục ignored `tmp/codex-be2-t01-review-r2/`; JSON/log/source ở đây là bản sao bàn giao. CI suite hiện tại chưa có hai negative cases mới phát hiện.
