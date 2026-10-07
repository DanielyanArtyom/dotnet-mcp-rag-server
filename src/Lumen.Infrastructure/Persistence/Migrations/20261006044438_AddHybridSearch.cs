using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Lumen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHybridSearch : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContextHeader",
                table: "DocumentChunks",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "SearchVector",
                table: "DocumentChunks",
                type: "tsvector",
                nullable: true,
                computedColumnSql: "to_tsvector('english', coalesce(\"ContextHeader\", '') || ' ' || \"Text\")",
                stored: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_SearchVector",
                table: "DocumentChunks",
                column: "SearchVector")
                .Annotation("Npgsql:IndexMethod", "GIN");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentChunks_SearchVector",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "SearchVector",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "ContextHeader",
                table: "DocumentChunks");
        }
    }
}
