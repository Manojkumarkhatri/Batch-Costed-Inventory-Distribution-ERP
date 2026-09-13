using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopApp.Data.Migrations
{
    /// <summary>
    /// Cash in the till on day one. Every later movement is derived, but the
    /// starting figure has no transaction behind it and has to be stated.
    /// Defaults to zero, which is correct for a fresh database.
    /// </summary>
    public partial class OpeningCash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "OpeningCashInHand",
                table: "Settings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "OpeningCashInHand", table: "Settings");
        }
    }
}
