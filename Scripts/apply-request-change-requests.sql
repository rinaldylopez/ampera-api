-- Solicitudes de cambio del portal cliente (extensión de plazo / modificación).
-- Idempotente: crea la tabla solo si no existe.

IF OBJECT_ID(N'dbo.RequestChangeRequests', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.RequestChangeRequests
    (
        RequestChangeRequestId INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_RequestChangeRequests PRIMARY KEY,
        RequestId INT NOT NULL,
        RequestedByUserId INT NOT NULL,
        RequestedAt DATETIME2 NOT NULL,
        Kind INT NOT NULL,
        ProposedDueDate DATETIME2 NULL,
        Reason NVARCHAR(2000) NOT NULL,
        Status INT NOT NULL,
        ReviewedByUserId INT NULL,
        ReviewedAt DATETIME2 NULL,
        ReviewNote NVARCHAR(2000) NULL,
        PreviousDueDate DATETIME2 NULL,
        CONSTRAINT FK_RequestChangeRequests_Requests_RequestId
            FOREIGN KEY (RequestId) REFERENCES dbo.Requests (RequestId) ON DELETE CASCADE,
        CONSTRAINT FK_RequestChangeRequests_Users_RequestedByUserId
            FOREIGN KEY (RequestedByUserId) REFERENCES dbo.Users (UserId),
        CONSTRAINT FK_RequestChangeRequests_Users_ReviewedByUserId
            FOREIGN KEY (ReviewedByUserId) REFERENCES dbo.Users (UserId) ON DELETE SET NULL
    );

    CREATE INDEX IX_RequestChangeRequests_RequestId
        ON dbo.RequestChangeRequests (RequestId);

    CREATE INDEX IX_RequestChangeRequests_RequestId_Status
        ON dbo.RequestChangeRequests (RequestId, Status);

    CREATE INDEX IX_RequestChangeRequests_RequestedByUserId
        ON dbo.RequestChangeRequests (RequestedByUserId);

    CREATE INDEX IX_RequestChangeRequests_ReviewedByUserId
        ON dbo.RequestChangeRequests (ReviewedByUserId);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261007210000_RequestChangeRequests'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261007210000_RequestChangeRequests', N'9.0.14');
END
GO
