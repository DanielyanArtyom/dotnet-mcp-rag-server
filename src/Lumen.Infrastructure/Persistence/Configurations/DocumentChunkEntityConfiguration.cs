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

        builder.HasOne<DocumentEntity>()
            .WithMany()
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
