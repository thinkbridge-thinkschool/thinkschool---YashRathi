using Microsoft.EntityFrameworkCore;
using QuotesApi.Abstractions;
using QuotesApi.Data;
using QuotesApi.Infrastructure;
using QuotesApi.Repositories;
namespace QuotesApi.Extensions;
public static class InfrastructureExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseSqlite("Data Source=quotes.db");
        });
        services.AddScoped<IQuoteRepository, QuoteRepository>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddTransient<IRequestLogger, RequestLogger>();
        return services;
    }
}