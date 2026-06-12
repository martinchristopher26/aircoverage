using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AirCoverage.Api.Migrations
{
    /// <inheritdoc />
    public partial class UnionMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastChangedWatermark",
                table: "SyncState");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedWatermark",
                table: "SyncState",
                type: "TEXT",
                nullable: true);
        }
    }
}
