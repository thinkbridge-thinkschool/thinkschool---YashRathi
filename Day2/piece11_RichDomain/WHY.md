# Why Rich Domain Model is Important

The old `Quote` model was only storing data. It had public setters, so anyone could change values without any checks. Every developer had to remember to write validation code again and again, and sometimes they could forget it.

The rich domain model solves this problem by keeping all rules inside the `Quote` class itself.

`Quote.Create(author, text, createdAt)` becomes the only way to create a quote.

If the author name is too long or the text is empty, it immediately returns an error and stops invalid data from being created.

---

## Benefits of Rich Domain Model

### 1. Single Place for Validation

All rules are written in one place only.

Example:
- Author name must be less than 200 characters.
- Quote text should not be empty.

If the rule changes later, we update it only once, and it works everywhere automatically.

---

### 2. Safer Data with Immutability

Properties like `Text` use `private set`.

That means no outside code can accidentally change the quote after it is created.

This protects data from unwanted updates or bugs.

---

### 3. Clear Soft Delete

Instead of directly removing data, we use a method like `SoftDelete()`.

This clearly shows that the quote is only marked as deleted, not permanently removed.

It helps future developers understand the code better.

---

# Example Bug in Anemic Model

Suppose after some months, a developer creates a CSV import feature:

The developer forgets to add validation.

Now a very large quote text (1500 characters) gets saved into the database and later causes errors in production.

With the rich model, this problem never happens because of :
Quote.Create(author, text, createdAt)

checks all rules before creating the object.
So invalid data is stopped at the beginning itself.
