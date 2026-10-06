using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EarlyInterventionCare.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSubmissionReceiptWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "receipt_expires_at_utc",
                table: "teacher_sessions",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "receipt_expires_at_utc",
                table: "teacher_sessions");
        }
    }
}
