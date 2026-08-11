using HarrisCountyAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarrisCountyAI.Infrastructure.Persistence.Configurations;

public class NormalizedDocumentConfiguration : IEntityTypeConfiguration<NormalizedDocument>
{
    public void Configure(EntityTypeBuilder<NormalizedDocument> builder)
    {
        builder.ToTable("NormalizedDocuments");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.DocumentId)
            .IsRequired();

        builder.HasIndex(d => d.DocumentId);

        builder.Property(d => d.CaseId)
            .IsRequired();

        builder.HasIndex(d => d.CaseId);

        builder.Property(d => d.DocumentType)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(d => d.RawText)
            .IsRequired();

        builder.Property(d => d.CreatedAt)
            .IsRequired();

        builder.OwnsMany(d => d.Pages, pages =>
        {
            pages.ToTable("NormalizedDocumentPages");
            pages.WithOwner().HasForeignKey("NormalizedDocumentId");

            pages.HasKey(p => p.Id);
            pages.Property(p => p.Id).ValueGeneratedNever();

            pages.Property(p => p.PageNumber)
                .IsRequired();

            pages.Property(p => p.Text)
                .IsRequired();

            pages.HasIndex("NormalizedDocumentId");
        });

        builder.OwnsMany(d => d.Fields, fields =>
        {
            fields.ToTable("NormalizedDocumentFields");
            fields.WithOwner().HasForeignKey("NormalizedDocumentId");

            fields.HasKey(f => f.Id);
            fields.Property(f => f.Id).ValueGeneratedNever();

            fields.Property(f => f.Name)
                .HasMaxLength(512)
                .IsRequired();

            fields.Property(f => f.Value);

            fields.Property(f => f.Kind)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            fields.Property(f => f.IsChecked);
            fields.Property(f => f.IsSigned);
            fields.Property(f => f.Confidence);
            fields.Property(f => f.PageNumber);

            fields.HasIndex("NormalizedDocumentId");
        });
    }
}
