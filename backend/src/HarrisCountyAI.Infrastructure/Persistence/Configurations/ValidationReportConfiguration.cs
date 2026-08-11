using HarrisCountyAI.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HarrisCountyAI.Infrastructure.Persistence.Configurations;

public class ValidationReportConfiguration : IEntityTypeConfiguration<ValidationReport>
{
    public void Configure(EntityTypeBuilder<ValidationReport> builder)
    {
        builder.ToTable("ValidationReports");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.CaseId)
            .IsRequired();

        builder.HasIndex(r => r.CaseId);

        builder.Property(r => r.WorkflowType)
            .HasConversion<string>()
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        builder.OwnsMany(r => r.Items, items =>
        {
            items.ToTable("ValidationReportItems");
            items.WithOwner().HasForeignKey("ValidationReportId");

            items.HasKey(i => i.Id);
            items.Property(i => i.Id).ValueGeneratedNever();

            items.Property(i => i.Order)
                .IsRequired();

            items.Property(i => i.RuleName)
                .HasMaxLength(256)
                .IsRequired();

            items.Property(i => i.Requirement)
                .HasMaxLength(256)
                .IsRequired();

            items.Property(i => i.ValidationType)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            items.Property(i => i.Status)
                .HasConversion<string>()
                .HasMaxLength(32)
                .IsRequired();

            items.Property(i => i.Message)
                .IsRequired();

            items.Property(i => i.ExtractedValue);

            items.Property(i => i.DocumentId);

            items.Property(i => i.DocumentType)
                .HasConversion<string>()
                .HasMaxLength(64);

            items.Property(i => i.PageNumber);

            items.HasIndex("ValidationReportId");
        });
    }
}
