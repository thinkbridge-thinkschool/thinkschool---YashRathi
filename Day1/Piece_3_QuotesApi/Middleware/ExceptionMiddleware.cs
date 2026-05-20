using Microsoft.AspNetCore.Mvc;

namespace QuotesApi.Middleware;

public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(
        RequestDelegate next,
        ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            _logger.LogError(
                exception,
                "Unhandled exception occurred");

            context.Response.StatusCode = 500;

            var problem = new ProblemDetails
            {
                Title = "Server Error",
                Detail = exception.Message,
                Status = 500
            };

            await context.Response.WriteAsJsonAsync(problem);
        }
    }
}