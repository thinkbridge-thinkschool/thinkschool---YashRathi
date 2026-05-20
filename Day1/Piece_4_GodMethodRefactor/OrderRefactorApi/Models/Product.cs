namespace OrderRefactorApi.Models;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public ProductCategory Category { get; set; }
}

public class Inventory
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public int QuantityOnHand { get; set; }
    public DateTime LastModified { get; set; }
    public string ModifiedBy { get; set; } = string.Empty;
}

public class Discount
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public decimal Percentage { get; set; }
    public bool Active { get; set; }
}