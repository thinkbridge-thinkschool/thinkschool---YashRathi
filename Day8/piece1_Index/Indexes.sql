USE master;
GO

-- Force drop the whole database (cleanest reset)
IF DB_ID('IndexDemo') IS NOT NULL
BEGIN
    ALTER DATABASE IndexDemo SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE IndexDemo;
END
GO

-- Fresh database
CREATE DATABASE IndexDemo;
GO

USE IndexDemo;
GO

SELECT DB_NAME() AS [Current Database];
GO

-- Create table fresh
CREATE TABLE dbo.Orders (
    order_id    INT            NOT NULL,
    customer_id INT            NOT NULL,
    order_date  DATE           NOT NULL,
    status      VARCHAR(20)    NOT NULL,
    amount      DECIMAL(10,2)  NOT NULL,
    CONSTRAINT PK_Orders PRIMARY KEY CLUSTERED (order_id)
);
GO

-- Insert 100,000 rows
WITH nums AS (
    SELECT TOP 100000
        ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n
    FROM sys.all_columns a
    CROSS JOIN sys.all_columns b
)
INSERT INTO dbo.Orders (order_id, customer_id, order_date, status, amount)
SELECT
    n,
    (ABS(CHECKSUM(NEWID())) % 5000) + 1,
    DATEADD(DAY, -((ABS(CHECKSUM(NEWID())) % 730)), '2024-12-31'),
    CASE (ABS(CHECKSUM(NEWID())) % 4)
        WHEN 0 THEN 'Pending'
        WHEN 1 THEN 'Shipped'
        WHEN 2 THEN 'Delivered'
        ELSE        'Cancelled'
    END,
    (CAST((ABS(CHECKSUM(NEWID())) % 99900) AS DECIMAL(10,2)) / 100.0) + 1.00
FROM nums;
GO

-- Verify
SELECT COUNT(*)  AS total_rows 
FROM dbo.Orders;

SELECT TOP 5 *   FROM dbo.Orders

USE IndexDemo;
GO

SELECT
    i.name AS index_name,
    i.type_desc AS index_type,
    COL_NAME(ic.object_id, ic.column_id) AS column_name,
    ic.is_included_column
FROM sys.indexes i
JOIN sys.index_columns ic
    ON ic.object_id = i.object_id
   AND ic.index_id  = i.index_id
WHERE i.object_id = OBJECT_ID('dbo.Orders')
ORDER BY i.index_id, ic.key_ordinal;

USE IndexDemo;
GO

SET STATISTICS IO ON;
SET STATISTICS TIME ON;

PRINT ' Query A: point lookup by order_id ';
SELECT * FROM dbo.Orders
WHERE order_id = 49999;

PRINT ' Query B: lookup by customer_id';
SELECT customer_id, order_date, amount
FROM dbo.Orders
WHERE customer_id = 1234;

PRINT ' Query C: date range';
SELECT order_date, status, amount
FROM dbo.Orders
WHERE order_date BETWEEN '2024-01-01' AND '2024-01-31';

SET STATISTICS IO OFF;
SET STATISTICS TIME OFF;

-- use of non-clustere index

USE IndexDemo;
GO

CREATE NONCLUSTERED INDEX IX_Orders_CustomerID
    ON dbo.Orders (customer_id)
    INCLUDE (order_date, amount)
    WITH (FILLFACTOR = 85, SORT_IN_TEMPDB = ON);

CREATE NONCLUSTERED INDEX IX_Orders_OrderDate
    ON dbo.Orders (order_date)
    INCLUDE (status, amount)
    WITH (FILLFACTOR = 80, SORT_IN_TEMPDB = ON);
GO

-- confirm all 3 indexes now exist
SELECT
    i.name        AS index_name,
    i.type_desc   AS index_type,
    c.name        AS column_name,
    ic.is_included_column
FROM sys.indexes i
JOIN sys.index_columns ic
ON ic.object_id = i.object_id AND ic.index_id  = i.index_id

JOIN sys.columns c       
ON c.object_id  = ic.object_id AND c.column_id  = ic.column_id
WHERE i.object_id = OBJECT_ID('dbo.Orders')
ORDER BY i.index_id, ic.key_ordinal;

USE IndexDemo;
GO

SET STATISTICS IO  ON;
SET STATISTICS TIME ON;

PRINT 'Query A: point lookup by order_id';
SELECT * FROM dbo.Orders
WHERE order_id = 49999;

PRINT 'Query B: lookup by customer_id ';
SELECT customer_id, order_date, amount
FROM dbo.Orders
WHERE customer_id = 1234;

PRINT 'Query C: date range ';
SELECT order_date, status, amount
FROM dbo.Orders
WHERE order_date BETWEEN '2024-01-01' AND '2024-01-31';

SET STATISTICS IO  OFF;
SET STATISTICS TIME OFF;

USE IndexDemo;
GO

-- Write tax: insert one row, observe 3 B-tree writes
SET STATISTICS IO ON;

INSERT INTO dbo.Orders
VALUES (100001, 999, '2025-05-26', 'Pending', 299.99);

SET STATISTICS IO OFF;
GO

SELECT
    [Query],
    [Logical Reads BEFORE],
    [Logical Reads AFTER],
    CAST(
        ([Logical Reads BEFORE] - [Logical Reads AFTER]) * 100.0
        / [Logical Reads BEFORE]
    AS DECIMAL(5,1))        AS [Reduction %],
    [Plan BEFORE],
    [Plan AFTER]
FROM (VALUES
    ('A — order_id lookup',  3,   3,   'Clustered Index Seek',  'Clustered Index Seek'),
    ('B — customer_id',      342, 4,   'Clustered Index SCAN',  'NC Index Seek (covering)'),
    ('C — date range',       342, 9,   'Clustered Index SCAN',  'NC Index Seek (covering)'),
    ('Write tax (INSERT)',   1,   3,   '1 B-tree write',        '3 B-tree writes')
) AS t([Query], [Logical Reads BEFORE], [Logical Reads AFTER], [Plan BEFORE], [Plan AFTER]);
