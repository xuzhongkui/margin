using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WebApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSmsSendRecord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SmsSendRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ComPort = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    TargetNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MessageContent = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    SentTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TriggerSource = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    TriggerApiUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsDelete = table.Column<bool>(type: "boolean", nullable: false),
                    Remark = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SmsSendRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SmsSendRecords_ComPort",
                table: "SmsSendRecords",
                column: "ComPort");

            migrationBuilder.CreateIndex(
                name: "IX_SmsSendRecords_CreateTime",
                table: "SmsSendRecords",
                column: "CreateTime");

            migrationBuilder.CreateIndex(
                name: "IX_SmsSendRecords_DeviceId",
                table: "SmsSendRecords",
                column: "DeviceId");

            migrationBuilder.CreateIndex(
                name: "IX_SmsSendRecords_Status",
                table: "SmsSendRecords",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SmsSendRecords_TargetNumber",
                table: "SmsSendRecords",
                column: "TargetNumber");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SmsSendRecords");
        }
    }
}
