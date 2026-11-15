---
name: claude-to-antigravity
description: Điều phối OISMS theo mô hình Claude lập plan, viết prompt, review và nghiệm thu; Antigravity code và test. Có cơ chế chống lặp vô hạn giữa hai AI, quy tắc giao việc/chia nhỏ/Claude tự làm và tối ưu chi phí. Dùng khi người dùng muốn giao task cho Antigravity, viết prompt cho Anti, review bàn giao của Anti hoặc nhắc đến "Claude to Antigravity". Không tự áp dụng cho yêu cầu code thông thường.
---

# Claude điều phối — Antigravity triển khai

| Vai | Làm | Không làm |
| --- | --- | --- |
| **Claude** | Đối chiếu docs, lập plan, viết prompt, review, nghiệm thu, tự sửa phần được định tuyến cho mình | Viết prompt dài lặp lại docs; review lan ngoài scope |
| **Antigravity** | Code, chạy test, bàn giao bằng chứng theo prompt | Tự đổi AC, tự nhận VERIFIED cho STANDARD/CRITICAL |
| **Người dùng** | Chuyển prompt giữa hai app, quyết định nghiệp vụ, đồng ý commit/push | — |

Claude đọc [references/claude.md](references/claude.md); Antigravity đọc [references/antigravity.md](references/antigravity.md). Chỉ đọc file đúng vai. Hai bên đều không tự gửi lệnh cho nhau, không chạy vòng nền. **Mọi commit, push, PR, merge đều phải có người dùng đồng ý trong chat.**

## Quy trình

1. **Đối chiếu:** Claude xác định task, nguồn docs ([bản đồ](references/oisms.md)), hiện trạng code.
2. **Định tuyến:** phân loại rủi ro, chọn GIAO / CHIA / TỰ LÀM (mục dưới), báo một dòng lý do.
3. **Giao:** tạo [sổ task](assets/task-ledger.md), viết [prompt](assets/prompt-implement.md), người dùng chuyển sang Antigravity.
4. **Làm:** Antigravity code, test, trả [bàn giao](assets/handoff.md).
5. **Nghiệm thu:** Claude review một lượt, kết luận `VERIFIED`, `CHANGES_REQUESTED` hoặc `BLOCKED`.
6. **Sửa nếu cần:** theo thang leo dưới đây, không vượt trần vòng.
7. **Kết thúc:** báo kết quả; hỏi người dùng trước khi commit. Không tự mở task kế tiếp.

## Mức rủi ro

Phân loại theo hành vi và dữ liệu bị tác động, không theo độ lớn diff. Chưa rõ thì chọn mức cao hơn; nâng mức ngay khi phát hiện rủi ro mới, không hạ để né gate.

| Mức | Điều kiện | Nghiệm thu | Trần vòng sửa của Anti |
| --- | --- | --- | --- |
| FAST | Nhỏ, dễ đảo ngược; không đổi nghiệp vụ, contract, dữ liệu, cơ chế an toàn | Anti tự kiểm chứng; Claude chỉ xem khi người dùng yêu cầu | 0 (Anti tự xử trong lượt) |
| STANDARD | API, service, validation, UI có logic, không chạm nhóm CRITICAL | Claude review diff + evidence | 2 |
| CRITICAL | Auth/RBAC, tenant isolation, schema/migration, transaction, ledger, WAC, reservation, idempotency, concurrency, nguy cơ mất dữ liệu | Claude review + chạy lại gate rủi ro trên PostgreSQL thật | 1 |

## Định tuyến: GIAO / CHIA / TỰ LÀM

Giao cho Anti phần tốn công nhưng rõ đường; Claude giữ phần cần phán đoán. Chọn đúng từ đầu rẻ hơn sửa sau.

| Quyết định | Khi nào | Cách làm |
| --- | --- | --- |
| **GIAO** | Đủ docs/mẫu trong repo; AC kiểm được bằng lệnh; CRUD, DTO, UI theo mẫu, test, map contract | Một prompt, tối đa ~8 AC |
| **CHIA** | Nhiều hơn ~8 AC, nhiều tầng, hoặc có lõi khó nằm trong phần dễ | Mỗi phần một prompt, nghiệm thu độc lập, chạy tuần tự. Anti làm phần nền trước (schema, DTO, CRUD, test khung, UI); Claude tự viết hoặc sửa phần lõi sau |
| **TỰ LÀM** | Sửa nhỏ ≤ ~20 dòng, rõ ràng, viết prompt tốn hơn tự sửa: Claude làm luôn và báo lại. Phần lớn hơn (thứ tự lock/deadlock, thiết kế transaction, lỗi Anti đã trượt hai lần): Claude đề xuất, người dùng chốt | Nêu lý do và ước lượng trong một câu |

Dấu hiệu "quá sức Anti" (có ≥ 2 thì không GIAO nguyên khối): không có mẫu tương tự trong repo; phải giữ invariant xuyên nhiều service; cần suy luận về chạy đồng thời; docs chưa chốt; lần trước đã trượt ở vùng này.

**Tự kiểm tra độc lập:** phần CRITICAL do Claude viết thì Claude không tự nhận đã review độc lập. Vẫn phải có gate PostgreSQL thật, ghi rõ "Claude vừa viết vừa kiểm" và để người dùng chọn có cần reviewer khác (Codex, `/code-review`).

## Chống lặp vô hạn

Đây là luật cứng, không phải khuyến nghị. Sổ task là nguồn sự thật cho các bộ đếm.

1. **Trần vòng:** mỗi prompt fix ghi `Vòng x/trần`. Hết trần thì không có prompt fix cùng phạm vi nữa.
2. **Trần tổng:** mỗi task tối đa 3 prompt gửi Anti (1 giao + 2 fix). Cần nhiều hơn nghĩa là task sai cỡ: chia lại hoặc đổi người làm.
3. **Một lỗi hai lần trượt:** finding sửa rồi vẫn tái hiện, hoặc đóng rồi mở lại, thì không trả về Anti. Chuyển ngay thang leo.
4. **Phải hội tụ:** số finding chặn phải giảm sau mỗi vòng. Không giảm thì dừng vòng, chuyển thang leo, kể cả chưa hết trần.
5. **Đóng băng phạm vi review:** vòng 1 review toàn diff của task. Từ vòng 2 chỉ xét finding mở, diff sửa và regression trực tiếp. Finding mới phải chỉ ra được do diff hiện tại gây ra hoặc chặn AC; còn lại đưa vào backlog. Style không bao giờ là finding chặn.
6. **Tranh luận một lượt:** Anti phản biện finding kèm bằng chứng một lần, Claude phán quyết một lần theo docs và test. Docs mâu thuẫn hoặc thiếu thì hỏi người dùng, không trao đổi tiếp.
7. **Không bóng bàn:** Claude đã nhận phần nào thì không trả lại Anti phần đó; Anti chỉ chạy test được yêu cầu.
8. **Dừng phía Anti:** cùng một lỗi thử hai lần không có giả thuyết mới thì dừng, trả `BLOCKED` kèm các thử nghiệm đã làm. Không nới assertion, bỏ test hay đổi AC để có PASS.
9. **Prompt phải khác:** không gửi lại prompt có cùng finding và cùng hướng dẫn. Không có thông tin mới thì không có prompt mới.

**Thang leo khi vướng** (đi từ trên xuống, không quay lại bậc trên):

1. Sửa nhỏ, rõ ràng → Claude tự sửa.
2. Lỗi thuộc một phần tách được → chia nhỏ phần đó, một prompt mới có bối cảnh chẩn đoán, chỉ một lần.
3. Khó và rộng → Claude đề xuất tự làm, nêu ước lượng.
4. Thiếu quyết định, quyền, môi trường, hoặc docs mâu thuẫn → `BLOCKED`, hỏi người dùng đúng một câu.

Hết thang mà chưa đạt thì báo thẳng phần đạt, phần chưa đạt, lý do. Không tuyên bố hoàn thành.

## Tối ưu chi phí

- **Prompt dẫn đường, không chép:** ghi đường dẫn và mục docs, không dán nội dung. Mục tiêu prompt giao ≤ ~60 dòng, prompt fix ≤ ~30 dòng.
- **Cổng rẻ trước đắt:** bàn giao thiếu evidence thì trả lại ngay, không đọc diff. Đọc `git diff --stat` trước, sau đó chỉ đọc file thuộc task, ưu tiên CodeGraph.
- **Tái dùng PASS:** evidence còn hợp lệ khi source, cấu hình, fixture, provider không đổi. Claude chỉ chạy lại test cho gate CRITICAL hoặc khi evidence đáng ngờ.
- **Gom một lượt:** gom mọi finding vào một prompt fix, không gửi lẻ.
- **Test theo tác động:** Anti chạy bộ liên quan khi đang sửa; full suite chỉ ở cuối đợt nếu đổi shared dependency hoặc brief yêu cầu.
- **Sổ task thay cho đọc lại:** phiên mới đọc sổ task, không đọc lại toàn hội thoại hay docs.
- **Không subagent mặc định, không audit toàn repo, không viết báo cáo khi không được yêu cầu.**

## Bám đúng tài liệu

- Mỗi AC ghi nguồn `đường-dẫn-docs#mục`. AC không có nguồn phải ghi rõ là quyết định của người dùng.
- Docs xác định yêu cầu, code và test xác định hiện trạng. Báo cáo kế hoạch không phải bằng chứng đã làm.
- Mâu thuẫn giữa docs với docs, hoặc docs với code, thì nêu ra để người dùng quyết định; không tự chọn và không âm thầm đổi AC.
- Làm khác docs do lý do chính đáng phải ghi vào bàn giao và đề xuất cập nhật docs. Không viết lại yêu cầu để khớp code.
- Giữ các invariant trong [references/oisms.md](references/oisms.md).
- **Luật cứng — docs đi cùng code:** thay đổi ảnh hưởng chức năng hiện có, contract API, quyền, trạng thái, giá vốn/kho hoặc database thì phải cập nhật docs tương ứng và viết báo cáo trong cùng task. Prompt phải có mục `DOCS PHẢI CẬP NHẬT` liệt kê từng file theo bảng "Thay đổi → docs" trong [oisms.md](references/oisms.md); thiếu docs là finding chặn, không được `VERIFIED`. Không sửa tài liệu lịch sử (sprint, báo cáo cá nhân, báo cáo cũ) để khớp hiện trạng.

## Kiểm chứng và trạng thái

- Mỗi kết quả ghi lệnh, cwd, source đã test, provider, PASS/FAIL/NOT RUN và giới hạn. Không gộp các test rời thành "luồng tích hợp đã chạy".
- EF InMemory và mock không chứng minh PostgreSQL, transaction hay concurrency. Build pass không có nghĩa feature pass.
- Bug tái hiện được: ưu tiên FAIL trước sửa → PASS sau sửa, cùng scenario.

Trạng thái: `READY → IMPLEMENTING → READY_FOR_REVIEW → VERIFIED`; có finding chặn thì `CHANGES_REQUESTED → IMPLEMENTING`; thiếu điều kiện thì `BLOCKED` kèm cách gỡ. FAST đủ evidence thì Anti tự `VERIFIED`. Gate FAIL hoặc NOT RUN, AC chưa đạt, finding P0/P1 còn mở đều cấm `VERIFIED`.
