using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using QuotesApi.Middleware;

namespace Quotes.Tests.Unit;

public class ExceptionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_HappyPath_CallsNext()
    {
        var logger = Substitute.For<ILogger<ExceptionMiddleware>>();
        var called = false;
        var middleware = new ExceptionMiddleware(_ => { called = true; return Task.CompletedTask; }, logger);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        called.Should().BeTrue();
        context.Response.StatusCode.Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_WhenNextThrows_Returns500WithErrorBody()
    {
        var logger = Substitute.For<ILogger<ExceptionMiddleware>>();
        var middleware = new ExceptionMiddleware(
            _ => throw new InvalidOperationException("boom"),
            logger);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.Should().Be(500);
        context.Response.Body.Length.Should().BeGreaterThan(0);
    }
}
