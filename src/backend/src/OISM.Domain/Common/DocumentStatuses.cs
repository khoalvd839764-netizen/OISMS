namespace OISM.Domain.Common;

public static class OrderStatus
{
    public const string Draft = "Draft";
    public const string Reserved = "Reserved";
    public const string Confirmed = "Confirmed";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
}

public static class ReservationStatus
{
    public const string Active = "Active";
    public const string Consumed = "Consumed";
    public const string Released = "Released";
    public const string Expired = "Expired";
}

public static class GoodsReceiptStatus
{
    public const string Draft = "Draft";
    public const string Confirmed = "Confirmed";
    public const string Cancelled = "Cancelled";
}

public static class PurchaseOrderStatus
{
    public const string Draft = "Draft";
    public const string Approved = "Approved";
    public const string PartiallyReceived = "PartiallyReceived";
    public const string Received = "Received";
    public const string Closed = "Closed";
    public const string Cancelled = "Cancelled";
}

public static class StockTransferStatus
{
    public const string Draft = "Draft";
    public const string InTransit = "InTransit";
    public const string Received = "Received";
    public const string Cancelled = "Cancelled";
}

public static class StockCountStatus
{
    public const string Draft = "Draft";
    public const string Counting = "Counting";
    public const string PendingApproval = "PendingApproval";
    public const string Confirmed = "Confirmed";
    public const string Cancelled = "Cancelled";
}

public static class StockCountPeriodStatus
{
    public const string Planned = "Planned";
    public const string InProgress = "InProgress";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
}

public static class InventoryAdjustmentStatus
{
    public const string Draft = "Draft";
    public const string Confirmed = "Confirmed";
    public const string Cancelled = "Cancelled";
}

public static class PaymentStatus
{
    public const string Pending = "Pending";
    public const string Succeeded = "Succeeded";
    public const string Failed = "Failed";
}

public static class PriceChangeRequestStatus
{
    public const string Pending = "Pending";
    public const string Approved = "Approved";
    public const string Rejected = "Rejected";
}
