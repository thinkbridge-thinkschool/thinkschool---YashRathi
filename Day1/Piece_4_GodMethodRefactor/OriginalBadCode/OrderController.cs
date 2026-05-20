// =============================================================
// ORIGINAL AI-GENERATED BAD CODE — DO NOT MODIFY
// This file was generated intentionally with code smells
// for the refactoring exercise. See REFACTOR_NOTES.md for
// the list of 10+ smells identified before refactoring.
// =============================================================

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[ApiController]
[Route("api/[controller]")]
public class OrderController : ControllerBase
{
    private readonly AppDbContext _db;

    public OrderController(AppDbContext db)
    {
        _db = db;
    }

    // Smell 1: Returns object instead of typed response
    [HttpPost]
    public async Task<object> CreateOrder([FromBody] dynamic request)
    {
        // Smell 2: Empty catch block swallowing all exceptions
        try
        {
            // Smell 3: No null check on request — null deref bug
            int customerId = (int)request.customerId;
            var items = request.items;
            string address = (string)request.shippingAddress;

            // Smell 4: Synchronous EF call inside async method
            var customer = _db.Customers.Where(c => c.Id == customerId).FirstOrDefault();

            if (customer == null)
            {
                return BadRequest("customer not found");
            }

            decimal subtotal = 0;
            int totalItems = 0;

            // Smell 5: All business logic mixed directly into controller
            foreach (var item in items)
            {
                int productId = (int)item.productId;
                int quantity = (int)item.quantity;

                // Smell 4 (continued): More synchronous EF calls
                var product = _db.Products.Where(p => p.Id == productId).FirstOrDefault();

                if (product == null)
                {
                    return BadRequest("product not found");
                }

                var inventory = _db.Inventory.Where(i => i.ProductId == productId).FirstOrDefault();

                // Smell 6: Off-by-one bug — should be >= not >
                // This means if quantity == stock it still proceeds, causing oversell
                if (inventory.QuantityOnHand > quantity)
                {
                    return BadRequest("insufficient inventory");
                }

                // Smell 7: Magic numbers hardcoded inline (0.9, 10, 20, 0.85)
                if (quantity >= 20)
                    subtotal += product.Price * quantity * 0.80m;
                else if (quantity >= 10)
                    subtotal += product.Price * quantity * 0.85m;
                else if (quantity >= 5)
                    subtotal += product.Price * quantity * 0.9m;
                else
                    subtotal += product.Price * quantity;

                totalItems += quantity;

                // Smell 8: SaveChanges called multiple times inside loop — no transaction
                inventory.QuantityOnHand -= quantity;
                _db.SaveChanges();
            }

            // Smell 7 (continued): More magic numbers — tax rate, shipping costs
            decimal tax = subtotal * 0.08m;

            decimal shipping = 15.0m;
            if (address.Contains("CA") || address.Contains("NY"))
                shipping = 25.0m;
            else if (address.Contains("TX"))
                shipping = 18.75m;

            if (subtotal > 200)
                shipping = 0;

            decimal total = subtotal + tax + shipping;

            // Smell 9: Null dereference bug — customer.Discounts not checked for null
            if (customer.Discounts.Count > 0)
            {
                var discount = customer.Discounts.FirstOrDefault(d => d.Active);
                if (discount != null)
                    total -= total * (discount.Percentage / 100m);
            }

            // Smell 5 (continued): Order creation inline, no service/repository layer
            var order = new Order
            {
                CustomerId = customerId,
                FinalTotal = total,
                Status = "Pending",
                // Smell 10: DateTime.Now instead of DateTime.UtcNow — timezone bug
                CreatedAt = DateTime.Now,
                OrderDate = DateTime.Now,
                ShippingAddress = address
            };

            _db.Orders.Add(order);

            // Smell 8 (continued): Another SaveChanges outside the loop — no single transaction
            _db.SaveChanges();

            customer.TotalOrderCount = customer.TotalOrderCount + 1;
            customer.TotalSpent = customer.TotalSpent + total;
            // Smell 11: Yet another synchronous SaveChanges
            _db.SaveChanges();

            // Smell 1 (continued): Returns anonymous object — no typed DTO
            return Ok(new {
                orderId = order.Id,
                total = total,
                status = "Pending",
                msg = "order created ok"
            });
        }
        catch { }  // Smell 2: Empty catch #1 — exception silently swallowed

        try
        {
            // Smell 2: Empty catch #2
        }
        catch { }

        try
        {
            // Smell 2: Empty catch #3
        }
        catch { }

        try
        {
            // Smell 2: Empty catch #4
        }
        catch { }

        return StatusCode(500);
    }
}
