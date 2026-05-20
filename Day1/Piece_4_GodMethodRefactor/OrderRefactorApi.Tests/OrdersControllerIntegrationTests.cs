using Xunit;
using Moq;
using Microsoft.Extensions.Logging;
using OrderRefactorApi.Services;
using OrderRefactorApi.Repositories;
using OrderRefactorApi.Models;
using OrderRefactorApi.Dtos;
using OrderRefactorApi.Data;
using Microsoft.EntityFrameworkCore;

namespace OrderRefactorApi.Tests;

public class OrdersControllerIntegrationTests
{
    private OrderService CreateService(
        Mock<IOrderRepository>? orderRepo = null,
        Mock<IProductRepository>? productRepo = null,
        Mock<ICustomerRepository>? customerRepo = null,
        Mock<IInventoryService>? inventoryService = null,
        Mock<IDiscountRepository>? discountRepo = null,
        Mock<IPricingService>? pricingService = null)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        var db = new AppDbContext(options);

        return new OrderService(
            orderRepo?.Object ?? Mock.Of<IOrderRepository>(),
            productRepo?.Object ?? Mock.Of<IProductRepository>(),
            customerRepo?.Object ?? Mock.Of<ICustomerRepository>(),
            inventoryService?.Object ?? Mock.Of<IInventoryService>(),
            discountRepo?.Object ?? Mock.Of<IDiscountRepository>(),
            pricingService?.Object ?? Mock.Of<IPricingService>(),
            db,
            Mock.Of<ILogger<OrderService>>());
    }

    // Integration-style test: deleted customer is rejected
    [Fact]
    public async Task CreateOrderAsync_ThrowsWhenCustomerIsDeleted()
    {
        var customerRepo = new Mock<ICustomerRepository>();
        customerRepo
            .Setup(r => r.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Customer
            {
                Id = 1,
                Type = CustomerType.Standard,
                IsDeleted = true  // deleted customer
            });

        var service = CreateService(customerRepo: customerRepo);
        var items = new List<OrderItemRequest>
        {
            new OrderItemRequest { ProductId = 1, Quantity = 1 }
        };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.CreateOrderAsync(1, items, "123 Main St", null));
    }
}