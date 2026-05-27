USE master;
GO

IF DB_ID('DeadlockDemo') IS NOT NULL
    DROP DATABASE DeadlockDemo;
GO

CREATE DATABASE DeadlockDemo;
GO

USE DeadlockDemo;
GO

CREATE TABLE dbo.AccountA
(
    Id      INT           NOT NULL PRIMARY KEY,
    Balance DECIMAL(10,2) NOT NULL
);

CREATE TABLE dbo.AccountB
(
    Id      INT           NOT NULL PRIMARY KEY,
    Balance DECIMAL(10,2) NOT NULL
);
GO

INSERT INTO dbo.AccountA VALUES (1, 1000.00);
INSERT INTO dbo.AccountB VALUES (1, 2000.00);
GO
