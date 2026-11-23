# OISMS-66-PDF — Prompt giao Antigravity xuất PDF báo cáo Final

Lập 23/11/2026. Định tuyến **GIAO**, mức **FAST** (chỉ dựng tài liệu, không chạm code sản phẩm). Trần: 1 prompt giao + 1 prompt fix.

```text
TASK OISMS-66-PDF — Xuất PDF 6 báo cáo Final (FAST, không chạm code sản phẩm)
Đọc trước: skills/claude-to-antigravity/references/antigravity.md

MỤC TIÊU
Xuất 6 file Markdown trong docs/09-personal-reports/Final/ thành PDF khổ A4, trình bày trang trọng, có logo thật:
- 01-bao-cao-tong-ket-oisms.md: báo cáo nộp thầy ThS. Nguyễn Văn Chiến -> mức trang trọng cao nhất.
- 02..06-bao-cao-*.md: 5 báo cáo cá nhân.
Kết quả ghi vào docs/09-personal-reports/Final/pdf/<cùng tên file>.pdf.

NGUỒN (đã xác minh tồn tại)
- Logo OISMS (vector, ưu tiên): src/backend/src/OISM.Application/Common/Email/oisms-logo.svg
  Màu thương hiệu: xanh #1769e0, chữ #172033.
- Logo trường UTH: src/backend/src/OISM.Application/Features/Catalog/Categories/logo_uth.png
- Ảnh sơ đồ: docs/09-personal-reports/Final/images/*.png (đường dẫn tương đối images/... trong md).
- Thông tin bìa: lấy từ YAML front matter (title, subtitle, author, instructor, date) và bảng "Thông tin" đầu mỗi file.
Chép 2 logo vào docs/09-personal-reports/Final/assets/ (logo-uth.png, oisms-logo.svg). KHÔNG xóa, KHÔNG di chuyển file gốc trong src/backend.

CÔNG CỤ
Máy không có pandoc, weasyprint, Chrome. Dựng script Node trong tmp/pdf-export/ (thư mục bị git ignore):
npm init -y && npm i markdown-it markdown-it-anchor gray-matter playwright && npx playwright install chromium
Luồng: gray-matter tách front matter -> markdown-it ra HTML -> chèn bìa + mục lục + CSS -> Playwright page.pdf({format:'A4', printBackground:true, displayHeaderFooter:true}).
Không thêm dependency vào package.json nào của repo. Font: Noto Sans (đã cài trên máy), cỡ chữ thân 11pt, giãn dòng 1.5.

YÊU CẦU TRÌNH BÀY
A. File 01 (nộp thầy):
  1. Trang bìa riêng, căn giữa, theo thứ tự: logo UTH -> "TRƯỜNG ĐẠI HỌC GIAO THÔNG VẬN TẢI THÀNH PHỐ HỒ CHÍ MINH" -> "ĐỒ ÁN MÔN HỌC CÔNG NGHỆ PHẦN MỀM" -> logo OISMS -> "BÁO CÁO TỔNG KẾT ĐỒ ÁN" -> tên đề tài (subtitle) -> "Giảng viên hướng dẫn: ThS. Nguyễn Văn Chiến" -> bảng thành viên (họ tên, vai trò; lấy ở mục 1 và 7.1 của file) -> "TP. Hồ Chí Minh, tháng 11 năm 2026".
  2. Trang mục lục tự sinh từ ## và ### (có số trang nếu làm được, tối thiểu có liên kết).
  3. Mỗi mục ## bắt đầu trang mới. "2. Lời Cảm Ơn" nằm trên một trang riêng.
  4. Bỏ dòng "# BÁO CÁO TỔNG KẾT ĐỒ ÁN OISMS" trong thân bài (bìa đã có), không in YAML thô.
B. File 02..06: bìa gọn (logo UTH + logo OISMS, "BÁO CÁO CÁ NHÂN", họ tên, vai trò, GVHD, tháng 11/2026) + mục lục; ## không bắt buộc sang trang.
C. Chung cho cả 6 file:
  - Header (trừ bìa): logo OISMS nhỏ bên trái + tên báo cáo bên phải. Footer: "Trang x / y" căn giữa.
  - Tiêu đề màu #1769e0; bảng rộng 100%, hàng tiêu đề nền xám nhạt, viền mảnh, không cắt ngang một hàng qua hai trang, chữ trong ô tự xuống dòng (kể cả `code`).
  - Ảnh: căn giữa, rộng tối đa 100% lề, không tách qua hai trang, chú thích bên dưới "Hình N. <alt>" đánh số liên tục trong từng file.
  - Khối trích dẫn câu hỏi/đáp giữ thụt lề rõ ràng; không có chữ hoặc bảng tràn lề phải.
  - Lề A4: trên/dưới 2 cm, trái 2,5 cm, phải 2 cm.

LUẬT CỨNG
- KHÔNG sửa nội dung bất kỳ file .md nào. Thấy lỗi nội dung/chính tả thì ghi vào bàn giao, không tự sửa.
- KHÔNG thêm thông tin không có trong file (MSSV, lớp, khoa, số liệu, chữ ký). Thiếu thì bỏ trống dòng đó, không bịa.
- KHÔNG commit/push, KHÔNG chạy docker, KHÔNG chạm src/ ngoài việc đọc 2 file logo.

TIÊU CHÍ NGHIỆM THU (tự kiểm, ghi lệnh + kết quả vào bàn giao)
AC1 Có đủ 6 PDF trong Final/pdf/; `pdfinfo` ghi Page size A4 và số trang từng file.
AC2 `pdftotext <file> - | grep -nE '^\s*---|\*\*|\| *:?-{3}|^title:'` không ra dòng nào (không lọt cú pháp Markdown/YAML).
AC3 `pdfimages -list` cho thấy đủ 7 ảnh sơ đồ + logo ở mỗi file; `pdffonts` cho thấy Noto Sans được nhúng (emb = yes).
AC4 `pdftotext` giữ đúng dấu tiếng Việt (kiểm vài cụm: "Giảng viên hướng dẫn", "Nguyễn Văn Chiến", "Sổ cái").
AC5 Ảnh xem trước bằng `pdftoppm -png -r 60` cho: bìa + mục lục + 1 trang có bảng rộng + 1 trang có sơ đồ, của file 01 và file 02; lưu tmp/pdf-export/preview/. Tự xem: không tràn lề, không trang trắng thừa.
AC6 `git status --short` chỉ có file mới trong docs/09-personal-reports/Final/assets/ và Final/pdf/ (tmp/ bị ignore); không file .md nào bị sửa.

BÀN GIAO: ghi tmp/claude-anti/OISMS-66-PDF/handoff.md theo skills/claude-to-antigravity/assets/handoff.md: lệnh đã chạy, số trang từng PDF, đường dẫn ảnh xem trước, lỗi nội dung phát hiện (nếu có), PASS/FAIL từng AC. Trạng thái tự ghi: VERIFIED nếu đủ 6 AC (task FAST).
```
