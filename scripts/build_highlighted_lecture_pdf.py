import subprocess
import os

html_content = """<!DOCTYPE html>
<html lang="vi">
<head>
<meta charset="UTF-8">
<title>TOÀN VĂN GHI ÂM CHI TIẾT LỜI GIẢNG VIÊN DẶN DÒ — HỌC PHẦN CÔNG NGHỆ PHẦN MỀM UTH</title>
<style>
  @page {
    size: A4;
    margin: 15mm 15mm 15mm 15mm;
    @top-right {
      content: "Công nghệ Phần mềm (122005) - UTH";
      font-size: 8.5pt;
      color: #64748b;
      font-style: italic;
    }
    @bottom-right {
      content: "Trang " counter(page);
      font-size: 9pt;
      color: #64748b;
      font-weight: bold;
    }
    @bottom-left {
      content: "Văn bản Ghi âm Lời Thầy dặn & Đánh dấu Trọng tâm";
      font-size: 8.5pt;
      color: #64748b;
    }
  }

  body {
    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Times New Roman", Times, serif;
    color: #1e293b;
    line-height: 1.5;
    font-size: 9.5pt;
    background: #fff;
    margin: 0;
    padding: 0;
  }

  /* Header UTH */
  .header-box {
    text-align: center;
    border-bottom: 2.5px solid #1e3a8a;
    padding-bottom: 8px;
    margin-bottom: 12px;
  }
  .header-box h4 {
    margin: 0;
    font-size: 10pt;
    font-weight: 600;
    color: #475569;
    text-transform: uppercase;
    letter-spacing: 0.5px;
  }
  .header-box h2 {
    margin: 3px 0 6px 0;
    font-size: 13.5pt;
    font-weight: 800;
    color: #1e3a8a;
    text-transform: uppercase;
  }
  .header-box .doc-title {
    font-size: 12pt;
    font-weight: 700;
    color: #dc2626;
    text-transform: uppercase;
    margin: 4px 0 2px 0;
  }
  .header-box .doc-desc {
    font-size: 8.5pt;
    font-style: italic;
    color: #64748b;
    margin: 0;
  }

  /* Headings */
  h2.part-header {
    font-size: 11pt;
    color: #0f172a;
    background: #f1f5f9;
    border-left: 5px solid #2563eb;
    padding: 5px 10px;
    margin: 14px 0 8px 0;
    text-transform: uppercase;
    font-weight: 800;
    page-break-after: avoid;
  }

  h3.section-header {
    font-size: 10.5pt;
    color: #1e40af;
    border-bottom: 1px solid #cbd5e1;
    padding-bottom: 2px;
    margin: 10px 0 5px 0;
    font-weight: 700;
    page-break-after: avoid;
  }

  p {
    margin: 4px 0;
    text-align: justify;
  }

  ul, ol {
    margin: 3px 0 6px 0;
    padding-left: 18px;
  }

  li {
    margin-bottom: 3px;
    text-align: justify;
  }

  hr {
    border: none;
    border-top: 1px dashed #cbd5e1;
    margin: 10px 0;
  }

  /* Badges & Tags */
  .badge {
    display: inline-block;
    padding: 1.5px 6px;
    border-radius: 4px;
    font-size: 7.5pt;
    font-weight: 800;
    text-transform: uppercase;
    margin-right: 4px;
    vertical-align: middle;
  }
  .badge-danger {
    background-color: #fee2e2;
    color: #991b1b;
    border: 1px solid #f87171;
  }
  .badge-warning {
    background-color: #fef3c7;
    color: #92400e;
    border: 1px solid #fcd34d;
  }
  .badge-success {
    background-color: #dcfce7;
    color: #166534;
    border: 1px solid #86efac;
  }
  .badge-info {
    background-color: #e0f2fe;
    color: #075985;
    border: 1px solid #7dd3fc;
  }
  .badge-purple {
    background-color: #f3e8ff;
    color: #6b21a8;
    border: 1px solid #d8b4fe;
  }

  /* Highlights */
  .hl-yellow {
    background-color: #fef08a;
    padding: 0 3px;
    border-radius: 2px;
    font-weight: 600;
  }
  .hl-red {
    background-color: #fee2e2;
    color: #b91c1c;
    padding: 0 3px;
    border-radius: 2px;
    font-weight: 700;
  }
  .hl-green {
    background-color: #dcfce7;
    color: #15803d;
    padding: 0 3px;
    border-radius: 2px;
    font-weight: 600;
  }
  .hl-blue {
    background-color: #e0f2fe;
    color: #0369a1;
    padding: 0 3px;
    border-radius: 2px;
    font-weight: 600;
  }

  /* Callout Boxes */
  .box-alert-danger {
    background-color: #fef2f2;
    border: 1.5px solid #ef4444;
    border-left: 6px solid #dc2626;
    border-radius: 5px;
    padding: 8px 12px;
    margin: 8px 0;
    page-break-inside: avoid;
  }

  .box-alert-warning {
    background-color: #fffbeb;
    border: 1.5px solid #f59e0b;
    border-left: 6px solid #d97706;
    border-radius: 5px;
    padding: 8px 12px;
    margin: 8px 0;
    page-break-inside: avoid;
  }

  .box-alert-focus {
    background-color: #f0fdf4;
    border: 1.5px solid #22c55e;
    border-left: 6px solid #16a34a;
    border-radius: 5px;
    padding: 8px 12px;
    margin: 8px 0;
    page-break-inside: avoid;
  }

  .box-alert-career {
    background-color: #faf5ff;
    border: 1.5px solid #a855f7;
    border-left: 6px solid #7e22ce;
    border-radius: 5px;
    padding: 8px 12px;
    margin: 8px 0;
    page-break-inside: avoid;
  }

  .avoid-break {
    page-break-inside: avoid;
  }

  code {
    font-family: SFMono-Regular, Consolas, "Liberation Mono", Menlo, monospace;
    font-size: 8.5pt;
    background-color: #f1f5f9;
    padding: 1px 4px;
    border-radius: 3px;
    border: 1px solid #cbd5e1;
    color: #0f172a;
  }
</style>
</head>
<body>

<!-- HEADER TRƯỜNG -->
<div class="header-box">
  <h4>TRƯỜNG ĐẠI HỌC GIAO THÔNG VẬN TẢI TP. HỒ CHÍ MINH (UTH)</h4>
  <h2>KHOA CÔNG NGHỆ THÔNG TIN — BỘ MÔN CÔNG NGHỆ PHẦN MỀM</h2>
  <div class="doc-title">TOÀN VĂN GHI ÂM LỜI GIẢNG VIÊN DẶN DÒ & ĐÁNH DẤU TRỌNG TÂM</div>
  <p class="doc-desc">Học phần: Công nghệ Phần mềm (122005) | Bảo lưu nguyên văn 100% nội dung ghi âm chi tiết — Trực quan hóa các cảnh báo kỷ luật, quy chuẩn kỹ thuật và trọng tâm vấn đáp</p>
</div>

<p style="font-size: 9pt; font-style: italic; color: #475569; margin-bottom: 10px;">
  Toàn bộ nội dung và thông tin bài giảng trong đoạn ghi âm được ghi lại chi tiết, đầy đủ và chuẩn xác theo từng phần như sau:
</p>

<!-- PHẦN I: TỔNG QUAN NỘI DUNG GHI ÂM (PHẦN ĐẦU) -->

<h2 class="part-header">PHẦN I: HƯỚNG DẪN ĐẶC TẢ YÊU CẦU & THIẾT KẾ HỆ THỐNG PHÒNG KHÁM NHA KHOA</h2>

<h3 class="section-header">### 1. Quản lý phiên bản, kiểm soát thay đổi và Baseline (3 tuần đầu)</h3>
<ul>
  <li><span class="badge badge-warning">Quy tắc nhóm</span> <strong>Làm việc trên nền tảng chung:</strong> Các nhóm phải <span class="hl-yellow">làm việc trên cùng một nền tảng thống nhất</span>, tổ chức các buổi họp nhóm và họp review định kỳ.</li>
  <li><span class="badge badge-info">Kiểm tra</span> <strong>Xác nhận bản thảo:</strong> Cần kiểm tra và <span class="hl-yellow">xác nhận lại toàn bộ các bản thảo đặc tả yêu cầu</span> đã được lập bởi các thành viên trong nhóm.</li>
  <li><span class="badge badge-danger">Bắt buộc</span> <strong>Quản lý phiên bản (Versioning) & Truy vết (Traceability):</strong> Khi có bất kỳ thay đổi nào phát sinh, <span class="hl-red">bắt buộc phải tạo và phân chia các version</span> để dễ dàng truy vết lại lịch sử thay đổi một cách logic.</li>
  <li>Đây là các <span class="hl-yellow">yêu cầu cốt lõi bắt buộc</span> phải hoàn thiện trong giai đoạn 3 tuần này.</li>
  <li>Trong phần slide (Chương 2 về phân tích và thiết kế trên trang môn học), giảng viên đã cung cấp các nội dung baseline liên quan để sinh viên tải về tham khảo.</li>
</ul>

<hr>

<h3 class="section-header">### 2. Phân tích chức năng thực tế: Quản lý lịch hẹn phòng khám nha khoa</h3>
<ul>
  <li><span class="badge badge-focus">Cốt lõi</span> <strong>Yêu cầu cốt lõi:</strong> Lịch hẹn phải đảm bảo tính <span class="hl-green">đáng tin cậy</span>. Để giải quyết câu hỏi <em>"thế nào là đáng tin cậy"</em>, người phân tích phải mô hình hóa hệ thống thông qua việc xác định rõ các tác nhân (Actors) tham gia tương tác:</li>
  <li><strong>Lễ tân (Receptionist):</strong> Đóng vai trò là người tiếp nhận, thiết lập (đặt) và thay đổi lịch hẹn.</li>
  <li><strong>Nha sĩ (Dentist):</strong> Đưa ra lịch biểu làm việc khả thi của mình hoặc đăng ký các ca làm việc với phòng khám.</li>
  <li><strong>Bệnh nhân (Patient):</strong> Xem hệ thống lịch (Calendar), tiến hành đăng ký lịch khám và theo dõi trạng thái lịch hẹn của mình. <span class="hl-yellow">Khi bệnh nhân đăng ký thành công, hệ thống phải hiển thị rõ thông tin lịch hẹn gồm ngày, giờ cụ thể và đích danh người khám.</span></li>
</ul>

<hr>

<h3 class="section-header">### 3. Quy trình chuyển đổi trạng thái (State Transition) của Lịch hẹn</h3>
<p>Mỗi hành động của từng Actor sẽ tác động trực tiếp làm thay đổi trạng thái của lịch hẹn:</p>
<ol>
  <li><strong>Khởi tạo:</strong> Khi lịch hẹn được đăng ký ban đầu (qua lễ tân hoặc bệnh nhân), lịch ở trạng thái <strong>Tạo mới</strong> (đã được tạo/confirm bước đầu).</li>
  <li><strong>Xác nhận nội bộ:</strong> Lễ tân có nhiệm vụ xác nhận (confirm) lại thông tin lịch hẹn với nha sĩ để chuyển trạng thái từ <strong>Tạo mới</strong> sang <strong>Sẵn sàng</strong>.</li>
  <li><strong>Nha sĩ tiếp nhận:</strong> Nha sĩ vào hệ thống xác nhận lịch đó và sẵn sàng cho ca khám, làm thay đổi trạng thái lịch hẹn.</li>
  <li><strong>Thanh toán & Kết thúc:</strong> Sau khi buổi khám diễn ra, người dùng xác nhận và hoàn tất thanh toán chi phí khám, trạng thái lịch hẹn chuyển sang <strong>Hoàn thành / Kết thúc</strong>.</li>
</ol>
<p style="font-style: italic; color: #1e40af;">
  * Một chức năng tưởng chừng đơn giản như "Đặt lịch" thực chất là một quy trình (process) liên tục có sự tương tác và làm thay đổi trạng thái bởi 3 Actor chính.
</p>

<hr>

<h3 class="section-header">### 4. Thiết kế hệ thống: Cấu trúc màn hình, Happy Case và Unhappy Case</h3>

<div class="box-alert-focus">
  <span class="badge badge-success">Mô hình hóa</span> <strong>Mô hình hóa hệ thống & cấu trúc màn hình:</strong> Không thể chỉ có duy nhất một màn hình đặt lịch, mà phải <span class="hl-yellow">thiết kế đồng bộ nhiều màn hình phụ trợ</span>: màn hình tạo lịch, màn hình cho bác sĩ đăng ký ca trực, màn hình cho khách hàng theo dõi danh sách lịch hẹn... tạo thành một hệ thống quy tắc và quy trình hoàn chỉnh.
</div>

<div class="box-alert-warning">
  <span class="badge badge-warning">Trọng tâm</span> <strong>Phân biệt Happy Case và Unhappy Case:</strong>
  <ul>
    <li><strong>Happy Case (Luồng lý tưởng):</strong> Quy trình diễn ra trơn tru từ đầu đến cuối mà không gặp bất kỳ sự cố nào.</li>
    <li><strong style="color: #b91c1c;">Unhappy Case (Luồng ngoại lệ / sự cố) - Bắt buộc phải xử lý:</strong>
      <ul>
        <li><em>Trường hợp khách hàng đặt lịch nhưng không đến (No-show):</em> Hệ thống phải xử lý trạng thái lịch và giải phóng tài nguyên ra sao?</li>
        <li><em>Trường hợp nha sĩ bận đột xuất hoặc gặp sự cố không thể tham gia ca khám:</em> Hệ thống có cho phép đổi sang nha sĩ khác không và luồng dữ liệu chuyển đổi như thế nào?</li>
      </ul>
    </li>
    <li><span class="hl-red">Người viết yêu cầu phải lường trước tất cả các kịch bản ngoại lệ này để đưa ra các ràng buộc cụ thể trong tài liệu đặc tả.</span></li>
  </ul>
</div>

<ul>
  <li><strong>Quy tắc hiển thị khung giờ (Slot):</strong>
    <ul>
      <li>Những khung giờ còn trống (Available) thì cho phép người dùng click chọn và đăng ký.</li>
      <li>Những khung giờ đã có người đặt thì <span class="hl-yellow">giao diện phải phản hồi rõ là đã được đăng ký, không cho bấm chọn tiếp</span>, giúp loại bỏ các lỗi thao tác của người dùng.</li>
    </ul>
  </li>
</ul>

<hr>

<h3 class="section-header">### 5. Cấu trúc chuyển hóa yêu cầu phần mềm (Requirement Hierarchy)</h3>
<p>Quá trình phân tích yêu cầu phải đi theo <span class="hl-yellow">mô hình từ trên xuống (Top-down approach)</span>:</p>
<ul>
  <li><strong>Business Requirement (Yêu cầu nghiệp vụ):</strong> Mô tả bức tranh nghiệp vụ tổng quan mà hệ thống cần phục vụ (ví dụ: nghiệp vụ quản lý và đăng ký khám nha khoa).</li>
  <li><strong>Business Rules / Policy (Quy tắc nghiệp vụ):</strong> Các quy định, chính sách bắt buộc tác động trực tiếp vào Business Requirement nhằm siết chặt nghiệp vụ và đảm bảo tính kín kẽ.</li>
  <li><strong>Scope Document (Phạm vi dự án):</strong> Từ Business Requirement và Business Rule hình thành nên tổng quan phạm vi dự án, cần được làm rõ và ghi lại thật chi tiết.</li>
  <li><strong>User Requirements (Yêu cầu người dùng):</strong> Được xây dựng dựa trên Scope Document và Business Rule. Mục này phải <span class="hl-yellow">chỉ rõ từng Actor khi vào hệ thống sẽ được phép làm gì, có quyền hạn nào và tương tác với hệ thống qua những bước cụ thể nào.</span></li>
  <li><strong>Functional Requirements (Yêu cầu chức năng):</strong> Chi tiết hóa toàn bộ các chức năng mà hệ thống phải xây dựng để người dùng thực hiện.</li>
  <li><strong>Non-functional Requirements / System Requirements (Yêu cầu phi chức năng / Hệ thống):</strong> Những yếu tố kỹ thuật mà người dùng không nhìn thấy trực tiếp trên giao diện nhưng hệ thống bắt buộc phải đáp ứng:
    <ul>
      <li>Tốc độ truy cập và phản hồi của hệ thống.</li>
      <li><span class="hl-red">Khả năng chịu tải khi có nhiều người cùng truy cập tại một thời điểm (Concurrency).</span></li>
      <li>Khả năng sao lưu dữ liệu (Backup) và phục hồi hệ thống khi có sự cố (Recovery / Disaster Recovery).</li>
    </ul>
  </li>
</ul>

<hr>

<h3 class="section-header">### 6. Thiết kế giao diện (UI) và ứng dụng AI / Agent</h3>
<ul>
  <li><span class="badge badge-info">Công cụ</span> <strong>Công cụ thiết kế khuyến nghị:</strong> Sử dụng <strong>Figma</strong> (<code>figma.com</code>) để thiết kế giao diện dựa trên danh sách các yêu cầu đã bóc tách.</li>
  <li><strong>Kết nối giữa Figma và Frontend:</strong> Sau khi thiết kế xong trên Figma, có thể xuất giao diện sang mã nguồn HTML hoặc React.</li>
  <li><strong>Ứng dụng AI/Agent trong lập trình:</strong> Có thể sử dụng AI Agent để đọc bản thiết kế/tài liệu và lập trình giao diện một cách nhanh chóng.</li>
</ul>

<hr>

<h3 class="section-header">### 7. Bản chất môn học Công nghệ phần mềm và quy chuẩn tài liệu</h3>
<div class="box-alert-focus">
  <span class="badge badge-success">Mục tiêu môn</span> <strong>Mục tiêu môn học:</strong> Môn Công nghệ phần mềm <span class="hl-red">không yêu cầu sinh viên phải "trâu bò" cắm đầu vào viết code</span>, mà đòi hỏi sự "trâu bò" trong việc <span class="hl-yellow">làm rõ yêu cầu (Requirements) và bóc tách cấu trúc hệ thống</span> một cách chặt chẽ.
</div>

<ul>
  <li><span class="badge badge-danger">Quy chuẩn Git</span> <strong>Định dạng tài liệu nộp:</strong>
    <ul>
      <li>Tài liệu đính kèm trong mã nguồn đẩy lên Git <span class="hl-red">phải được viết dưới dạng các file Markdown (`.md`)</span>, không dùng định dạng văn bản thông thường của các môn học trước.</li>
      <li>Nội dung trong Markdown phải mô tả chi tiết, rõ ràng để AI / Agent có thể đọc hiểu và thực thi công việc được.</li>
      <li>Sinh viên có thể tự viết tài liệu, sau đó nhờ AI review và sửa lại trước khi đưa cho agent triển khai.</li>
    </ul>
  </li>
  <li><span class="badge badge-info">Sản phẩm cốt lõi</span> <strong>Sản phẩm cốt lõi:</strong> Kết quả cuối cùng của chương này là bộ tài liệu đặc tả yêu cầu <span class="hl-yellow">SRS (Software Requirement Specification) hoàn chỉnh</span>, bao gồm đầy đủ các ràng buộc nghiệp vụ (Business Rules) để đảm bảo hệ thống vận hành kín kẽ và chính xác.</li>
</ul>

<!-- PHẦN II: NGUYÊN TẮC LÀM VIỆC NHÓM & QUY CHẾ VẤN ĐÁP KỶ LUẬT -->

<h2 class="part-header" style="border-left-color: #dc2626; background: #fff1f2;">PHẦN II: NGUYÊN TẮC LÀM VIỆC NHÓM, ĐỊNH HƯỚNG VẤN ĐÁP & QUY CHẾ ĐIỂM LIỆT TỪ TUẦN SAU</h2>

<h3 class="section-header" style="color: #991b1b;">### 1. Nguyên tắc phân công công việc và đánh giá sự hợp tác trong nhóm</h3>

<div class="box-alert-danger">
  <span class="badge badge-danger">Nghiêm cấm</span> <strong>Bóc tách chi tiết công việc:</strong> <span class="hl-red">Nghiêm cấm tuyệt đối việc chia việc theo kiểu "người A chỉ viết tài liệu (docs), người B làm slide/video, người C viết test...".</span> Toàn bộ khối lượng công việc bắt buộc phải được bóc tách thành từng phần, phân rã đến từng chi tiết nhỏ và giao trách nhiệm cụ thể đến từng thành viên.
</div>

<ul>
  <li><strong>Tính logic trong phân chia đầu việc:</strong> Sự phân công cho từng thành viên phải có tính logic chặt chẽ. <span class="hl-red">Nếu phân công thiếu logic, giảng viên sẽ đánh giá nhóm không hiểu và không làm được việc, làm việc một cách hời hợt và thiếu sự phối hợp.</span> Nhóm phân công cẩu thả sẽ phải tự chịu trách nhiệm khi bị xử lý kỷ luật hoặc phạt vấn đáp.</li>
  <li><strong>Trách nhiệm tập thể:</strong> Dù có phân công riêng, <span class="hl-yellow">giảng viên sẽ kiểm tra và vấn đáp cả nhóm</span>. Không chấp nhận tình trạng trong nhóm chỉ có một người làm hoặc cả nhóm bỏ mặc không ai làm.</li>
</ul>

<hr>

<h3 class="section-header" style="color: #991b1b;">### 2. Định hướng đánh giá và vấn đáp môn học: Tập trung vào Nghiệp vụ (Business)</h3>

<div class="box-alert-focus">
  <span class="badge badge-success">Định hướng thi</span> <strong>Trọng tâm là giải quyết bài toán nghiệp vụ:</strong> Giảng viên sẽ <span class="hl-green">xoáy sâu vào bản chất nghiệp vụ (business)</span>. Cụ thể, giảng viên sẽ đặt ra một vấn đề/sự cố thực tế (problem) và yêu cầu sinh viên phải trình bày rõ phương án giải quyết bài toán đó ra sao.
</div>

<ul>
  <li><strong>Không kiểm tra kỹ thuật lập trình cơ bản:</strong> Giảng viên sẽ không đi sâu vào các vấn đề kỹ thuật thuần túy (technical) như: đoạn code này viết như thế nào, chức năng này gọi ra sao, hay bắt xóa code đi để gõ lại. Giảng viên coi các bài kiểm tra gõ code này là cấp độ cơ bản (<em>"đồ trẻ con"</em>).</li>
  <li><strong>Quan điểm về việc viết code và AI:</strong> Việc sinh code hiện nay đã có các công cụ AI hỗ trợ thực hiện, do đó giảng viên không quá bận tâm đến việc gõ code thủ công. <span class="hl-yellow">Giá trị cốt lõi của người học nằm ở tư duy phân tích và thiết kế nghiệp vụ.</span></li>
</ul>

<hr>

<h3 class="section-header" style="color: #991b1b;">### 3. Yêu cầu nghiệp vụ (Business Requirements) và xử lý kịch bản ngoại lệ (Unhappy Cases)</h3>
<ul>
  <li><strong>Bản chất của Business Requirement:</strong> Là kết quả của quá trình vận hành nghiệp vụ và là lý do cần phải cải tiến, thay đổi. Khi nghiệp vụ thay đổi, toàn bộ các thành phần liên quan trong hệ thống cũng sẽ phải thay đổi theo.</li>
  <li><strong>Giải quyết xung đột nghiệp vụ (Conflict Handling):</strong> Nghiệp vụ thực tế phát sinh thay đổi liên tục. Hệ thống phải đưa ra các giải pháp để giảm thiểu tối đa các xung đột phát sinh trong quá trình vận hành.</li>
</ul>

<div class="box-alert-warning">
  <span class="badge badge-warning">Bài toán ví dụ</span> <strong>Bài toán ví dụ về đăng ký lịch hẹn / giữ chỗ:</strong>
  <ul>
    <li><em>Tình huống đặt ra:</em> Trong cùng một thời điểm, có nhiều người (ông A, ông B, ông C) cùng truy cập vào một khung giờ (slot) đang trống và cùng gửi yêu cầu đăng ký. Nếu không có giải pháp xử lý, hệ thống sẽ bị lỗi trùng lịch. Hệ thống phải xác định rõ tiêu chí giải quyết cho ai và từ chối ai.</li>
    <li><em>Giải pháp thực tế (tương tự mô hình đặt vé tàu, vé máy bay):</em> <span class="hl-yellow">Khi người dùng click chọn slot, hệ thống phải cho phép giữ chỗ tạm thời (lock/hold) trong một khoảng thời gian nhất định (khoảng 5 đến 10 phút) kể cả khi họ chưa hoàn tất việc thanh toán hay nhập form.</span> Nếu sau 5–10 phút mà người đó không hoàn tất đơn, slot này sẽ tự động được mở khóa (unlock) để giải phóng cho người khác đăng ký. Điều này khác hoàn toàn với việc người dùng đang xem dở thì bị người khác chen ngang đăng ký mất.</li>
  </ul>
</div>

<div class="box-alert-danger">
  <span class="badge badge-danger">Trọng tâm vấn đáp</span> <strong>Hình thức vấn đáp trọng tâm:</strong> Khi vấn đáp, giảng viên sẽ <span class="hl-red">chủ yếu xoáy sâu vào các Unhappy Cases</span>. Thành viên nào không tham gia trực tiếp vào quy trình làm việc nhóm thì chắc chắn sẽ không thể đưa ra được phương án xử lý.
  <br><strong>Tư duy xử lý Unhappy Cases:</strong> Chỉ khi thực sự làm việc nhóm sâu sát thì các thành viên mới phát hiện ra vô số trường hợp ngoại lệ (unhappy cases) và tình huống lỗi phát sinh cần phải xử lý. Tư duy kỹ sư nằm ở khả năng giải quyết các kịch bản bất thường này, chứ không phải chỉ dừng lại ở việc tạo ra một màn hình gồm các nút chức năng A, B, C thông thường (những thứ AI có thể tự sinh ra được).
</div>

<ul>
  <li><strong>Tiêu chí của yêu cầu thay đổi:</strong> Mọi yêu cầu thay đổi nghiệp vụ đưa ra đều phải đo lường được, thống nhất về các trạng thái chuyển đổi, xác định rõ ràng mục tiêu và mốc thời gian thực hiện.</li>
</ul>

<hr>

<h3 class="section-header" style="color: #991b1b;">### 4. Quy chế kiểm tra tiến độ và kỷ luật từ tuần sau</h3>

<div class="box-alert-danger" style="background-color: #fff1f2; border: 2px solid #b91c1c;">
  <span class="badge badge-danger" style="font-size: 8.5pt;">QUY CHẾ KỶ LUẬT ĐIỂM LIỆT — BẮT ĐẦU TỪ TUẦN SAU</span>
  <ul>
    <li><strong>Quy định đối với tuần hiện tại:</strong> Tuần này giảng viên mang tính chất nhắc nhở, có thể châm chước bỏ qua.</li>
    <li><strong style="color: #b91c1c;">Quy chế kiểm tra nghiêm ngặt từ tuần sau:</strong> Bắt đầu từ tuần sau, giảng viên sẽ kiểm tra trực tiếp từng nhóm.</li>
    <li><strong>Hình thức kiểm tra:</strong> Gọi ngẫu nhiên (random) vào đầu giờ, giữa giờ; hoặc trong quá trình giảng bài, nếu phát hiện nhóm nào, sinh viên nào mất tập trung thì sẽ gọi lên vấn đáp ngay lập tức. Nếu sinh viên được gọi không trả lời được, giảng viên sẽ tiến hành kiểm tra toàn bộ nhóm đó.</li>
    <li><strong style="color: #b91c1c; font-size: 10pt;">Mức phạt:</strong> Từ tuần sau, nếu kiểm tra mà nhóm không có tiến độ/kết quả theo yêu cầu thì sẽ <span class="hl-red">BỊ ĐÁNH ĐIỂM LIỆT CẢ NHÓM (RỚT MÔN / HỦY KẾT QUẢ)</span>.</li>
    <li><strong style="color: #b91c1c;">Quy định "không xé nháp":</strong> Giảng viên tuyên bố tuyệt đối <span class="hl-red">không có lần đầu, không có làm nháp hay thông cảm làm lại</span>, vì sinh viên đã có trọn vẹn 4 tuần chuẩn bị.</li>
    <li>Từ tuần thứ 5, thứ 6 trở đi, hoạt động kiểm tra tiến độ sẽ diễn ra liên tục qua từng tuần để kiểm soát gắt gao kết quả thực hiện của từng nhóm.</li>
  </ul>
</div>

<p style="font-style: italic;">* Thực tế triển khai so với tài liệu lý thuyết: Slide bài giảng chỉ tóm tắt 4–5 dòng ngắn gọn về baseline, nhưng khi bắt tay vào thực hiện thực tế thì đòi hỏi nỗ lực rất lớn của toàn nhóm.</p>

<!-- PHẦN III: QUY TRÌNH PHÁT TRIỂN & CƠ CẤU NHÂN SỰ -->

<h2 class="part-header" style="border-left-color: #0284c7; background: #f0f9ff;">PHẦN III: QUY TRÌNH PHÁT TRIỂN YÊU CẦU, CƠ CẤU NHÂN SỰ & THIẾT KẾ UI FIGMA</h2>

<h3 class="section-header">### 1. Quy trình phát triển yêu cầu phần mềm và các vai trò tham gia</h3>
<ul>
  <li><strong>Mức độ yêu cầu nghiệp vụ (Business Requirements):</strong> Đội ngũ quản lý (Manager) và nhân viên kinh doanh/Marketing trực tiếp làm việc với khách hàng để thu thập và làm rõ yêu cầu nghiệp vụ. Ở giai đoạn này, các yêu cầu mới chỉ dừng lại ở mức độ tổng quan (High-level) chứ chưa đi vào chi tiết kỹ thuật.</li>
  <li><strong>Mức độ yêu cầu người dùng (User Requirements):</strong> Chuyên viên phân tích nghiệp vụ (Business Analyst - BA) và Quản lý dự án (Project Manager - PM) tiếp nhận và phân tích sâu hơn. Nhiệm vụ chính là xác định rõ: <span class="hl-yellow">Ai tham gia vào hệ thống, họ thực hiện những công việc gì, làm như thế nào và tính logic chặt chẽ của các luồng nghiệp vụ ra sao.</span></li>
  <li><strong>Mức độ yêu cầu chức năng (Functional Requirements):</strong> Quá trình chuyển đổi từ yêu cầu người dùng sang yêu cầu chức năng trải qua các bước: Làm sáng tỏ toàn bộ khúc mắc (<em>"Clear" vấn đề</em>) và làm mịn, chuẩn hóa chi tiết nhất (<em>"Clean" vấn đề</em>) để sẵn sàng cho khâu lập trình (Develop). Đội ngũ lập trình viên (Developer) và kiểm thử viên (Tester) bắt đầu tham gia sâu từ bước triển khai này.</li>
  <li><span class="badge badge-info">Chu trình SRS</span> Cốt lõi của tài liệu SRS được đúc kết qua chu trình: <strong>Clear</strong> (làm rõ hết các vấn đề) &rarr; <strong>Clean</strong> (phân tích chi tiết nhất có thể) &rarr; <strong>Develop</strong> (tiến hành phát triển).</li>
</ul>

<hr>

<h3 class="section-header">### 2. Cơ cấu nhân sự dự án phần mềm và yêu cầu phân công công việc</h3>
<ul>
  <li><strong>Cơ cấu nhóm dự án tiêu chuẩn (Project Team):</strong> Một nhóm phát triển cơ bản gói gọn gồm: PM, BA, Developer và Tester. Trong các mô hình nhóm quy mô nhỏ: BA có thể kiêm nhiệm luôn vai trò Tester; Developer kiêm nhiệm luôn việc viết Unit Test.</li>
  <li><strong>Vị trí Kiến trúc sư ứng dụng / Kiến trúc sư giải pháp kỹ thuật (Application Architect / Technical Architect - thường tương đương Giám đốc công nghệ CTO):</strong> Đảm nhiệm việc định nghĩa kiến trúc hệ thống phần mềm, thường tham gia vào một giai đoạn nhất định hoặc quản lý kiến trúc xuyên suốt nhiều dự án cùng lúc.</li>
  <li>Bộ phận làm việc thường trực trong dự án gồm PM và BA (làm việc trực tiếp với khách hàng; BA là cầu nối giữa khách hàng và nhóm kỹ thuật); Developer và Tester tập trung liên tục vào công đoạn kỹ thuật.</li>
</ul>

<div class="box-alert-warning">
  <span class="badge badge-warning">Cảnh báo PM</span> <strong>Quy định phân công công việc trong nhóm sinh viên:</strong>
  <ul>
    <li>Giảng viên <span class="hl-red">nghiêm cấm tình trạng sinh viên nhận vai trò PM chỉ để "chỉ tay năm ngón", giao tiếp và phân việc</span>. Nếu giữ vị trí đó thì phải thể hiện được trình độ chuyên môn nghiệp vụ rất sâu, và khi vấn đáp giảng viên sẽ hỏi xoáy thẳng vào nghiệp vụ kinh doanh và quy trình.</li>
    <li><span class="hl-yellow">Tất cả các thành viên trong nhóm bắt buộc đều phải bắt tay vào làm việc cụ thể.</span></li>
    <li>Thời lượng môn học là 8 tuần nhưng thực tế thời gian để làm sản phẩm chỉ có khoảng 4 đến 5 tuần, do đó công việc phải được phân bổ đều và chi tiết để tránh việc chỉ một người gánh toàn bộ nhóm.</li>
  </ul>
</div>

<div class="box-alert-danger">
  <span class="badge badge-danger">Tiêu chí chấm</span> <strong>Tiêu chí chấm điểm đồ án:</strong>
  <ul>
    <li>Giảng viên sẽ <span class="hl-red">đánh giá tổng quan sản phẩm cuối cùng xem có hoàn thiện và hoạt động được hay không trước khi xét đến đóng góp của từng cá nhân</span>.</li>
    <li><span class="hl-red">Nếu sản phẩm không hoàn thành hoặc không có gì để chấm thì không xét đến các vị trí quản lý hay đóng góp chi tiết.</span></li>
  </ul>
</div>

<hr>

<h3 class="section-header">### 3. Yêu cầu chức năng và Thiết kế giao diện (UI)</h3>
<ul>
  <li><strong>Định nghĩa Yêu cầu chức năng (Functional Requirements):</strong> Functional Requirement là sự kết hợp chặt chẽ giữa các quy tắc nghiệp vụ (Business Rules / Constraints) và phản hồi tương ứng của hệ thống (System Response). Tài liệu phải mô tả rành mạch: Chức năng đó làm nhiệm vụ gì và kết quả đạt được là gì.</li>
</ul>

<div class="box-alert-focus">
  <span class="badge badge-success">Quy chuẩn UI</span> <strong>Quy chuẩn thiết kế UI trên Figma:</strong>
  <ul>
    <li><span class="hl-yellow">Toàn bộ giao diện bắt buộc phải được thiết kế trên Figma.</span></li>
    <li>Quy trình chuẩn là phải <span class="hl-green">phân tích và vẽ thiết kế giao diện trên Figma trước khi bắt đầu lập trình</span>. Giảng viên sẽ kiểm tra tiến độ thiết kế trước mã nguồn, <span class="hl-red">không chấp nhận việc viết code xong xuôi rồi mới quay lại vẽ giao diện hay viết tài liệu bổ sung</span>.</li>
    <li>Các công việc gồm phân tích, thiết kế, lập trình và kiểm thử phải được triển khai song song liên tục.</li>
  </ul>
</div>

<hr>

<h3 class="section-header">### 4. Các yêu cầu phi chức năng (Non-functional Requirements)</h3>
<ul>
  <li><strong>Hiệu năng (Performance):</strong> Người dùng không quan tâm hệ thống dùng thuật toán nào bên dưới, họ chỉ đánh giá hệ thống phản hồi nhanh đến mức nào và khả năng chịu tải được bao nhiêu người dùng đồng thời.</li>
  <li><strong>Tính sẵn sàng (Availability):</strong>
    <ul>
      <li>Đánh giá mức độ hoạt động 24/7 của hệ thống và thời gian ngừng hoạt động (downtime) khi bảo trì.</li>
      <li><em>So sánh kiến trúc:</em> Trong các hệ thống TMĐT lớn áp dụng kiến trúc Microservices, khi cần bảo trì thì hệ thống chỉ tạm dừng một vài dịch vụ cụ thể mà không làm gián đoạn toàn bộ hệ thống; ngược lại, nếu dùng kiến trúc nguyên khối (Monolithic), một dịch vụ gặp sự cố hoặc tắt đi bảo trì sẽ khiến toàn bộ hệ thống sụp đổ.</li>
    </ul>
  </li>
  <li><strong>Bảo mật (Security):</strong> Phải thiết lập các Business Rules cụ thể để giải quyết rành mạch hai câu hỏi: <span class="hl-yellow">Ai được phép truy cập vào dữ liệu nào? Ai được quyền thực thi chức năng nào?</span> Tất cả phải được mô tả kỹ lưỡng thông qua các kịch bản Use Case.</li>
  <li><strong>Mẫu tài liệu (Template):</strong> Giảng viên đã tải sẵn biểu mẫu đặc tả lên hệ thống môn học ngay từ tuần đầu tiên, sinh viên phải tự tải về nghiên cứu để làm theo thay vì hỏi lại.</li>
</ul>

<hr>

<h3 class="section-header">### 5. Ràng buộc nghiệp vụ (Business Rules), Khoảng đệm (Buffer Time) và Giữ chỗ (Locking)</h3>
<ul>
  <li><strong>Quy tắc xếp lịch phòng khám nha khoa:</strong>
    <ul>
      <li>Chính sách (Policy) quy định rõ: Một nha sĩ không thể có các lịch hẹn bị trùng nhau, trong một khung giờ (slot) không thể tiếp nhận đồng thời hai bệnh nhân.</li>
      <li><span class="hl-yellow">Bắt buộc phải có khoảng đệm thời gian trống (Buffer Time) giữa các slot (khoảng 15 đến 30 phút)</span> để dự phòng trường hợp ca khám trước kéo dài vượt giờ, tránh làm ảnh hưởng hoặc đè lên ca khám tiếp theo.</li>
    </ul>
  </li>
  <li><strong>Hành vi xử lý của hệ thống (System Behaviors):</strong>
    <ul>
      <li>Hệ thống phải có cơ chế từ chối các yêu cầu đặt lịch không hợp lệ hoặc bị xung đột.</li>
      <li>Phải thực hiện kiểm thử biên (Boundary test) tại các điểm giới hạn trên và dưới của từng slot giờ.</li>
    </ul>
  </li>
  <li><strong>Giao diện trao đổi và Cơ chế giữ chỗ tạm thời (Holding Slot):</strong>
    <ul>
      <li>Tương tự cơ chế của các hệ thống bán vé máy bay hoặc vé xem phim: Khi người dùng click chọn một slot giờ, hệ thống phải kích hoạt trạng thái giữ chỗ tạm thời trong một khoảng thời gian nhất định (ngay cả khi người dùng chưa thanh toán) để ngăn chặn người khác đặt đè lên.</li>
      <li>Kịch bản xử lý này và quy trình kiểm thử phải được thể hiện chi tiết trên bản vẽ mẫu (prototype) của Figma.</li>
    </ul>
  </li>
</ul>

<hr>

<h3 class="section-header">### 6. Ý thức học tập của sinh viên và ứng dụng công cụ</h3>
<ul>
  <li>Sinh viên năm 2, năm 3 (khoảng 19 - 20 tuổi) đã ở độ tuổi trưởng thành, cần chủ động nghiên cứu và thực hành nhiều.</li>
  <li>Các công cụ phần mềm hiện nay (như Figma) đều hỗ trợ thao tác kéo thả, tạo liên kết prototype và đã tích hợp nhiều tính năng hỗ trợ từ AI.</li>
  <li><span class="hl-red">Giảng viên không chấp nhận các lý do như "em không biết" hay than vãn khó khăn</span>; sinh viên không làm được chỉ vì chưa thực sự dành thời gian để nghiên cứu và thực hiện.</li>
</ul>

<hr>

<h3 class="section-header">### 7. Cấu trúc môn học, Thang điểm và Đánh giá kết quả</h3>
<div class="box-alert-danger">
  <span class="badge badge-danger">Thang điểm</span> <strong>Cơ cấu điểm số & Thang điểm:</strong>
  <ul>
    <li>Điểm thi kết thúc học phần chỉ chiếm <strong>30%</strong> tổng điểm.</li>
    <li><strong style="color: #b91c1c; font-size: 10pt;">Điểm quá trình chiếm tỷ trọng lớn (70%) và phụ thuộc hoàn toàn vào kết quả thực hiện Đồ án (Project).</strong></li>
    <li><span class="hl-red">Kể cả khi thi lý thuyết đạt 10 điểm nhưng điểm quá trình bị đánh giá thấp (ví dụ 4.5, 4.8 hoặc 5.5 do không tham gia làm đồ án nghiêm túc) thì vẫn bị trượt môn.</span> Nếu nhóm làm việc đàng hoàng, nghiêm túc thì giảng viên sẽ đánh giá đúng năng lực chứ không đánh điểm thấp.</li>
  </ul>
</div>

<ul>
  <li><strong>Tài liệu và Ngôn ngữ:</strong> Slide bài giảng sử dụng hoàn toàn bằng tiếng Anh để bám sát đề cương chi tiết và các giáo trình tham khảo chuẩn đã được nhà trường công bố.</li>
  <li><strong>Tiến độ giảng dạy:</strong> Theo đề cương (Syllabus), chương này được bố trí dạy trong 3 buổi. Tiến độ giữa các lớp được giảng viên cân đối đồng bộ về số lượng slide và khối lượng bài tập để đảm bảo tính công bằng giữa các nhóm.</li>
</ul>

<hr>

<h3 class="section-header">### 8. Xử lý kịch bản ngoại lệ (Unhappy Cases) thông qua Hệ thống thông báo (Notification)</h3>
<ul>
  <li>Các ràng buộc hệ thống cần có sự kết hợp của cơ chế gửi thông báo (Notification / Alert).</li>
  <li><strong>Tình huống thực tế:</strong> Khi bệnh nhân đặt lịch thành công, hệ thống cần gửi thông báo nhắc nhở trước ngày/giờ khám. Nếu bệnh nhân quên và không đến khám, lịch hẹn sẽ rơi vào kịch bản ngoại lệ (Unhappy Case) khiến ca khám bị thất bại (Failed) và lịch bị hủy bỏ.</li>
  <li><span class="hl-green">Việc thiết lập cơ chế thông báo tự động (tương tự các ứng dụng di động như MoMo hay hệ thống lịch trên điện thoại) là giải pháp nghiệp vụ then chốt để hạn chế tối đa các Unhappy Cases xảy ra trong quá trình vận hành hệ thống.</span></li>
</ul>

<!-- PHẦN IV: QUẢN LÝ MÃ NGUỒN, GIAO TIẾP VÀ TÂM SỰ NGHỀ NGHIỆP -->

<h2 class="part-header" style="border-left-color: #7e22ce; background: #faf5ff;">PHẦN IV: QUẢN LÝ MÃ NGUỒN, TÀI LIỆU VÀ BÀI TOÁN SINH TỒN NGHỀ NGHIỆP</h2>

<h3 class="section-header">### 1. Kiến trúc phần mềm và quản lý mã nguồn (Backend & Frontend)</h3>
<ul>
  <li><strong>Thiết kế kiến trúc hệ thống:</strong> Trong chương Software Design, giảng viên sẽ hướng dẫn các mô hình kiến trúc phần mềm phổ biến. Sinh viên có nhiệm vụ nghiên cứu sâu về các kiến trúc này và <span class="hl-yellow">tạo một file `README.md` riêng cho phần Backend</span>.</li>
  <li><strong>Đặc tả Frontend:</strong> Tương tự Backend, sinh viên cần xác định rõ Frontend sử dụng kiến trúc gì, công nghệ nào và <span class="hl-yellow">mô tả cụ thể vào một file `README.md` của Frontend</span>. Thao tác này chỉ mất khoảng 5 phút để hoàn thành, trọng tâm là phải định nghĩa (define) thật chi tiết.</li>
  <li><strong>Giao tiếp giữa Backend và Frontend:</strong> Sinh viên phải làm rõ giao thức kết nối (giao thức HTTP) và viết các script mẫu để hệ thống chạy và thực thi từng bước một.</li>
  <li><strong>Lý do phân chia công việc trong nhóm:</strong> Mỗi sinh viên đều bị giới hạn về lượng token khi sử dụng các công cụ AI (token không phải vô hạn), do đó một người không thể tự chạy hết mọi tác vụ mà <span class="hl-red">bắt buộc cả nhóm phải chia nhỏ đầu việc để cùng làm</span>.</li>
</ul>

<hr>

<h3 class="section-header">### 2. Bóc tách yêu cầu và quản lý tài liệu</h3>
<ul>
  <li><strong>Đóng góp của sinh viên:</strong> Phần đóng góp lớn nhất của sinh viên là phải viết các yêu cầu (Requirements) thật chi tiết.</li>
  <li><strong>Đồng bộ hóa tài liệu:</strong> Từ các yêu cầu chi tiết đã phân tích, sinh viên phải cập nhật ngược lại vào hệ thống tài liệu trên nền tảng Confluence.</li>
  <li><span class="badge badge-info">Cấu trúc Docs</span> <strong>Cấu trúc thư mục Docs:</strong> <span class="hl-yellow">Trong thư mục `Docs/` của dự án phải chứa nhiều file `README.md` bóc tách từng phần. Mỗi khi có sự thay đổi, bắt buộc phải cập nhật và đánh số phiên bản (version) liên tục chứ không phải chỉ viết một lần là xong.</span></li>
  <li><strong>Quy trình kết hợp với AI:</strong> Sinh viên nên tự tay viết bản thảo yêu cầu trước, sau đó mới dùng AI để tinh chỉnh (refine) lại câu chữ theo đúng ý đồ của mình. <span class="hl-red">Tuyệt đối không phó mặc hoàn toàn cho AI viết từ đầu đến cuối, vì nếu không trực tiếp làm thì đến lúc vấn đáp sẽ không hiểu gì để trả lời.</span></li>
</ul>

<hr>

<h3 class="section-header">### 3. Quy chuẩn vấn đáp môn học</h3>
<div class="box-alert-focus">
  <span class="badge badge-success">Quy chuẩn thi</span> <strong>Trọng tâm kiểm tra vấn đáp:</strong>
  <ul>
    <li>Giảng viên khẳng định <span class="hl-green">chỉ tập trung vấn đáp sâu về mặt nghiệp vụ (Business) của hệ thống</span>.</li>
    <li><span class="hl-yellow">Không kiểm tra kỹ thuật lập trình:</span> Giảng viên sẽ không hỏi về công nghệ hay cú pháp code, trừ trường hợp sinh viên có nguyện vọng muốn được vấn đáp về code.</li>
    <li><strong>Yêu cầu sinh viên:</strong> Sinh viên cần nghiêm túc ngồi lại để phân rã (breakdown) cấu trúc tài liệu và xác định rõ cách viết chi tiết cho từng phần.</li>
  </ul>
</div>

<hr>

<h3 class="section-header">### 4. Năng lực cạnh tranh và bài toán sinh tồn tại thành phố</h3>

<div class="box-alert-career">
  <span class="badge badge-purple">Tâm sự nghề nghiệp</span> <strong>Tâm sự thực tế của Giảng viên về Năng lực cạnh tranh và Bài toán sinh tồn:</strong>
  <ul>
    <li><strong>Thái độ học tập:</strong> Giảng viên phê bình thái độ than vãn <em>"khó quá không học được"</em> hay <em>"không làm được"</em>. Vấn đề then chốt nằm ở việc sinh viên đã dành bao nhiêu thời gian cho việc học và có thực sự đam mê với ngành hay không.</li>
    <li><strong>Yếu tố đam mê:</strong> Nếu không có đam mê thì không thể theo đuổi ngành công nghệ phần mềm lâu dài, kể cả sau khi tốt nghiệp ra trường.</li>
    <li><strong>Con đường phát triển:</strong> Ngoại trừ những trường hợp có quan hệ gia đình (con ông cháu cha - COCC) trong các tập đoàn lớn để có sẵn việc làm sau khi nhận bằng, các sinh viên đi lên từ năng lực tự thân (<em>"từ chân phương, chân đất"</em>) muốn bám trụ lại thành phố <span class="hl-red">chỉ có một con đường duy nhất là chứng minh bằng thực lực và năng lực thực tế</span>.</li>
  </ul>

  <h4 style="margin: 6px 0 3px 0; color: #6b21a8; font-size: 9.5pt;">Đối thoại thực tế về chi phí sinh hoạt (Giảng viên trao đổi cùng sinh viên):</h4>
  <p>Giảng viên đặt câu hỏi: <em>Sau khi ra trường trong 5 năm đầu, nếu bố mẹ không chu cấp tiền nữa thì sinh viên sẽ sống như thế nào?</em></p>
  <ul>
    <li>Tiền thuê phòng trọ: Khoảng 2 triệu đồng/tháng.</li>
    <li>Tiền ăn uống: Khoảng 2 triệu đồng/tháng.</li>
    <li>Tiền sinh hoạt, chi tiêu khác: Khoảng 1 triệu đồng/tháng.</li>
    <li>&rarr; <strong>Tổng chi phí tối thiểu:</strong> <span class="hl-yellow">5 triệu đồng/tháng</span>.</li>
    <li><strong>Mức lương kỳ vọng của sinh viên:</strong> Khoảng <strong>6 triệu đồng/tháng</strong>.</li>
  </ul>

  <h4 style="margin: 6px 0 3px 0; color: #6b21a8; font-size: 9.5pt;">Phân tích rủi ro tài chính của giảng viên:</h4>
  <ul>
    <li>Mức lương 6 triệu đồng là mức rất bấp bênh; nếu ốm đau, bệnh tật thì không có tiền mua thuốc.</li>
    <li>Nếu gặp rủi ro phát sinh như xe máy hỏng giữa đường thì lại rơi vào cảnh phải xin tiền gia đình dưới quê gửi lên để sửa xe.</li>
    <li>Để có thể tồn tại được với mức thu nhập 6 triệu đồng tại thành phố: Phải ở ghép từ 3 đến 4 người trong một phòng để chia nhỏ tiền phòng, phải chi tiêu cực kỳ tiết kiệm, và phải rèn luyện thể dục thể thao để giữ gìn sức khỏe, tránh ốm yếu quặt quẹo.</li>
    <li>Với mức chi phí 5 triệu và lương 6 triệu, trong 2 năm đầu tiên xác định phải sống độc thân vì chưa có điều kiện để hẹn hò hay lo cho người khác.</li>
  </ul>

  <p style="margin-top: 6px; font-weight: bold; color: #991b1b;">
    👉 Lời khuyên hành động: Cuộc sống thực tế rất khắc nghiệt, sinh viên (đặc biệt là các bạn nam) bắt buộc phải nỗ lực cày ải gấp đôi, gấp ba ngay từ thời điểm hiện tại. Nếu chỉ biết ăn uống xong rồi ngồi chơi game qua ngày thì sau khi học xong chắc chắn sẽ phải bỏ về quê.
  </p>
</div>

<hr>

<h3 class="section-header">### 5. Hướng dẫn tra cứu biểu mẫu (Template) tài liệu</h3>
<ul>
  <li>Giảng viên lưu ý sinh viên <span class="hl-red">không lên hỏi giảng viên về việc viết tài liệu như thế nào hay tìm template ở đâu</span>.</li>
  <li>Toàn bộ tài liệu tham khảo và biểu mẫu chuẩn đã được cung cấp đầy đủ trong mục tài liệu môn học trên hệ thống, sinh viên phải chủ động vào đó tải về để nghiên cứu và làm theo.</li>
</ul>

<div style="text-align: center; margin-top: 15px; padding-top: 8px; border-top: 1px solid #cbd5e1; font-size: 8.5pt; color: #64748b;">
  --- HẾT NỘI DUNG VĂN BẢN GHI ÂM (BẢO LƯU TOÀN VẸN 100% CHI TIẾT) ---
</div>

</body>
</html>
"""

html_path = "/Users/bo/OISMS/docs/GHI_AM_CHI_TIET_THAY_DAN_VA_DANH_DAU_QUAN_TRONG.html"
pdf_path = "/Users/bo/OISMS/docs/GHI_AM_CHI_TIET_THAY_DAN_VA_DANH_DAU_QUAN_TRONG.pdf"

with open(html_path, "w", encoding="utf-8") as f:
    f.write(html_content)

print(f"HTML created at: {html_path}")

cmd = [
    "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
    "--headless",
    "--disable-gpu",
    f"--print-to-pdf={pdf_path}",
    "--no-pdf-header-footer",
    html_path
]

res = subprocess.run(cmd, capture_output=True, text=True)
if os.path.exists(pdf_path):
    size = os.path.getsize(pdf_path)
    print(f"PDF created successfully at: {pdf_path} (Size: {size} bytes)")
else:
    print(f"Error creating PDF: {res.stderr}")
