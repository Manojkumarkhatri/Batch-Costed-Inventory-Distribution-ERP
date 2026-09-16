using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopApp.Data.Migrations
{
    /// <summary>
    /// Our own numbering for payment vouchers, and the settings behind it.
    ///
    /// VoucherNo is nullable: payments recorded before this migration have no
    /// number and are left alone rather than renumbered. Renumbering history
    /// would change a figure somebody may already have written down.
    /// </summary>
    public partial class PaymentVouchers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VoucherNo", table: "Payments",
                type: "TEXT", maxLength: 30, nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Payments_VoucherNo", table: "Payments", column: "VoucherNo");

            migrationBuilder.AddColumn<string>(
                name: "PaymentInPrefix", table: "Settings",
                type: "TEXT", nullable: false, defaultValue: "RV-");

            migrationBuilder.AddColumn<int>(
                name: "PaymentInNextNumber", table: "Settings",
                type: "INTEGER", nullable: false, defaultValue: 1);

            migrationBuilder.AddColumn<string>(
                name: "PaymentOutPrefix", table: "Settings",
                type: "TEXT", nullable: false, defaultValue: "PV-");

            migrationBuilder.AddColumn<int>(
                name: "PaymentOutNextNumber", table: "Settings",
                type: "INTEGER", nullable: false, defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "PaymentNumberPadding", table: "Settings",
                type: "INTEGER", nullable: false, defaultValue: 3);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(name: "IX_Payments_VoucherNo", table: "Payments");
            migrationBuilder.DropColumn(name: "VoucherNo", table: "Payments");
            migrationBuilder.DropColumn(name: "PaymentInPrefix", table: "Settings");
            migrationBuilder.DropColumn(name: "PaymentInNextNumber", table: "Settings");
            migrationBuilder.DropColumn(name: "PaymentOutPrefix", table: "Settings");
            migrationBuilder.DropColumn(name: "PaymentOutNextNumber", table: "Settings");
            migrationBuilder.DropColumn(name: "PaymentNumberPadding", table: "Settings");
        }
    }
}
