
-- Classic lock-ordering deadlock (Session 1 acquires A then B)


USE DeadlockDemo;
GO

-- Step 1: Lock AccountA first, pause, then try to lock AccountB.
-- While this session sleeps, Session 2 does the reverse, creating
-- a circular wait - deadlock.

BEGIN TRANSACTION;

    -- Acquire X-lock on AccountA row (Id = 1)
    UPDATE dbo.AccountA
    SET    Balance = Balance - 100
    WHERE  Id = 1;

    PRINT 'Session 1: locked AccountA. Sleeping 5 s …';
    WAITFOR DELAY '00:00:05';

    -- Now try to lock AccountB (Session 2 already holds it → DEADLOCK)
    UPDATE dbo.AccountB
    SET    Balance = Balance + 100
    WHERE  Id = 1;

COMMIT;
GO

-- If this session is chosen as the VICTIM I see:
--   Msg 1205, Level 13, State 51
--   Transaction (Process ID xx) was deadlocked on lock resources
--   with another process and has been chosen as the deadlock victim.
--   Rerun the transaction.
