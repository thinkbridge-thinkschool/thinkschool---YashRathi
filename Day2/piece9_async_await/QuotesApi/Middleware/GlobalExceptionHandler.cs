using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace QuotesApi.Middleware;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        // 499 Client Closed Request: client disconnected before the server finished.
        // OperationCanceledException propagates from repository/EF when HttpContext.RequestAborted fires.
        if (exception is OperationCanceledException)
        {
            _logger.LogInformation("Request cancelled by client.");
            httpContext.Response.StatusCode = 499;
            return true;
        }

        _logger.LogError(exception, "An unhandled exception occurred.");

        var inner = exception.InnerException?.Message;
        var problemDetails = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Server Error",
            Detail = inner is not null ? $"{exception.Message} | Inner: {inner}" : exception.Message
        };

        httpContext.Response.StatusCode = problemDetails.Status.Value;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }
}
