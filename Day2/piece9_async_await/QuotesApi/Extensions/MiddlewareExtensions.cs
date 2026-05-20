using Microsoft.AspNetCore.Builder;
using QuotesApi.Middleware;

namespace QuotesApi.Extensions;

public static class MiddlewareExtensions
{
    public static IApplicationBuilder UseCustomExceptionMiddleware(
        this IApplicationBuilder app)
    {
        app.UseMiddleware<ExceptionMiddleware>();

        return app;
    }
}