# Piece 4 — God Method Refactor

## Task Summary

Refactored a deliberately bad `OrderController.cs` (AI-generated with 10+ code smells) into a clean, layered ASP.NET Core 10 API.

## Project Structure

```
Piece_4_GodMethodRefactor/
├── INITIAL_PROMPT.md              ← Prompt used to generate the bad code
├── REFACTOR_NOTES.md              ← 10+ smells identified with fixes
├── POST_REQUEST_EXAMPLES.md       ← How to test the API
├── OriginalBadCode/
│   └── OrderController.cs         ← AI-generated original (do not modify)
├── OrderRefactorApi/              ← Refactored clean project
│   ├── Controllers/               ← Thin HTTP layer only
│   ├── Services/                  ← Business logic
│   ├── Repositories/              ← Data access layer
│   ├── Models/                    ← Domain entities
│   ├── Dtos/                      ← Typed request/response shapes
│   ├── Data/                      ← EF Core DbContext
│   ├── Extensions/                ← Seed data
│   └── Constants/                 ← No more magic numbers
└── OrderRefactorApi.Tests/        ← 3 unit tests + 1 integration test
```

## How to Run

```bash
cd OrderRefactorApi
dotnet run
```

API starts at `http://localhost:5000`

## How to Test POST /api/orders

```bash
curl -X POST http://localhost:5000/api/orders \
  -H "Content-Type: application/json" \
  -d '{
    "customerId": 1,
    "items": [{"productId": 1, "quantity": 5}],
    "shippingAddress": "123 Main Street, New York, NY 10001"
  }'
```

## How to Run Tests

```bash
cd OrderRefactorApi.Tests
dotnet test
```

Expected: **4 tests passed** (3 unit + 1 integration)

## What Was Fixed

 

Problem 1: Large Controller Method
The CreateOrder action contains validation, business logic, pricing calculations, inventory updates and database access in one method.

Consequence:
Any future pricing change would require touching a very risky method and increases chances of breaking flow of unrelated logic.

Fix:
divide business logic into OrderService and data access it into Repository layer.


Problem 2: Synchronous EF Calls Inside Async Method
Uses synchronous EF queries like FirstOrDefault() and SaveChanges() inside an async controller action.

Consequence:
Under multiple concurrent requests this can slow the API because database calls are blocking the request thread.

Fix:
Replace with async EF methods such as FirstOrDefaultAsync() and SaveChangesAsync().

Problem 3: Empty Catch Blocks
Several catch blocks swallow exceptions without logging or handling them.

Consequence:
Errors become invisible and debugging becomes extremely difficult.

Fix:
I Use specific exception handling with ILogger logging and rethrow exceptions when necessary.
Real-world impact:
If production failures happen, there would be no logs or stack traces available to investigate the issue.

Problem 4: Mixed Responsibilities
Controller handles HTTP logic, pricing rules, inventory management, validation and persistence.

Consequence:
Violates Single Responsibility Principle and tightly couples multiple layers.

Fix:
Separate into Controller, Service and Repository layers using dependency injection.


Problem 5: Magic Numbers
Hardcoded discount percentages, tax rates and shipping costs are scattered throughout the code.

Consequence:
Business rules become difficult to update and error-prone.

Fix:
Move constants into configuration or dedicated pricing logic classes.


Problem 6 : Deeply Nested Conditional Logic
Complex nested if/else blocks for pricing logic.

Consequence:
difficult to read, maintain and test.

Fix:
Extract pricing logic into smaller service methods.


Problem 7: Duplicate Inventory Update Logic
Inventory update logic appears multiple times.

Consequence:
Risk of inconsistent behavior and duplicated bugs.

Fix:
Centralize inventory updates inside service methods.


Problem 8: Null Reference Risk
Discount object is used without checking for null.

Consequence:
Can cause runtime NullReferenceException.

Fix:
Add null validation before accessing properties.


Problem 9: Inventory Bug off by one
Inventory validation uses item.Quantity - 1.

Consequence:
Orders may incorrectly pass inventory validation.

Fix:
Compare inventory directly against requested quantity.


Problem 10: No Cancellation Token Usage
Database operations do not support cancellation tokens.

Consequence:
Requests cannot cancel long-running operations cleanly.

Fix:
Pass CancellationToken through controller, service and repository layers.
