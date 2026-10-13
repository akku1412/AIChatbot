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

