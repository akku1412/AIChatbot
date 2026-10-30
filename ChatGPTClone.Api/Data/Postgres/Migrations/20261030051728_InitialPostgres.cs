using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ChatGPTClone.Api.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OtpVerifications",
                columns: table => new
                {
                    OtpVerificationId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OtpHash = table.Column<string>(type: "character varying(128)", unicode: false, maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    IsUsed = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OtpVerifications", x => x.OtpVerificationId);
                    table.CheckConstraint("CK_OtpVerifications_AttemptCount", "\"AttemptCount\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    GoogleId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    FullName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ProfileImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    IsEmailVerified = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "User"),
                    IsBlocked = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.UserId);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    ProjectId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Instructions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.ProjectId);
                    table.ForeignKey(
                        name: "FK_Projects_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    RefreshTokenId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(128)", unicode: false, maxLength: 128, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.RefreshTokenId);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UploadedFiles",
                columns: table => new
                {
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(100)", unicode: false, maxLength: 100, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(150)", unicode: false, maxLength: 150, nullable: false),
                    FileExtension = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: false),
                    FileSize = table.Column<long>(type: "bigint", nullable: false),
                    ProcessingStatus = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    StatusMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    StatusReason = table.Column<string>(type: "character varying(50)", unicode: false, maxLength: 50, nullable: true),
                    ProgressCurrent = table.Column<int>(type: "integer", nullable: true),
                    ProgressTotal = table.Column<int>(type: "integer", nullable: true),
                    PageCount = table.Column<int>(type: "integer", nullable: true),
                    ExtractedCharacters = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadedFiles", x => x.FileId);
                    table.CheckConstraint("CK_UploadedFiles_FileSize", "\"FileSize\" >= 0");
                    table.CheckConstraint("CK_UploadedFiles_Status", "\"ProcessingStatus\" IN ('uploaded', 'processing', 'completed', 'failed')");
                    table.ForeignKey(
                        name: "FK_UploadedFiles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UsageDaily",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Date = table.Column<DateTime>(type: "date", nullable: false),
                    MessageCount = table.Column<int>(type: "integer", nullable: false),
                    InputTokens = table.Column<long>(type: "bigint", nullable: false),
                    OutputTokens = table.Column<long>(type: "bigint", nullable: false),
                    UploadCount = table.Column<int>(type: "integer", nullable: false),
                    UploadBytes = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsageDaily", x => new { x.UserId, x.Date });
                    table.ForeignKey(
                        name: "FK_UsageDaily_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSettings",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    AboutMe = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: true),
                    ResponseStyle = table.Column<string>(type: "character varying(1500)", maxLength: 1500, nullable: true),
                    Theme = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSettings", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserSettings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Conversations",
                columns: table => new
                {
                    ConversationId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    IsPinned = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ArchivedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true),
                    ModelId = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: true),
                    ProjectId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.ConversationId);
                    table.ForeignKey(
                        name: "FK_Conversations_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId");
                    table.ForeignKey(
                        name: "FK_Conversations_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DocumentChunks",
                columns: table => new
                {
                    ChunkId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChunkIndex = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    Location = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Source = table.Column<string>(type: "character varying(10)", unicode: false, maxLength: 10, nullable: false, defaultValue: "text")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentChunks", x => x.ChunkId);
                    table.ForeignKey(
                        name: "FK_DocumentChunks_UploadedFiles_FileId",
                        column: x => x.FileId,
                        principalTable: "UploadedFiles",
                        principalColumn: "FileId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectFiles",
                columns: table => new
                {
                    ProjectId = table.Column<int>(type: "integer", nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectFiles", x => new { x.ProjectId, x.FileId });
                    table.ForeignKey(
                        name: "FK_ProjectFiles_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectFiles_UploadedFiles_FileId",
                        column: x => x.FileId,
                        principalTable: "UploadedFiles",
                        principalColumn: "FileId");
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    MessageId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConversationId = table.Column<int>(type: "integer", nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", unicode: false, maxLength: 20, nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    TokenUsage = table.Column<int>(type: "integer", nullable: true),
                    ParentMessageId = table.Column<long>(type: "bigint", nullable: true),
                    VersionIndex = table.Column<int>(type: "integer", nullable: false, defaultValue: 1),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    IsTruncated = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    ModelId = table.Column<string>(type: "character varying(32)", unicode: false, maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.MessageId);
                    table.CheckConstraint("CK_Messages_Role", "\"Role\" IN ('user', 'assistant', 'system')");
                    table.CheckConstraint("CK_Messages_TokenUsage", "\"TokenUsage\" IS NULL OR \"TokenUsage\" >= 0");
                    table.ForeignKey(
                        name: "FK_Messages_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "ConversationId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Messages_Messages_ParentMessageId",
                        column: x => x.ParentMessageId,
                        principalTable: "Messages",
                        principalColumn: "MessageId");
                });

            migrationBuilder.CreateTable(
                name: "SharedLinks",
                columns: table => new
                {
                    SharedLinkId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ConversationId = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SnapshotJson = table.Column<string>(type: "text", nullable: false),
                    MessageCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharedLinks", x => x.SharedLinkId);
                    table.ForeignKey(
                        name: "FK_SharedLinks_Conversations_ConversationId",
                        column: x => x.ConversationId,
                        principalTable: "Conversations",
                        principalColumn: "ConversationId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChunkEmbeddings",
                columns: table => new
                {
                    ChunkId = table.Column<long>(type: "bigint", nullable: false),
                    Model = table.Column<string>(type: "character varying(100)", unicode: false, maxLength: 100, nullable: false),
                    Dimensions = table.Column<int>(type: "integer", nullable: false),
                    Vector = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChunkEmbeddings", x => x.ChunkId);
                    table.CheckConstraint("CK_ChunkEmbeddings_Dimensions", "\"Dimensions\" > 0");
                    table.ForeignKey(
                        name: "FK_ChunkEmbeddings_DocumentChunks_ChunkId",
                        column: x => x.ChunkId,
                        principalTable: "DocumentChunks",
                        principalColumn: "ChunkId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MessageAttachments",
                columns: table => new
                {
                    MessageAttachmentId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    FileId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageAttachments", x => x.MessageAttachmentId);
                    table.ForeignKey(
                        name: "FK_MessageAttachments_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "MessageId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_MessageAttachments_UploadedFiles_FileId",
                        column: x => x.FileId,
                        principalTable: "UploadedFiles",
                        principalColumn: "FileId");
                });

            migrationBuilder.CreateTable(
                name: "MessageFeedback",
                columns: table => new
                {
                    MessageFeedbackId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Rating = table.Column<short>(type: "smallint", nullable: false),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp(3) with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageFeedback", x => x.MessageFeedbackId);
                    table.CheckConstraint("CK_MessageFeedback_Rating", "\"Rating\" IN (-1, 1)");
                    table.ForeignKey(
                        name: "FK_MessageFeedback_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "MessageId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MessageSources",
                columns: table => new
                {
                    MessageSourceId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    ChunkId = table.Column<long>(type: "bigint", nullable: false),
                    Score = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageSources", x => x.MessageSourceId);
                    table.ForeignKey(
                        name: "FK_MessageSources_DocumentChunks_ChunkId",
                        column: x => x.ChunkId,
                        principalTable: "DocumentChunks",
                        principalColumn: "ChunkId");
                    table.ForeignKey(
                        name: "FK_MessageSources_Messages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "Messages",
                        principalColumn: "MessageId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_ProjectId",
                table: "Conversations",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_UserId_Archived_Pinned_UpdatedAt",
                table: "Conversations",
                columns: new[] { "UserId", "ArchivedAt", "IsPinned", "UpdatedAt" },
                descending: new[] { false, false, true, true });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_UserId_UpdatedAt",
                table: "Conversations",
                columns: new[] { "UserId", "UpdatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "UX_DocumentChunks_FileId_ChunkIndex",
                table: "DocumentChunks",
                columns: new[] { "FileId", "ChunkIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MessageAttachments_FileId",
                table: "MessageAttachments",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "UX_MessageAttachments_MessageId_FileId",
                table: "MessageAttachments",
                columns: new[] { "MessageId", "FileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MessageFeedback_CreatedAt",
                table: "MessageFeedback",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_MessageFeedback_UserId_CreatedAt",
                table: "MessageFeedback",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_MessageFeedback_MessageId",
                table: "MessageFeedback",
                column: "MessageId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId_CreatedAt",
                table: "Messages",
                columns: new[] { "ConversationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId_ParentMessageId",
                table: "Messages",
                columns: new[] { "ConversationId", "ParentMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ParentMessageId",
                table: "Messages",
                column: "ParentMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageSources_ChunkId",
                table: "MessageSources",
                column: "ChunkId");

            migrationBuilder.CreateIndex(
                name: "UX_MessageSources_MessageId_ChunkId",
                table: "MessageSources",
                columns: new[] { "MessageId", "ChunkId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OtpVerifications_Email_IsUsed_CreatedAt",
                table: "OtpVerifications",
                columns: new[] { "Email", "IsUsed", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFiles_FileId",
                table: "ProjectFiles",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_UserId_Name",
                table: "Projects",
                columns: new[] { "UserId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId",
                table: "RefreshTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "UX_RefreshTokens_TokenHash",
                table: "RefreshTokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SharedLinks_ConversationId_RevokedAt",
                table: "SharedLinks",
                columns: new[] { "ConversationId", "RevokedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_SharedLinks_TokenHash",
                table: "SharedLinks",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UploadedFiles_UserId_CreatedAt",
                table: "UploadedFiles",
                columns: new[] { "UserId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "UX_UploadedFiles_StorageKey",
                table: "UploadedFiles",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UsageDaily_Date",
                table: "UsageDaily",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "UX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_Users_GoogleId",
                table: "Users",
                column: "GoogleId",
                unique: true,
                filter: "\"GoogleId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChunkEmbeddings");

            migrationBuilder.DropTable(
                name: "MessageAttachments");

            migrationBuilder.DropTable(
                name: "MessageFeedback");

            migrationBuilder.DropTable(
                name: "MessageSources");

            migrationBuilder.DropTable(
                name: "OtpVerifications");

            migrationBuilder.DropTable(
                name: "ProjectFiles");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "SharedLinks");

            migrationBuilder.DropTable(
                name: "UsageDaily");

            migrationBuilder.DropTable(
                name: "UserSettings");

            migrationBuilder.DropTable(
                name: "DocumentChunks");

            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "UploadedFiles");

            migrationBuilder.DropTable(
                name: "Conversations");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
