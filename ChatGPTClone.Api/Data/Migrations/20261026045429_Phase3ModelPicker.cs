using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatGPTClone.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Phase3ModelPicker : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ModelId",
                table: "Messages",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ModelId",
                table: "Conversations",
                type: "varchar(32)",
                unicode: false,
                maxLength: 32,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ModelId",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "ModelId",
                table: "Conversations");
        }
    }
}
