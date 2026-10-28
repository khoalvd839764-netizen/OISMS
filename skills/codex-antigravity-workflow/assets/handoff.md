# Bàn giao — {TASK-ID} / {revision}

- Vai trò / trạng thái / vòng review:
- Baseline và phạm vi diff được báo cáo:
- Hành vi đã thay đổi, file chính và lý do:
- README/personal report đã cập nhật:

## Bằng chứng

| AC | Đạt / chưa đạt / chưa kiểm chứng | Bằng chứng hoặc giới hạn |
| --- | --- | --- |
| AC-01 | {Trạng thái} | {File/dòng, scenario, kết quả} |

| Lệnh và thư mục chạy | Môi trường/provider | Pass / fail / not run | Kết quả và giới hạn |
| --- | --- | --- | --- |
| {Lệnh thực tế} | {Môi trường thực tế} | {Kết quả} | {Số test nếu có; nguyên nhân nếu chưa chạy} |

## Finding và bước tiếp theo

| ID | Mở / đã sửa chờ review / đã đóng bởi reviewer | Nguyên nhân và phần sửa | Scenario / bằng chứng |
| --- | --- | --- | --- |
| F-01 | {Trạng thái} | {Nguyên nhân và caller/state liên quan} | {Test hành vi, log baseline FAIL → sau sửa PASS; hoặc giới hạn nếu chưa tái hiện/chưa chạy được} |

Với finding code: ghi source/hash của baseline và bản sau sửa, lệnh/cwd/provider và vị trí raw log đủ để reviewer tái chạy. Với thay đổi không phải sửa lỗi code, bỏ yêu cầu log FAIL trước sửa. Không nhận “đã đóng” thay reviewer hoặc cộng kết quả của các test rời nhau thành scenario tích hợp chưa chạy.

- Blocker còn lại và điều kiện gỡ:
- Các cách sửa đã thử nhưng thất bại, giả thuyết mới (nếu có):
- Đề xuất ngoài phạm vi, không chặn nghiệm thu (nếu có):
- Kết luận của reviewer và bằng chứng (chỉ reviewer điền):
