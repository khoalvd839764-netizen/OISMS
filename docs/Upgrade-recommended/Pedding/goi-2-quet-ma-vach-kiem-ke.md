# Gói 2 — Quét mã vạch khi kiểm kê

> **Quyết định 21/11/2026: không làm luồng quét barcode ở màn kiểm kê** vì nhóm không có thiết bị quét để triển khai và kiểm chứng. Phần biên bản E2E chuyển kho/kiểm kê vẫn làm, xem task FIN-H trong [plan final](../../../skills/claude-to-antigravity/references/OISMS-66-final-prompts.md).

## Hiện trạng

`FR-PROD-03` đã có mã vạch và luồng quét POS. Giao diện kiểm kê hiện chọn dòng SKU và nhập số đếm. Hồ sơ kiểm kê và chuyển kho đã có integration test, còn thiếu biên bản E2E giao diện cho bản hiện tại. [Catalog POS](../../../src/frontend/pos-app/src/components/pos/ProductCatalog.tsx) · [Chi tiết phiếu kiểm kê](../../../src/frontend/admin-dashboard/src/components/stockCounts/StockCountDetailDrawer.tsx)

## Việc cần làm

- [ ] Cho quét EAN-13 hoặc Code128 trong phiếu kiểm kê và chọn đúng dòng SKU thuộc phạm vi phiếu, tenant và kho. Giữ cách nhập số đếm hiện có.
- [ ] Chốt cách xử lý mã quy cách nếu bộ quét nhận mã của đơn vị mua/bán. Khi có quy đổi, hiển thị đơn vị và ghi số đếm đúng theo đơn vị gốc.
- [ ] Xử lý mã không tồn tại, SKU ngoài phạm vi và phiếu đã khóa. Quét để chọn dòng chưa làm phát sinh bút toán.

## Bằng chứng nghiệm thu

- [ ] Quét được hai loại mã, chọn đúng SKU, ghi và tải lại đúng số đếm. Có ca quét mã lạ, mã ngoài phiếu và phiếu không còn cho ghi.
- [x] Ghi lại E2E tạo phiếu, ghi số đếm, duyệt và đối chiếu bút toán. Bổ sung bằng chứng giao diện chuyển kho từ xuất đến nhận theo mục 3.3 của checklist (`OISMS-66-FIN-H`: [`docs/06-testing-and-qa/evidence/e2e-kho/README.md`](../../06-testing-and-qa/evidence/e2e-kho/README.md)). [Đặc tả catalog](../../02-functional-requirements/fr-02-product-catalog.md) · [Kiểm thử kho](../../06-testing-and-qa/02-unit-and-integration-test.md)
