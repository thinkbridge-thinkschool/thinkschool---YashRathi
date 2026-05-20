namespace OrderRefactorApi.Dtos;

/// <summary>Request body for POST /api/orders</summary>
public class CreateOrderRequest
{
    public int CustomerId { get; set; }
    public List<OrderItemRequest> Items { get; set; } = [];
    public string ShippingAddress { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

/// <summary>A single line item in the order request</summary>
public class OrderItemRequest
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
}
