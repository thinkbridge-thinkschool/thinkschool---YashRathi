# Order API - POST Request Examples

## Quick Start - Test the Refactored API

### Prerequisites
1. Ensure the database is seeded with test data
2. Start the application: `dotnet run`
3. Use the following POST requests to test

---

## Example 1: Simple Order (Standard Customer)

**Endpoint:** `POST /api/orders`

**Request Body:**
```json
{
  "customerId": 1,
  "items": [
    {
      "productId": 1,
      "quantity": 5
    }
  ],
  "shippingAddress": "123 Main Street, New York, NY 10001",
  "notes": "Please deliver on weekdays"
}
```

**Expected Response (201 Created):**
```json
{
  "orderId": 1,
  "customerId": 1,
  "orderDate": "2025-05-19T14:30:00Z",
  "items": [
    {
      "productId": 1,
      "productName": "Laptop",
      "quantity": 5,
      "unitPrice": 900.00,
      "lineTotal": 4500.00
    }
  ],
  "subTotal": 4500.00,
  "tax": 360.00,
  "shippingCost": 12.50,
  "discountAmount": 0.00,
  "finalTotal": 4872.50,
  "status": "Pending",
  "shippingAddress": "123 Main Street, New York, NY 10001"
}
```

---

## Example 2: Multiple Items with Bulk Discount

**Endpoint:** `POST /api/orders`

**Request Body:**
```json
{
  "customerId": 1,
  "items": [
    {
      "productId": 1,
      "quantity": 30
    },
    {
      "productId": 2,
      "quantity": 25
    }
  ],
  "shippingAddress": "456 Oak Avenue, Los Angeles, CA 90001"
}
```

**Key Points:**
- 30 + 25 = 55 total items (qualifies for bulk discount tier 1)
- Shipping address in CA gets CA shipping rate
- High order value gets shipping discount

---

## Example 3: Premium Customer Order

**Endpoint:** `POST /api/orders`

**Request Body:**
```json
{
  "customerId": 2,
  "items": [
    {
      "productId": 1,
      "quantity": 10
    }
  ],
  "shippingAddress": "789 Pine Road, Houston, TX 77001",
  "notes": "Premium customer order"
}
```

**Key Points:**
- Premium customers get special discounts
- Electronics category: 25% discount
- TX shipping: $18.75
- Higher loyalty points earned

---

## Example 4: Large Order (100+ items - Maximum Bulk Discount)

**Endpoint:** `POST /api/orders`

**Request Body:**
```json
{
  "customerId": 1,
  "items": [
    {
      "productId": 1,
      "quantity": 60
    },
    {
      "productId": 2,
      "quantity": 50
    }
  ],
  "shippingAddress": "999 Commerce Drive, Chicago, IL 60601"
}
```

**Key Points:**
- 110 total items (qualifies for maximum bulk discount: 30% off)
- Free shipping (order > $200)
- Max loyalty points earned

---

## Error Scenarios - Test Validation

### Error 1: Missing Customer ID

**Request Body:**
```json
{
  "customerId": 0,
  "items": [{"productId": 1, "quantity": 5}],
  "shippingAddress": "123 Main St"
}
```

**Expected Response (400 Bad Request):**
```json
{
  "message": "Invalid customer ID",
  "code": "INVALID_CUSTOMER_ID"
}
```

---

### Error 2: Empty Items List

**Request Body:**
```json
{
  "customerId": 1,
  "items": [],
  "shippingAddress": "123 Main St"
}
```

**Expected Response (400 Bad Request):**
```json
{
  "message": "Order must contain at least one item",
  "code": "NO_ITEMS"
}
```

---

### Error 3: Missing Shipping Address

**Request Body:**
```json
{
  "customerId": 1,
  "items": [{"productId": 1, "quantity": 5}],
  "shippingAddress": ""
}
```

**Expected Response (400 Bad Request):**
```json
{
  "message": "Shipping address is required",
  "code": "MISSING_ADDRESS"
}
```

---

### Error 4: Invalid Quantity

**Request Body:**
```json
{
  "customerId": 1,
  "items": [{"productId": 1, "quantity": 0}],
  "shippingAddress": "123 Main St"
}
```

**Expected Response (400 Bad Request):**
```json
{
  "message": "Quantity must be between 1 and 9999",
  "code": "INVALID_QUANTITY"
}
```

---

### Error 5: Non-existent Product

**Request Body:**
```json
{
  "customerId": 1,
  "items": [{"productId": 999, "quantity": 5}],
  "shippingAddress": "123 Main St"
}
```

**Expected Response (400 Bad Request):**
```json
{
  "message": "Product 999 not found",
  "code": "VALIDATION_ERROR"
}
```

---

### Error 6: Insufficient Inventory

**Request Body:**
```json
{
  "customerId": 1,
  "items": [{"productId": 1, "quantity": 10000}],
  "shippingAddress": "123 Main St"
}
```

**Expected Response (400 Bad Request):**
```json
{
  "message": "Insufficient inventory for product 1",
  "code": "VALIDATION_ERROR"
}
```

---

## Testing with cURL

```bash
# Test basic order creation
curl -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": 1,
    "items": [{"productId": 1, "quantity": 5}],
    "shippingAddress": "123 Main St"
  }'

# Test with multiple items
curl -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": 1,
    "items": [
      {"productId": 1, "quantity": 20},
      {"productId": 2, "quantity": 30}
    ],
    "shippingAddress": "456 Oak Ave, CA",
    "notes": "Bulk order"
  }'
```

---

## Testing with Postman

1. Create a new POST request
2. URL: `http://localhost:5000/api/orders`
3. Header: `Content-Type: application/json`
4. Select "Body" → "raw" → "JSON"
5. Paste any of the request bodies above
6. Click "Send"

---

## Database Seeding

Make sure your database has test data. Example seed script locations:
- SQLite database: `orders.db` (created automatically on first run)
- Test data should include:
  - At least 2 customers (1 Standard, 1 Premium)
  - At least 2 products with different categories
  - Inventory records for each product
  - Optional: Discount records

---

## Verification Checklist After Refactoring

✅ **Original OrderController issues fixed:**
- ✅ No more empty catch blocks
- ✅ All EF calls are async (`await` used throughout)
- ✅ Typed response (OrderCreateResponse DTO)
- ✅ Dependency injection abstractions (services, repositories)
- ✅ Off-by-one bug fixed (inventory check: `<` not `< -1`)
- ✅ Null reference bug fixed (proper null checks)
- ✅ Transactional consistency (single transaction wraps entire operation)
- ✅ Proper logging at each step
- ✅ Magic numbers extracted to constants
- ✅ Duplicate code removed
- ✅ Deeply nested logic extracted to PricingService
- ✅ HTTP responses properly typed
- ✅ Cancellation token support throughout
- ✅ Tests provided (unit + integration)

---

## Running Tests

```bash
# Run all tests
dotnet test OrderRefactorApi/Tests/OrderRefactorApi.Tests.csproj

# Run only unit tests
dotnet test OrderRefactorApi/Tests/OrderRefactorApi.Tests.csproj -k "Unit"

# Run only integration tests
dotnet test OrderRefactorApi/Tests/OrderRefactorApi.Tests.csproj -k "Integration"

# Run with verbose output
dotnet test OrderRefactorApi/Tests/OrderRefactorApi.Tests.csproj -v detailed
```

---

## Performance Expectations

**Refactored Version:**
- Proper async/await prevents thread pool starvation
- Single database transaction ensures consistency
- Logging helps identify bottlenecks
- Testable architecture enables performance testing
- Dependency injection allows for caching layers to be added

---

## API Contract

### Success Response (201 Created)
- HTTP Status: 201
- Headers include: `Location: /api/orders/{orderId}`
- Body: OrderCreateResponse DTO with all order details

### Error Responses
- 400 Bad Request: Validation or business logic errors
- 404 Not Found: Customer/Product not found
- 500 Internal Server Error: Unexpected errors

---

## Next Steps

1. ✅ Generated bad code (DONE)
2. ✅ Documented smells in REFACTOR_NOTES.md (DONE)
3. ✅ Refactored into layers (DONE)
4. ✅ Added unit tests (DONE)
5. ✅ Added integration tests (DONE)
6. Run tests and verify all pass
7. Commit to git with CI badge
