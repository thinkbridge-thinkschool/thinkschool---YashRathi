using System.Diagnostics;

namespace QuotesApi;

public static class Telemetry
{
    public const string ServiceName = "QuotesApi";

    public static readonly ActivitySource Source = new(ServiceName);
}
