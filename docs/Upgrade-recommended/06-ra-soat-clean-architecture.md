# 06. Chuẩn bị rà soát Clean Architecture và tổ chức mã nguồn

> Trạng thái: kế hoạch rà soát kèm ví dụ đã kiểm tra ngày 29/10/2026. Đây chưa phải kết luận rằng mọi file trong repository đã được kiểm tra hoặc đã được sửa.

## Mục tiêu và phạm vi

GVHD Nguyễn Văn Chiến nhận xét nhóm đã học Clean Architecture nhưng một số file chưa tuân theo cách tổ chức kiến trúc. Đợt rà soát cần chỉ ra **file nào đặt sai trách nhiệm, phụ thuộc sai chiều, hoặc gom quá nhiều trách nhiệm**, kèm bằng chứng và cách chuyển an toàn. Không đánh giá một file chỉ qua tên thư mục hay số dòng.

Phạm vi chính là backend trong `src/backend/src/` theo [tài liệu kiến trúc hiện có](../04-architecture-and-design/01-clean-architecture.md). Frontend React được kiểm tra riêng về ranh giới `pages` / `components` / `services` / `context`; frontend không phải bốn project Clean Architecture của backend.

## Quy tắc cần dùng khi rà soát

| Tầng/thành phần | Nên giữ trách nhiệm gì | Dấu hiệu cần xem xét |
| :--- | :--- | :--- |
| `OISM.Domain` | Entity, quy tắc và bất biến nghiệp vụ không phụ thuộc web/EF | Import `WebApi`, `Infrastructure`, DTO HTTP hoặc API framework |
| `OISM.Application` | Use case, kiểm tra quy tắc nghiệp vụ, interface cho dịch vụ ngoài | Phụ thuộc trực tiếp implementation trong `Infrastructure`; để SQL/chi tiết EF lan rộng vào use case |
| `OISM.Infrastructure` | EF Core, PostgreSQL, JWT, job và adapter kỹ thuật | Chứa quyết định nghiệp vụ vốn phải được kiểm thử độc lập ở Application/Domain |
| `OISM.WebApi` | Nhận request, xác thực/phân quyền, gọi use case, đổi kết quả thành HTTP response | Controller tự sửa entity, tự mở transaction, tự gọi `SaveChangesAsync()` cho một nghiệp vụ |
| Frontend `pages` / `components` | Hiển thị và điều phối tương tác | Một page chứa quá nhiều luồng gọi API, xử lý dữ liệu, lưu nháp và UI khó thay đổi độc lập |
| Frontend `context` | Trạng thái thực sự cần chia sẻ qua nhiều nhánh component | Tạo Context cho từng file/page dù state chỉ dùng cục bộ; hoặc giấu toàn bộ nghiệp vụ vào Context |

## Ảnh chụp hiện trạng đã kiểm tra

1. **Chiều tham chiếu project backend hiện đúng ở cấp `.csproj`.** `Application` tham chiếu `Domain`; `Infrastructure` tham chiếu `Application` và `Domain`; `WebApi` tham chiếu `Application` và `Infrastructure`. `Domain` không khai báo project reference. Đây là điểm đã đúng, không cần đảo chiều project. Xem các file `.csproj` trong [`src/backend/src`](../../src/backend/src/).
2. **Một số controller đang làm nghiệp vụ và truy cập EF trực tiếp.** [BrandsController.cs](../../src/backend/src/OISM.WebApi/Controllers/BrandsController.cs) tự tạo validator/entity, gọi `AppDbContext` và `SaveChangesAsync()`. [ProductsController.cs](../../src/backend/src/OISM.WebApi/Controllers/ProductsController.cs) còn tự mở transaction và thực thi SQL liên quan catalog. [AuthController.cs](../../src/backend/src/OISM.WebApi/Controllers/AuthController.cs) cũng trực tiếp thao tác DbContext/transaction. Đây là các ứng viên rõ nhất để chuyển hành vi sang Application; cần giữ nguyên chính sách tenant và tính nguyên tử khi chuyển.
3. **Đã có mẫu controller mỏng để đối chiếu.** [OrdersController.cs](../../src/backend/src/OISM.WebApi/Controllers/OrdersController.cs), [GoodsReceiptsController.cs](../../src/backend/src/OISM.WebApi/Controllers/GoodsReceiptsController.cs) và [StockCountsController.cs](../../src/backend/src/OISM.WebApi/Controllers/StockCountsController.cs) gọi interface của Application. Có thể dùng cách phân chia trách nhiệm này làm mẫu, nhưng vẫn phải xem từng method cụ thể.
4. **Application hiện gắn với EF Core qua hợp đồng dữ liệu.** [IAppDbContext.cs](../../src/backend/src/OISM.Application/Common/Interfaces/IAppDbContext.cs) công khai `DbSet`, `IDbContextTransaction`, execution strategy và raw SQL; nhiều service Application dùng LINQ EF. Như vậy chiều phụ thuộc project vẫn đúng, nhưng use case phụ thuộc API của EF. Đây là mức độ Clean Architecture đã được nới lỏng; nên quyết định rõ có chấp nhận hay sẽ tách repository/unit-of-work theo từng use case. Không thay đồng loạt khi chưa kiểm tra giao dịch và hiệu năng.
5. **Có file gom nhiều trách nhiệm, cần chia theo luồng nghiệp vụ sau khi có kiểm thử bảo vệ.** [OrderService.cs](../../src/backend/src/OISM.Application/Features/Orders/Services/OrderService.cs) khoảng 2.900 dòng và hiện thực cả `IOrderService` lẫn `IPosCheckoutService`. Frontend có [InventoryPage.tsx](../../src/frontend/admin-dashboard/src/pages/InventoryPage.tsx) khoảng 2.300 dòng, [ProductsPage.tsx](../../src/frontend/admin-dashboard/src/pages/ProductsPage.tsx) khoảng 1.200 dòng và [apiAdapter.ts](../../src/frontend/admin-dashboard/src/services/apiAdapter.ts) khoảng 1.000 dòng. Số dòng là tín hiệu cần xem xét, **chưa tự nó chứng minh sai kiến trúc**.
6. **Tài liệu kiến trúc cần cập nhật theo tên code hiện hành.** [01-clean-architecture.md](../04-architecture-and-design/01-clean-architecture.md) dùng các tên ví dụ như `ApplicationDbContext`, `IApplicationDbContext`, `ITenantContext`, trong khi code hiện dùng `AppDbContext`, `IAppDbContext`, `ICurrentTenantService`. Tránh dùng tài liệu cũ làm tiêu chuẩn kiểm tra theo đúng từng tên file. *(Đã cập nhật ngày 06/11/2026: `01-clean-architecture.md`, `02-multi-tenant-design.md` dùng `AppDbContext`, `IAppDbContext`, `ICurrentTenantService`, `IMustHaveTenant`.)*

## “File có Context, file không” nên hiểu thế nào?

Từ `Context` trong dự án có ít nhất ba nghĩa khác nhau:

| Khái niệm | File hiện có | Quy tắc áp dụng |
| :--- | :--- | :--- |
| **React Context** | [AuthContext.tsx](../../src/frontend/admin-dashboard/src/context/AuthContext.tsx) được `App.tsx` bọc ở mức ứng dụng; các trang dùng `useAuth()` | Hợp lý cho phiên đăng nhập cần chia sẻ rộng. State của một form hoặc một page nên để local/hook theo feature; không cần tạo `Context.tsx` tương ứng cho mọi page. |
| **EF `DbContext`** | [AppDbContext.cs](../../src/backend/src/OISM.Infrastructure/Persistence/AppDbContext.cs) hiện thực `IAppDbContext` | Là adapter truy cập dữ liệu của Infrastructure. Không tạo một DbContext cho mỗi entity/file chỉ để “đủ Context”. |
| **Request/tenant context** | [ICurrentTenantService.cs](../../src/backend/src/OISM.Application/Common/Interfaces/ICurrentTenantService.cs) và [CurrentTenantService.cs](../../src/backend/src/OISM.Infrastructure/Services/CurrentTenantService.cs) | Cung cấp tenant của request cho các use case và bộ lọc dữ liệu; không truyền `TenantId` tùy ý từ client như nguồn đáng tin. |

Vì vậy, **không có tiêu chí “mọi file đều phải có Context”**. Cần xác định file đó đang cần trạng thái chia sẻ, truy cập dữ liệu hay thông tin request; chỉ tạo abstraction phù hợp khi có nhu cầu thật.

## Kế hoạch rà soát và sửa theo đợt

| Đợt | Việc cần làm | Kết quả có thể kiểm tra |
| :--- | :--- | :--- |
| 1. Chốt tiêu chuẩn | Cập nhật tài liệu kiến trúc theo tên class hiện hành; thống nhất mức chấp nhận EF trong Application và quy tắc controller mỏng. | Một bảng trách nhiệm/tầng được nhóm và GVHD chốt; không còn tiêu chuẩn mâu thuẫn giữa tài liệu và code. |
| 2. Lập danh sách file | Với từng feature, ghi `file → tầng hiện tại → trách nhiệm thực tế → phụ thuộc ngoài → mức độ rủi ro → hành động`. Ưu tiên controller trực tiếp ghi DB và các luồng kho/đơn/giá. | Danh sách có đường dẫn, method và bằng chứng cụ thể; phân biệt lỗi đã xác nhận với giả thuyết cần kiểm tra. |
| 3. Chuyển use case | Di chuyển validation/transaction/quy tắc nghiệp vụ từ controller sang Application theo từng feature nhỏ. Controller chỉ lo HTTP và phân quyền. | API contract không đổi ngoài ý định; kiểm thử nghiệp vụ, tenant isolation và rollback vẫn đạt. |
| 4. Giảm kết dính dữ liệu | Sau khi các luồng ổn định, xem `IAppDbContext` cần tách cổng truy cập theo use case hay tiếp tục dùng EF có kiểm soát; xử lý từng phần, không thay toàn hệ thống một lần. | Có lý do thiết kế, tác động hiệu năng và kiểm thử cho mỗi thay đổi interface. |
| 5. Tách frontend | Chia page lớn theo feature component/hook và chia `apiAdapter` theo miền; giữ `AuthContext` cho auth, state cục bộ ở nơi sử dụng. | Build UI đạt, các luồng tạo/chốt chứng từ và khôi phục nháp không đổi hành vi. |

## Checklist cho từng file khi rà soát

- File thuộc feature và tầng nào? Tên/thư mục có phản ánh trách nhiệm thực tế không?
- Nó import project/tầng nào? Có đưa chi tiết HTTP, EF, PostgreSQL hay React UI vào lõi nghiệp vụ không?
- Quy tắc nghiệp vụ được kiểm tra ở backend hay chỉ ở giao diện/controller?
- Nếu file dùng `Context`, đó là React state, EF persistence hay tenant request? Phạm vi sống của nó có đúng không?
- Nếu tách file, transaction, tenant filter, idempotency, audit và API contract sẽ được bảo vệ bằng kiểm thử nào?
- Dòng nào là lỗi kiến trúc đã xác nhận, dòng nào chỉ là vấn đề khả năng bảo trì hoặc ý tưởng cải tiến?

## Giới hạn của bản chuẩn bị

Các ví dụ trên được chọn để mở đầu đợt rà soát, chưa phải kết quả kiểm toán toàn bộ repository. Chưa sửa code và chưa chạy bộ test vì tài liệu này chỉ lập tiêu chuẩn, bằng chứng ban đầu và thứ tự xử lý. Việc quyết định cấm hoàn toàn EF ở Application hay chấp nhận `IAppDbContext` hiện tại cần chốt trước khi triển khai đợt 4.
