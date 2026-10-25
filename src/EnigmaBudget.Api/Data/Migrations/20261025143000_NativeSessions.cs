using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnigmaBudget.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class NativeSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NativeAuthChallenges",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    NonceDigest = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    Platform = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NativeAuthChallenges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NativeSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Subject = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    DeviceName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    AccessTokenDigest = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    AccessExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NativeSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "NativeRefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenDigest = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReplacedById = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NativeRefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NativeRefreshTokens_NativeSessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "NativeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_NativeRefreshTokens_SessionId",
                table: "NativeRefreshTokens",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_NativeRefreshTokens_TokenDigest",
                table: "NativeRefreshTokens",
                column: "TokenDigest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NativeSessions_AccessTokenDigest",
                table: "NativeSessions",
                column: "AccessTokenDigest",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NativeSessions_UserId_RevokedAt",
                table: "NativeSessions",
                columns: new[] { "UserId", "RevokedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NativeAuthChallenges");

            migrationBuilder.DropTable(
                name: "NativeRefreshTokens");

            migrationBuilder.DropTable(
                name: "NativeSessions");
        }
    }
}
