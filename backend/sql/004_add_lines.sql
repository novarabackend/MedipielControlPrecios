-- Add Lines table
USE MedipielControlPrecios;
GO

IF OBJECT_ID('dbo.Lines', 'U') IS NULL
BEGIN
  CREATE TABLE dbo.Lines (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name NVARCHAR(200) NOT NULL,
    CreatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
  );
  CREATE UNIQUE INDEX UX_Lines_Name ON dbo.Lines(Name);
END
GO
