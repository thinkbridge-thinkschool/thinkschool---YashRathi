# Day 7 — Set Operations Output

## Exercise
Given the schema, answer three questions using UNION / INTERSECT / EXCEPT where appropriate. Note which set operator you used for each and why.

---

## Step 1 — Drop Existing Tables

```sql
DROP TABLE IF EXISTS tags;
DROP TABLE IF EXISTS quotes;
DROP TABLE IF EXISTS authors;
```

---

## Step 2 — Create Tables

```sql
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
```

---

## Step 3 — Insert Sample Data

```sql
INSERT INTO authors (name, category) VALUES
  ('Yash Rathi',     'classic'),
  ('Avishkar Patil', 'classic'),
  ('Vedang Shinde',  'classic'),
  ('Amey Khot',      'modern'),
  ('Anuj Chaudari',  'modern'),
  ('Aashish Bonde',  'modern'),
  ('Bhushan Kolhe',  'modern'),
  ('Vedant Patil',   'classic');

-- Added to create overlap for INTERSECT
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
```

---

## Step 4 — Verify Data

```sql
SELECT * FROM authors;
SELECT * FROM quotes;
SELECT * FROM tags;
```

**authors table:**

```
id  | name            | category
----|-----------------|----------
1   | Yash Rathi      | classic
2   | Avishkar Patil  | classic
3   | Vedang Shinde   | classic
4   | Amey Khot       | modern
5   | Anuj Chaudari   | modern
6   | Aashish Bonde   | modern
7   | Bhushan Kolhe   | modern
8   | Vedant Patil    | classic
9   | Bhushan Kolhe   | classic
```

**quotes table:**

```
id  | author_id | quote
----|-----------|----------------------------------------------
1   | 1         | Honesty is the best policy
2   | 1         | an apple a day keeps doctor away
3   | 2         | You have power over your mind
4   | 2         | In the end we only regret the chances
5   | 3         | Make the best use of what is in your power
6   | 4         | Vulnerability is not weakness
7   | 5         | There is a new way there
```

**tags table:**

```
id  | quote_id | tag
----|----------|-------------
1   | 1        | time
2   | 1        | stoicism
3   | 2        | focus
4   | 3        | mind
5   | 4        | resilience
6   | 5        | stoicism
7   | 6        | courage
8   | 6        | vulnerability
```

---

## Query 1 — Authors with quotes but no tags

**Operator: EXCEPT**

I used EXCEPT because I wanted authors who have quotes but do not have any tags. It removes the authors whose quotes already contain tags.

```sql
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
```

**Output:**

```
name
--------------
Anuj Chaudari
```

---

## Query 2 — Authors in both 'classic' and 'modern'

**Operator: INTERSECT**

I used INTERSECT because I needed only the common authors who are present in both classic and modern categories.

```sql
-- Authors in classic
SELECT name FROM authors WHERE category = 'classic'

INTERSECT

-- Authors in modern
SELECT name FROM authors WHERE category = 'modern';
```

**Output:**

```
name
--------------
Bhushan Kolhe
```

---

## Query 3 — Combined distinct tag list across both categories

**Operator: UNION**

I used UNION because I wanted to combine tags from both classic and modern authors into one list, and it automatically removed duplicate tags.

```sql
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
```

**Output:**

```
tag
--------------
courage
focus
mind
resilience
stoicism
time
vulnerability
```

---

## Summary

| Question | Operator | Why |
|---|---|---|
| Authors with quotes but no tags | EXCEPT | Subtracts tagged-authors from all authors with quotes |
| Authors in both classic and modern | INTERSECT | Returns only the overlap between two sets |
| Combined distinct tag list | UNION | Merges two lists and removes duplicates automatically |
