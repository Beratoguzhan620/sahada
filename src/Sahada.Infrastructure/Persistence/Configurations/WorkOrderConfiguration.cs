using Sahada.Core.Companies;
using Sahada.Core.Customers;
using Sahada.Core.Users;
using Sahada.Core.WorkOrders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Sahada.Infrastructure.Persistence.Configurations;

internal sealed class WorkOrderConfiguration : IEntityTypeConfiguration<WorkOrder>
{
    public void Configure(EntityTypeBuilder<WorkOrder> builder)
    {
        builder.ToTable("work_orders");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id).ValueGeneratedNever();
        builder.Ignore(w => w.Code);
        builder.Ignore(w => w.IsOpen);
        builder.Ignore(w => w.ScheduledEnd);
        builder.Property(w => w.Title).HasMaxLength(WorkOrder.TitleMaxLength);
        builder.Property(w => w.Description).HasMaxLength(WorkOrder.DescriptionMaxLength);
        builder.Property(w => w.RequiredSkill).HasMaxLength(WorkOrder.SkillMaxLength);
        builder.Property(w => w.HoldReason).HasMaxLength(WorkOrder.ReasonMaxLength);
        builder.Property(w => w.CancelReason).HasMaxLength(WorkOrder.ReasonMaxLength);
        builder.Property(w => w.Resolution).HasMaxLength(WorkOrder.ResolutionMaxLength);
        builder.Property(w => w.Priority).HasConversion<string>().HasMaxLength(10);
        builder.Property(w => w.Status).HasConversion<string>().HasMaxLength(12);
        // Two people changing the same order at once: the second save fails instead of silently undoing the first.
        builder.Property<uint>("Version").IsRowVersion();

        builder.HasOne<Company>().WithMany().HasForeignKey(w => w.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Customer>().WithMany().HasForeignKey(w => w.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Site>().WithMany().HasForeignKey(w => w.SiteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Equipment>().WithMany().HasForeignKey(w => w.EquipmentId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<User>().WithMany().HasForeignKey(w => w.TechnicianId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(w => w.CreatedBy).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(w => w.Checklist).WithOne().HasForeignKey(i => i.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(w => w.Checklist).HasField("_checklist").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(w => new { w.CompanyId, w.Number }).IsUnique();
        builder.HasIndex(w => new { w.CompanyId, w.Status, w.DueAt });
        builder.HasIndex(w => new { w.TechnicianId, w.ScheduledStart });
        builder.HasIndex(w => w.SiteId);
    }
}

internal sealed class ChecklistItemConfiguration : IEntityTypeConfiguration<ChecklistItem>
{
    public void Configure(EntityTypeBuilder<ChecklistItem> builder)
    {
        builder.ToTable("checklist_items");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Text).HasMaxLength(ChecklistItem.TextMaxLength);
        builder.HasIndex(i => new { i.WorkOrderId, i.Position }).IsUnique();
    }
}

internal sealed class WorkOrderEventConfiguration : IEntityTypeConfiguration<WorkOrderEvent>
{
    public void Configure(EntityTypeBuilder<WorkOrderEvent> builder)
    {
        builder.ToTable("work_order_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).UseIdentityAlwaysColumn();
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.FromStatus).HasConversion<string>().HasMaxLength(12);
        builder.Property(e => e.ToStatus).HasConversion<string>().HasMaxLength(12);
        builder.Property(e => e.Detail).HasMaxLength(WorkOrderEvent.DetailMaxLength);
        builder.HasOne<WorkOrder>().WithMany().HasForeignKey(e => e.WorkOrderId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<User>().WithMany().HasForeignKey(e => e.ActorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.WorkOrderId, e.Id });
    }
}
