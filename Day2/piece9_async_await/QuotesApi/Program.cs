using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<QuoteDbContext>();
    dbContext.Database.Migrate();
}

app.MapQuoteEndpoints();
app.MapCollectionEndpoints();

app.Run();

// Required so WebApplicationFactory<Program> can reference this assembly's entry point from tests.
public partial class Program { }
