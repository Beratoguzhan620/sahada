using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sahada.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Sync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "previous_technician_id",
                table: "work_orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "revision",
                table: "work_orders",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "signature",
                table: "work_orders",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "signed_at",
                table: "work_orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "signed_by",
                table: "work_orders",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "flagged",
                table: "work_order_events",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    content_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    caption = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    flagged = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attachments", x => x.id);
                    table.ForeignKey(
                        name: "fk_attachments_users_uploaded_by",
                        column: x => x.uploaded_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_attachments_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "part_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    part_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    quantity = table.Column<decimal>(type: "numeric(9,2)", precision: 9, scale: 2, nullable: false),
                    unit_price_minor = table.Column<long>(type: "bigint", nullable: false),
                    added_by = table.Column<Guid>(type: "uuid", nullable: false),
                    added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    removed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    flagged = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_part_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_part_lines_parts_part_id",
                        column: x => x.part_id,
                        principalTable: "parts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_part_lines_users_added_by",
                        column: x => x.added_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_part_lines_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sync_operations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    company_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    work_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    device_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    outcome = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sync_operations", x => x.id);
                    table.ForeignKey(
                        name: "fk_sync_operations_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_sync_operations_work_orders_work_order_id",
                        column: x => x.work_order_id,
                        principalTable: "work_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_work_orders_previous_technician_id",
                table: "work_orders",
                column: "previous_technician_id");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_uploaded_by",
                table: "attachments",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "ix_attachments_work_order_id",
                table: "attachments",
                column: "work_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_part_lines_added_by",
                table: "part_lines",
                column: "added_by");

            migrationBuilder.CreateIndex(
                name: "ix_part_lines_part_id",
                table: "part_lines",
                column: "part_id");

            migrationBuilder.CreateIndex(
                name: "ix_part_lines_work_order_id",
                table: "part_lines",
                column: "work_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_sync_operations_company_id_received_at",
                table: "sync_operations",
                columns: new[] { "company_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_sync_operations_user_id",
                table: "sync_operations",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_sync_operations_work_order_id",
                table: "sync_operations",
                column: "work_order_id");

            migrationBuilder.AddForeignKey(
                name: "fk_work_orders_users_previous_technician_id",
                table: "work_orders",
                column: "previous_technician_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Devices pull what changed since their last pull. Every insert or update of a work order is stamped with
            // the id of the transaction that made it; a pull returns the rows stamped between its cursor and the oldest
            // transaction still running, so a change that commits late is never skipped.
            migrationBuilder.Sql("""
                ALTER TABLE work_orders ADD COLUMN change_xid xid8 NOT NULL DEFAULT '0';

                CREATE FUNCTION work_orders_stamp_change() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    NEW.change_xid := pg_current_xact_id();
                    RETURN NEW;
                END
                $$;

                CREATE TRIGGER work_orders_stamp_change
                    BEFORE INSERT OR UPDATE ON work_orders
                    FOR EACH ROW EXECUTE FUNCTION work_orders_stamp_change();

                UPDATE work_orders SET change_xid = pg_current_xact_id();

                CREATE INDEX ix_work_orders_technician_id_change_xid ON work_orders (technician_id, change_xid);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER work_orders_stamp_change ON work_orders;
                DROP FUNCTION work_orders_stamp_change();
                DROP INDEX ix_work_orders_technician_id_change_xid;
                ALTER TABLE work_orders DROP COLUMN change_xid;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_work_orders_users_previous_technician_id",
                table: "work_orders");

            migrationBuilder.DropTable(
                name: "attachments");

            migrationBuilder.DropTable(
                name: "part_lines");

            migrationBuilder.DropTable(
                name: "sync_operations");

            migrationBuilder.DropIndex(
                name: "ix_work_orders_previous_technician_id",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "previous_technician_id",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "revision",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "signature",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "signed_at",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "signed_by",
                table: "work_orders");

            migrationBuilder.DropColumn(
                name: "flagged",
                table: "work_order_events");
        }
    }
}
