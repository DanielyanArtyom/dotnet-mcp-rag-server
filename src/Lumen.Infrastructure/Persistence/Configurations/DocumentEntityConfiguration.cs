namespace Lumen.Infrastructure.Persistence.Configurations;

public class DocumentEntityConfiguration : IEntityTypeConfiguration<DocumentEntity>
{
    public void Configure(EntityTypeBuilder<DocumentEntity> builder)
    {
        builder.ToTable(nameof(AppDbContext.Documents));
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Title).IsRequired().HasMaxLength(500);
        builder.Property(d => d.Content).IsRequired();
        builder.Property(d => d.RawFilePath).IsRequired().HasMaxLength(1000);
        builder.Property(d => d.FilingDate).IsRequired();
        builder.HasIndex(d => d.TenantId);
        builder.HasOne<TenantEntity>()
            .WithMany()
            .HasForeignKey(d => d.TenantId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
