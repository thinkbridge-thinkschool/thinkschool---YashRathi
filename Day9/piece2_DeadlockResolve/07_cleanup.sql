-- Disable trace flag 1222.
DBCC TRACEOFF(1222, -1);
GO

USE master;
GO

DROP DATABASE IF EXISTS DeadlockDemo;
GO
