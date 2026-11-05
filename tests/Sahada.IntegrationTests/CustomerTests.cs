using System.Net;
using System.Net.Http.Json;
using Sahada.IntegrationTests.Support;

namespace Sahada.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class CustomerTests(ApiFactory factory)
{
    [Fact]
    public async Task A_customer_comes_with_their_sites_and_equipment()
    {
        var firm = await factory.FirmAsync();

        var customer = await (await firm.Owner.Http.GetAsync($"{firm.Path}/customers/{firm.CustomerId}")).ReadAsync<CustomerDto>();

        var site = Assert.Single(customer.Sites);
        Assert.Equal(("Kazan dairesi", "Samsun", 41.3335, "Anahtar kapıcıda."), (site.Label, site.City, site.Latitude, site.AccessNotes));
        var boiler = Assert.Single(site.Equipment);
        Assert.Equal(("Kombi", "DD-2291", new DateOnly(2027, 5, 1)), (boiler.Kind, boiler.SerialNumber, boiler.WarrantyUntil));
    }

    [Fact]
    public async Task Search_finds_customers_by_name_phone_or_email_and_treats_wildcards_as_text()
    {
        var firm = await factory.FirmAsync();
        await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/customers", new { name = "Lider Market", phone = "0532 111 22 33", email = "info@lidermarket.example" });
        await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/customers", new { name = "%100 Fırın" });

        async Task<List<string>> Find(string q) =>
            (await (await firm.Owner.Http.GetAsync($"{firm.Path}/customers?q={Uri.EscapeDataString(q)}")).ReadAsync<PageDto<CustomerSummaryDto>>()).Items.Select(c => c.Name).ToList();

        Assert.Equal(["Lider Market"], await Find("lider"));
        Assert.Equal(["Lider Market"], await Find("111 22"));
        Assert.Equal(["Lider Market"], await Find("LIDERMARKET"));
        Assert.Equal(["%100 Fırın"], await Find("%"));
        Assert.Equal(3, (await (await firm.Owner.Http.GetAsync($"{firm.Path}/customers")).ReadAsync<PageDto<CustomerSummaryDto>>()).Total);
    }

    [Fact]
    public async Task Sites_are_checked_and_changed()
    {
        var firm = await factory.FirmAsync();
        var path = $"{firm.Path}/customers/{firm.CustomerId}/sites";

        var bad = await firm.Owner.Http.PostAsJsonAsync(path, new { label = "Depo", address = "Sanayi Sitesi", city = "Samsun", latitude = 95.0, longitude = 36.0 });
        var changed = await firm.Owner.Http.PutAsJsonAsync($"{path}/{firm.SiteId}", new { label = "Kazan dairesi", address = "Cumhuriyet Cd. 14", city = "Samsun", latitude = 41.3, longitude = 36.3, accessNotes = "" });

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("validation_failed", await bad.ProblemCodeAsync());
        var site = await changed.ReadAsync<SiteDto>();
        Assert.Equal(("Cumhuriyet Cd. 14", 1), (site.Address, site.Equipment.Count));
    }

    [Fact]
    public async Task Customers_and_sites_with_work_orders_keep_their_history()
    {
        var firm = await factory.FirmAsync();
        await firm.OrderAsync();

        var site = await firm.Owner.Http.DeleteAsync($"{firm.Path}/customers/{firm.CustomerId}/sites/{firm.SiteId}");
        var customer = await firm.Owner.Http.DeleteAsync($"{firm.Path}/customers/{firm.CustomerId}");

        Assert.Equal(HttpStatusCode.Conflict, site.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, customer.StatusCode);
    }

    [Fact]
    public async Task A_customer_without_work_orders_can_go()
    {
        var firm = await factory.FirmAsync();

        var deleted = await firm.Owner.Http.DeleteAsync($"{firm.Path}/customers/{firm.CustomerId}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await firm.Owner.Http.GetAsync($"{firm.Path}/customers/{firm.CustomerId}")).StatusCode);
        Assert.Equal(0L, await factory.ScalarAsync<long>("SELECT count(*) FROM equipment WHERE id = @p0", firm.EquipmentId));
    }

    [Fact]
    public async Task Another_companys_customer_is_not_found()
    {
        var mine = await factory.FirmAsync();
        var theirs = await factory.FirmAsync("Güney Isı");

        var response = await mine.Owner.Http.GetAsync($"{mine.Path}/customers/{theirs.CustomerId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
