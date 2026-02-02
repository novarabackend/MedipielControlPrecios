-- Add LineId to Products
USE MedipielControlPrecios;
GO

IF COL_LENGTH('dbo.Products', 'LineId') IS NULL
BEGIN
  ALTER TABLE dbo.Products
  ADD LineId INT NULL;

  ALTER TABLE dbo.Products
  ADD CONSTRAINT FK_Products_Lines FOREIGN KEY (LineId) REFERENCES dbo.Lines(Id);

  CREATE INDEX IX_Products_LineId ON dbo.Products(LineId);
END
GO
