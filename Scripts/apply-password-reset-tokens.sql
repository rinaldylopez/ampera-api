-- Enlaces de restablecimiento de contraseña (un solo uso; solo se guarda el hash).
-- Idempotente.

IF OBJECT_ID(N'dbo.PasswordResetTokens', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.PasswordResetTokens
    (
        PasswordResetTokenId INT IDENTITY(1,1) NOT NULL
            CONSTRAINT PK_PasswordResetTokens PRIMARY KEY,
        UserId INT NOT NULL,
        TokenHash NVARCHAR(64) NOT NULL,
        CreatedAt DATETIME2 NOT NULL,
        ExpiresAt DATETIME2 NOT NULL,
        UsedAt DATETIME2 NULL,
        RequestedByUserId INT NULL,
        FailedAttempts INT NOT NULL CONSTRAINT DF_PasswordResetTokens_FailedAttempts DEFAULT (0),
        CONSTRAINT FK_PasswordResetTokens_Users_UserId
            FOREIGN KEY (UserId) REFERENCES dbo.Users (UserId) ON DELETE CASCADE
    );

    CREATE UNIQUE INDEX IX_PasswordResetTokens_TokenHash
        ON dbo.PasswordResetTokens (TokenHash);

    CREATE INDEX IX_PasswordResetTokens_UserId
        ON dbo.PasswordResetTokens (UserId);
END
GO

IF NOT EXISTS (
    SELECT 1 FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261009140000_PasswordResetTokens'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261009140000_PasswordResetTokens', N'9.0.14');
END
GO
