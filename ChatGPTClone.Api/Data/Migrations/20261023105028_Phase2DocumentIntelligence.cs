using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatGPTClone.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase2DocumentIntelligence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProgressCurrent",
                table: "UploadedFiles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProgressTotal",
                table: "UploadedFiles",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StatusReason",
                table: "UploadedFiles",
                type: "varchar(50)",
                unicode: false,
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "DocumentChunks",
                type: "varchar(10)",
                unicode: false,
                maxLength: 10,
                nullable: false,
                defaultValue: "text");

            migrationBuilder.CreateTable(
                name: "ChunkEmbeddings",
                columns: table => new
                {
                    ChunkId = table.Column<long>(type: "bigint", nullable: false),
                    Model = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    Dimensions = table.Column<int>(type: "int", nullable: false),
                    Vector = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChunkEmbeddings", x => x.ChunkId);
                    table.CheckConstraint("CK_ChunkEmbeddings_Dimensions", "[Dimensions] > 0");
                    table.ForeignKey(
                        name: "FK_ChunkEmbeddings_DocumentChunks_ChunkId",
                        column: x => x.ChunkId,
                        principalTable: "DocumentChunks",
                        principalColumn: "ChunkId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MessageSources",
                columns: table => new
                {
                    MessageSourceId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    MessageId = table.Column<long>(type: "bigint", nullable: false),
                    ChunkId = table.Column<long>(type: "bigint", nullable: false),
                    Score = table.Column<double>(type: "float", nullable: false)
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
                name: "IX_MessageSources_ChunkId",
                table: "MessageSources",
                column: "ChunkId");

            migrationBuilder.CreateIndex(
                name: "UX_MessageSources_MessageId_ChunkId",
                table: "MessageSources",
                columns: new[] { "MessageId", "ChunkId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChunkEmbeddings");

            migrationBuilder.DropTable(
                name: "MessageSources");

            migrationBuilder.DropColumn(
                name: "ProgressCurrent",
                table: "UploadedFiles");

            migrationBuilder.DropColumn(
                name: "ProgressTotal",
                table: "UploadedFiles");

            migrationBuilder.DropColumn(
                name: "StatusReason",
                table: "UploadedFiles");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "DocumentChunks");
        }
    }
}
