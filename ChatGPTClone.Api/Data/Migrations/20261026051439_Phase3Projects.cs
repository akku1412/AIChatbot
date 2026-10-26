using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatGPTClone.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3Projects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProjectId",
                table: "Conversations",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    ProjectId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Instructions = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
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
                name: "ProjectFiles",
                columns: table => new
                {
                    ProjectId = table.Column<int>(type: "int", nullable: false),
                    FileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2(3)", nullable: false)
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

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_ProjectId",
                table: "Conversations",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFiles_FileId",
                table: "ProjectFiles",
                column: "FileId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_UserId_Name",
                table: "Projects",
                columns: new[] { "UserId", "Name" });

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_Projects_ProjectId",
                table: "Conversations",
                column: "ProjectId",
                principalTable: "Projects",
                principalColumn: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_Projects_ProjectId",
                table: "Conversations");

            migrationBuilder.DropTable(
                name: "ProjectFiles");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_ProjectId",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "ProjectId",
                table: "Conversations");
        }
    }
}
