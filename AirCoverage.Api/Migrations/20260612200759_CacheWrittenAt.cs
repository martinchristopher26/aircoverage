using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AirCoverage.Api.Migrations
{
    /// <inheritdoc />
    public partial class CacheWrittenAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CacheWrittenAt",
                table: "Items",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CacheWrittenAt",
                table: "Items");
        }
    }
}
