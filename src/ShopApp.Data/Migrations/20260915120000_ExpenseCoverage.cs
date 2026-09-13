using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShopApp.Data.Migrations
{
    /// <summary>
    /// Adds the period an expense covers, so rent paid in advance can be
    /// charged a day at a time instead of landing wholly on the day it was
    /// paid. Both columns are nullable: an expense with neither set belongs
    /// entirely to its own date, which is every expense recorded before this.
    /// </summary>
    public partial class ExpenseCoverage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CoversFrom",
                table: "Expenses",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CoversTo",
                table: "Expenses",
                type: "TEXT",
                nullable: true);

            // Reports ask "which expenses touch this window", which means
            // testing both ends of the covered period, not the payment date.
            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CoversFrom_CoversTo",
                table: "Expenses",
                columns: new[] { "CoversFrom", "CoversTo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Expenses_CoversFrom_CoversTo",
                table: "Expenses");

            migrationBuilder.DropColumn(name: "CoversFrom", table: "Expenses");
            migrationBuilder.DropColumn(name: "CoversTo", table: "Expenses");
        }
    }
}
