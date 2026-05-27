-- 01_enable_deadlock_trace.sql
-- Turn on trace flag 1222 so SQL Server writes the full
-- deadlock graph (XML) to the Error Log every time a deadlock
-- is chosen.  No XE session needed; works on all editions.
-- Enable trace flag 1222 globally for this server instance.
-- 1222 = verbose deadlock graph in XML (superset of 1204).
DBCC TRACEON(1222, -1);
GO

-- Confirm it is active.
DBCC TRACESTATUS(1222);
GO

