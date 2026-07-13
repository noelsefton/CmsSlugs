-- CmsSlugs.MsSql schema. One row per (Culture, Slug); Data is a JSON blob, round-tripped whole.
IF OBJECT_ID(N'dbo.CmsSlugsEntries', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.CmsSlugsEntries (
        Culture    NVARCHAR(16)  NOT NULL,
        Slug       NVARCHAR(400) NOT NULL,
        ContentId  NVARCHAR(128) NOT NULL,
        Data       NVARCHAR(MAX) NULL,        -- JSON: {"title":"...","sku":"..."}
        CONSTRAINT PK_CmsSlugsEntries PRIMARY KEY (Culture, Slug)
    );
    CREATE INDEX IX_CmsSlugsEntries_Content ON dbo.CmsSlugsEntries (ContentId);
END
