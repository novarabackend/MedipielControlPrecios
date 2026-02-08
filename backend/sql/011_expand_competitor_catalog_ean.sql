USE MedipielControlPrecios;
GO

IF COL_LENGTH('dbo.CompetitorCatalog', 'Ean') IS NOT NULL
BEGIN
    ALTER TABLE dbo.CompetitorCatalog ALTER COLUMN Ean NVARCHAR(500) NULL;
END
GO
