using Microsoft.EntityFrameworkCore;
using OrderRefactorApi.Data;
using OrderRefactorApi.Models;

namespace OrderRefactorApi.Repositories;

public class ProductRepository : IProductRepository
{
    private readonly AppDbContext _context;

    public ProductRepository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Product?> GetByIdAsync(int productId, CancellationToken cancellationToken = default)
    {
        return await _context.Products
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken);
    }
}

public class CustomerRepository : ICustomerRepository
{
    private readonly AppDbContext _context;

    public CustomerRepository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Customer?> GetByIdAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == customerId, cancellationToken);
    }

    public async Task UpdateAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        _context.Customers.Update(customer);
        await _context.SaveChangesAsync(cancellationToken);
    }
}

public class InventoryRepository : IInventoryRepository
{
    private readonly AppDbContext _context;

    public InventoryRepository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Inventory?> GetByProductIdAsync(int productId, CancellationToken cancellationToken = default)
    {
        return await _context.Inventory
            .FirstOrDefaultAsync(i => i.ProductId == productId, cancellationToken);
    }

    public async Task UpdateAsync(Inventory inventory, CancellationToken cancellationToken = default)
    {
        _context.Inventory.Update(inventory);
        await _context.SaveChangesAsync(cancellationToken);
    }
}

public class DiscountRepository : IDiscountRepository
{
    private readonly AppDbContext _context;

    public DiscountRepository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<Discount?> GetActiveDiscountForCustomerAsync(int customerId, CancellationToken cancellationToken = default)
    {
        return await _context.Discounts
            .FirstOrDefaultAsync(d => d.CustomerId == customerId && d.Active, cancellationToken);
    }
}

public class OrderRepository : IOrderRepository
{
    private readonly AppDbContext _context;

    public OrderRepository(AppDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public async Task<int> AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        _context.Orders.Add(order);
        await _context.SaveChangesAsync(cancellationToken);
        return order.Id;
    }
}
