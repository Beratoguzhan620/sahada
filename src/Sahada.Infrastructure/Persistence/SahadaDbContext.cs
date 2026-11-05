using Sahada.Core.Companies;
using Sahada.Core.Customers;
using Sahada.Core.Parts;
using Sahada.Core.Users;
using Sahada.Core.WorkOrders;
using Microsoft.EntityFrameworkCore;

namespace Sahada.Infrastructure.Persistence;

/// <summary>Accounts, companies and their teams, customers with their sites and equipment, parts and work orders.</summary>
public sealed class SahadaDbContext(DbContextOptions<SahadaDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Company> Companies => Set<Company>();

    public DbSet<Membership> Memberships => Set<Membership>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Site> Sites => Set<Site>();

    public DbSet<Equipment> Equipment => Set<Equipment>();

    public DbSet<Part> Parts => Set<Part>();

    public DbSet<WorkOrder> WorkOrders => Set<WorkOrder>();

    public DbSet<ChecklistItem> ChecklistItems => Set<ChecklistItem>();

    public DbSet<WorkOrderEvent> WorkOrderEvents => Set<WorkOrderEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SahadaDbContext).Assembly);
}
