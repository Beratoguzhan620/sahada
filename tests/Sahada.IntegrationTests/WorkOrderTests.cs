using System.Net;
using System.Net.Http.Json;
using Sahada.IntegrationTests.Support;

namespace Sahada.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class WorkOrderTests(ApiFactory factory)
{
    [Fact]
    public async Task A_new_order_gets_a_number_a_deadline_and_a_first_history_line()
    {
        var firm = await factory.FirmAsync();
        var now = factory.Clock.GetUtcNow();

        var order = await firm.OrderAsync(TestApi.OrderBody(firm, priority: "Urgent"));

        Assert.Equal(("SO-01001", "New", "hvac"), (order.Code, order.Status, order.RequiredSkill));
        Assert.Equal(now.AddHours(4), order.DueAt, TimeSpan.FromSeconds(1));
        Assert.Equal(("Atakum Apartmanı", "Kazan dairesi", "Kombi"), (order.Customer.Name, order.Site.Label, order.Equipment!.Kind));
        Assert.True(order.Equipment.UnderWarranty);
        Assert.Equal(["Basıncı ölç", "Gaz kaçağı testi"], order.Checklist.Select(i => i.Text));
        var created = Assert.Single(order.History);
        Assert.Equal(("Created", "Deniz Sahibi", "New"), (created.Kind, created.ActorName, created.ToStatus));
    }

    [Fact]
    public async Task Numbers_count_up_per_company_without_gaps_even_when_opened_at_once()
    {
        var firm = await factory.FirmAsync();
        var other = await factory.FirmAsync("Güney Isı");

        var orders = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => firm.OrderAsync()));
        var theirs = await other.OrderAsync();

        Assert.Equal(Enumerable.Range(1001, 12), orders.Select(o => o.Number).Order());
        Assert.Equal(1001, theirs.Number);
    }

    [Fact]
    public async Task The_site_must_belong_to_the_customer()
    {
        var firm = await factory.FirmAsync();
        var other = await factory.FirmAsync("Güney Isı");
        var body = new { customerId = firm.CustomerId, siteId = other.SiteId, title = "Bakım", priority = "Low", estimatedMinutes = 60 };

        var response = await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/work-orders", body);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task A_technician_takes_a_job_from_scheduled_to_completed()
    {
        var firm = await factory.FirmAsync();
        using var technician = await factory.RegisterAsync("Teknisyen Ali");
        await firm.AddMemberAsync(technician, "Technician");
        var order = await firm.ScheduledAsync(factory, technician);
        var path = $"{firm.Path}/work-orders/{order.Id}";

        Assert.Equal(("Scheduled", "Teknisyen Ali"), (order.Status, order.TechnicianName));
        Assert.Equal(order.ScheduledStart!.Value.AddMinutes(90), order.ScheduledEnd);
        (await technician.StatusAsync(firm, order.Id, "EnRoute")).EnsureSuccessStatusCode();
        (await technician.StatusAsync(firm, order.Id, "InProgress")).EnsureSuccessStatusCode();
        var early = await technician.StatusAsync(firm, order.Id, "Completed", resolution: "Conta değişti.");
        foreach (var item in order.Checklist)
        {
            (await technician.Http.PutAsJsonAsync($"{path}/checklist/{item.Id}", new { done = true })).EnsureSuccessStatusCode();
        }

        var done = await (await technician.StatusAsync(firm, order.Id, "Completed", resolution: "Conta değişti, basınç 1.5 bar.")).ReadAsync<WorkOrderDto>();

        Assert.Equal(HttpStatusCode.Conflict, early.StatusCode);
        Assert.Equal(("Completed", "Conta değişti, basınç 1.5 bar."), (done.Status, done.Resolution));
        Assert.All(done.Checklist, i => Assert.True(i.Done));
        Assert.Equal(
            ["Created", "Scheduled", "StatusChanged", "StatusChanged", "ChecklistChanged", "ChecklistChanged", "StatusChanged"],
            done.History.Select(h => h.Kind));
        Assert.Equal("InProgress→Completed", $"{done.History[^1].FromStatus}→{done.History[^1].ToStatus}");
    }

    [Fact]
    public async Task Technicians_only_see_and_work_their_own_jobs()
    {
        var firm = await factory.FirmAsync();
        using var ali = await factory.RegisterAsync("Ali");
        using var veli = await factory.RegisterAsync("Veli");
        await firm.AddMemberAsync(ali, "Technician");
        await firm.AddMemberAsync(veli, "Technician");
        var alis = await firm.ScheduledAsync(factory, ali);
        await firm.OrderAsync();

        var aliList = await (await ali.Http.GetAsync($"{firm.Path}/work-orders")).ReadAsync<PageDto<WorkOrderSummaryDto>>();
        var veliList = await (await veli.Http.GetAsync($"{firm.Path}/work-orders")).ReadAsync<PageDto<WorkOrderSummaryDto>>();
        var peek = await veli.Http.GetAsync($"{firm.Path}/work-orders/{alis.Id}");
        var meddle = await veli.StatusAsync(firm, alis.Id, "EnRoute");

        Assert.Equal([alis.Id], aliList.Items.Select(o => o.Id));
        Assert.Empty(veliList.Items);
        Assert.Equal(HttpStatusCode.NotFound, peek.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, meddle.StatusCode);
    }

    [Fact]
    public async Task Only_team_members_can_be_scheduled()
    {
        var firm = await factory.FirmAsync();
        using var stranger = await factory.RegisterAsync("Yabancı");
        var order = await firm.OrderAsync();

        var response = await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/work-orders/{order.Id}/schedule", new { technicianId = stranger.Id, start = factory.Clock.GetUtcNow().AddHours(1) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Hold_needs_a_reason_and_cancelled_jobs_are_frozen()
    {
        var firm = await factory.FirmAsync();
        using var technician = await factory.RegisterAsync("Teknisyen");
        await firm.AddMemberAsync(technician, "Technician");
        var order = await firm.ScheduledAsync(factory, technician);
        (await technician.StatusAsync(firm, order.Id, "InProgress")).EnsureSuccessStatusCode();

        var noReason = await technician.StatusAsync(firm, order.Id, "OnHold");
        var held = await (await technician.StatusAsync(firm, order.Id, "OnHold", reason: "Parça bekleniyor")).ReadAsync<WorkOrderDto>();
        var cancelled = await (await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/work-orders/{order.Id}/cancel", new { reason = "Müşteri başka firmayla anlaştı" })).ReadAsync<WorkOrderDto>();
        var afterwards = await technician.StatusAsync(firm, order.Id, "InProgress");
        var note = await technician.Http.PostAsJsonAsync($"{firm.Path}/work-orders/{order.Id}/notes", new { text = "Parça geldi." });

        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);
        Assert.Equal(("OnHold", "Parça bekleniyor"), (held.Status, held.HoldReason));
        Assert.Equal(("Cancelled", "Müşteri başka firmayla anlaştı"), (cancelled.Status, cancelled.CancelReason));
        Assert.Equal(HttpStatusCode.Conflict, afterwards.StatusCode);
        Assert.Equal("invalid_state", await afterwards.ProblemCodeAsync());
        Assert.Equal(HttpStatusCode.OK, note.StatusCode);
    }

    [Fact]
    public async Task Status_cannot_be_used_to_cancel_or_schedule()
    {
        var firm = await factory.FirmAsync();
        var order = await firm.OrderAsync();

        var response = await firm.Owner.StatusAsync(firm, order.Id, "Cancelled");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Editing_records_what_changed_and_moves_the_deadline()
    {
        var firm = await factory.FirmAsync();
        var order = await firm.OrderAsync(TestApi.OrderBody(firm, priority: "Low"));

        var edited = await (await firm.Owner.Http.PutAsJsonAsync($"{firm.Path}/work-orders/{order.Id}",
            new { title = order.Title, description = order.Description, priority = "High", requiredSkill = "hvac", estimatedMinutes = 120 })).ReadAsync<WorkOrderDto>();

        Assert.Equal(order.CreatedAt.AddHours(24), edited.DueAt);
        Assert.Equal("Changed priority Low → High, estimate 90 → 120 min", edited.History[^1].Detail);
    }

    [Fact]
    public async Task Filters_combine_and_overdue_follows_the_clock()
    {
        var firm = await factory.FirmAsync();
        var urgent = await firm.OrderAsync(TestApi.OrderBody(firm, "Acil arıza", "Urgent"));
        var low = await firm.OrderAsync(TestApi.OrderBody(firm, "Yıllık bakım", "Low"));
        await firm.Owner.Http.PostAsJsonAsync($"{firm.Path}/work-orders/{low.Id}/cancel", new { reason = "Ertelendi" });

        async Task<List<string>> Codes(string query) =>
            (await (await firm.Owner.Http.GetAsync($"{firm.Path}/work-orders?{query}")).ReadAsync<PageDto<WorkOrderSummaryDto>>()).Items.Select(o => o.Code).ToList();

        Assert.Equal([urgent.Code, low.Code], await Codes(""));
        Assert.Equal([urgent.Code], await Codes("status=New&status=Scheduled"));
        Assert.Equal([low.Code], await Codes("status=cancelled"));
        Assert.Equal([low.Code], await Codes("q=bakım"));
        Assert.Equal([urgent.Code], await Codes($"q={urgent.Code}"));
        Assert.Equal([low.Code, urgent.Code], await Codes("sort=newest"));
        Assert.Empty(await Codes("overdue=true"));

        factory.Clock.Advance(TimeSpan.FromHours(5));

        var late = (await (await firm.Owner.Http.GetAsync($"{firm.Path}/work-orders?overdue=true")).ReadAsync<PageDto<WorkOrderSummaryDto>>()).Items;
        Assert.Equal([urgent.Code], late.Select(o => o.Code));
        Assert.True(late[0].Overdue);
    }

    [Fact]
    public async Task Unknown_filters_are_reported()
    {
        var firm = await factory.FirmAsync();

        var response = await firm.Owner.Http.GetAsync($"{firm.Path}/work-orders?status=Lost&sort=random");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Two_saves_of_the_same_order_at_once_do_not_overwrite_each_other()
    {
        var firm = await factory.FirmAsync();
        var order = await firm.OrderAsync(TestApi.OrderBody(firm, checklist: []));

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(i => firm.Owner.Http.PostAsJsonAsync(
            $"{firm.Path}/work-orders/{order.Id}/checklist", new { text = $"Adım {i}" })));

        var final = await (await firm.Owner.Http.GetAsync($"{firm.Path}/work-orders/{order.Id}")).ReadAsync<WorkOrderDto>();
        var saved = results.Count(r => r.StatusCode == HttpStatusCode.OK);
        Assert.All(results, r => Assert.Contains(r.StatusCode, new[] { HttpStatusCode.OK, HttpStatusCode.Conflict }));
        Assert.Equal(saved, final.Checklist.Count);
        Assert.Equal(Enumerable.Range(1, saved), final.Checklist.Select(i => i.Position));
    }
}
