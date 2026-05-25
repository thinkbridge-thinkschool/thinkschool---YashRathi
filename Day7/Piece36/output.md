# Day 7 — Piece 36 | SQL Joins and CTEs

## Exercise
Build a query that returns each author with their quote count and their most-recent quote — using a CTE, not a correlated subquery.

---

## SQL Query

​```sql
WITH quote_counts AS (
    SELECT
        author_id,
        COUNT(*) AS total_quotes
    FROM quotes
    GROUP BY author_id
),
latest_quote AS (
    SELECT
        q.author_id,
        q.quote_text AS most_recent_quote,
        q.created_at AS quote_date
    FROM quotes q
    INNER JOIN (
        SELECT author_id, MAX(created_at) AS max_date
        FROM quotes
        GROUP BY author_id
    ) mx
        ON  q.author_id  = mx.author_id
        AND q.created_at = mx.max_date
)
SELECT TOP 10
    a.author_name,
    COALESCE(qc.total_quotes, 0) AS quote_count,
    lq.quote_date                AS most_recent_date,
    lq.most_recent_quote
FROM authors a
LEFT JOIN quote_counts qc ON a.author_id = qc.author_id
LEFT JOIN latest_quote lq ON a.author_id = lq.author_id
ORDER BY qc.total_quotes DESC, a.author_name;
​```

---

## Result Set (Top 10 Rows)

| author_name     | quote_count | most_recent_date | most_recent_quote                                        |
|-----------------|-------------|------------------|----------------------------------------------------------|
| Maya Angelou    | 3           | 2024-06-18       | If you dont like something, change it.                   |
| Albert Einstein | 2           | 2024-04-30       | Life is like riding a bicycle.                           |
| Mark Twain      | 2           | 2024-03-22       | Truth is stranger than fiction.                          |
| Rumi            | 2           | 2024-08-01       | The wound is the place where the light enters.           |
| Ada Lovelace    | 1           | 2024-06-05       | That brain of mine is something more than mortal.        |
| Aristotle       | 1           | 2024-02-28       | Excellence is never an accident.                         |
| Nikola Tesla    | 1           | 2024-05-20       | The present is theirs; the future is mine.               |
| Oscar Wilde     | 1           | 2024-07-11       | Be yourself; everyone else is already taken.             |
| Plato           | 1           | 2024-01-30       | Courage is knowing what not to fear.                     |
| Seneca          | 1           | 2024-07-25       | Luck is what happens when preparation meets opportunity. |

---

## Why CTE over Correlated Subquery?

A CTE runs the aggregation **once** and is reused across the query; a correlated subquery re-executes for **every row** in the outer query — at scale that is 2 total scans (CTE) vs 2×N scans (correlated), making CTEs dramatically faster and far easier to read and maintain.
