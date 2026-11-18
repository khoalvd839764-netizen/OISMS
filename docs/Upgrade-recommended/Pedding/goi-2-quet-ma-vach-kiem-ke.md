# Gói 2 — Quét mã vạch khi kiểm kê

## Hiện trạng

`FR-PROD-03` đã có mã vạch và luồng quét POS. Giao diện kiểm kê hiện chọn dòng SKU và nhập số đếm. Hồ sơ kiểm kê và chuyển kho đã có integration test, còn thiếu biên bản E2E giao diện cho bản hiện tại. [Catalog POS](../../../src/frontend/pos-app/src/components/pos/ProductCatalog.tsx) · [Chi tiết phiếu kiểm kê](../../../src/frontend/admin-dashboard/src/components/stockCounts/StockCountDetailDrawer.tsx)

## Việc cần làm

- [ ] Cho quét EAN-13 hoặc Code128 trong phiếu kiểm kê và chọn đúng dòng SKU thuộc phạm vi phiếu, tenant và kho. Giữ cách nhập số đếm hiện có.
- [ ] Chốt cách xử lý mã quy cách nếu bộ quét nhận mã của đơn vị mua/bán. Khi có quy đổi, hiển thị đơn vị và ghi số đếm đúng theo đơn vị gốc.
- [ ] Xử lý mã không tồn tại, SKU ngoài phạm vi và phiếu đã khóa. Quét để chọn dòng chưa làm phát sinh bút toán.

## Bằng chứng nghiệm thu

- [ ] Quét được hai loại mã, chọn đúng SKU, ghi và tải lại đúng số đếm. Có ca quét mã lạ, mã ngoài phiếu và phiếu không còn cho ghi.
- [ ] Ghi lại E2E tạo phiếu, ghi số đếm, duyệt và đối chiếu bút toán. Bổ sung bằng chứng giao diện chuyển kho từ xuất đến nhận theo mục 3.3 của checklist. [Đặc tả catalog](../../02-functional-requirements/fr-02-product-catalog.md) · [Kiểm thử kho](../../06-testing-and-qa/02-unit-and-integration-test.md)
