USE QuotesDB;

CREATE TABLE authors (
    author_id   INT PRIMARY KEY,
    author_name VARCHAR(100)
);

CREATE TABLE quotes (
    quote_id    INT PRIMARY KEY,
    author_id   INT REFERENCES authors(author_id),
    quote_text  VARCHAR(500),
    created_at  DATE
);

INSERT INTO authors VALUES
(1,'Mark Twain'),(2,'Maya Angelou'),(3,'Albert Einstein'),
(4,'Oscar Wilde'),(5,'Aristotle'),(6,'Rumi'),
(7,'Nikola Tesla'),(8,'Ada Lovelace'),(9,'Plato'),(10,'Seneca');

INSERT INTO quotes VALUES
(1,  1, 'The secret of getting ahead is getting started.',        '2024-01-15'),
(2,  1, 'Truth is stranger than fiction.',                        '2024-03-22'),
(3,  2, 'You may not control all events that happen to you.',     '2024-02-10'),
(4,  2, 'Nothing will work unless you do.',                       '2024-05-01'),
(5,  2, 'If you dont like something, change it.',                 '2024-06-18'),
(6,  3, 'Imagination is more important than knowledge.',          '2024-01-05'),
(7,  3, 'Life is like riding a bicycle.',                         '2024-04-30'),
(8,  4, 'Be yourself; everyone else is already taken.',           '2024-07-11'),
(9,  5, 'Excellence is never an accident.',                       '2024-02-28'),
(10, 6, 'Out beyond ideas of wrongdoing there is a field.',       '2024-03-15'),
(11, 6, 'The wound is the place where the light enters.',         '2024-08-01'),
(12, 7, 'The present is theirs; the future is mine.',             '2024-05-20'),
(13, 8, 'That brain of mine is something more than mortal.',      '2024-06-05'),
(14, 9, 'Courage is knowing what not to fear.',                   '2024-01-30'),
(15,10, 'Luck is what happens when preparation meets opportunity.','2024-07-25');

DELETE FROM quotes;

DELETE FROM authors;

INSERT INTO authors VALUES
(1,'Mark Twain'),(2,'Maya Angelou'),(3,'Albert Einstein'),
(4,'Oscar Wilde'),(5,'Aristotle'),(6,'Rumi'),
(7,'Nikola Tesla'),(8,'Ada Lovelace'),(9,'Plato'),(10,'Seneca');

SELECT * FROM authors;

INSERT INTO quotes VALUES
(1,  1, 'The secret of getting ahead is getting started.',        '2024-01-15'),
(2,  1, 'Truth is stranger than fiction.',                        '2024-03-22'),
(3,  2, 'You may not control all events that happen to you.',     '2024-02-10'),
(4,  2, 'Nothing will work unless you do.',                       '2024-05-01'),
(5,  2, 'If you dont like something, change it.',                 '2024-06-18'),
(6,  3, 'Imagination is more important than knowledge.',          '2024-01-05'),
(7,  3, 'Life is like riding a bicycle.',                         '2024-04-30'),
(8,  4, 'Be yourself; everyone else is already taken.',           '2024-07-11'),
(9,  5, 'Excellence is never an accident.',                       '2024-02-28'),
(10, 6, 'Out beyond ideas of wrongdoing there is a field.',       '2024-03-15'),
(11, 6, 'The wound is the place where the light enters.',         '2024-08-01'),
(12, 7, 'The present is theirs; the future is mine.',             '2024-05-20'),
(13, 8, 'That brain of mine is something more than mortal.',      '2024-06-05'),
(14, 9, 'Courage is knowing what not to fear.',                   '2024-01-30'),
(15,10, 'Luck is what happens when preparation meets opportunity.','2024-07-25');


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
        q.quote_text  AS most_recent_quote,
        q.created_at  AS quote_date
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