-- Make EAN the unique key and SKU optional
USE MedipielControlPrecios;
GO

IF COL_LENGTH('dbo.Products', 'Sku') IS NOT NULL
BEGIN
  ALTER TABLE dbo.Products ALTER COLUMN Sku NVARCHAR(100) NULL;
END
GO

IF EXISTS (
  SELECT 1 FROM sys.indexes
  WHERE name = 'UX_Products_Sku' AND object_id = OBJECT_ID('dbo.Products')
)
BEGIN
  DROP INDEX UX_Products_Sku ON dbo.Products;
END
GO

IF EXISTS (
  SELECT 1 FROM sys.indexes
  WHERE name = 'IX_Products_Ean' AND object_id = OBJECT_ID('dbo.Products')
)
BEGIN
  DROP INDEX IX_Products_Ean ON dbo.Products;
END
GO

IF EXISTS (
  SELECT 1 FROM sys.indexes
  WHERE name = 'UX_Products_Ean' AND object_id = OBJECT_ID('dbo.Products')
)
BEGIN
  DROP INDEX UX_Products_Ean ON dbo.Products;
END
GO

SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET ARITHABORT ON;
SET NUMERIC_ROUNDABORT OFF;
GO

CREATE UNIQUE INDEX UX_Products_Ean ON dbo.Products(Ean)
WHERE Ean IS NOT NULL;
GO
