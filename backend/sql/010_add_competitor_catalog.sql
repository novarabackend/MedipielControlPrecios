USE MedipielControlPrecios;
GO

IF OBJECT_ID('dbo.CompetitorCatalog', 'U') IS NULL
BEGIN
    CREATE TABLE dbo.CompetitorCatalog (
        Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CompetitorId INT NOT NULL,
        Url NVARCHAR(500) NOT NULL,
        Name NVARCHAR(300) NULL,
        Description NVARCHAR(MAX) NULL,
        Ean NVARCHAR(50) NULL,
        CompetitorSku NVARCHAR(100) NULL,
        Brand NVARCHAR(200) NULL,
        Categories NVARCHAR(500) NULL,
        ListPrice DECIMAL(18,2) NULL,
        PromoPrice DECIMAL(18,2) NULL,
        ExtractedAt DATETIME2 NOT NULL CONSTRAINT DF_CompetitorCatalog_ExtractedAt DEFAULT SYSUTCDATETIME(),
        UpdatedAt DATETIME2 NOT NULL CONSTRAINT DF_CompetitorCatalog_UpdatedAt DEFAULT SYSUTCDATETIME()
    );

    CREATE UNIQUE INDEX IX_CompetitorCatalog_Competitor_Url
        ON dbo.CompetitorCatalog (CompetitorId, Url);

    CREATE INDEX IX_CompetitorCatalog_Competitor_Ean
        ON dbo.CompetitorCatalog (CompetitorId, Ean);
END
GO
