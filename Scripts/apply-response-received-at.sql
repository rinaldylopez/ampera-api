-- Aplicar en batches separados (SQL Server no permite usar la columna nueva en el mismo batch del ALTER).

IF COL_LENGTH('dbo.Requests', 'ResponseReceivedAt') IS NULL
BEGIN
    ALTER TABLE dbo.Requests ADD ResponseReceivedAt datetime2 NULL;
END
GO

UPDATE r
SET ResponseReceivedAt = h.LatestAt
FROM Requests AS r
INNER JOIN (
    SELECT RequestId, MAX(ActionDate) AS LatestAt
    FROM RequestHistory
    WHERE StatusAtMoment = N'Flujo portal'
      AND Observation LIKE N'%envió la documentación a revisión%'
    GROUP BY RequestId
) AS h ON h.RequestId = r.RequestId
WHERE r.ResponseReceivedAt IS NULL
  AND r.PortalHandoffStatus = 2;
GO

IF NOT EXISTS (SELECT 1 FROM [__EFMigrationsHistory] WHERE [MigrationId] = N'20261007190000_RequestResponseReceivedAt')
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261007190000_RequestResponseReceivedAt', N'9.0.14');
END
GO
