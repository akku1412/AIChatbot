IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013061613_InitialCreate'
)
BEGIN
    CREATE TABLE [Users] (
        [UserId] int NOT NULL IDENTITY,
        [GoogleId] nvarchar(255) NOT NULL,
        [FullName] nvarchar(200) NOT NULL,
        [Email] nvarchar(256) NOT NULL,
        [ProfileImageUrl] nvarchar(2048) NULL,
        [IsEmailVerified] bit NOT NULL DEFAULT CAST(0 AS bit),
        [CreatedAt] datetime2(3) NOT NULL,
        [UpdatedAt] datetime2(3) NOT NULL,
        CONSTRAINT [PK_Users] PRIMARY KEY ([UserId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013061613_InitialCreate'
)
BEGIN
    CREATE TABLE [Conversations] (
        [ConversationId] int NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [Title] nvarchar(200) NOT NULL,
        [CreatedAt] datetime2(3) NOT NULL,
        [UpdatedAt] datetime2(3) NOT NULL,
        CONSTRAINT [PK_Conversations] PRIMARY KEY ([ConversationId]),
        CONSTRAINT [FK_Conversations_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013061613_InitialCreate'
)
BEGIN
    CREATE TABLE [OtpVerifications] (
        [OtpVerificationId] int NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [OtpHash] varchar(128) NOT NULL,
        [ExpiresAt] datetime2(3) NOT NULL,
        [IsUsed] bit NOT NULL DEFAULT CAST(0 AS bit),
        [AttemptCount] int NOT NULL DEFAULT 0,
        [CreatedAt] datetime2(3) NOT NULL,
        CONSTRAINT [PK_OtpVerifications] PRIMARY KEY ([OtpVerificationId]),
        CONSTRAINT [CK_OtpVerifications_AttemptCount] CHECK ([AttemptCount] >= 0),
        CONSTRAINT [FK_OtpVerifications_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013061613_InitialCreate'
)
BEGIN
    CREATE TABLE [RefreshTokens] (
        [RefreshTokenId] int NOT NULL IDENTITY,
        [UserId] int NOT NULL,
        [TokenHash] varchar(128) NOT NULL,
        [ExpiresAt] datetime2(3) NOT NULL,
        [RevokedAt] datetime2(3) NULL,
        [CreatedAt] datetime2(3) NOT NULL,
        CONSTRAINT [PK_RefreshTokens] PRIMARY KEY ([RefreshTokenId]),
        CONSTRAINT [FK_RefreshTokens_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013061613_InitialCreate'
)
BEGIN
    CREATE TABLE [Messages] (
        [MessageId] bigint NOT NULL IDENTITY,
        [ConversationId] int NOT NULL,
        [Role] varchar(20) NOT NULL,
        [Content] nvarchar(max) NOT NULL,
        [CreatedAt] datetime2(3) NOT NULL,
        [TokenUsage] int NULL,
        CONSTRAINT [PK_Messages] PRIMARY KEY ([MessageId]),
        CONSTRAINT [CK_Messages_Role] CHECK ([Role] IN ('user', 'assistant', 'system')),
        CONSTRAINT [CK_Messages_TokenUsage] CHECK ([TokenUsage] IS NULL OR [TokenUsage] >= 0),
        CONSTRAINT [FK_Messages_Conversations_ConversationId] FOREIGN KEY ([ConversationId]) REFERENCES [Conversations] ([ConversationId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013061613_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Conversations_UserId_UpdatedAt] ON [Conversations] ([UserId], [UpdatedAt] DESC);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013061613_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Messages_ConversationId_CreatedAt] ON [Messages] ([ConversationId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013061613_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_OtpVerifications_UserId_IsUsed_CreatedAt] ON [OtpVerifications] ([UserId], [IsUsed], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013061613_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_RefreshTokens_UserId] ON [RefreshTokens] ([UserId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013061613_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_RefreshTokens_TokenHash] ON [RefreshTokens] ([TokenHash]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013061613_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Users_Email] ON [Users] ([Email]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013061613_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [UX_Users_GoogleId] ON [Users] ([GoogleId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261013061613_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261013061613_InitialCreate', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015064133_EmailOtpLoginAndOptionalGoogleId'
)
BEGIN
    DELETE FROM [OtpVerifications];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015064133_EmailOtpLoginAndOptionalGoogleId'
)
BEGIN
    ALTER TABLE [OtpVerifications] DROP CONSTRAINT [FK_OtpVerifications_Users_UserId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015064133_EmailOtpLoginAndOptionalGoogleId'
)
BEGIN
    DROP INDEX [UX_Users_GoogleId] ON [Users];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015064133_EmailOtpLoginAndOptionalGoogleId'
)
BEGIN
    DROP INDEX [IX_OtpVerifications_UserId_IsUsed_CreatedAt] ON [OtpVerifications];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015064133_EmailOtpLoginAndOptionalGoogleId'
)
BEGIN
    DECLARE @var0 sysname;
    SELECT @var0 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OtpVerifications]') AND [c].[name] = N'UserId');
    IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [OtpVerifications] DROP CONSTRAINT [' + @var0 + '];');
    ALTER TABLE [OtpVerifications] DROP COLUMN [UserId];
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015064133_EmailOtpLoginAndOptionalGoogleId'
)
BEGIN
    DECLARE @var1 sysname;
    SELECT @var1 = [d].[name]
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[Users]') AND [c].[name] = N'GoogleId');
    IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [Users] DROP CONSTRAINT [' + @var1 + '];');
    ALTER TABLE [Users] ALTER COLUMN [GoogleId] nvarchar(255) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015064133_EmailOtpLoginAndOptionalGoogleId'
)
BEGIN
    ALTER TABLE [OtpVerifications] ADD [Email] nvarchar(256) NOT NULL DEFAULT N'';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015064133_EmailOtpLoginAndOptionalGoogleId'
)
BEGIN
    EXEC(N'CREATE UNIQUE INDEX [UX_Users_GoogleId] ON [Users] ([GoogleId]) WHERE [GoogleId] IS NOT NULL');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015064133_EmailOtpLoginAndOptionalGoogleId'
)
BEGIN
    CREATE INDEX [IX_OtpVerifications_Email_IsUsed_CreatedAt] ON [OtpVerifications] ([Email], [IsUsed], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015064133_EmailOtpLoginAndOptionalGoogleId'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261015064133_EmailOtpLoginAndOptionalGoogleId', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016053559_AddFileUploadsAndAttachments'
)
BEGIN
    CREATE TABLE [UploadedFiles] (
        [FileId] uniqueidentifier NOT NULL,
        [UserId] int NOT NULL,
        [OriginalFileName] nvarchar(255) NOT NULL,
        [StorageKey] varchar(100) NOT NULL,
        [ContentType] varchar(150) NOT NULL,
        [FileExtension] varchar(10) NOT NULL,
        [FileSize] bigint NOT NULL,
        [ProcessingStatus] varchar(20) NOT NULL,
        [StatusMessage] nvarchar(500) NULL,
        [PageCount] int NULL,
        [ExtractedCharacters] int NOT NULL,
        [CreatedAt] datetime2(3) NOT NULL,
        [UpdatedAt] datetime2(3) NOT NULL,
        CONSTRAINT [PK_UploadedFiles] PRIMARY KEY ([FileId]),
        CONSTRAINT [CK_UploadedFiles_FileSize] CHECK ([FileSize] >= 0),
        CONSTRAINT [CK_UploadedFiles_Status] CHECK ([ProcessingStatus] IN ('uploaded', 'processing', 'completed', 'failed')),
        CONSTRAINT [FK_UploadedFiles_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016053559_AddFileUploadsAndAttachments'
)
BEGIN
    CREATE TABLE [DocumentChunks] (
        [ChunkId] bigint NOT NULL IDENTITY,
        [FileId] uniqueidentifier NOT NULL,
        [ChunkIndex] int NOT NULL,
        [Kind] varchar(20) NOT NULL,
        [Location] nvarchar(300) NOT NULL,
        [Content] nvarchar(max) NOT NULL,
        CONSTRAINT [PK_DocumentChunks] PRIMARY KEY ([ChunkId]),
        CONSTRAINT [FK_DocumentChunks_UploadedFiles_FileId] FOREIGN KEY ([FileId]) REFERENCES [UploadedFiles] ([FileId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016053559_AddFileUploadsAndAttachments'
)
BEGIN
    CREATE TABLE [MessageAttachments] (
        [MessageAttachmentId] bigint NOT NULL IDENTITY,
        [MessageId] bigint NOT NULL,
        [FileId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_MessageAttachments] PRIMARY KEY ([MessageAttachmentId]),
        CONSTRAINT [FK_MessageAttachments_Messages_MessageId] FOREIGN KEY ([MessageId]) REFERENCES [Messages] ([MessageId]) ON DELETE CASCADE,
        CONSTRAINT [FK_MessageAttachments_UploadedFiles_FileId] FOREIGN KEY ([FileId]) REFERENCES [UploadedFiles] ([FileId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016053559_AddFileUploadsAndAttachments'
)
BEGIN
    CREATE UNIQUE INDEX [UX_DocumentChunks_FileId_ChunkIndex] ON [DocumentChunks] ([FileId], [ChunkIndex]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016053559_AddFileUploadsAndAttachments'
)
BEGIN
    CREATE INDEX [IX_MessageAttachments_FileId] ON [MessageAttachments] ([FileId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016053559_AddFileUploadsAndAttachments'
)
BEGIN
    CREATE UNIQUE INDEX [UX_MessageAttachments_MessageId_FileId] ON [MessageAttachments] ([MessageId], [FileId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016053559_AddFileUploadsAndAttachments'
)
BEGIN
    CREATE INDEX [IX_UploadedFiles_UserId_CreatedAt] ON [UploadedFiles] ([UserId], [CreatedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016053559_AddFileUploadsAndAttachments'
)
BEGIN
    CREATE UNIQUE INDEX [UX_UploadedFiles_StorageKey] ON [UploadedFiles] ([StorageKey]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261016053559_AddFileUploadsAndAttachments'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261016053559_AddFileUploadsAndAttachments', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023054650_Phase1ChatExperience'
)
BEGIN
    ALTER TABLE [Messages] ADD [IsActive] bit NOT NULL DEFAULT CAST(1 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023054650_Phase1ChatExperience'
)
BEGIN
    ALTER TABLE [Messages] ADD [IsTruncated] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023054650_Phase1ChatExperience'
)
BEGIN
    ALTER TABLE [Messages] ADD [ParentMessageId] bigint NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023054650_Phase1ChatExperience'
)
BEGIN
    ALTER TABLE [Messages] ADD [VersionIndex] int NOT NULL DEFAULT 1;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023054650_Phase1ChatExperience'
)
BEGIN
    ALTER TABLE [Conversations] ADD [ArchivedAt] datetime2(3) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023054650_Phase1ChatExperience'
)
BEGIN
    ALTER TABLE [Conversations] ADD [IsPinned] bit NOT NULL DEFAULT CAST(0 AS bit);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023054650_Phase1ChatExperience'
)
BEGIN
    CREATE INDEX [IX_Messages_ConversationId_ParentMessageId] ON [Messages] ([ConversationId], [ParentMessageId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023054650_Phase1ChatExperience'
)
BEGIN
    CREATE INDEX [IX_Messages_ParentMessageId] ON [Messages] ([ParentMessageId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023054650_Phase1ChatExperience'
)
BEGIN
    CREATE INDEX [IX_Conversations_UserId_Archived_Pinned_UpdatedAt] ON [Conversations] ([UserId], [ArchivedAt], [IsPinned] DESC, [UpdatedAt] DESC);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023054650_Phase1ChatExperience'
)
BEGIN

    UPDATE m SET m.ParentMessageId = p.PreviousId
    FROM Messages AS m
    JOIN (SELECT MessageId, LAG(MessageId) OVER (PARTITION BY ConversationId ORDER BY MessageId) AS PreviousId FROM Messages) AS p
        ON p.MessageId = m.MessageId
    WHERE m.ParentMessageId IS NULL AND p.PreviousId IS NOT NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023054650_Phase1ChatExperience'
)
BEGIN
    ALTER TABLE [Messages] ADD CONSTRAINT [FK_Messages_Messages_ParentMessageId] FOREIGN KEY ([ParentMessageId]) REFERENCES [Messages] ([MessageId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023054650_Phase1ChatExperience'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261023054650_Phase1ChatExperience', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023105028_Phase2DocumentIntelligence'
)
BEGIN
    ALTER TABLE [UploadedFiles] ADD [ProgressCurrent] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023105028_Phase2DocumentIntelligence'
)
BEGIN
    ALTER TABLE [UploadedFiles] ADD [ProgressTotal] int NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023105028_Phase2DocumentIntelligence'
)
BEGIN
    ALTER TABLE [UploadedFiles] ADD [StatusReason] varchar(50) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023105028_Phase2DocumentIntelligence'
)
BEGIN
    ALTER TABLE [DocumentChunks] ADD [Source] varchar(10) NOT NULL DEFAULT 'text';
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023105028_Phase2DocumentIntelligence'
)
BEGIN
    CREATE TABLE [ChunkEmbeddings] (
        [ChunkId] bigint NOT NULL,
        [Model] varchar(100) NOT NULL,
        [Dimensions] int NOT NULL,
        [Vector] varbinary(max) NOT NULL,
        CONSTRAINT [PK_ChunkEmbeddings] PRIMARY KEY ([ChunkId]),
        CONSTRAINT [CK_ChunkEmbeddings_Dimensions] CHECK ([Dimensions] > 0),
        CONSTRAINT [FK_ChunkEmbeddings_DocumentChunks_ChunkId] FOREIGN KEY ([ChunkId]) REFERENCES [DocumentChunks] ([ChunkId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023105028_Phase2DocumentIntelligence'
)
BEGIN
    CREATE TABLE [MessageSources] (
        [MessageSourceId] bigint NOT NULL IDENTITY,
        [MessageId] bigint NOT NULL,
        [ChunkId] bigint NOT NULL,
        [Score] float NOT NULL,
        CONSTRAINT [PK_MessageSources] PRIMARY KEY ([MessageSourceId]),
        CONSTRAINT [FK_MessageSources_DocumentChunks_ChunkId] FOREIGN KEY ([ChunkId]) REFERENCES [DocumentChunks] ([ChunkId]),
        CONSTRAINT [FK_MessageSources_Messages_MessageId] FOREIGN KEY ([MessageId]) REFERENCES [Messages] ([MessageId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023105028_Phase2DocumentIntelligence'
)
BEGIN
    CREATE INDEX [IX_MessageSources_ChunkId] ON [MessageSources] ([ChunkId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023105028_Phase2DocumentIntelligence'
)
BEGIN
    CREATE UNIQUE INDEX [UX_MessageSources_MessageId_ChunkId] ON [MessageSources] ([MessageId], [ChunkId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261023105028_Phase2DocumentIntelligence'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261023105028_Phase2DocumentIntelligence', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026045429_Phase3ModelPicker'
)
BEGIN
    ALTER TABLE [Messages] ADD [ModelId] varchar(32) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026045429_Phase3ModelPicker'
)
BEGIN
    ALTER TABLE [Conversations] ADD [ModelId] varchar(32) NULL;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026045429_Phase3ModelPicker'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261026045429_Phase3ModelPicker', N'8.0.31');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026050603_Phase3CustomInstructions'
)
BEGIN
    CREATE TABLE [UserSettings] (
        [UserId] int NOT NULL,
        [AboutMe] nvarchar(1500) NULL,
        [ResponseStyle] nvarchar(1500) NULL,
        [CreatedAt] datetime2(3) NOT NULL,
        [UpdatedAt] datetime2(3) NOT NULL,
        CONSTRAINT [PK_UserSettings] PRIMARY KEY ([UserId]),
        CONSTRAINT [FK_UserSettings_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([UserId]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261026050603_Phase3CustomInstructions'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261026050603_Phase3CustomInstructions', N'8.0.31');
END;
GO

COMMIT;
GO

