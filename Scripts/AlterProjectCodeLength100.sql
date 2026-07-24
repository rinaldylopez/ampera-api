-- Amplía ProjectCode de nvarchar(20) a nvarchar(100).
ALTER TABLE Projects ALTER COLUMN ProjectCode nvarchar(100) NOT NULL;
