using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IpamService.Data.Migrations.Postgres
{
    /// <inheritdoc />
    public partial class RemoveTenancyIdFromAllocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TenancyId",
                table: "Allocations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TenancyId",
                table: "Allocations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));
        }
    }
}
