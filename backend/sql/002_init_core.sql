-- Init core schema (SQL Server)

IF DB_ID('MedipielControlPrecios') IS NULL
BEGIN
  CREATE DATABASE MedipielControlPrecios;
END
GO

USE MedipielControlPrecios;
GO

IF OBJECT_ID('dbo.Competitors', 'U') IS NULL
BEGIN
  CREATE TABLE dbo.Competitors (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name NVARCHAR(200) NOT NULL,
    BaseUrl NVARCHAR(500) NULL,
    AdapterId NVARCHAR(100) NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
  );
  CREATE UNIQUE INDEX UX_Competitors_Name ON dbo.Competitors(Name);
END
GO

IF OBJECT_ID('dbo.Products', 'U') IS NULL
BEGIN
  CREATE TABLE dbo.Products (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Sku NVARCHAR(100) NULL,
    Ean NVARCHAR(100) NULL,
    Description NVARCHAR(500) NOT NULL,
    BrandId INT NULL,
    SupplierId INT NULL,
    CategoryId INT NULL,
    LineId INT NULL,
    MedipielListPrice DECIMAL(18,2) NULL,
    MedipielPromoPrice DECIMAL(18,2) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_Products_Brands FOREIGN KEY (BrandId) REFERENCES dbo.Brands(Id),
    CONSTRAINT FK_Products_Suppliers FOREIGN KEY (SupplierId) REFERENCES dbo.Suppliers(Id),
    CONSTRAINT FK_Products_Categories FOREIGN KEY (CategoryId) REFERENCES dbo.Categories(Id),
    CONSTRAINT FK_Products_Lines FOREIGN KEY (LineId) REFERENCES dbo.Lines(Id)
  );
  CREATE UNIQUE INDEX UX_Products_Ean ON dbo.Products(Ean)
  WHERE Ean IS NOT NULL;
  CREATE INDEX IX_Products_LineId ON dbo.Products(LineId);
END
GO

IF OBJECT_ID('dbo.CompetitorProducts', 'U') IS NULL
BEGIN
  CREATE TABLE dbo.CompetitorProducts (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ProductId INT NOT NULL,
    CompetitorId INT NOT NULL,
    Url NVARCHAR(1000) NULL,
    Name NVARCHAR(500) NULL,
    MatchMethod NVARCHAR(50) NULL,
    MatchScore DECIMAL(5,3) NULL,
    LastMatchedAt DATETIME2 NULL,
    CONSTRAINT FK_CompetitorProducts_Products FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id),
    CONSTRAINT FK_CompetitorProducts_Competitors FOREIGN KEY (CompetitorId) REFERENCES dbo.Competitors(Id)
  );
  CREATE UNIQUE INDEX UX_CompetitorProducts_Product_Competitor ON dbo.CompetitorProducts(ProductId, CompetitorId);
END
GO

IF OBJECT_ID('dbo.PriceSnapshots', 'U') IS NULL
BEGIN
  CREATE TABLE dbo.PriceSnapshots (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ProductId INT NOT NULL,
    CompetitorId INT NOT NULL,
    SnapshotDate DATE NOT NULL,
    ListPrice DECIMAL(18,2) NULL,
    PromoPrice DECIMAL(18,2) NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_PriceSnapshots_Products FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id),
    CONSTRAINT FK_PriceSnapshots_Competitors FOREIGN KEY (CompetitorId) REFERENCES dbo.Competitors(Id)
  );
  CREATE UNIQUE INDEX UX_PriceSnapshots_Product_Competitor_Date ON dbo.PriceSnapshots(ProductId, CompetitorId, SnapshotDate);
END
GO

IF OBJECT_ID('dbo.AlertRules', 'U') IS NULL
BEGIN
  CREATE TABLE dbo.AlertRules (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    BrandId INT NOT NULL,
    ListPriceThresholdPercent DECIMAL(5,2) NULL,
    PromoPriceThresholdPercent DECIMAL(5,2) NULL,
    Active BIT NOT NULL DEFAULT 1,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    CONSTRAINT FK_AlertRules_Brands FOREIGN KEY (BrandId) REFERENCES dbo.Brands(Id)
  );
END
GO

IF OBJECT_ID('dbo.Alerts', 'U') IS NULL
BEGIN
  CREATE TABLE dbo.Alerts (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    ProductId INT NOT NULL,
    CompetitorId INT NOT NULL,
    AlertRuleId INT NULL,
    Type NVARCHAR(100) NOT NULL,
    Message NVARCHAR(1000) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    Status NVARCHAR(50) NOT NULL DEFAULT 'open',
    CONSTRAINT FK_Alerts_Products FOREIGN KEY (ProductId) REFERENCES dbo.Products(Id),
    CONSTRAINT FK_Alerts_Competitors FOREIGN KEY (CompetitorId) REFERENCES dbo.Competitors(Id),
    CONSTRAINT FK_Alerts_AlertRules FOREIGN KEY (AlertRuleId) REFERENCES dbo.AlertRules(Id)
  );
END
GO

IF OBJECT_ID('dbo.ImportBatches', 'U') IS NULL
BEGIN
  CREATE TABLE dbo.ImportBatches (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    FileName NVARCHAR(500) NOT NULL,
    RowsTotal INT NOT NULL,
    RowsProcessed INT NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
  );
END
GO
