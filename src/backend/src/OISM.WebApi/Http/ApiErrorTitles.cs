namespace OISM.WebApi.Http;

internal static class ApiErrorTitles
{
    public static string Get(ApiErrorProfile profile, string code) => profile switch
    {
        ApiErrorProfile.GoodsReceipts or ApiErrorProfile.GoodsReceiptCreate or ApiErrorProfile.GoodsReceiptRead or ApiErrorProfile.PurchaseOrders => GoodsReceipts(code),
        ApiErrorProfile.Orders or ApiErrorProfile.OrderProcess => Orders(code),
        ApiErrorProfile.PosCheckout => PosCheckout(code),
        ApiErrorProfile.PaymentAccounts => PaymentAccounts(code),
        ApiErrorProfile.Pricing => Pricing(code),
        ApiErrorProfile.SalesInvoices => SalesInvoices(code),
        ApiErrorProfile.SalesReturns => SalesReturns(code),
        ApiErrorProfile.StockCountPeriods => StockCountPeriods(code),
        ApiErrorProfile.StockCountSchedules => StockCountSchedules(code),
        ApiErrorProfile.StockCounts => StockCounts(code),
        ApiErrorProfile.StockTransfers => StockTransfers(code),
        ApiErrorProfile.Webhooks => Webhooks(code),
        ApiErrorProfile.InventoryTrace => "Lỗi truy vết",
        ApiErrorProfile.Units or ApiErrorProfile.Simulator => code,
        _ => "Lỗi xử lý nghiệp vụ"
    };

    private static string GoodsReceipts(string code) => code switch
    {
        "PURCHASE_ORDER_REQUIRED" => "Yêu cầu phiếu mua hàng",
        "PURCHASE_ORDER_NOT_RECEIVABLE" => "Phiếu mua không ở trạng thái nhận hàng",
        "PURCHASE_ORDER_OVER_RECEIPT" => "Vượt số lượng đặt mua",
        "PURCHASE_ORDER_ITEM_MISMATCH" => "Dòng hàng không khớp phiếu mua",
        "PURCHASE_ORDER_ZERO_PRICE_CONFIRM_REQUIRED" => "Yêu cầu xác nhận đơn giá 0",
        "UNIT_NOT_ALLOWED" => "Đơn vị tính không hợp lệ",
        "INVALID_UNIT_QUANTITY" => "Số lượng đơn vị không hợp lệ",
        "IDEMPOTENCY_PAYLOAD_MISMATCH" => "Xung đột khóa Idempotency",
        "INVALID_STATE_TRANSITION" => "Chuyển trạng thái không hợp lệ",
        "VALIDATION_ERROR" => "Lỗi xác thực dữ liệu",
        "NOT_FOUND" => "Không tìm thấy dữ liệu",
        "UNAUTHORIZED" => "Chưa xác thực",
        _ => "Lỗi xử lý nghiệp vụ"
    };

    private static string Orders(string code) => code switch
    {
        "VALIDATION_ERROR" => "Lỗi xác thực dữ liệu",
        "ORDER_NOT_FOUND" => "Không tìm thấy đơn hàng",
        "INVALID_STATE_TRANSITION" => "Chuyển đổi trạng thái không hợp lệ",
        "INSUFFICIENT_STOCK" => "Tồn kho không đủ",
        "PRICE_MISMATCH" => "Sai lệch đơn giá",
        "SKU_NOT_FOUND" => "Không tìm thấy mặt hàng",
        "SKU_INACTIVE" => "Mặt hàng ngừng hoạt động",
        "BRANCH_NOT_FOUND" => "Không tìm thấy chi nhánh",
        "BRANCH_INACTIVE" => "Chi nhánh ngừng hoạt động",
        "NO_ACTIVE_WAREHOUSE" => "Không có kho hàng hoạt động",
        "CHANNEL_NOT_ALLOWED" => "Kênh bán không được phép",
        "ORDER_ITEMS_EMPTY" => "Danh sách hàng rỗng",
        "ORDER_ALREADY_CANCELLED" => "Đơn hàng đã bị hủy",
        _ => "Lỗi xử lý đơn hàng"
    };

    private static string PosCheckout(string code) => code switch
    {
        "INSUFFICIENT_STOCK" => "Tồn kho không đủ",
        "IDEMPOTENCY_PAYLOAD_MISMATCH" => "Xung đột khóa Idempotency",
        "VALIDATION_ERROR" => "Lỗi xác thực dữ liệu",
        "BRANCH_INACTIVE_OR_INVALID" => "Chi nhánh không hợp lệ",
        "SKU_NOT_FOUND" => "Mặt hàng không tồn tại",
        "SKU_INACTIVE" => "Mặt hàng đã ngừng hoạt động",
        "PRICE_MISMATCH" => "Sai lệch đơn giá niêm yết",
        "INSUFFICIENT_CASH_RECEIVED" => "Tiền khách đưa không đủ",
        "INVALID_PAYMENT_METHOD" => "Phương thức thanh toán không hợp lệ",
        "NO_ACTIVE_WAREHOUSE" => "Không tìm thấy kho khả dụng",
        "ORDER_NOT_FOUND" => "Không tìm thấy đơn hàng",
        "INVALID_STATE_TRANSITION" => "Chuyển trạng thái không hợp lệ",
        "RESERVATION_EXPIRED" => "Phiếu giữ chỗ đã hết hạn",
        "PAYMENT_ACCOUNT_NOT_FOUND" => "Không tìm thấy tài khoản nhận tiền",
        "PAYMENT_ACCOUNT_INACTIVE" => "Tài khoản nhận tiền đã ngừng sử dụng",
        _ => "Lỗi xử lý nghiệp vụ"
    };

    private static string PaymentAccounts(string code) => code switch
    {
        "VALIDATION_ERROR" => "Lỗi xác thực dữ liệu",
        "PAYMENT_ACCOUNT_NOT_FOUND" => "Không tìm thấy tài khoản nhận tiền",
        "PAYMENT_ACCOUNT_DUPLICATE" => "Tài khoản nhận tiền đã tồn tại",
        "PAYMENT_ACCOUNT_INACTIVE" => "Tài khoản nhận tiền đã ngừng sử dụng",
        "PAYMENT_ACCOUNT_IDENTITY_IMMUTABLE" => "Không được đổi ngân hàng hoặc số tài khoản",
        "PAYMENT_ACCOUNT_CONFLICT" => "Xung đột cập nhật tài khoản mặc định",
        _ => "Lỗi xử lý nghiệp vụ"
    };

    private static string Pricing(string code) => code switch
    {
        "PRICE_VALID_FROM_IN_PAST" => "Mốc thời gian hiệu lực ở quá khứ",
        "PRICE_VALID_FROM_CONFLICT" => "Trùng mốc thời gian hiệu lực",
        "PRICE_ALREADY_EFFECTIVE" => "Giá đã có hiệu lực",
        "PRICE_LIST_NOT_FOUND" => "Bảng giá không tồn tại",
        "DIRECT_PRICE_UPDATE_NOT_ALLOWED" => "Cấm cập nhật giá trực tiếp",
        "IDEMPOTENCY_PAYLOAD_MISMATCH" => "Xung đột khóa Idempotency",
        "INVALID_STATE_TRANSITION" => "Chuyển trạng thái không hợp lệ",
        "UNIT_NOT_ALLOWED" => "Đơn vị không được phép",
        "VALIDATION_ERROR" => "Lỗi xác thực dữ liệu",
        "NOT_FOUND" => "Không tìm thấy dữ liệu",
        "FORBIDDEN_ACCESS" => "Không có quyền truy cập",
        "UNAUTHORIZED" => "Chưa xác thực",
        _ => "Lỗi xử lý nghiệp vụ"
    };

    private static string SalesInvoices(string code) => code switch
    {
        "SALES_INVOICE_NOT_FOUND" => "Không tìm thấy hóa đơn bán hàng",
        "ORDER_NOT_FOUND" => "Không tìm thấy đơn hàng",
        "LEDGER_ENTRY_NOT_FOUND" => "Không tìm thấy dòng sổ kho",
        "VALIDATION_ERROR" => "Lỗi xác thực dữ liệu",
        "UNAUTHORIZED" => "Chưa xác thực",
        _ => "Lỗi xử lý nghiệp vụ"
    };

    private static string SalesReturns(string code) => code switch
    {
        "SALES_RETURN_NOT_FOUND" => "Không tìm thấy phiếu trả hàng",
        "ORDER_NOT_RETURNABLE" => "Đơn hàng không thể trả",
        "RETURN_QUANTITY_EXCEEDED" => "Vượt số lượng được trả",
        "REFUND_AMOUNT_EXCEEDED" => "Tiền hoàn vượt mức cho phép",
        "RETURN_WAREHOUSE_MISMATCH" => "Kho nhận không thuộc chi nhánh đơn hàng",
        "INVALID_STATE_TRANSITION" => "Chuyển trạng thái không hợp lệ",
        "IDEMPOTENCY_PAYLOAD_MISMATCH" => "Xung đột khóa Idempotency",
        "VALIDATION_ERROR" => "Lỗi xác thực dữ liệu",
        "ORDER_NOT_FOUND" => "Không tìm thấy đơn hàng",
        "NOT_FOUND" => "Không tìm thấy dữ liệu",
        "UNAUTHORIZED" => "Chưa xác thực",
        _ => "Lỗi xử lý nghiệp vụ"
    };

    private static string StockCountPeriods(string code) => code switch
    {
        "NOT_FOUND" => "Không tìm thấy dữ liệu",
        "EMPTY_WAREHOUSE_STOCK_COUNT" => "Kho không có sản phẩm để kiểm kê",
        "VALIDATION_ERROR" => "Lỗi xác thực dữ liệu",
        "INVALID_STATE_TRANSITION" => "Chuyển trạng thái không hợp lệ",
        _ => "Lỗi xử lý nghiệp vụ"
    };

    private static string StockCountSchedules(string code) => code switch
    {
        "STOCK_COUNT_SCHEDULE_DUPLICATE" => "Lịch kiểm kê bị trùng lặp",
        "NOT_FOUND" => "Không tìm thấy dữ liệu",
        "VALIDATION_ERROR" => "Lỗi xác thực dữ liệu",
        _ => "Lỗi xử lý nghiệp vụ"
    };

    private static string StockCounts(string code) => code switch
    {
        "INSUFFICIENT_STOCK" => "Tồn kho không đủ",
        "INSUFFICIENT_STOCK_FOR_RESERVATION" => "Tồn kho không đủ đáp ứng hàng giữ chỗ",
        "IDEMPOTENCY_CONFLICT" => "Xung đột khóa Idempotency",
        "IDEMPOTENCY_PAYLOAD_MISMATCH" => "Xung đột khóa Idempotency",
        "STOCK_COUNT_DRIFT_DETECTED" => "Sai lệch sequence kiểm kê (drift)",
        "STOCK_COUNT_SEQUENCE_DRIFT" => "Sai lệch sequence kiểm kê",
        "STOCK_COUNT_SELF_APPROVAL" => "Không được tự duyệt phiếu kiểm kê",
        "STOCK_COUNT_APPROVAL_REASON_REQUIRED" => "Bắt buộc nhập lý do duyệt",
        "STOCK_COUNT_SKU_NOT_DRIFTED" => "Sản phẩm không bị lệch sequence",
        "STOCK_COUNT_SCHEDULE_DUPLICATE" => "Lịch kiểm kê bị trùng lặp",
        "EMPTY_WAREHOUSE_STOCK_COUNT" => "Kho không có sản phẩm để kiểm kê",
        "INVALID_COUNT_ITEM" => "Sản phẩm kiểm đếm không hợp lệ",
        "INCOMPLETE_STOCK_COUNT" => "Chưa kiểm đếm đủ sản phẩm",
        "ADJUSTMENT_COST_REQUIRED" => "Bắt buộc cung cấp đơn giá điều chỉnh",
        "SKU_INACTIVE" => "Sản phẩm hoặc biến thể bị khóa",
        "VALIDATION_ERROR" => "Lỗi xác thực dữ liệu",
        "NOT_FOUND" => "Không tìm thấy dữ liệu",
        "INVALID_STATE_TRANSITION" => "Chuyển trạng thái không hợp lệ",
        "UNAUTHORIZED" => "Chưa xác thực",
        _ => "Lỗi xử lý nghiệp vụ"
    };

    private static string StockTransfers(string code) => code switch
    {
        "INSUFFICIENT_STOCK" => "Tồn kho không đủ",
        "IDEMPOTENCY_PAYLOAD_MISMATCH" => "Xung đột khóa Idempotency",
        "VALIDATION_ERROR" => "Lỗi xác thực dữ liệu",
        "NOT_FOUND" => "Không tìm thấy dữ liệu",
        "INVALID_STATE_TRANSITION" => "Chuyển trạng thái không hợp lệ",
        "UNAUTHORIZED" => "Chưa xác thực",
        _ => "Lỗi xử lý nghiệp vụ"
    };

    private static string Webhooks(string code) => code switch
    {
        "CHANNEL_MISMATCH" => "Sai lệch kênh bán hàng",
        "TOTAL_AMOUNT_MISMATCH" => "Sai lệch tổng tiền đơn hàng",
        "VALIDATION_ERROR" => "Lỗi xác thực dữ liệu",
        "UNAUTHORIZED" => "Không được phép truy cập",
        "FORBIDDEN" => "Bị cấm truy cập",
        "INTEGRATION_NOT_FOUND" => "Không tìm thấy cấu hình tích hợp gian hàng",
        "BRANCH_INACTIVE_OR_INVALID" => "Chi nhánh fulfillment không hợp lệ",
        "SKU_NOT_FOUND" => "Mặt hàng biến thể không tồn tại",
        "SKU_INACTIVE" => "Mặt hàng biến thể đã ngừng hoạt động",
        "INSUFFICIENT_STOCK" => "Tồn kho khả dụng không đủ",
        "WEBHOOK_PAYLOAD_MISMATCH" => "Sai lệch nội dung đơn hàng sàn",
        "INVALID_STATE_TRANSITION" => "Chuyển trạng thái không hợp lệ",
        _ => "Lỗi xử lý nghiệp vụ"
    };
}
