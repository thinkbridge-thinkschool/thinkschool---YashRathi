-- ANOMALY 2: NON-REPEATABLE READ
-- A non-repeatable read happens when Session A reads the SAME
-- row twice inside ONE transaction and gets DIFFERENT values
-- because Session B committed an UPDATE in between.
--
-- WHAT IS A NON-REPEATABLE READ? (Concept)
--   A: BEGIN TRAN → READ Alice balance → 1000
--   B:              UPDATE Alice balance → 200, COMMIT
--   A:              READ Alice balance → 200   ← different value!
--   A: END TRAN
--
-- Same query, same transaction, DIFFERENT results. That is
-- a non-repeatable read. The read was not "repeatable."


-- STEP 1 — Reset data

USE IsolationDemo;
GO
UPDATE Accounts SET Balance = 1000.00 WHERE AccountId = 1;
PRINT 'Data reset. Alice balance = 1000.00';
GO


-- SESSION A  (first query window for this block)

USE IsolationDemo;
GO

SET TRANSACTION ISOLATION LEVEL READ COMMITTED;

BEGIN TRANSACTION;

    SELECT AccountId, Owner, Balance,
           '1st read — READ COMMITTED' AS ReadNumber
    FROM   Accounts
    WHERE  AccountId = 1;

    PRINT 'Session A: First read done. Now run Session B UPDATE.';
    PRINT 'After B commits, run the second SELECT below.';


    -- Second read of the SAME row
    SELECT AccountId, Owner, Balance,
           '2nd read — READ COMMITTED (value changed!)' AS ReadNumber
    FROM   Accounts
    WHERE  AccountId = 1;

COMMIT TRANSACTION;
GO


-- ===  SESSION B  (Second window)
-- Run this AFTER Session A's first read, BEFORE Session A's second read


/*

USE IsolationDemo;
GO

UPDATE Accounts
SET    Balance = 200.00
WHERE  AccountId = 1;           -- Alice deducted

PRINT 'Session B: Alice balance updated to 200 and COMMITTED.';

-- (auto-committed — no explicit BEGIN TRAN needed)
*/


--  Use REPEATABLE READ in Session A
-- Reset first, then re-run the whole Session A block with
-- REPEATABLE READ to see that the second read is now blocked
-- until B finishes, and both reads return the SAME value.

USE IsolationDemo;
GO
UPDATE Accounts SET Balance = 1000.00 WHERE AccountId = 1;  -- reset
GO

SET TRANSACTION ISOLATION LEVEL REPEATABLE READ;

BEGIN TRANSACTION;

    SELECT AccountId, Owner, Balance,
           '1st read — REPEATABLE READ' AS ReadNumber
    FROM   Accounts
    WHERE  AccountId = 1;

    PRINT 'Now try running Session B UPDATE — it will BLOCK.';
    PRINT 'B cannot change the row while A holds a shared lock.';

    -- Session B's UPDATE will block here until A commits
    WAITFOR DELAY '00:00:05';   -- simulate A doing other work

    SELECT AccountId, Owner, Balance,
           '2nd read — REPEATABLE READ (same value, non-repeatable read prevented)' AS ReadNumber
    FROM   Accounts
    WHERE  AccountId = 1;

COMMIT TRANSACTION;
GO

-- OUTPUT 

-- READ COMMITTED   → 1st read: 1000 | 2nd read: 200  (anomaly!)
-- REPEATABLE READ  → 1st read: 1000 | 2nd read: 1000 (consistent)
--                    Session B's UPDATE blocks until A commits
