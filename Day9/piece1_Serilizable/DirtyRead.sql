-- ANOMALY 1: DIRTY READ
--
-- A dirty read happens when Session A reads data that Session B
-- has written but NOT yet committed. If B later rolls back,
-- A has read data that never officially existed.
-- WHAT IS A DIRTY READ? (Concept)
--
--   B: BEGIN TRAN → UPDATE Alice balance 1000 → 9000
--   A: READ Alice balance  ← sees 9000  (dirty! not committed)
--   B: ROLLBACK            ← 9000 never really existed
--   A: READ Alice balance  ← now sees 1000 again

-- STEP 1 — Reset data 

USE IsolationDemo;
GO
UPDATE Accounts SET Balance = 1000.00 WHERE AccountId = 1;   -- reset Alice
PRINT 'Data reset. Alice balance = 1000.00';
GO


--  SESSION B  (NEW query window for this block) 

-- Purpose: Write an uncommitted change
-- Run this FIRST, then immediately switch to Session A below.


/*

USE IsolationDemo;
GO

BEGIN TRANSACTION;

    -- Simulate a large deposit that has NOT been committed yet
    UPDATE Accounts
    SET    Balance = 9000.00
    WHERE  AccountId = 1;       -- Alice's account

    PRINT 'Session B: Updated Alice to 9000 — NOT committed yet.';
    PRINT 'Session B: Now switch to Session A and read.';

    -- DO NOT COMMIT YET — leave this transaction open
    -- After Session A reads, come back and run ROLLBACK below

    -- ROLLBACK TRANSACTION;   <-- run this line after Session A reads
*/


-- Purpose: Read while B's transaction is still open

USE IsolationDemo;
GO

-- DEMONSTRATES DIRTY READ
-- READ UNCOMMITTED lets us see B's uncommitted change
SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED;

SELECT AccountId,
       Owner,
       Balance,
       'READ UNCOMMITTED — can see uncommitted data!' AS Note
FROM   Accounts
WHERE  AccountId = 1;
GO

-- PREVENTS DIRTY READ
-- READ COMMITTED waits for B to commit/rollback before returning
SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

SELECT AccountId,
       Owner,
       Balance,
       'READ COMMITTED — this query BLOCKS until B commits or rolls back' AS Note
FROM   Accounts
WHERE  AccountId = 1;
GO

-- OUTPUT EXPLAINED
-- READ UNCOMMITTED - Balance = 9000  (dirty read! B not committed)
-- READ COMMITTED - query blocks until B rolls back, then shows 1000

