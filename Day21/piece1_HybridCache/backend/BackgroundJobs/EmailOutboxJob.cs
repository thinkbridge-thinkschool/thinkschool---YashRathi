namespace QuotesApi.BackgroundJobs;

public sealed record EmailOutboxJob(string To, string Subject, string Body);
