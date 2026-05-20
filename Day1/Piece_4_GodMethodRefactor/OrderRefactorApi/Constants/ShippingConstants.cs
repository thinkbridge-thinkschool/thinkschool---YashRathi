namespace OrderRefactorApi.Constants;

public static class ShippingConstants
{
    public const decimal DefaultShippingCost = 15.99m;
    public const decimal CaliforniaShippingCost = 12.50m;
    public const decimal NewYorkShippingCost = 12.50m;
    public const decimal TexasShippingCost = 18.75m;
    
    public const decimal HighOrderThreshold = 200m;
    public const decimal ShippingDiscountRate = 0.5m; // 50% off for high orders
}
