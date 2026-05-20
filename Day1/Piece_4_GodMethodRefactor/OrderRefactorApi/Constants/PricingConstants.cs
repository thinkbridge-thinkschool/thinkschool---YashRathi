namespace OrderRefactorApi.Constants;

public static class PricingConstants
{
    // Quantity-based discount tiers
    public const int MinQuantityForSmallDiscount = 5;
    public const decimal SmallDiscountRate = 0.9m; // 10%

    public const int MinQuantityForMediumDiscount = 10;
    public const decimal MediumDiscountRate = 0.85m; // 15%

    public const int MinQuantityForLargeDiscount = 20;
    public const decimal LargeDiscountRate = 0.8m; // 20%

    // Bulk order discounts
    public const int BulkThreshold1 = 50;
    public const decimal BulkDiscount1 = 0.75m; // 25%

    public const int BulkThreshold2 = 100;
    public const decimal BulkDiscount2 = 0.7m; // 30%

    // Customer type discounts
    public const decimal PremiumCustomerBaseDiscount = 0.75m; // 25%
    public const decimal PremiumCustomerElectronicsDiscount = 0.75m; // 25%
    public const decimal PremiumCustomerBooksDiscount = 0.7m; // 30%
    public const decimal PremiumCustomerBooksHighQuantityDiscount = 0.6m; // 40%
    public const int PremiumCustomerLoyaltyThreshold = 1000;
    public const decimal PremiumCustomerClothingLoyaltyDiscount = 0.5m; // 50%

    public const decimal GoldCustomerDiscount = 0.88m; // 12%

    public const int MinQuantityForGoldDiscount = 3;

    // Tax
    public const decimal TaxRate = 0.08m; // 8%

    // Loyalty points
    public const decimal LoyaltyPointsDivisor = 10m;
}
