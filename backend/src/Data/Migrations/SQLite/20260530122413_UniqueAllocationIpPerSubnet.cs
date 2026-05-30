using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IpamService.Data.Migrations.SQLite
{
    /// <inheritdoc />
    public partial class UniqueAllocationIpPerSubnet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Allocations_SubnetId_IpAddress",
                table: "Allocations",
                columns: new[] { "SubnetId", "IpAddress" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Allocations_SubnetId_IpAddress",
                table: "Allocations");
        }
    }
}
