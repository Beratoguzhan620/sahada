using System.Net;
using System.Net.Http.Json;
using Sahada.IntegrationTests.Support;

namespace Sahada.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class TeamTests(ApiFactory factory)
{
    [Fact]
    public async Task Creating_a_company_makes_you_its_owner()
    {
        using var person = await factory.RegisterAsync();

        var created = await person.Http.PostAsJsonAsync("/api/companies", new { name = "  Kuzey   Klima " });

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var me = await (await person.Http.GetAsync("/api/me")).ReadAsync<MeDto>();
        var company = Assert.Single(me.Companies);
        Assert.Equal(("Kuzey Klima", "Owner", 1, "Europe/Istanbul"), (company.Name, company.Role, company.Members, company.TimeZone));
    }

    [Fact]
    public async Task Technicians_read_the_customers_but_cannot_change_them()
    {
        var firm = await factory.FirmAsync();
        using var technician = await factory.RegisterAsync("Teknisyen Ali");
        await firm.AddMemberAsync(technician, "Technician");

        var read = await technician.Http.GetAsync($"{firm.Path}/customers/{firm.CustomerId}");
        var write = await technician.Http.PostAsJsonAsync($"{firm.Path}/customers", new { name = "Başka Müşteri" });
        var order = await technician.Http.PostAsJsonAsync($"{firm.Path}/work-orders", TestApi.OrderBody(firm));

        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, write.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, order.StatusCode);
        Assert.Equal("forbidden", await order.ProblemCodeAsync());
    }

    [Fact]
    public async Task Dispatchers_run_the_work_but_only_owners_manage_the_team()
    {
        var firm = await factory.FirmAsync();
        using var dispatcher = await factory.RegisterAsync("Planlamacı");
        using var newcomer = await factory.RegisterAsync("Yeni");
        await firm.AddMemberAsync(dispatcher, "Dispatcher");

        var order = await dispatcher.Http.PostAsJsonAsync($"{firm.Path}/work-orders", TestApi.OrderBody(firm));
        var add = await dispatcher.Http.PostAsJsonAsync($"{firm.Path}/members", new { email = newcomer.Email, role = "Technician" });

        Assert.Equal(HttpStatusCode.Created, order.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, add.StatusCode);
    }

    [Fact]
    public async Task Outsiders_cannot_tell_a_company_exists()
    {
        var firm = await factory.FirmAsync();
        using var outsider = await factory.RegisterAsync("Yabancı");

        var response = await outsider.Http.GetAsync($"{firm.Path}/work-orders");
        var missing = await outsider.Http.GetAsync($"/api/companies/{Guid.NewGuid()}/work-orders");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(await missing.ProblemCodeAsync(), await response.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task The_last_owner_cannot_leave_or_be_demoted()
    {
        var firm = await factory.FirmAsync();
        var path = $"{firm.Path}/members/{firm.Owner.Id}";

        var demote = await firm.Owner.Http.PatchAsJsonAsync(path, new { role = "Dispatcher" });
        var leave = await firm.Owner.Http.DeleteAsync(path);

        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, leave.StatusCode);
    }

    [Fact]
    public async Task A_removed_member_loses_access_at_once()
    {
        var firm = await factory.FirmAsync();
        using var dispatcher = await factory.RegisterAsync("Planlamacı");
        await firm.AddMemberAsync(dispatcher, "Dispatcher");
        Assert.Equal(HttpStatusCode.OK, (await dispatcher.Http.GetAsync($"{firm.Path}/customers")).StatusCode);

        (await firm.Owner.Http.DeleteAsync($"{firm.Path}/members/{dispatcher.Id}")).EnsureSuccessStatusCode();

        Assert.Equal(HttpStatusCode.NotFound, (await dispatcher.Http.GetAsync($"{firm.Path}/customers")).StatusCode);
    }

    [Fact]
    public async Task Members_are_added_by_the_email_of_their_account()
    {
        var firm = await factory.FirmAsync();
        using var technician = await factory.RegisterAsync("Teknisyen");

        var unknown = await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/members", new { email = "nobody-here@example.com", role = "Technician" });
        await firm.AddMemberAsync(technician, "Technician");
        var twice = await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/members", new { email = technician.Email, role = "Dispatcher" });
        var team = await (await firm.Owner.Http.GetAsync($"{firm.Path}/members")).ReadAsync<List<MemberDto>>();

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);
        Assert.Equal(["Owner", "Technician"], team.Select(m => m.Role));
    }
}
