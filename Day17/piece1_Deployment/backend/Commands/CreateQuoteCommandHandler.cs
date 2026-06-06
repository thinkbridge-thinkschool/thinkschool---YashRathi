using QuotesApi.Abstractions;
using QuotesApi.Models;
using QuotesApi.Repositories;

namespace QuotesApi.Commands;

public sealed class CreateQuoteCommandHandler
{
    private readonly IQuoteRepository _repository;
    private readonly IClock _clock;

    public CreateQuoteCommandHandler(IQuoteRepository repository, IClock clock)
    {
        _repository = repository;
        _clock = clock;
    }

    // Returns the new quote's ID, or a domain error if validation fails.
    public async Task<Result<int>> HandleAsync(CreateQuoteCommand command, CancellationToken cancellationToken)
    {
        var result = Quote.Create(command.Author, command.Text, _clock.UtcNow, command.OwnerId);
        if (!result.IsSuccess)
            return Result<int>.Fail(result.Error!.Message);

        var created = await _repository.AddAsync(result.Value!, cancellationToken);
        return Result<int>.Ok(created.Id);
    }
}
