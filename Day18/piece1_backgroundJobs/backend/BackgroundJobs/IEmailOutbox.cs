namespace QuotesApi.BackgroundJobs;

public interface IEmailOutbox
{
    ValueTask EnqueueAsync(EmailOutboxJob job, CancellationToken ct = default);
}
