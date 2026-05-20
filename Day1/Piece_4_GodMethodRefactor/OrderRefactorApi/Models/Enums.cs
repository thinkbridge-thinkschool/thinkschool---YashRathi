namespace OrderRefactorApi.Models;

public enum CustomerType
{
    Standard = 0,
    Gold = 1,
    Premium = 2
}

public enum ProductCategory
{
    Electronics = 0,
    Books = 1,
    Clothing = 2,
    Other = 3
}

public enum OrderStatus
{
    Pending = 0,
    Confirmed = 1,
    Shipped = 2,
    Delivered = 3,
    Cancelled = 4
}
