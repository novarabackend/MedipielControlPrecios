-- Add adapter metadata to competitors
USE MedipielControlPrecios;
GO

IF COL_LENGTH('dbo.Competitors', 'AdapterId') IS NULL
BEGIN
  ALTER TABLE dbo.Competitors ADD AdapterId NVARCHAR(100) NULL;
END
GO

IF COL_LENGTH('dbo.Competitors', 'IsActive') IS NULL
BEGIN
  ALTER TABLE dbo.Competitors ADD IsActive BIT NOT NULL DEFAULT 1;
END
GO
