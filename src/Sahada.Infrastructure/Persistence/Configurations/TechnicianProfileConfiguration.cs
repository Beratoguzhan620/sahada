using Sahada.Core.Companies;
using Sahada.Core.Dispatching;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Sahada.Infrastructure.Persistence.Configurations;

internal sealed class TechnicianProfileConfiguration : IEntityTypeConfiguration<TechnicianProfile>
{
    public void Configure(EntityTypeBuilder<TechnicianProfile> builder)
    {
        builder.ToTable("technician_profiles");
        builder.HasKey(t => new { t.CompanyId, t.UserId });
        builder.Ignore(t => t.Days);
        builder.Property(t => t.BaseLabel).HasMaxLength(TechnicianProfile.BaseLabelMaxLength);
        // Leaving the team takes the profile with it.
        builder.HasOne<Membership>().WithOne().HasForeignKey<TechnicianProfile>(t => new { t.CompanyId, t.UserId }).OnDelete(DeleteBehavior.Cascade);
    }
}
