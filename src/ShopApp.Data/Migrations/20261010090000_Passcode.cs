using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopApp.Data.Migrations
{
    /// <summary>
    /// The application passcode, its recovery question, and the idle timeout.
    ///
    /// All nullable: an existing installation has no passcode and is not
    /// locked out by this upgrade. He opts in from Settings.
    /// </summary>
    public partial class Passcode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "PasscodeHash", table: "Settings", type: "BLOB", nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "PasscodeSalt", table: "Settings", type: "BLOB", nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryQuestion", table: "Settings",
                type: "TEXT", maxLength: 200, nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RecoveryAnswerHash", table: "Settings", type: "BLOB", nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RecoveryAnswerSalt", table: "Settings", type: "BLOB", nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AutoLockMinutes", table: "Settings",
                type: "INTEGER", nullable: false, defaultValue: 15);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "PasscodeHash", table: "Settings");
            migrationBuilder.DropColumn(name: "PasscodeSalt", table: "Settings");
            migrationBuilder.DropColumn(name: "RecoveryQuestion", table: "Settings");
            migrationBuilder.DropColumn(name: "RecoveryAnswerHash", table: "Settings");
            migrationBuilder.DropColumn(name: "RecoveryAnswerSalt", table: "Settings");
            migrationBuilder.DropColumn(name: "AutoLockMinutes", table: "Settings");
        }
    }
}
