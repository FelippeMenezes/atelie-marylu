namespace AtelieMarylu.Domain.Orders;

public enum OrderStatus
{
    AwaitingPayment = 1,
    Paid = 2,
    Preparing = 3,
    Shipped = 4,
    Delivered = 5,
    Cancelled = 6,
}
