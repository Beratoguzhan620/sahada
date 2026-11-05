using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sahada.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    display_name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "companies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    time_zone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    next_work_order_number = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_companies", x => x.id);
                    table.ForeignKey(
                        name: "fk_companies_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "customers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    phone = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_customers", x => x.id);
                    table.ForeignKey(
                        name: "fk_customers_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "memberships",
                columns: table => new
                {
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_memberships", x => new { x.company_id, x.user_id });
                    table.ForeignKey(
                        name: "fk_memberships_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_memberships_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "parts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    unit_price_minor = table.Column<long>(type: "bigint", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_parts", x => x.id);
                    table.ForeignKey(
                        name: "fk_parts_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sites",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    label = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    address = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    city = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    access_notes = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sites", x => x.id);
                    table.ForeignKey(
                        name: "fk_sites_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "equipment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    brand = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    model = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    serial_number = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    installed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    warranty_until = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_equipment", x => x.id);
                    table.ForeignKey(
                        name: "fk_equipment_sites_site_id",
                        column: x => x.site_id,
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    customer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    site_id = table.Column<Guid>(type: "uuid", nullable: false),
                    equipment_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    priority = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    required_skill = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    estimated_minutes = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    technician_id = table.Column<Guid>(type: "uuid", nullable: true),
                    scheduled_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    hold_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    cancel_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    resolution = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_work_orders_companies_company_id",
                        column: x => x.company_id,
                        principalTable: "companies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_work_orders_customers_customer_id",
                        column: x => x.customer_id,
                        principalTable: "customers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_orders_equipment_equipment_id",
                        column: x => x.equipment_id,
                        principalTable: "equipment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_work_orders_sites_site_id",
                        column: x => x.site_id,
                        principalTable: "sites",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_orders_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_orders_users_technician_id",
                        column: x => x.technician_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "checklist_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    done = table.Column<bool>(type: "boolean", nullable: false),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_checklist_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_checklist_items_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "work_order_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    from_status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    to_status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: true),
                    detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_order_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_work_order_events_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_work_order_events_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_checklist_items_work_order_id_position",
                table: "checklist_items",
                columns: new[] { "work_order_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_companies_created_by",
                table: "companies",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_customers_company_id_name",
                table: "customers",
                columns: new[] { "company_id", "name" });

            migrationBuilder.CreateIndex(
                name: "ix_equipment_site_id",
                table: "equipment",
                column: "site_id");

            migrationBuilder.CreateIndex(
                name: "ix_memberships_user_id",
                table: "memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_parts_company_id_sku",
                table: "parts",
                columns: new[] { "company_id", "sku" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sites_company_id",
                table: "sites",
                column: "company_id");

            migrationBuilder.CreateIndex(
                name: "ix_sites_customer_id",
                table: "sites",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_order_events_actor_id",
                table: "work_order_events",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_order_events_work_order_id_id",
                table: "work_order_events",
                columns: new[] { "work_order_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_company_id_number",
                table: "work_orders",
                columns: new[] { "company_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_company_id_status_due_at",
                table: "work_orders",
                columns: new[] { "company_id", "status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_created_by",
                table: "work_orders",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_customer_id",
                table: "work_orders",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_equipment_id",
                table: "work_orders",
                column: "equipment_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_site_id",
                table: "work_orders",
                column: "site_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_technician_id_scheduled_start",
                table: "work_orders",
                columns: new[] { "technician_id", "scheduled_start" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "checklist_items");

            migrationBuilder.DropTable(
                name: "memberships");

            migrationBuilder.DropTable(
                name: "parts");

            migrationBuilder.DropTable(
                name: "work_order_events");

            migrationBuilder.DropTable(
                name: "work_orders");

            migrationBuilder.DropTable(
                name: "equipment");

            migrationBuilder.DropTable(
                name: "sites");

            migrationBuilder.DropTable(
                name: "customers");

            migrationBuilder.DropTable(
                name: "companies");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
