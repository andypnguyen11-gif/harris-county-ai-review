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

            // Owned types flatten to columns on NormalizedDocumentFields.
            // Each navigation MUST be marked optional: without IsRequired(false)
            // EF can treat the owned entity as required and materialize a
            // default BoundingBox when every column is null, which would make
            // the null round-trip assertions below pass against a non-null box.
            fields.OwnsOne(f => f.KeyBoundingBox, box =>
            {
                box.Property(b => b.PageNumber).HasColumnName("KeyBoundingBox_PageNumber");
                box.Property(b => b.X).HasColumnName("KeyBoundingBox_X");
                box.Property(b => b.Y).HasColumnName("KeyBoundingBox_Y");
                box.Property(b => b.Width).HasColumnName("KeyBoundingBox_Width");
                box.Property(b => b.Height).HasColumnName("KeyBoundingBox_Height");
            });
            fields.Navigation(f => f.KeyBoundingBox).IsRequired(false);

            fields.OwnsOne(f => f.ValueBoundingBox, box =>
            {
                box.Property(b => b.PageNumber).HasColumnName("ValueBoundingBox_PageNumber");
                box.Property(b => b.X).HasColumnName("ValueBoundingBox_X");
                box.Property(b => b.Y).HasColumnName("ValueBoundingBox_Y");
                box.Property(b => b.Width).HasColumnName("ValueBoundingBox_Width");
                box.Property(b => b.Height).HasColumnName("ValueBoundingBox_Height");
            });
            fields.Navigation(f => f.ValueBoundingBox).IsRequired(false);

            fields.HasIndex("NormalizedDocumentId");
        });
    }
}
