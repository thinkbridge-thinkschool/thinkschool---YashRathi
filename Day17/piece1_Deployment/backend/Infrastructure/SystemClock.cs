using System.Diagnostics.CodeAnalysis;
using QuotesApi.Abstractions;

namespace QuotesApi.Infrastructure;

[ExcludeFromCodeCoverage]
public class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
