# Day 7 — Window Functions Output

## Exercise
Write a query that returns, per author, each quote with a running count and the gap in days since their previous quote (LAG).

---

## Environment
- Database: SQL Server (T-SQL)

---

## Table Setup

```sql
CREATE TABLE quotes (
    id         INT IDENTITY(1,1) PRIMARY KEY,
    author     NVARCHAR(100),
    quote      NVARCHAR(500),
    created_at DATE
);
```

---

## Sample Data

```sql
INSERT INTO quotes (author, quote, created_at) VALUES
  ('Seneca',    'Dum differtur vita transcurrit',             '2024-01-05'),
  ('Seneca',    'Nusquam est qui ubique est',                 '2024-01-12'),
  ('Seneca',    'Per aspera ad astra',                        '2024-02-03'),
  ('Aurelius',  'You have power over your mind',              '2024-01-08'),
  ('Aurelius',  'The impediment to action advances',          '2024-01-20'),
  ('Aurelius',  'Loss is nothing else but change',            '2024-02-15'),
  ('Epictetus', 'Make the best use of what is in your power', '2024-01-15'),
  ('Epictetus', 'He is a wise man who does not grieve',       '2024-02-01');
```

---

## Query

```sql
SELECT
    author,
    created_at,
    LEFT(quote, 40)                          AS quote_snippet,

    ROW_NUMBER() OVER (
        PARTITION BY author
        ORDER BY created_at
    )                                        AS quote_num,

    SUM(1) OVER (
        PARTITION BY author
        ORDER BY created_at
    )                                        AS running_count,

    LAG(created_at) OVER (
        PARTITION BY author
        ORDER BY created_at
    )                                        AS prev_quote_date,

    DATEDIFF(
        DAY,
        LAG(created_at) OVER (
            PARTITION BY author
            ORDER BY created_at
        ),
        created_at
    )                                        AS days_since_prev

FROM quotes
ORDER BY author, created_at;
```

---

## Output

| author    | created_at | quote_snippet                     | quote_num | running_count | prev_quote_date | days_since_prev |
|-----------|------------|-----------------------------------|-----------|---------------|-----------------|-----------------|
| Aurelius  | 2024-01-08 | You have power over your mind     | 1         | 1             | NULL            | NULL            |
| Aurelius  | 2024-01-20 | The impediment to action advan... | 2         | 2             | 2024-01-08      | 12              |
| Aurelius  | 2024-02-15 | Loss is nothing else but chang... | 3         | 3             | 2024-01-20      | 26              |
| Epictetus | 2024-01-15 | Make the best use of what is i... | 1         | 1             | NULL            | NULL            |
| Epictetus | 2024-02-01 | He is a wise man who does not ... | 2         | 2             | 2024-01-15      | 17              |
| Seneca    | 2024-01-05 | Dum differtur vita transcurrit    | 1         | 1             | NULL            | NULL            |
| Seneca    | 2024-01-12 | Nusquam est qui ubique est        | 2         | 2             | 2024-01-05      | 7               |
| Seneca    | 2024-02-03 | Per aspera ad astra               | 3         | 3             | 2024-01-12      | 22              |

---

## Window Functions Used

| Function | Column | What it does |
|----------|--------|--------------|
| `ROW_NUMBER()` | `quote_num` | Sequential number per author, resets for each author |
| `SUM(1) OVER` | `running_count` | Cumulative count of quotes per author |
| `LAG(created_at)` | `prev_quote_date` | Date of the previous quote by the same author |
| `DATEDIFF + LAG` | `days_since_prev` | Gap in days between consecutive quotes per author |

---

## Key Observations

- First quote per author always has `NULL` for `prev_quote_date` and `days_since_prev` — no previous row exists.
- `PARTITION BY author` ensures each window function resets independently per author.
- `DATEDIFF(DAY, LAG(...), created_at)` is the SQL Server equivalent of PostgreSQL's simple date subtraction.
- `running_count` equals `quote_num` here since no rows are filtered — in a real dataset they would differ.
