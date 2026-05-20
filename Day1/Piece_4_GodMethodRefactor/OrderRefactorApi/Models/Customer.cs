namespace OrderRefactorApi.Models;

public class Customer
{
    public int Id { get; set; }
    public CustomerType Type { get; set; }
    public bool IsDeleted { get; set; }
    public int TotalOrderCount { get; set; }
    public decimal TotalSpent { get; set; }
    public DateTime LastOrderDate { get; set; }
    public int LoyaltyPoints { get; set; }
}