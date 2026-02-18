using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSmsMessageTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SmsMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ComPort = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    SenderNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MessageContent = table.Column<string>(type: "text", nullable: false),
                    ReceivedTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SmsTimestamp = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDelete = table.Column<bool>(type: "boolean", nullable: false),
                    Remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsMessages", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_ComPort",
                table: "SmsMessages",
                column: "ComPort");

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_DeviceId",
                table: "SmsMessages",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_ReceivedTime",
                table: "SmsMessages",
                column: "ReceivedTime");

            migrationBuilder.CreateIndex(
                name: "IX_SmsMessages_SenderNumber",
                table: "SmsMessages",
                column: "SenderNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SmsMessages");
        }
    }
}
