-- Digest diario de vencidos: bitácora por Usuario + día (un correo agregado, no uno por solicitud).
-- Si existía la versión anterior (RequestId), se recrea la tabla.

IF OBJECT_ID(N'dbo.RequestOverdueReminderDispatches', N'U') IS NOT NULL
BEGIN
    DROP TABLE dbo.RequestOverdueReminderDispatches;
END
GO

CREATE TABLE dbo.RequestOverdueReminderDispatches
(
    RequestOverdueReminderDispatchId INT IDENTITY(1,1) NOT NULL
        CONSTRAINT PK_RequestOverdueReminderDispatches PRIMARY KEY,
    UserId INT NOT NULL,
    NotifyOnDate DATETIME2 NOT NULL,
    DispatchedAt DATETIME2 NOT NULL,
    CONSTRAINT FK_RequestOverdueReminderDispatches_Users_UserId
        FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId) ON DELETE CASCADE
);

CREATE UNIQUE INDEX IX_RequestOverdueReminderDispatches_UserId_NotifyOnDate
    ON dbo.RequestOverdueReminderDispatches (UserId, NotifyOnDate);
GO

IF NOT EXISTS (
    SELECT 1 FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007200000_RequestOverdueWeeklyReminders'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261007200000_RequestOverdueWeeklyReminders', N'9.0.14');
END
GO
