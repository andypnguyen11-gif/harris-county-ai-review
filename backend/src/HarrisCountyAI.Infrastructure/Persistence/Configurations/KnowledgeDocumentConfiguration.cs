using HarrisCountyAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarrisCountyAI.Infrastructure.Persistence.Configurations;

public class KnowledgeDocumentConfiguration : IEntityTypeConfiguration<KnowledgeDocument>
{
    public void Configure(EntityTypeBuilder<KnowledgeDocument> builder)
    {
        builder.ToTable("KnowledgeDocuments");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.Title)
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(d => d.FileName)
            .HasMaxLength(260)
            .IsRequired();

        builder.Property(d => d.BlobPath)
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(d => d.Department)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(d => d.DocumentType)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(d => d.PermitType)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(d => d.Version)
            .HasMaxLength(50);

        builder.Property(d => d.SourceUrl)
            .HasMaxLength(2048);

        builder.Property(d => d.IngestionStatus)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(d => d.CreatedAt)
            .IsRequired();

        builder.Property(d => d.UpdatedAt)
            .IsRequired();

        builder.HasIndex(d => d.IngestionStatus);
    }
}
