using OrderRefactorApi.Constants;
using OrderRefactorApi.Models;

namespace OrderRefactorApi.Services;

public interface IPricingService
{
    decimal CalculateLinePrice(Product product, int quantity, Customer customer);
    decimal CalculateBulkDiscount(decimal subtotal, int totalQuantity);
    decimal CalculateTax(decimal subtotal);
    decimal CalculateShippingCost(decimal subtotal, string shippingAddress);
    decimal CalculateLoyaltyPoints(decimal orderTotal);
}

public class PricingService : IPricingService
{
    public decimal CalculateLinePrice(Product product, int quantity, Customer customer)
    {
        if (product == null)
            throw new ArgumentNullException(nameof(product));
        if (customer == null)
            throw new ArgumentNullException(nameof(customer));

        decimal unitPrice = product.Price;

        // Apply quantity-based discounts
        unitPrice = ApplyQuantityDiscounts(unitPrice, quantity);

        // Apply customer-type and category-based discounts
        unitPrice = ApplyCustomerDiscounts(unitPrice, quantity, customer, product);

        return unitPrice * quantity;
    }

    private decimal ApplyQuantityDiscounts(decimal unitPrice, int quantity)
    {
        if (quantity >= PricingConstants.MinQuantityForLargeDiscount)
            return unitPrice * PricingConstants.LargeDiscountRate;
        
        if (quantity >= PricingConstants.MinQuantityForMediumDiscount)
            return unitPrice * PricingConstants.MediumDiscountRate;
        
        if (quantity >= PricingConstants.MinQuantityForSmallDiscount)
            return unitPrice * PricingConstants.SmallDiscountRate;

        return unitPrice;
    }

    private decimal ApplyCustomerDiscounts(decimal unitPrice, int quantity, Customer customer, Product product)
    {
        if (customer.Type == CustomerType.Premium && quantity >= PricingConstants.MinQuantityForSmallDiscount)
        {
            return ApplyPremiumCustomerDiscount(unitPrice, quantity, customer, product);
        }

        if (customer.Type == CustomerType.Gold && quantity > PricingConstants.MinQuantityForGoldDiscount)
        {
            return unitPrice * PricingConstants.GoldCustomerDiscount;
        }

        return unitPrice;
    }

    private decimal ApplyPremiumCustomerDiscount(decimal unitPrice, int quantity, Customer customer, Product product)
    {
        return product.Category switch
        {
            ProductCategory.Electronics => unitPrice * PricingConstants.PremiumCustomerElectronicsDiscount,
            ProductCategory.Books => ApplyPremiumBooksDiscount(unitPrice, quantity),
            ProductCategory.Clothing => ApplyPremiumClothingDiscount(unitPrice, customer),
            _ => unitPrice * PricingConstants.PremiumCustomerBaseDiscount
        };
    }

    private decimal ApplyPremiumBooksDiscount(decimal unitPrice, int quantity)
    {
        if (quantity >= PricingConstants.BulkThreshold1)
            return unitPrice * PricingConstants.PremiumCustomerBooksHighQuantityDiscount;

        if (quantity >= PricingConstants.MinQuantityForSmallDiscount)
            return unitPrice * PricingConstants.PremiumCustomerBooksDiscount;

        return unitPrice;
    }

    private decimal ApplyPremiumClothingDiscount(decimal unitPrice, Customer customer)
    {
        if (customer.LoyaltyPoints > PricingConstants.PremiumCustomerLoyaltyThreshold)
            return unitPrice * PricingConstants.PremiumCustomerClothingLoyaltyDiscount;

        return unitPrice;
    }

    public decimal CalculateBulkDiscount(decimal subtotal, int totalQuantity)
    {
        if (totalQuantity >= PricingConstants.BulkThreshold2)
            return subtotal * PricingConstants.BulkDiscount2;

        if (totalQuantity >= PricingConstants.BulkThreshold1)
            return subtotal * PricingConstants.BulkDiscount1;

        return subtotal;
    }

    public decimal CalculateTax(decimal subtotal)
    {
        return subtotal * PricingConstants.TaxRate;
    }

    public decimal CalculateShippingCost(decimal subtotal, string shippingAddress)
    {
        decimal shippingCost = DetermineBaseShippingCost(shippingAddress);

        if (subtotal > ShippingConstants.HighOrderThreshold)
            shippingCost *= ShippingConstants.ShippingDiscountRate;

        return shippingCost;
    }

    private decimal DetermineBaseShippingCost(string shippingAddress)
    {
        if (shippingAddress.Contains("CA", StringComparison.OrdinalIgnoreCase) || 
            shippingAddress.Contains("NY", StringComparison.OrdinalIgnoreCase))
            return ShippingConstants.CaliforniaShippingCost;

        if (shippingAddress.Contains("TX", StringComparison.OrdinalIgnoreCase))
            return ShippingConstants.TexasShippingCost;

        return ShippingConstants.DefaultShippingCost;
    }

    public decimal CalculateLoyaltyPoints(decimal orderTotal)
    {
        return orderTotal / PricingConstants.LoyaltyPointsDivisor;
    }
}
