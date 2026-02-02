-- Add scheduler settings and runs
USE MedipielControlPrecios;
GO

IF OBJECT_ID('dbo.SchedulerSettings', 'U') IS NULL
BEGIN
  CREATE TABLE dbo.SchedulerSettings (
    Id INT NOT NULL PRIMARY KEY,
    DailyTime TIME NOT NULL,
    DaysOfWeekMask INT NOT NULL,
    Enabled BIT NOT NULL,
    Mode NVARCHAR(20) NOT NULL,
    UpdatedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME()
  );
END
GO

IF NOT EXISTS (SELECT 1 FROM dbo.SchedulerSettings WHERE Id = 1)
BEGIN
  INSERT INTO dbo.SchedulerSettings (Id, DailyTime, DaysOfWeekMask, Enabled, Mode)
  VALUES (1, '06:00', 127, 1, 'Complete');
END
GO

IF OBJECT_ID('dbo.SchedulerRuns', 'U') IS NULL
BEGIN
  CREATE TABLE dbo.SchedulerRuns (
    Id INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    StartedAt DATETIME2 NOT NULL DEFAULT SYSUTCDATETIME(),
    FinishedAt DATETIME2 NULL,
    Status NVARCHAR(20) NOT NULL,
    TriggerType NVARCHAR(20) NOT NULL,
    Message NVARCHAR(4000) NULL
  );
END
GO

IF NOT EXISTS (
  SELECT 1
  FROM sys.indexes
  WHERE name = 'UX_SchedulerRuns_Running' AND object_id = OBJECT_ID('dbo.SchedulerRuns')
)
BEGIN
  SET ANSI_NULLS ON;
  SET QUOTED_IDENTIFIER ON;
  SET ANSI_PADDING ON;
  SET ANSI_WARNINGS ON;
  SET CONCAT_NULL_YIELDS_NULL ON;
  SET ARITHABORT ON;
  SET NUMERIC_ROUNDABORT OFF;
  CREATE UNIQUE INDEX UX_SchedulerRuns_Running ON dbo.SchedulerRuns(Status)
  WHERE Status = 'Running';
END
GO
