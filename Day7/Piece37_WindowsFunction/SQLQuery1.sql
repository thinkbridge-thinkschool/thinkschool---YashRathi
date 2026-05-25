CREATE TABLE quotes (
    id         INT IDENTITY(1,1) PRIMARY KEY,
    author     NVARCHAR(100),
    quote      NVARCHAR(500),
    created_at DATE
);

INSERT INTO quotes (author, quote, created_at) VALUES
  ('Seneca',    'Dum differtur vita transcurrit',             '2024-01-05'),
  ('Seneca',    'Nusquam est qui ubique est',                 '2024-01-12'),
  ('Seneca',    'Per aspera ad astra',                        '2024-02-03'),
  ('Aurelius',  'You have power over your mind',              '2024-01-08'),
  ('Aurelius',  'The impediment to action advances',          '2024-01-20'),
  ('Aurelius',  'Loss is nothing else but change',            '2024-02-15'),
  ('Epictetus', 'Make the best use of what is in your power', '2024-01-15'),
  ('Epictetus', 'He is a wise man who does not grieve',       '2024-02-01');

  SELECT * FROM quotes ORDER BY author, created_at;

  SELECT
    author,
    created_at,
    LEFT(quote, 40) AS quote_snippet,

    ROW_NUMBER() OVER (
        PARTITION BY author
        ORDER BY created_at
    ) AS quote_num,

    SUM(1) OVER (
        PARTITION BY author
        ORDER BY created_at
    ) AS running_count,

    LAG(created_at) OVER (
        PARTITION BY author
        ORDER BY created_at
    ) AS prev_quote_date,

    DATEDIFF(
        DAY,
        LAG(created_at) OVER (
            PARTITION BY author
            ORDER BY created_at
        ),
        created_at
    )AS days_since_prev

FROM quotes
ORDER BY author, created_at;