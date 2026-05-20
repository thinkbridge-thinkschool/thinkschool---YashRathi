using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Endpoints;
using QuotesApi.Extensions;
using QuotesApi.Middleware;
var builder = WebApplication.CreateBuilder(args);
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();
var app = builder.Build();
app.UseMiddleware<ExceptionMiddleware>();
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider
        .GetRequiredService<AppDbContext>();
    dbContext.Database.Migrate();
}
app.MapQuoteEndpoints();
app.Run();