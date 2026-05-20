using OrderRefactorApi.Models;

namespace OrderRefactorApi.Repositories;

public interface IProductRepository
{
    Task<Product?> GetByIdAsync(int productId, CancellationToken cancellationToken = default);
}

public interface ICustomerRepository
{
    Task<Customer?> GetByIdAsync(int customerId, CancellationToken cancellationToken = default);
    Task UpdateAsync(Customer customer, CancellationToken cancellationToken = default);
}

public interface IInventoryRepository
{
    Task<Inventory?> GetByProductIdAsync(int productId, CancellationToken cancellationToken = default);
    Task UpdateAsync(Inventory inventory, CancellationToken cancellationToken = default);
}

public interface IDiscountRepository
{
    Task<Discount?> GetActiveDiscountForCustomerAsync(int customerId, CancellationToken cancellationToken = default);
}

public interface IOrderRepository
{
    Task<int> AddAsync(Order order, CancellationToken cancellationToken = default);
}
