-- FIX: enforce the same lock-acquisition order in every session.
-- Both sessions now lock AccountA first, then AccountB.
-- A circular wait is structurally impossible → no deadlock.
-- ============================================================

USE DeadlockDemo;
GO

-- Reset balances so both fixed sessions start clean.
UPDATE dbo.AccountA SET Balance = 1000 WHERE Id = 1;
UPDATE dbo.AccountB SET Balance = 2000 WHERE Id = 1;
GO

-- Fixed Session 1 (run in Window 1)
-- Debit AccountA, credit AccountB  (order: A → B)
BEGIN TRANSACTION;

    UPDATE dbo.AccountA          -- locks A first
    SET    Balance = Balance - 100
    WHERE  Id = 1;

    WAITFOR DELAY '00:00:05';    -- simulate work; Session 2 is also waiting on A

    UPDATE dbo.AccountB          -- then locks B
    SET    Balance = Balance + 100
    WHERE  Id = 1;

COMMIT;
GO


-- Fixed Session 2 
-- Debit AccountB, credit AccountA  (order still: A → B)
BEGIN TRANSACTION;

    -- Must lock A first even though we ultimately debit B.
    -- Use UPDLOCK hint or simply update A before B to match order.
    UPDATE dbo.AccountA          -- locks A first (will BLOCK until S1 commits)
    SET    Balance = Balance + 200
    WHERE  Id = 1;

    UPDATE dbo.AccountB          -- then locks B (S1 has already released it)
    SET    Balance = Balance - 200
    WHERE  Id = 1;

COMMIT;
GO

-- Result:
--   Session 2 blocks on AccountA until Session 1 commits (normal blocking).
--   After Session 1 commits, Session 2 proceeds and completes.
--   No circular wait → no deadlock → no victim → no data loss.
