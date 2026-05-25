DROP TABLE IF EXISTS tags;
DROP TABLE IF EXISTS quotes;
DROP TABLE IF EXISTS authors;

CREATE TABLE authors (
    id       INT IDENTITY(1,1) PRIMARY KEY,
    name     NVARCHAR(100),
    category NVARCHAR(50)
);

CREATE TABLE quotes (
    id        INT IDENTITY(1,1) PRIMARY KEY,
    author_id INT,
    quote     NVARCHAR(500)
);

CREATE TABLE tags (
    id       INT IDENTITY(1,1) PRIMARY KEY,
    quote_id INT,
    tag      NVARCHAR(100)
);

INSERT INTO authors (name, category) VALUES
  ('Yash Rathi',      'classic'),
  ('Avishkar Patil',    'classic'),
  ('Vedang Shinde',   'classic'),
  ('Amey Khot', 'modern'),
  ('Anuj Chaudari', 'modern'),
  ('Aashish Bonde',    'modern'),
  ('Bhushan Kolhe', 'modern'),
  ('Vedant Patil',   'classic');

  INSERT INTO authors (name, category) VALUES ('Bhushan Kolhe', 'classic');

  INSERT INTO quotes (author_id, quote) VALUES
  (1, 'Honesty is the best policy'),
  (1, 'an apple a day keeps doctor away'),
  (2, 'You have power over your mind'),
  (2, 'In the end we only regret the chances'),
  (3, 'Make the best use of what is in your power'),
  (4, 'Vulnerability is not weakness'),
  (5, 'There is a new way there');

  INSERT INTO tags (quote_id, tag) VALUES
  (1, 'time'),
  (1, 'stoicism'),
  (2, 'focus'),
  (3, 'mind'),
  (4, 'resilience'),
  (5, 'stoicism'),
  (6, 'courage'),
  (6, 'vulnerability');

  SELECT * FROM authors;
  SELECT * FROM quotes;
  SELECT * FROM tags;

  -- Authors who HAVE quotes
    SELECT DISTINCT a.name
    FROM authors a
    JOIN quotes q ON a.id = q.author_id

    EXCEPT

    -- Authors whose quotes HAVE tags
    SELECT DISTINCT a.name
    FROM authors a
    JOIN quotes q ON a.id = q.author_id
    JOIN tags   t ON q.id = t.quote_id;

    -- Authors in classic
    SELECT name FROM authors WHERE category = 'classic'

    INTERSECT

    -- Authors in modern
    SELECT name FROM authors WHERE category = 'modern';

    -- Tags from classic authors
    SELECT DISTINCT t.tag
    FROM tags t
    JOIN quotes  q ON t.quote_id = q.id
    JOIN authors a ON q.author_id = a.id
    WHERE a.category = 'classic'

    UNION

    -- Tags from modern authors
    SELECT DISTINCT t.tag
    FROM tags t
    JOIN quotes  q ON t.quote_id = q.id
    JOIN authors a ON q.author_id = a.id
    WHERE a.category = 'modern';

    SELECT DISTINCT a.name
    FROM authors a
    JOIN quotes q ON a.id = q.author_id

    EXCEPT

    SELECT DISTINCT a.name
    FROM authors a
    JOIN quotes q ON a.id = q.author_id
    JOIN tags   t ON q.id = t.quote_id;

    SELECT name FROM authors WHERE category = 'classic'

    INTERSECT

    SELECT name FROM authors WHERE category = 'modern';

    --Union Query

    SELECT DISTINCT t.tag
    FROM tags t
    JOIN quotes  q ON t.quote_id = q.id
    JOIN authors a ON q.author_id = a.id
    WHERE a.category = 'classic'

    UNION

    SELECT DISTINCT t.tag
    FROM tags t
    JOIN quotes  q ON t.quote_id = q.id
    JOIN authors a ON q.author_id = a.id
    WHERE a.category = 'modern';