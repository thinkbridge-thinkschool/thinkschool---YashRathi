Write a deliberately bad ASP.NET Core 10 OrderController.cs for a Web API project.

Requirements:
- around 300 lines
- one giant POST /api/orders action
- mixes business logic, EF Core data access, validation, pricing logic, inventory logic, and HTTP response shaping inline
- synchronous EF calls inside async action
- returns object instead of typed responses
- 4 empty catch { } blocks swallowing exceptions
- duplicate code
- magic numbers
- deeply nested if statements
- poor naming
- nullable bugs
- at least one off-by-one bug
- one null reference risk
- no dependency injection abstractions
- no services
- no repositories
- tightly coupled code
- hardcoded strings
- poor error handling
- no unit tests
- no logging
- use AppDbContext directly inside controller
- intentionally difficult to maintain but still compilable

Do NOT refactor it.
Do NOT explain anything.
Generate only the code.