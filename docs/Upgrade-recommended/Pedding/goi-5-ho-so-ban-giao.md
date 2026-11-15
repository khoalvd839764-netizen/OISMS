# Gói 5 — Hoàn thiện hồ sơ bàn giao

## Hiện trạng

Checklist mục 5.2 còn mở bốn nhóm hồ sơ. Báo cáo Sprint 5 ghi SDD mục 1 cần rà lại sơ đồ gói, các mục 3.4–3.6 và 3.10–3.11 chưa đạt. API contracts và tài liệu kiểm thử đã có để đưa vào bộ hồ sơ. [Checklist hồ sơ](../../../checklist.md#52-sáu-nhóm-hồ-sơ-nghiệm-thu) · [Báo cáo Sprint 5](../../08-sprints/sprint-5/BAO_CAO_TONG_KET_NGHIEM_THU_SPRINT_5.md#b-phân-hệ-tài-liệu-thiết-kế-chi-tiết-sdd-nhóm-3-bạn-tài-liệu--hỗ-trợ--jira-oisms-5359)

## Việc cần làm

- [ ] **URS và RTM.** Đối chiếu mã FR/NFR với đặc tả, phần hiện thực, test và kết quả. Các yêu cầu chưa đạt ghi rõ bằng chứng còn thiếu hoặc đầu việc liên quan. [RTM](../../06-testing-and-qa/01-requirements-matrix-rtm.md)
- [ ] **Tài liệu kiến trúc.** Cập nhật sơ đồ gói và chiều phụ thuộc theo solution hiện tại. So lại sơ đồ với thiết kế đa tenant và các điểm kiến trúc còn mở. [Tài liệu kiến trúc](../../04-architecture-and-design/README.md)
- [ ] **SDD UML 2.0.** Rà lại 3.4–3.6 cho mua hàng, chuyển kho, kiểm kê và 3.10–3.11 cho nhân sự/RBAC, báo cáo. So class, sequence, trạng thái và dữ liệu với code, rồi đưa các mục đã rà soát vào bộ hồ sơ cùng ERD. [Thiết kế CSDL](../../04-architecture-and-design/03-database-design.md) · [Máy trạng thái](../../04-architecture-and-design/04-state-machines.md)
- [ ] **Hướng dẫn cài đặt và sử dụng.** Đối chiếu các bước cấu hình, khởi chạy và tạo dữ liệu demo. Bổ sung các luồng Admin/POS còn thiếu, gồm quyền cần có, thao tác và kết quả mong đợi. [Hướng dẫn Docker](../../07-devops-and-deployment/01-docker-compose-guide.md) · [Tài liệu màn hình](../../11-ui-screens/README.md)

## Bằng chứng nghiệm thu

- [ ] Có danh mục hồ sơ theo sáu nhóm của đề bài. Mỗi tài liệu có phiên bản hoặc mốc đối chiếu code và liên kết mở được. [Danh mục theo đề bài](../../00-overview/03-de-bai-cnpm-reference.md#3-danh-mục-hồ-sơ-nghiệm-thu-theo-đề-bài-mandatory-deliverables-checklist)
- [ ] Có kết quả review cho các mục SDD còn mở và bằng chứng chạy theo hướng dẫn trên môi trường demo. Cập nhật trạng thái hồ sơ ở checklist mục 5.2 khi có kết quả.
