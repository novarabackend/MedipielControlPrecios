-- Add Name column to CompetitorProducts

USE MedipielControlPrecios;
GO

IF COL_LENGTH('dbo.CompetitorProducts', 'Name') IS NULL
BEGIN
  ALTER TABLE dbo.CompetitorProducts
    ADD Name NVARCHAR(500) NULL;
END
GO
