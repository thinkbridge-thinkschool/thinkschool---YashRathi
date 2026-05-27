USE master;
GO

-- Drop and recreate a clean demo database
IF DB_ID('IsolationDemo') IS NOT NULL
    DROP DATABASE IsolationDemo;
GO

CREATE DATABASE IsolationDemo;
GO

USE IsolationDemo;
GO

-- A simple bank-accounts table
CREATE TABLE Accounts (
    AccountId   INT          PRIMARY KEY,
    Owner       NVARCHAR(50) NOT NULL,
    Balance     DECIMAL(10,2) NOT NULL
);
GO

-- A products table used for the phantom-read demo
CREATE TABLE Products (
    ProductId   INT          PRIMARY KEY IDENTITY(1,1),
    Name        NVARCHAR(50) NOT NULL,
    Price       DECIMAL(10,2) NOT NULL
);
GO

-- Seed data
INSERT INTO Accounts VALUES (1, 'Alice', 1000.00);
INSERT INTO Accounts VALUES (2, 'Bob',     500.00);

INSERT INTO Products (Name, Price) VALUES ('Pen',    1.50);
INSERT INTO Products (Name, Price) VALUES ('Pencil', 0.75);
GO

SELECT 'Setup complete. Accounts:' AS Info;
SELECT * FROM Accounts;
SELECT 'Products:' AS Info;
SELECT * FROM Products;
GO
