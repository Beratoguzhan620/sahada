using Sahada.Core.Companies;
using Sahada.Core.Parts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Sahada.Infrastructure.Persistence.Configurations;

internal sealed class PartConfiguration : IEntityTypeConfiguration<Part>
{
    public const string SkuIndex = "ix_parts_company_id_sku";

    public void Configure(EntityTypeBuilder<Part> builder)
    {
        builder.ToTable("parts");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();
        builder.Property(p => p.Sku).HasMaxLength(Part.SkuMaxLength);
        builder.Property(p => p.Name).HasMaxLength(Part.NameMaxLength);
        builder.HasOne<Company>().WithMany().HasForeignKey(p => p.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => new { p.CompanyId, p.Sku }).IsUnique().HasDatabaseName(SkuIndex);
    }
}
