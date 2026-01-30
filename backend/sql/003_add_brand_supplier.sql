-- Add SupplierId to Brands
USE MedipielControlPrecios;
GO

IF COL_LENGTH('dbo.Brands', 'SupplierId') IS NULL
BEGIN
  ALTER TABLE dbo.Brands ADD SupplierId INT NULL;
END
GO

IF NOT EXISTS (
  SELECT 1
  FROM sys.foreign_keys
  WHERE name = 'FK_Brands_Suppliers'
)
BEGIN
  ALTER TABLE dbo.Brands
    ADD CONSTRAINT FK_Brands_Suppliers FOREIGN KEY (SupplierId)
    REFERENCES dbo.Suppliers(Id)
    ON DELETE SET NULL;
END
GO
