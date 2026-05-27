


-- SQL Server's built-in system_health session always captures
-- deadlock graphs (xml_deadlock_report event) regardless of TF 1222.

SELECT
    xdr.value('@timestamp', 'datetime2')        AS deadlock_time,
    xdr.query('.')                              AS deadlock_graph_xml
FROM (
    SELECT CAST(target_data AS XML) AS ring_data
    FROM   sys.dm_xe_session_targets  t
    JOIN   sys.dm_xe_sessions         s ON s.address = t.event_session_address
    WHERE  s.name          = 'system_health'
    AND    t.target_name   = 'ring_buffer'
) AS data
CROSS APPLY ring_data.nodes('//RingBufferTarget/event[@name="xml_deadlock_report"]') AS xr(xdr)
ORDER BY deadlock_time DESC;
GO

-- other methods to read the deadlock graph:
-- After TF 1222 fires you will see blocks like:
--   deadlock-list
--     deadlock victim=...
--       process-list / resource-list
-- in SSMS → Management → SQL Server Logs, or via:

EXEC sp_readerrorlog 0, 1, 'deadlock';
GO
