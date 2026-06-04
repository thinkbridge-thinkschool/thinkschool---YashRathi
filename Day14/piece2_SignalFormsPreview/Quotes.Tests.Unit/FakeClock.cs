using QuotesApi.Abstractions;

namespace Quotes.Tests.Unit;

internal sealed class FakeClock : IClock
{
    private DateTimeOffset _now;

    public FakeClock(DateTimeOffset now) => _now = now;

    public DateTimeOffset UtcNow => _now;

    public void Advance(TimeSpan by) => _now += by;

    public void Set(DateTimeOffset to) => _now = to;
}
