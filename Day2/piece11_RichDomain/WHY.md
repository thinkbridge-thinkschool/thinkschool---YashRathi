# Why the Rich Domain Model Matters

The anemic `Quote` was just a data bag: public setters, no rules, no
knowledge of what made it valid. Every caller that constructed a `Quote`
had to remember to validate it themselves — and they had to get it right
every single time.

The rich model moves the rules inside the entity. `Quote.Create(author,
text, createdAt)` is the only door in. If the text is empty, or the
author is 250 characters, the factory returns a `DomainError` before a
`Quote` object ever exists. No caller can accidentally build a `Quote`
that breaks the invariants — not an HTTP endpoint, not a background job,
not a unit test.

Three concrete wins:

1. **Single source of truth.** The "Author ≤ 200 chars" rule lives in
   exactly one place. Change it there, and every entry point picks it up
   automatically.

2. **Immutability by design.** `Text` has a `private set`. Nothing outside
   the class can change it after creation — no accidental mutation, no
   PATCH endpoint silently wiping a quote's content.

3. **Soft delete is explicit.** `SoftDelete()` is a named operation with
   intent. It can never be confused with a hard delete by a future
   developer who just calls `Remove()`.

**Bug the anemic model would have shipped:**  
Imagine a bulk-import endpoint added three months later:

```csharp
var quote = new Quote { Author = csvRow.Author, Text = csvRow.Text };
```

The developer forgot to copy the validation from the original endpoint.
A 1 500-character text sails into the database, and your API starts
returning truncation errors at read time — in production, on user data.
With the rich model, `Quote.Create` rejects that row at the boundary.
The bug never reaches the database.
