using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderRefactorApi.Data;
using OrderRefactorApi.Dtos;
using OrderRefactorApi.Models;
using OrderRefactorApi.Repositories;

namespace OrderRefactorApi.Services;

public interface IOrderService
{
    Task<OrderCreateResponse> CreateOrderAsync(
        int customerId,
        List<OrderItemRequest> items,
        string shippingAddress,
        string? notes,
        CancellationToken cancellationToken = default);
}

public class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IProductRepository _productRepository;
    private readonly ICustomerRepository _customerRepository;
    private readonly IInventoryService _inventoryService;
    private readonly IDiscountRepository _discountRepository;
    private readonly IPricingService _pricingService;
    private readonly AppDbContext _dbContext;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IOrderRepository orderRepository,
        IProductRepository productRepository,
        ICustomerRepository customerRepository,
        IInventoryService inventoryService,
        IDiscountRepository discountRepository,
        IPricingService pricingService,
        AppDbContext dbContext,
        ILogger<OrderService> logger)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _productRepository = productRepository ?? throw new ArgumentNullException(nameof(productRepository));
        _customerRepository = customerRepository ?? throw new ArgumentNullException(nameof(customerRepository));
        _inventoryService = inventoryService ?? throw new ArgumentNullException(nameof(inventoryService));
        _discountRepository = discountRepository ?? throw new ArgumentNullException(nameof(discountRepository));
        _pricingService = pricingService ?? throw new ArgumentNullException(nameof(pricingService));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<OrderCreateResponse> CreateOrderAsync(
        int customerId,
        List<OrderItemRequest> items,
        string shippingAddress,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating order for customer {CustomerId}", customerId);

        using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            // Validate customer
            var customer = await _customerRepository.GetByIdAsync(customerId, cancellationToken);
            if (customer == null)
                throw new InvalidOperationException($"Customer {customerId} not found");

            if (customer.IsDeleted)
                throw new InvalidOperationException($"Customer {customerId} is inactive");

            _logger.LogInformation("Customer {CustomerId} validated", customerId);

            // Calculate order totals
            decimal subtotal = 0;
            int totalQuantity = 0;
            var orderItems = new List<OrderItemDto>();

            foreach (var item in items)
            {
                var product = await _productRepository.GetByIdAsync(item.ProductId, cancellationToken);
                if (product == null)
                    throw new InvalidOperationException($"Product {item.ProductId} not found");

                // Check inventory
                bool isAvailable = await _inventoryService.IsAvailableAsync(item.ProductId, item.Quantity, cancellationToken);
                if (!isAvailable)
                    throw new InvalidOperationException($"Insufficient inventory for product {item.ProductId}");

                // Calculate line price
                decimal linePrice = _pricingService.CalculateLinePrice(product, item.Quantity, customer);
                subtotal += linePrice;
                totalQuantity += item.Quantity;

                orderItems.Add(new OrderItemDto
                {
                    ProductId = product.Id,
                    ProductName = product.Name,
                    Quantity = item.Quantity,
                    UnitPrice = linePrice / item.Quantity,
                    LineTotal = linePrice
                });

                _logger.LogInformation("Item {ProductId} added to order: quantity={Quantity}, linePrice={LinePrice}",
                    product.Id, item.Quantity, linePrice);
            }

            // Apply bulk discount
            subtotal = _pricingService.CalculateBulkDiscount(subtotal, totalQuantity);

            // Calculate charges
            decimal tax = _pricingService.CalculateTax(subtotal);
            decimal shippingCost = _pricingService.CalculateShippingCost(subtotal, shippingAddress);

            // Apply customer discount if available
            decimal discountAmount = 0;
            var discount = await _discountRepository.GetActiveDiscountForCustomerAsync(customerId, cancellationToken);
            if (discount != null)
            {
                discountAmount = subtotal * (discount.Percentage / 100m);
                _logger.LogInformation("Applied discount {DiscountPercentage}% for customer {CustomerId}",
                    discount.Percentage, customerId);
            }

            decimal finalTotal = subtotal + tax + shippingCost - discountAmount;
            if (finalTotal < 0)
            {
                _logger.LogWarning("Final total was negative, clamping to 0");
                finalTotal = 0;
            }

            // Create order entity
            var order = new Order
            {
                CustomerId = customerId,
                OrderDate = DateTime.UtcNow,
                Items = orderItems.Select(oi => new OrderItem
                {
                    ProductId = oi.ProductId,
                    ProductName = oi.ProductName,
                    Quantity = oi.Quantity,
                    UnitPrice = oi.UnitPrice,
                    LineTotal = oi.LineTotal
                }).ToList(),
                SubTotal = subtotal,
                Tax = tax,
                ShippingCost = shippingCost,
                DiscountAmount = discountAmount,
                FinalTotal = finalTotal,
                Status = OrderStatus.Pending.ToString(),
                ShippingAddress = shippingAddress,
                Notes = notes ?? string.Empty,
                CreatedBy = "OrderService",
                CreatedAt = DateTime.UtcNow
            };

            // Save order
            int orderId = await _orderRepository.AddAsync(order, cancellationToken);
            _logger.LogInformation("Order {OrderId} created successfully", orderId);

            // Decrement inventory
            foreach (var item in items)
            {
                await _inventoryService.DecrementAsync(item.ProductId, item.Quantity, cancellationToken);
            }

            // Update customer
            customer.TotalOrderCount++;
            customer.TotalSpent += finalTotal;
            customer.LastOrderDate = DateTime.UtcNow;
            customer.LoyaltyPoints += (int)_pricingService.CalculateLoyaltyPoints(finalTotal);

            await _customerRepository.UpdateAsync(customer, cancellationToken);
            _logger.LogInformation("Customer {CustomerId} updated with order statistics", customerId);

            await transaction.CommitAsync(cancellationToken);
            _logger.LogInformation("Order transaction committed for order {OrderId}", orderId);

            return new OrderCreateResponse
            {
                OrderId = orderId,
                CustomerId = customerId,
                OrderDate = order.OrderDate,
                Items = orderItems,
                SubTotal = subtotal,
                Tax = tax,
                ShippingCost = shippingCost,
                DiscountAmount = discountAmount,
                FinalTotal = finalTotal,
                Status = order.Status,
                ShippingAddress = shippingAddress
            };
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(ex, "Error creating order for customer {CustomerId}. Transaction rolled back.", customerId);
            throw;
        }
    }
}
