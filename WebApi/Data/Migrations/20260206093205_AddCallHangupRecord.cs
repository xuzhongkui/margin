using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCallHangupRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CallHangupRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ComPort = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CallerNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    HangupTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reason = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    RawLine = table.Column<string>(type: "text", nullable: true),
                    CreateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDelete = table.Column<bool>(type: "boolean", nullable: false),
                    Remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CallHangupRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CallHangupRecords_CallerNumber",
                table: "CallHangupRecords",
                column: "CallerNumber");

            migrationBuilder.CreateIndex(
                name: "IX_CallHangupRecords_ComPort",
                table: "CallHangupRecords",
                column: "ComPort");

            migrationBuilder.CreateIndex(
                name: "IX_CallHangupRecords_DeviceId",
                table: "CallHangupRecords",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_CallHangupRecords_HangupTime",
                table: "CallHangupRecords",
                column: "HangupTime");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CallHangupRecords");
        }
    }
}
