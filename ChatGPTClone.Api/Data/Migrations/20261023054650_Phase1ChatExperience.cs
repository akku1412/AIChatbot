using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatGPTClone.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase1ChatExperience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                table: "Messages",
                type: "bit",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsTruncated",
                table: "Messages",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<long>(
                name: "ParentMessageId",
                table: "Messages",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VersionIndex",
                table: "Messages",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTime>(
                name: "ArchivedAt",
                table: "Conversations",
                type: "datetime2(3)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsPinned",
                table: "Conversations",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ConversationId_ParentMessageId",
                table: "Messages",
                columns: new[] { "ConversationId", "ParentMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_Messages_ParentMessageId",
                table: "Messages",
                column: "ParentMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_UserId_Archived_Pinned_UpdatedAt",
                table: "Conversations",
                columns: new[] { "UserId", "ArchivedAt", "IsPinned", "UpdatedAt" },
                descending: new[] { false, false, true, true });

            // Existing chats are linear: each message continues the one before it (version 1, active).
            // LAG works on SQL Server 2012+; only rows without a parent are touched, so re-running is harmless.
            migrationBuilder.Sql(@"
UPDATE m SET m.ParentMessageId = p.PreviousId
FROM Messages AS m
JOIN (SELECT MessageId, LAG(MessageId) OVER (PARTITION BY ConversationId ORDER BY MessageId) AS PreviousId FROM Messages) AS p
    ON p.MessageId = m.MessageId
WHERE m.ParentMessageId IS NULL AND p.PreviousId IS NOT NULL;");

            migrationBuilder.AddForeignKey(
                name: "FK_Messages_Messages_ParentMessageId",
                table: "Messages",
                column: "ParentMessageId",
                principalTable: "Messages",
                principalColumn: "MessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Messages_Messages_ParentMessageId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_ConversationId_ParentMessageId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Messages_ParentMessageId",
                table: "Messages");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_UserId_Archived_Pinned_UpdatedAt",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "IsActive",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "IsTruncated",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ParentMessageId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "VersionIndex",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "IsPinned",
                table: "Conversations");
        }
    }
}
