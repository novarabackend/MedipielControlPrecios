-- Init masters schema (SQL Server)

IF DB_ID('MedipielControlPrecios') IS NULL
BEGIN
  CREATE DATABASE MedipielControlPrecios;
END
GO

USE MedipielControlPrecios;
GO

IF OBJECT_ID('dbo.Brands', 'U') IS NULL
BEGIN
  CREATE TABLE dbo.Brands (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name NVARCHAR(200) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
  );
  CREATE UNIQUE INDEX UX_Brands_Name ON dbo.Brands(Name);
END
GO

IF OBJECT_ID('dbo.Suppliers', 'U') IS NULL
BEGIN
  CREATE TABLE dbo.Suppliers (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name NVARCHAR(200) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
  );
  CREATE UNIQUE INDEX UX_Suppliers_Name ON dbo.Suppliers(Name);
END
GO

IF OBJECT_ID('dbo.Categories', 'U') IS NULL
BEGIN
  CREATE TABLE dbo.Categories (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name NVARCHAR(200) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
  );
  CREATE UNIQUE INDEX UX_Categories_Name ON dbo.Categories(Name);
END
GO
