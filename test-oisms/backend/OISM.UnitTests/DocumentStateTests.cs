using OISM.Domain.Common;
using OISM.Domain.Entities;

namespace OISM.UnitTests;

public class DocumentStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 8, 0, 0, TimeSpan.Zero);
    private static readonly Guid Actor = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void Order_RequiresReservationBeforeConfirmAndConfirmationBeforeCompletion()
    {
        var order = new Order { Channel = "Admin" };
        Assert.Throws<InvalidStateTransitionException>(() => order.Confirm(Actor, Now));
        Assert.Throws<InvalidStateTransitionException>(() => order.Complete(Now));
        order.Reserve();
        order.Confirm(Actor, Now);
        order.Complete(Now.AddMinutes(1));

        Assert.Equal(OrderStatus.Completed, order.Status);
        Assert.Equal(Actor, order.ConfirmedBy);
        Assert.Equal(Now, order.ConfirmedAt);
        Assert.Equal(Now.AddMinutes(1), order.CompletedAt);
        Assert.Throws<InvalidStateTransitionException>(() => order.Cancel(Now));
    }

    [Fact]
    public void Order_CheckoutIsPosOnlyAndRetainsConfirmationTimestamp()
    {
        var admin = new Order { Channel = "Admin" };
        Assert.Throws<InvalidStateTransitionException>(() => admin.CompleteAtCheckout(Actor, Now));
        Assert.Equal(OrderStatus.Draft, admin.Status);

        var pos = new Order();
        pos.CompleteAtCheckout(Actor, Now);
        Assert.Equal(OrderStatus.Completed, pos.Status);
        Assert.Equal(Now, pos.ConfirmedAt);
        Assert.Equal(Now, pos.CompletedAt);
    }

    [Fact]
    public void Order_CancellationDistinguishesLegacyUnpaidPosAndExpiry()
    {
        var paid = new Payment();
        paid.Succeed(Now);
        Assert.Throws<InvalidStateTransitionException>(() => paid.Succeed(Now.AddMinutes(1)));
        Assert.Equal(Now, paid.PaidAt);

        var paidPos = new Order { Payment = paid };
        Assert.Throws<InvalidStateTransitionException>(() => paidPos.Cancel(Now));
        Assert.False(paidPos.CanExpireReservations);

        var legacyPos = new Order();
        Assert.True(legacyPos.CanExpireReservations);
        legacyPos.Cancel(Now);
        Assert.Equal(OrderStatus.Cancelled, legacyPos.Status);
        Assert.Equal(Now, legacyPos.CancelledAt);

        var admin = new Order { Channel = "Admin", Payment = paid };
        admin.Reserve();
        Assert.True(admin.CanCancel);
        Assert.False(admin.CanExpireReservations);
    }

    [Fact]
    public void Reservation_ExpiresAtTheCutoffAndRejectsOtherTerminalTransitions()
    {
        var expires = new Reservation { ExpiresAt = Now };
        Assert.Throws<InvalidStateTransitionException>(() => expires.Consume(Now));
        Assert.Throws<InvalidStateTransitionException>(() => expires.Expire(Now.AddSeconds(-1)));
        expires.Expire(Now);
        Assert.Equal(ReservationStatus.Expired, expires.Status);
        Assert.Throws<InvalidStateTransitionException>(expires.Release);

        var consumed = new Reservation { ExpiresAt = Now.AddMinutes(1) };
        consumed.Consume(Now);
        Assert.Equal(ReservationStatus.Consumed, consumed.Status);
        Assert.Throws<InvalidStateTransitionException>(consumed.Release);

        var released = new Reservation { ExpiresAt = Now };
        released.Release();
        Assert.Equal(ReservationStatus.Released, released.Status);
    }

    [Fact]
    public void GoodsReceipt_RequiresDraftAndRecordsWhoConfirmedIt()
    {
        var confirmed = new GoodsReceipt();
        confirmed.Confirm(Actor, Now);
        Assert.Equal(GoodsReceiptStatus.Confirmed, confirmed.Status);
        Assert.Equal(Actor, confirmed.ConfirmedBy);
        Assert.Equal(Now, confirmed.ConfirmedAt);
        Assert.Throws<InvalidStateTransitionException>(() => confirmed.Cancel(Now));

        var cancelled = new GoodsReceipt();
        cancelled.Cancel(Now);
        Assert.Equal(GoodsReceiptStatus.Cancelled, cancelled.Status);
        Assert.Throws<InvalidStateTransitionException>(() => cancelled.Confirm(Actor, Now));
    }

    [Fact]
    public void PurchaseOrder_UsesBaseQuantityToDetermineReceivingStatus()
    {
        var line = new PurchaseOrderItem { Quantity = 2m, FactorSnapshot = 10m };
        var order = new PurchaseOrder { Items = [line] };
        Assert.Throws<InvalidStateTransitionException>(() => order.UpdateReceivingStatus(Now));
        order.Approve(Actor, Now);
        line.ReceivedBaseQuantity = 10m;
        order.UpdateReceivingStatus(Now);
        Assert.Equal(PurchaseOrderStatus.PartiallyReceived, order.Status);
        Assert.Throws<InvalidStateTransitionException>(() => order.Cancel(Actor, Now));
        line.ReceivedBaseQuantity = 20m;
        order.UpdateReceivingStatus(Now);
        Assert.Equal(PurchaseOrderStatus.Received, order.Status);
        Assert.Throws<InvalidStateTransitionException>(() => order.Close(Actor, "Done", Now));
    }

    [Fact]
    public void PurchaseOrder_ClosesWithReasonOrCancelsBeforeReceiving()
    {
        var closed = new PurchaseOrder();
        closed.Approve(Actor, Now);
        Assert.Throws<InvalidStateTransitionException>(() => closed.Close(Actor, " ", Now));
        closed.Close(Actor, "Supplier unavailable", Now);
        Assert.Equal(PurchaseOrderStatus.Closed, closed.Status);
        Assert.Equal("Supplier unavailable", closed.CloseReason);
        Assert.Equal(Actor, closed.ClosedBy);

        var cancelled = new PurchaseOrder();
        cancelled.Approve(Actor, Now);
        cancelled.Cancel(Actor, Now);
        Assert.Equal(PurchaseOrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(Now, cancelled.CancelledAt);
    }

    [Fact]
    public void StockTransfer_DispatchesBeforeReceivingAndCancelsOnlyInDraft()
    {
        var transfer = new StockTransfer();
        Assert.Throws<InvalidStateTransitionException>(() => transfer.Receive(Actor, Now));
        transfer.Dispatch(Actor, Now);
        Assert.Equal(StockTransferStatus.InTransit, transfer.Status);
        Assert.Throws<InvalidStateTransitionException>(() => transfer.Cancel(Now));
        transfer.Receive(Actor, Now.AddMinutes(1));
        Assert.Equal(StockTransferStatus.Received, transfer.Status);
        Assert.Equal(Now, transfer.DispatchedAt);
        Assert.Equal(Now.AddMinutes(1), transfer.ReceivedAt);

        var cancelled = new StockTransfer();
        cancelled.Cancel(Now);
        Assert.Equal(StockTransferStatus.Cancelled, cancelled.Status);
        Assert.Throws<InvalidStateTransitionException>(() => cancelled.Dispatch(Actor, Now));
    }

    [Fact]
    public void StockCount_RecountClearsSubmissionAndConfirmationRequiresResubmission()
    {
        var count = new StockCount();
        Assert.Throws<InvalidStateTransitionException>(() => count.Submit(Actor, Now));
        count.StartCounting(Now);
        count.Submit(Actor, Now);
        Assert.Equal(StockCountStatus.PendingApproval, count.Status);
        count.RequestRecount(Now);
        Assert.Equal(StockCountStatus.Counting, count.Status);
        Assert.Null(count.SubmittedBy);
        Assert.Null(count.SubmittedAt);
        Assert.Throws<InvalidStateTransitionException>(() => count.Confirm(Actor, Guid.Empty, Now));
        count.Submit(Actor, Now);
        var adjustment = new InventoryAdjustment { Id = Guid.NewGuid() };
        adjustment.Confirm(Actor, Now);
        count.Confirm(Actor, adjustment.Id, Now);
        Assert.Equal(InventoryAdjustmentStatus.Confirmed, adjustment.Status);
        Assert.Equal(StockCountStatus.Confirmed, count.Status);
        Assert.Equal(adjustment.Id, count.GeneratedAdjustmentId);
        Assert.Throws<InvalidStateTransitionException>(() => count.Cancel(Now));
    }

    [Theory]
    [InlineData(StockCountStatus.Draft)]
    [InlineData(StockCountStatus.Counting)]
    [InlineData(StockCountStatus.PendingApproval)]
    public void StockCount_CancelsBeforeConfirmation(string status)
    {
        var count = new StockCount { Status = status };
        count.Cancel(Now);
        Assert.Equal(StockCountStatus.Cancelled, count.Status);
        Assert.Throws<InvalidStateTransitionException>(() => count.StartCounting(Now));
    }

    [Fact]
    public void StockCountPeriod_SupportsReopenAndFutureRescheduling()
    {
        var period = new StockCountPeriod();
        Assert.Throws<InvalidStateTransitionException>(() => period.Complete(Now));
        period.Start(Now);
        period.Reopen(Now);
        Assert.Equal(StockCountPeriodStatus.Planned, period.Status);
        period.Cancel(Now);
        Assert.Throws<InvalidStateTransitionException>(() => period.Start(Now));
        Assert.Throws<InvalidStateTransitionException>(() => period.Reschedule(Now, Now.AddDays(1), Actor, Now));
        period.Reschedule(Now.AddDays(1), Now.AddDays(2), Actor, Now);
        Assert.Equal(StockCountPeriodStatus.Planned, period.Status);
        Assert.Equal(Actor, period.AssigneeUserId);
        period.Start(Now);
        period.Complete(Now);
        Assert.Equal(StockCountPeriodStatus.Completed, period.Status);
        Assert.Equal(Now, period.CompletedAt);
        Assert.Throws<InvalidStateTransitionException>(() => period.Reopen(Now));
    }

    [Fact]
    public void PriceChangeRequest_AllowsOnlyOneReview()
    {
        var approved = new PriceChangeRequest { Status = PriceChangeRequestStatus.Pending };
        approved.Approve(Actor, Now);
        Assert.Equal(PriceChangeRequestStatus.Approved, approved.Status);
        Assert.Equal(Actor, approved.ReviewedBy);
        Assert.Throws<InvalidStateTransitionException>(() => approved.Reject(Actor, "No", Now));

        var rejected = new PriceChangeRequest { Status = PriceChangeRequestStatus.Pending };
        rejected.Reject(Actor, "No", Now);
        Assert.Equal(PriceChangeRequestStatus.Rejected, rejected.Status);
        Assert.Equal("No", rejected.ReviewNote);
        Assert.Throws<InvalidStateTransitionException>(() => rejected.Approve(Actor, Now));
    }
}
