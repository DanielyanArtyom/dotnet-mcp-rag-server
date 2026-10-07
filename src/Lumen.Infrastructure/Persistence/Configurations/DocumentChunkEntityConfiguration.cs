namespace Lumen.Infrastructure.Persistence.Configurations;

public class DocumentChunkEntityConfiguration : IEntityTypeConfiguration<DocumentChunkEntity>
{
    public void Configure(EntityTypeBuilder<DocumentChunkEntity> builder)
    {
        builder.ToTable(nameof(AppDbContext.DocumentChunks));
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Text).IsRequired();
        builder.HasIndex(c => c.TenantId);
        builder.HasIndex(c => new { c.DocumentId, c.ChunkIndex });

        builder.Property(c => c.Embedding).HasColumnType($"vector({EmbeddingConstants.Dimensions})").IsRequired();
        builder.Property(c => c.EmbeddingModel).IsRequired().HasMaxLength(100);
        builder.HasIndex(c => new { c.TenantId, c.EmbeddingModel });

        // Shadow property: the tsvector is computed by Postgres from the stored text and never read by the app,
        // so neither the domain model nor the mapping profile needs to know it exists.
        builder.Property<NpgsqlTsVector>("SearchVector")
            .HasColumnType("tsvector")
            .HasComputedColumnSql("""to_tsvector('english', coalesce("ContextHeader", '') || ' ' || "Text")""", stored: true);
        builder.HasIndex("SearchVector").HasMethod("GIN");

        builder.HasOne<DocumentEntity>()
            .WithMany()
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
