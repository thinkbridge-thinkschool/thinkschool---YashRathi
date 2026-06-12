using System.Text.Json;
using QuotesApi.Abstractions;
using QuotesApi.Data;
using QuotesApi.Models;

namespace QuotesApi.Commands;

public sealed class CreateQuoteCommandHandler
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;

    public CreateQuoteCommandHandler(AppDbContext db, IClock clock)
    {
        _db = db;
        _clock = clock;
    }

    // Writes the Quote row AND an OutboxMessage row in a single EF transaction.
    // Either both are committed or neither — the queue publish can never diverge from the DB write.
    public async Task<Result<int>> HandleAsync(CreateQuoteCommand command, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var result = Quote.Create(command.Author, command.Text, now, command.OwnerId);
        if (!result.IsSuccess)
            return Result<int>.Fail(result.Error!.Message);

        var quote = result.Value!;

        var payload = JsonSerializer.Serialize(new
        {
            quote.Author,
            quote.Text,
            CreatedAt = now,
            quote.OwnerId
        });

        var outboxMessage = OutboxMessage.Create("quote.created", payload, now);

        // BEGIN TRANSACTION
        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        _db.Quotes.Add(quote);
        _db.OutboxMessages.Add(outboxMessage);
        await _db.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);
        // END TRANSACTION — both rows committed atomically.
        // If we crash after commit, the outbox row is present → relay publishes on next poll.
        // If we crash before commit, neither row exists → no phantom message.

        return Result<int>.Ok(quote.Id);
    }
}
