using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace Lumen.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddChunkEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Vector>(
                name: "Embedding",
                table: "DocumentChunks",
                type: "vector(1536)",
                nullable: false);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingModel",
                table: "DocumentChunks",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_TenantId_EmbeddingModel",
                table: "DocumentChunks",
                columns: new[] { "TenantId", "EmbeddingModel" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DocumentChunks_TenantId_EmbeddingModel",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "Embedding",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "EmbeddingModel",
                table: "DocumentChunks");
        }
    }
}
