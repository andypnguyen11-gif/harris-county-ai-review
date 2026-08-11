using HarrisCountyAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarrisCountyAI.Infrastructure.Persistence.Configurations;

public class DocumentConfiguration : IEntityTypeConfiguration<Document>
{
    public void Configure(EntityTypeBuilder<Document> builder)
    {
        builder.ToTable("Documents");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.CaseId)
            .IsRequired();

        builder.HasIndex(d => d.CaseId);

        builder.Property(d => d.FileName)
            .HasMaxLength(260)
            .IsRequired();

        builder.Property(d => d.BlobPath)
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(d => d.DocumentType)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(d => d.ProcessingStatus)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(d => d.CreatedAt)
            .IsRequired();
    }
}
