using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sahada.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TechnicianProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "technician_profiles",
                columns: table => new
                {
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    skills = table.Column<List<string>>(type: "text[]", nullable: false),
                    base_label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    base_latitude = table.Column<double>(type: "double precision", nullable: false),
                    base_longitude = table.Column<double>(type: "double precision", nullable: false),
                    shift_start = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    shift_end = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    work_days = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_technician_profiles", x => new { x.company_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_technician_profiles_memberships_company_id_user_id",
                        columns: x => new { x.company_id, x.user_id },
                        principalTable: "memberships",
                        principalColumns: new[] { "company_id", "user_id" },
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "technician_profiles");
        }
    }
}
