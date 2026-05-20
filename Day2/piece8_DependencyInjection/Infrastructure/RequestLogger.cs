namespace QuotesApi.Infrastructure;
public interface IRequestLogger
{
    void Log(string message);
}
public class RequestLogger : IRequestLogger
{
    public void Log(string message)
    {
        Console.WriteLine($"[{Guid.NewGuid()}] {message}");
    }
}