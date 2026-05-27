-- ANOMALY 3: PHANTOM READ
--
-- A phantom read happens when Session A runs the SAME range
-- query twice inside ONE transaction and gets a DIFFERENT SET
-- OF ROWS because Session B inserted or deleted rows in between.
--
-- Key difference from non-repeatable read:
--   Non-repeatable = same row, different VALUE
--   Phantom        = same filter, different NUMBER OF ROWS
--
-- Isolation level that PREVENTS it: SERIALIZABLE

-- WHAT IS A PHANTOM READ? (Concept)
-- Timeline:
--   A: BEGIN TRAN → SELECT products WHERE price < 2.00 → 2 rows
--   B:              INSERT a new product (price 1.25), COMMIT
--   A:              SELECT products WHERE price < 2.00 → 3 rows!
--   A: END TRAN


-- STEP 1 — Reset data 

USE IsolationDemo;
GO

DELETE FROM Products WHERE Name NOT IN ('Pen', 'Pencil');
DBCC CHECKIDENT ('Products', RESEED, 2);   
PRINT 'Products reset to 2 rows (Pen, Pencil).';
SELECT * FROM Products;
GO

-- ===  SESSION A  (first window)
USE IsolationDemo;
GO

SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;  -- NOT enough to stop phantoms

BEGIN TRANSACTION;

    -- First range query
    SELECT ProductId, Name, Price,
           '1st scan — REPEATABLE READ' AS ScanNumber
    FROM   Products
    WHERE  Price < 2.00;

    PRINT 'Session A: First scan done — 2 rows.';
    PRINT 'Now run Session B INSERT, then come back for the second scan.';



    -- Second range query (same filter)
    SELECT ProductId, Name, Price,
           '2nd scan — REPEATABLE READ (phantom appeared!)' AS ScanNumber
    FROM   Products
    WHERE  Price < 2.00;

COMMIT TRANSACTION;
GO


-
-- ===  SESSION B  (Second window))

--- PASTE THIS INTO SESSION B ---

USE IsolationDemo;
GO

INSERT INTO Products (Name, Price) VALUES ('Eraser', 1.25);

PRINT 'Session B: Inserted Eraser (price 1.25) and COMMITTED.';
*/


-- ===  NOW FIX IT — Use SERIALIZABLE in Session A

-- Reset first, then re-run with SERIALIZABLE to see that
-- Session B's INSERT blocks until A's transaction finishes.


USE IsolationDemo;
GO
DELETE FROM Products WHERE Name = 'Eraser';     d
GO


SET TRANSACTION ISOLATION LEVEL SERIALIZABLE;

BEGIN TRANSACTION;

    SELECT ProductId, Name, Price,
           '1st scan — SERIALIZABLE' AS ScanNumber
    FROM   Products
    WHERE  Price < 2.00;

    PRINT 'Now try Session B INSERT — it will BLOCK.';
    PRINT 'SERIALIZABLE holds a RANGE lock on Price < 2.00.';

    WAITFOR DELAY '00:00:05';   -- simulate A doing other work

    SELECT ProductId, Name, Price,
           '2nd scan — SERIALIZABLE (same 2 rows, phantom prevented)' AS ScanNumber
    FROM   Products
    WHERE  Price < 2.00;

COMMIT TRANSACTION;
GO


-- OUTPUT 

-- REPEATABLE READ  → 1st scan: 2 rows | 2nd scan: 3 rows (phantom)
-- SERIALIZABLE     → 1st scan: 2 rows | 2nd scan: 2 rows (consistent)
--                    Session B's INSERT blocks until A commits

