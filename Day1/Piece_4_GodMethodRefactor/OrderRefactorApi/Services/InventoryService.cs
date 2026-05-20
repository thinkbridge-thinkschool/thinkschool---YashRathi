using Microsoft.Extensions.Logging;
using OrderRefactorApi.Repositories;

namespace OrderRefactorApi.Services;

public interface IInventoryService
{
    Task<bool> IsAvailableAsync(int productId, int requestedQuantity, CancellationToken cancellationToken = default);
    Task DecrementAsync(int productId, int quantity, CancellationToken cancellationToken = default);
}

public class InventoryService : IInventoryService
{
    private readonly IInventoryRepository _inventoryRepository;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(IInventoryRepository inventoryRepository, ILogger<InventoryService> logger)
    {
        _inventoryRepository = inventoryRepository ?? throw new ArgumentNullException(nameof(inventoryRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> IsAvailableAsync(int productId, int requestedQuantity, CancellationToken cancellationToken = default)
    {
        try
        {
            var inventory = await _inventoryRepository.GetByProductIdAsync(productId, cancellationToken);
            
            if (inventory == null)
            {
                _logger.LogWarning("Inventory not found for product {ProductId}", productId);
                return false;
            }

            bool isAvailable = inventory.QuantityOnHand >= requestedQuantity;
            
            _logger.LogInformation(
                "Inventory check for product {ProductId}: requested={RequestedQuantity}, available={AvailableQuantity}, result={IsAvailable}",
                productId, requestedQuantity, inventory.QuantityOnHand, isAvailable);

            return isAvailable;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking inventory for product {ProductId}", productId);
            throw;
        }
    }

    public async Task DecrementAsync(int productId, int quantity, CancellationToken cancellationToken = default)
    {
        try
        {
            var inventory = await _inventoryRepository.GetByProductIdAsync(productId, cancellationToken);

            if (inventory == null)
            {
                throw new InvalidOperationException($"Inventory not found for product {productId}");
            }

            if (inventory.QuantityOnHand < quantity)
            {
                throw new InvalidOperationException(
                    $"Insufficient inventory for product {productId}. Available: {inventory.QuantityOnHand}, Requested: {quantity}");
            }

            inventory.QuantityOnHand -= quantity;
            inventory.LastModified = DateTime.UtcNow;
            inventory.ModifiedBy = "OrderService";

            await _inventoryRepository.UpdateAsync(inventory, cancellationToken);

            _logger.LogInformation(
                "Inventory decremented for product {ProductId}: quantity={DecrementedQuantity}, remaining={RemainingQuantity}",
                productId, quantity, inventory.QuantityOnHand);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error decrementing inventory for product {ProductId} by {Quantity}", productId, quantity);
            throw;
        }
    }
}
