-- SUMMARY: Isolation Levels vs Read Anomalies


USE IsolationDemo;
GO

SELECT
    IsolationLevel,
    DirtyRead,
    NonRepeatableRead,
    PhantomRead,
    Notes
FROM (VALUES
    --  Level              Dirty   NonRep  Phantom  Notes
    ('READ UNCOMMITTED',  'YES',  'YES',  'YES',   'No locks held on reads. Fastest, least safe.'),
    ('READ COMMITTED',    'NO',   'YES',  'YES',   'Default in SQL Server. Releases shared locks immediately after read.'),
    ('REPEATABLE READ',   'NO',   'NO',   'YES',   'Holds shared locks until end of transaction. Blocks UPDATEs on read rows.'),
    ('SERIALIZABLE',      'NO',   'NO',   'NO',    'Holds RANGE locks. Blocks INSERTs/DELETEs in the scanned range. Safest.')
) AS T(IsolationLevel, DirtyRead, NonRepeatableRead, PhantomRead, Notes);
GO


--  Anomaly              Caused by            Prevented from
--  -------------------  -------------------  -------------------------
--  Dirty Read           Reading uncommitted  READ COMMITTED  and above
--  Non-Repeatable Read  Row UPDATED between  REPEATABLE READ and above
--                       two reads
--  Phantom Read         Row INSERTED/DELETED SERIALIZABLE
--                       between two scans
