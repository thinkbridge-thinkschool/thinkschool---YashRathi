USE master;
GO

IF DB_ID('CoveringIndexDemo') IS NOT NULL
BEGIN
    ALTER DATABASE CoveringIndexDemo SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE CoveringIndexDemo;
END
GO

CREATE DATABASE CoveringIndexDemo;
GO

USE CoveringIndexDemo;
GO

CREATE TABLE Orders
(
    OrderID      INT           NOT NULL IDENTITY(1,1),
    CustomerID   INT           NOT NULL,
    OrderDate    DATE          NOT NULL,
    TotalAmount  DECIMAL(10,2) NOT NULL,
    Status       NVARCHAR(20)  NOT NULL,
    Notes        NVARCHAR(200) NULL,
    CONSTRAINT PK_Orders PRIMARY KEY CLUSTERED (OrderID)
);
GO

-- Insert 50 000 rows using a stacked CTE numbers generator (no catalog joins)

WITH
  L0 AS (SELECT 1 c UNION ALL SELECT 1),
  L1 AS (SELECT 1 c FROM L0 a CROSS JOIN L0 b),
  L2 AS (SELECT 1 c FROM L1 a CROSS JOIN L1 b),
  L3 AS (SELECT 1 c FROM L2 a CROSS JOIN L2 b),
  L4 AS (SELECT 1 c FROM L3 a CROSS JOIN L3 b)
INSERT INTO Orders (CustomerID, OrderDate, TotalAmount, Status, Notes)
SELECT TOP 50000
    ABS(CHECKSUM(NEWID())) % 500 + 1,
    DATEADD(DAY, ABS(CHECKSUM(NEWID())) % 1825, '2020-01-01'),
    CAST(ABS(CHECKSUM(NEWID())) % 10000 AS DECIMAL(10,2)) + 0.99,
    CASE ABS(CHECKSUM(NEWID())) % 3
         WHEN 0 THEN 'Pending'
         WHEN 1 THEN 'Shipped'
         ELSE        'Completed'
    END,
    REPLICATE('x', ABS(CHECKSUM(NEWID())) % 50)
FROM L4;
GO

-- STEP 1 — Partial (non-covering) index on CustomerID only

CREATE NONCLUSTERED INDEX IX_Orders_CustomerID
    ON Orders (CustomerID);
GO

-- BEFORE: Query with Key Lookup

SET STATISTICS IO ON;
GO


SELECT  CustomerID,
        OrderDate,
        TotalAmount
FROM    Orders
WHERE   CustomerID = 42;
GO

SET STATISTICS IO OFF;
GO



-- STEP 2 — Drop partial index, create COVERING index with INCLUDE

DROP INDEX IX_Orders_CustomerID ON Orders;
GO

CREATE NONCLUSTERED INDEX IX_Orders_CustomerID_Covering
    ON Orders (CustomerID)
    INCLUDE (OrderDate, TotalAmount);
GO


-- AFTER: Same query — Key Lookup should be GONE

SET STATISTICS IO ON;
GO

SELECT  CustomerID,
        OrderDate,
        TotalAmount
FROM    Orders
WHERE   CustomerID = 42;
GO

SET STATISTICS IO OFF;
GO

-- verify index definition via catalog

SELECT
    i.name AS index_name,
    c.name  AS column_name,
    ic.is_included_column,
    ic.key_ordinal
FROM sys.indexes i
JOIN sys.index_columns    ic 
ON ic.object_id = i.object_id
AND ic.index_id  = i.index_id

JOIN sys.columns c  
ON c.object_id  = i.object_id
AND c.column_id  = ic.column_id

WHERE i.object_id = OBJECT_ID('Orders')
AND i.name      = 'IX_Orders_CustomerID_Covering'
ORDER BY ic.is_included_column, ic.key_ordinal;
GO

