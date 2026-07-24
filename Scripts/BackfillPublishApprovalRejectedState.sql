-- Marca borradores cuyo último evento de bitácora es «Rechazado» (envío al cliente).
-- Ejecutar una vez tras desplegar el estado PublishToClientApprovalState = 2.
UPDATE r
SET PublishToClientApprovalState = 2
FROM Requests r
INNER JOIN (
    SELECT e.RequestId
    FROM RequestPublishApprovalEvents e
    INNER JOIN (
        SELECT RequestId, MAX(OccurredAtUtc) AS MaxAt
        FROM RequestPublishApprovalEvents
        GROUP BY RequestId
    ) latest ON latest.RequestId = e.RequestId AND latest.MaxAt = e.OccurredAtUtc
    WHERE e.EventType = 3
) rej ON rej.RequestId = r.RequestId
WHERE r.Status = 0
  AND r.PublishToClientApprovalState = 0;
