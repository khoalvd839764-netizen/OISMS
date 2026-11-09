# test-data

Dữ liệu mẫu dùng chung cho test (fixture JSON, script SQL seed cho test, payload webhook mẫu…).

Hiện chưa có file nào: unit test tạo dữ liệu trong code (`TestDbContextFactory`), integration test PostgreSQL tự tạo DB riêng mỗi lần chạy (`PostgresTestEnvironment`).

Khi thêm fixture:
- Đặt tên theo module, ví dụ `orders/pos-checkout-3-items.json`.
- Không chứa mật khẩu thật, token hay dữ liệu khách hàng thật.
- Test .NET muốn đọc file ở đây thì khai báo trong `.csproj` bằng `<None Include="..\..\test-data\**" CopyToOutputDirectory="PreserveNewest" />`.
