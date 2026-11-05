using Sahada.Core.Companies;
using Sahada.Core.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Sahada.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(Customer.NameMaxLength);
        builder.Property(c => c.Phone).HasMaxLength(Customer.PhoneMaxLength);
        builder.Property(c => c.Email).HasMaxLength(Customer.EmailMaxLength);
        builder.Property(c => c.Notes).HasMaxLength(Customer.NotesMaxLength);
        builder.HasOne<Company>().WithMany().HasForeignKey(c => c.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.CompanyId, c.Name });
    }
}

internal sealed class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    public void Configure(EntityTypeBuilder<Site> builder)
    {
        builder.ToTable("sites");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Label).HasMaxLength(Site.LabelMaxLength);
        builder.Property(s => s.Address).HasMaxLength(Site.AddressMaxLength);
        builder.Property(s => s.City).HasMaxLength(Site.CityMaxLength);
        builder.Property(s => s.AccessNotes).HasMaxLength(Site.AccessMaxLength);
        builder.HasOne<Customer>().WithMany().HasForeignKey(s => s.CustomerId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(s => s.CustomerId);
        builder.HasIndex(s => s.CompanyId);
    }
}

internal sealed class EquipmentConfiguration : IEntityTypeConfiguration<Equipment>
{
    public void Configure(EntityTypeBuilder<Equipment> builder)
    {
        builder.ToTable("equipment");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Kind).HasMaxLength(Equipment.FieldMaxLength);
        builder.Property(e => e.Brand).HasMaxLength(Equipment.FieldMaxLength);
        builder.Property(e => e.Model).HasMaxLength(Equipment.FieldMaxLength);
        builder.Property(e => e.SerialNumber).HasMaxLength(Equipment.FieldMaxLength);
        builder.HasOne<Site>().WithMany().HasForeignKey(e => e.SiteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.SiteId);
    }
}
