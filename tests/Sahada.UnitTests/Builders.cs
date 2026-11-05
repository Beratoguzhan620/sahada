using Sahada.Core.Companies;
using Sahada.Core.Customers;
using Sahada.Core.WorkOrders;

namespace Sahada.UnitTests;

internal static class Builders
{
    public static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    public static readonly Guid Dispatcher = Guid.NewGuid();

    public static readonly Guid Technician = Guid.NewGuid();

    public static Site Site()
    {
        var (company, _) = Company.Create("Kuzey Klima", Dispatcher, Now);
        var customer = Customer.Create(company.Id, "Atakum Apartmanı", "0362 000 00 00", null, null, Now);
        return Sahada.Core.Customers.Site.Create(customer, "Kazan dairesi", "Cumhuriyet Cd. 12", "Samsun", 41.33, 36.27, "Anahtar kapıcıda", Now);
    }

    public static WorkOrder Order(Priority priority = Priority.Normal, params string[] checklist)
    {
        var site = Site();
        return WorkOrder.Create(site.CompanyId, 1001, site.CustomerId, site.Id, null, "Kombi su kaçırıyor", "Alttan damlıyor.", priority, "HVAC", 90, checklist, Dispatcher, Now);
    }

    /// <summary>An order a technician has started.</summary>
    public static WorkOrder Started(params string[] checklist)
    {
        var order = Order(Priority.High, checklist);
        order.Schedule(Technician, Now.AddHours(2), Now);
        order.SetOut(Now.AddHours(2));
        order.Start(Now.AddHours(3), Now.AddHours(3));
        return order;
    }
}
