using Microsoft.AspNetCore.Mvc;
using OrderRefactorApi.Dtos;
using OrderRefactorApi.Services;

namespace OrderRefactorApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;
    private readonly ILogger<OrdersController> _logger;

    public OrdersController(IOrderService orderService, ILogger<OrdersController> logger)
    {
        _orderService = orderService ?? throw new ArgumentNullException(nameof(orderService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a new order for a customer
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<OrderCreateResponse>> CreateOrder(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Received order creation request for customer {CustomerId}", request.CustomerId);

            // Input validation
            if (request == null)
            {
                _logger.LogWarning("Order request is null");
                return BadRequest(new ErrorResponse { Message = "Order request cannot be null", Code = "INVALID_REQUEST" });
            }

            if (request.Items == null || request.Items.Count == 0)
            {
                _logger.LogWarning("Order items are empty");
                return BadRequest(new ErrorResponse { Message = "Order must contain at least one item", Code = "NO_ITEMS" });
            }

            if (request.CustomerId <= 0)
            {
                _logger.LogWarning("Invalid customer ID: {CustomerId}", request.CustomerId);
                return BadRequest(new ErrorResponse { Message = "Invalid customer ID", Code = "INVALID_CUSTOMER_ID" });
            }

            if (string.IsNullOrWhiteSpace(request.ShippingAddress))
            {
                _logger.LogWarning("Shipping address is empty");
                return BadRequest(new ErrorResponse { Message = "Shipping address is required", Code = "MISSING_ADDRESS" });
            }

            foreach (var item in request.Items)
            {
                if (item.ProductId <= 0)
                {
                    _logger.LogWarning("Invalid product ID: {ProductId}", item.ProductId);
                    return BadRequest(new ErrorResponse { Message = "Invalid product ID", Code = "INVALID_PRODUCT_ID" });
                }

                if (item.Quantity <= 0 || item.Quantity > 9999)
                {
                    _logger.LogWarning("Invalid quantity: {Quantity}", item.Quantity);
                    return BadRequest(new ErrorResponse { Message = "Quantity must be between 1 and 9999", Code = "INVALID_QUANTITY" });
                }
            }

            _logger.LogInformation("Validation passed for order creation");

            // Create order
            var response = await _orderService.CreateOrderAsync(
                request.CustomerId,
                request.Items,
                request.ShippingAddress,
                request.Notes,
                cancellationToken);

            _logger.LogInformation("Order {OrderId} created successfully", response.OrderId);

            return CreatedAtAction(nameof(CreateOrder), new { id = response.OrderId }, response);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Order validation failed: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status400BadRequest,
                new ErrorResponse { Message = ex.Message, Code = "VALIDATION_ERROR" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating order for customer {CustomerId}", request?.CustomerId);
            return StatusCode(StatusCodes.Status500InternalServerError,
                new ErrorResponse { Message = "An error occurred while processing your order", Code = "SERVER_ERROR" });
        }
    }
}

public class ErrorResponse
{
    public string Message { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
}
