using System.Data;
using System.Globalization;
using Dapper;
using Microsoft.EntityFrameworkCore;
using QuotesApi.Data;
using QuotesApi.ReadModels;

namespace QuotesApi.Queries;

// EF equivalent (GetQuotesQueryHandler) sends:
//
//   SELECT "q"."Id", "q"."Author", "q"."Text", "q"."CreatedAt"
//   FROM "Quotes" AS "q"
//   WHERE NOT ("q"."IsDeleted")
//   ORDER BY "q"."Id"
//   LIMIT @__p_1 OFFSET @__p_0
//
// Dapper sends this verbatim — no LINQ expression-tree compilation, no model-snapshot
// lookup, no result-set shaper construction per call:
//
//   SELECT Id, Author, Text, CreatedAt
//   FROM Quotes
//   WHERE IsDeleted = 0
//   ORDER BY Id
//   LIMIT @Size OFFSET @Offset
//
// Logical plan and index usage are identical. The observable delta is pure ORM plumbing.

public sealed class GetQuotesDapperQueryHandler
{
    private readonly AppDbContext _context;

    public GetQuotesDapperQueryHandler(AppDbContext context) => _context = context;

    public async Task<IReadOnlyList<QuoteListItem>> HandleAsync(
        GetQuotesQuery query, CancellationToken cancellationToken)
    {
        var conn = _context.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open)
            await _context.Database.OpenConnectionAsync(cancellationToken);

        // SQLite's ADO.NET driver surfaces INTEGER as long and DATETIME as string.
        // We map to a private mutable row type (Dapper uses property setters, which
        // accept native ADO.NET types) and project to QuoteListItem afterwards.
        var rows = await conn.QueryAsync<QuoteRow>(
            "SELECT Id, Author, Text, CreatedAt FROM Quotes WHERE IsDeleted = 0 ORDER BY Id LIMIT @Size OFFSET @Offset",
            new { Size = query.Size, Offset = (query.Page - 1) * query.Size });

        return rows
            .Select(r => new QuoteListItem(
                (int)r.Id,
                r.Author,
                r.Text,
                DateTimeOffset.Parse(r.CreatedAt, null, DateTimeStyles.RoundtripKind)))
            .ToList();
    }

    // SQLite INTEGER → long, TEXT → string. These match the ADO.NET types
    // so Dapper's IL property-setter path works without custom type handlers.
    private sealed class QuoteRow
    {
        public long Id { get; set; }
        public string Author { get; set; } = string.Empty;
        public string Text { get; set; } = string.Empty;
        public string CreatedAt { get; set; } = string.Empty;
    }
}
