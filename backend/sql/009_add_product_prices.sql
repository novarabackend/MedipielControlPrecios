USE MedipielControlPrecios;
GO

IF COL_LENGTH('dbo.Products', 'MedipielListPrice') IS NULL
BEGIN
    ALTER TABLE dbo.Products ADD MedipielListPrice DECIMAL(18,2) NULL;
END
GO

IF COL_LENGTH('dbo.Products', 'MedipielPromoPrice') IS NULL
BEGIN
    ALTER TABLE dbo.Products ADD MedipielPromoPrice DECIMAL(18,2) NULL;
END
GO
