using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WestMarch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3Encounters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Encounters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdventureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ReadAloud = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Encounters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Encounters_Adventures_AdventureId",
                        column: x => x.AdventureId,
                        principalTable: "Adventures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Monsters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(150)", maxLength: 150, nullable: false),
                    ChallengeRating = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CrValue = table.Column<decimal>(type: "decimal(6,3)", precision: 6, scale: 3, nullable: false),
                    Xp = table.Column<int>(type: "int", nullable: true),
                    ArmorClass = table.Column<int>(type: "int", nullable: false),
                    MaxHitPoints = table.Column<int>(type: "int", nullable: false),
                    HitDice = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: true),
                    Size = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatureType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Alignment = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    StatsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ImportKey = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: false),
                    LastImportBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Monsters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EncounterNpcs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Stats = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EncounterNpcs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EncounterNpcs_Encounters_EncounterId",
                        column: x => x.EncounterId,
                        principalTable: "Encounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EncounterMonsters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EncounterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MonsterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Count = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EncounterMonsters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EncounterMonsters_Encounters_EncounterId",
                        column: x => x.EncounterId,
                        principalTable: "Encounters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EncounterMonsters_Monsters_MonsterId",
                        column: x => x.MonsterId,
                        principalTable: "Monsters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EncounterMonsters_EncounterId",
                table: "EncounterMonsters",
                column: "EncounterId");

            migrationBuilder.CreateIndex(
                name: "IX_EncounterMonsters_MonsterId",
                table: "EncounterMonsters",
                column: "MonsterId");

            migrationBuilder.CreateIndex(
                name: "IX_EncounterNpcs_EncounterId",
                table: "EncounterNpcs",
                column: "EncounterId");

            migrationBuilder.CreateIndex(
                name: "IX_Encounters_AdventureId",
                table: "Encounters",
                column: "AdventureId");

            migrationBuilder.CreateIndex(
                name: "IX_Monsters_ImportKey",
                table: "Monsters",
                column: "ImportKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Monsters_IsActive_CrValue",
                table: "Monsters",
                columns: new[] { "IsActive", "CrValue" });

            // Preserve authored content: any existing monster-stat-blocks text becomes a
            // starter encounter on its adventure, then the legacy column is dropped.
            migrationBuilder.Sql("""
                INSERT INTO Encounters (Id, AdventureId, Title, SortOrder, Description, ReadAloud)
                SELECT NEWID(), Id, N'Monsters & notes (from earlier draft)', 0, MonsterStatBlocks, NULL
                FROM Adventures
                WHERE MonsterStatBlocks IS NOT NULL AND LEN(MonsterStatBlocks) > 0;
                """);

            migrationBuilder.DropColumn(
                name: "MonsterStatBlocks",
                table: "Adventures");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EncounterMonsters");

            migrationBuilder.DropTable(
                name: "EncounterNpcs");

            migrationBuilder.DropTable(
                name: "Monsters");

            migrationBuilder.DropTable(
                name: "Encounters");

            migrationBuilder.AddColumn<string>(
                name: "MonsterStatBlocks",
                table: "Adventures",
                type: "nvarchar(max)",
                nullable: true);
        }
    }
}
