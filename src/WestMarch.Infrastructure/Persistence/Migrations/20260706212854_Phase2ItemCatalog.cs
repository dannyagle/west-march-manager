using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WestMarch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase2ItemCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RewardsClaimedAt",
                table: "SessionSignups",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GoldGp",
                table: "Characters",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "CatalogItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Rarity = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    RequiresAttunement = table.Column<bool>(type: "bit", nullable: false),
                    BasePriceGp = table.Column<int>(type: "int", nullable: true),
                    PriceRaw = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    PriceIsBasePlus = table.Column<bool>(type: "bit", nullable: false),
                    CampaignPriceGp = table.Column<int>(type: "int", nullable: true),
                    ExternalUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    DetailsJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Source = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    ImportKey = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    LastImportBatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CatalogItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ImportBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    SourceNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    UploadedByUserId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    AddedCount = table.Column<int>(type: "int", nullable: false),
                    UpdatedCount = table.Column<int>(type: "int", nullable: false),
                    DeactivatedCount = table.Column<int>(type: "int", nullable: false),
                    UnchangedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImportBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LedgerEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    GoldDelta = table.Column<int>(type: "int", nullable: false),
                    ItemInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ItemName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ListingId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CounterpartyCharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Description = table.Column<string>(type: "nvarchar(600)", maxLength: 600, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItemInstances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CatalogItemId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OwnerCharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AcquiredAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItemInstances_CatalogItems_CatalogItemId",
                        column: x => x.CatalogItemId,
                        principalTable: "CatalogItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItemInstances_Characters_OwnerCharacterId",
                        column: x => x.OwnerCharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "MarketListings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemInstanceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SellerCharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AskingPriceGp = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    ListedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    BuyerCharacterId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketListings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketListings_ItemInstances_ItemInstanceId",
                        column: x => x.ItemInstanceId,
                        principalTable: "ItemInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RewardOptions_CatalogItemId",
                table: "RewardOptions",
                column: "CatalogItemId");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_ImportKey",
                table: "CatalogItems",
                column: "ImportKey");

            migrationBuilder.CreateIndex(
                name: "IX_CatalogItems_Kind_IsActive",
                table: "CatalogItems",
                columns: new[] { "Kind", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ItemInstances_CatalogItemId",
                table: "ItemInstances",
                column: "CatalogItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ItemInstances_OwnerCharacterId_Status",
                table: "ItemInstances",
                columns: new[] { "OwnerCharacterId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_CharacterId_OccurredAt",
                table: "LedgerEntries",
                columns: new[] { "CharacterId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerEntries_OccurredAt",
                table: "LedgerEntries",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_MarketListings_ItemInstanceId",
                table: "MarketListings",
                column: "ItemInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketListings_Status",
                table: "MarketListings",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_RewardOptions_CatalogItems_CatalogItemId",
                table: "RewardOptions",
                column: "CatalogItemId",
                principalTable: "CatalogItems",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_RewardOptions_CatalogItems_CatalogItemId",
                table: "RewardOptions");

            migrationBuilder.DropTable(
                name: "ImportBatches");

            migrationBuilder.DropTable(
                name: "LedgerEntries");

            migrationBuilder.DropTable(
                name: "MarketListings");

            migrationBuilder.DropTable(
                name: "ItemInstances");

            migrationBuilder.DropTable(
                name: "CatalogItems");

            migrationBuilder.DropIndex(
                name: "IX_RewardOptions_CatalogItemId",
                table: "RewardOptions");

            migrationBuilder.DropColumn(
                name: "RewardsClaimedAt",
                table: "SessionSignups");

            migrationBuilder.DropColumn(
                name: "GoldGp",
                table: "Characters");
        }
    }
}
