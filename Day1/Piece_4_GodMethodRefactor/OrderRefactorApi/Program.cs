using OrderRefactorApi.Repositories;
using OrderRefactorApi.Services;
using Microsoft.EntityFrameworkCore;
using OrderRefactorApi.Data;
using OrderRefactorApi.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddLogging();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=orders.db"));

// Register repositories
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IInventoryRepository, InventoryRepository>();
builder.Services.AddScoped<IDiscountRepository, DiscountRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();

// Register services
builder.Services.AddScoped<IPricingService, PricingService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IOrderService, OrderService>();

var app = builder.Build();

// Seed data
await app.SeedDataAsync();

app.MapControllers();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
