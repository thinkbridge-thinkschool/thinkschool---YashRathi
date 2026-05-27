-- Session 2 acquires B first, then A  (opposite order → deadlock)


USE DeadlockDemo;
GO

BEGIN TRANSACTION;

    -- Acquire X-lock on AccountB row (Id = 1)
    UPDATE dbo.AccountB
    SET    Balance = Balance - 200
    WHERE  Id = 1;

    PRINT 'Session 2: locked AccountB. Sleeping 5 s …';
    WAITFOR DELAY '00:00:05';

    -- Now try to lock AccountA (Session 1 already holds it → DEADLOCK)
    UPDATE dbo.AccountA
    SET    Balance = Balance + 200
    WHERE  Id = 1;

COMMIT;
GO
