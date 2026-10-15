using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChatGPTClone.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class EmailOtpLoginAndOptionalGoogleId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing codes were tied to a user, not an email, and are all short-lived (5 minutes):
            // clear them so the new non-null Email column never holds placeholder values.
            migrationBuilder.Sql("DELETE FROM [OtpVerifications];");

            migrationBuilder.DropForeignKey(
                name: "FK_OtpVerifications_Users_UserId",
                table: "OtpVerifications");

            migrationBuilder.DropIndex(
                name: "UX_Users_GoogleId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_OtpVerifications_UserId_IsUsed_CreatedAt",
                table: "OtpVerifications");

            migrationBuilder.DropColumn(
                name: "UserId",
                table: "OtpVerifications");

            migrationBuilder.AlterColumn<string>(
                name: "GoogleId",
                table: "Users",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "OtpVerifications",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "UX_Users_GoogleId",
                table: "Users",
                column: "GoogleId",
                unique: true,
                filter: "[GoogleId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OtpVerifications_Email_IsUsed_CreatedAt",
                table: "OtpVerifications",
                columns: new[] { "Email", "IsUsed", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM [OtpVerifications];");

            migrationBuilder.DropIndex(
                name: "UX_Users_GoogleId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_OtpVerifications_Email_IsUsed_CreatedAt",
                table: "OtpVerifications");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "OtpVerifications");

            migrationBuilder.AlterColumn<string>(
                name: "GoogleId",
                table: "Users",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(255)",
                oldMaxLength: 255,
                oldNullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UserId",
                table: "OtpVerifications",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "UX_Users_GoogleId",
                table: "Users",
                column: "GoogleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OtpVerifications_UserId_IsUsed_CreatedAt",
                table: "OtpVerifications",
                columns: new[] { "UserId", "IsUsed", "CreatedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_OtpVerifications_Users_UserId",
                table: "OtpVerifications",
                column: "UserId",
                principalTable: "Users",
                principalColumn: "UserId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
