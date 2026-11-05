using System.Net;
using System.Net.Http.Json;
using Sahada.IntegrationTests.Support;

namespace Sahada.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class DispatchTests(ApiFactory factory)
{
    [Fact]
    public async Task Profiles_put_team_members_on_the_board()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.TechnicianAsync(factory, "Ali", TestApi.ProfileBody(["HVAC", "electrical"]));
        using var stranger = await factory.RegisterAsync("Yabancı");

        var team = await (await ali.Http.GetAsync($"{firm.Path}/technicians")).ReadAsync<List<TechnicianDto>>();
        var outsider = await firm.Owner.Http.PutAsJsonAsync($"{firm.Path}/technicians/{stranger.Id}", TestApi.ProfileBody());
        var bySelf = await ali.Http.PutAsJsonAsync($"{firm.Path}/technicians/{ali.Id}", TestApi.ProfileBody());

        var profile = Assert.Single(team);
        Assert.Equal(["electrical", "hvac"], profile.Skills);
        Assert.Equal((new TimeOnly(8, 30), 5, "Technician"), (profile.ShiftStart, profile.WorkDays.Count, profile.Role));
        Assert.Equal(HttpStatusCode.NotFound, outsider.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, bySelf.StatusCode);
    }

    [Fact]
    public async Task Leaving_the_team_takes_the_profile_along()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.TechnicianAsync(factory);

        (await firm.Owner.Http.DeleteAsync($"{firm.Path}/members/{ali.Id}")).EnsureSuccessStatusCode();

        Assert.Empty(await (await firm.Owner.Http.GetAsync($"{firm.Path}/technicians")).ReadAsync<List<TechnicianDto>>());
    }

    [Fact]
    public async Task Scheduling_checks_the_profile_the_skill_the_shift_and_the_gaps()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.TechnicianAsync(factory);
        using var veli = await firm.TechnicianAsync(factory, "Veli", TestApi.ProfileBody(["electrical"]));
        using var newcomer = await factory.RegisterAsync("Yeni");
        await firm.AddMemberAsync(newcomer, "Technician");
        var day = factory.NextWorkday();
        var first = await firm.OrderAsync();
        var second = await firm.OrderAsync();
        (await firm.ScheduleAsync(first.Id, ali, TestApi.At(day, 10))).EnsureSuccessStatusCode();

        var notOnBoard = await firm.ScheduleAsync(second.Id, newcomer, TestApi.At(day, 10));
        var noSkill = await firm.ScheduleAsync(second.Id, veli, TestApi.At(day, 10));
        var tooEarly = await firm.ScheduleAsync(second.Id, ali, TestApi.At(day, 7));
        var clash = await firm.ScheduleAsync(second.Id, ali, TestApi.At(day, 11));
        var after = await firm.ScheduleAsync(second.Id, ali, TestApi.At(day, 11, 30));

        Assert.Equal("not_in_field", await notOnBoard.ProblemCodeAsync());
        Assert.Equal((HttpStatusCode.UnprocessableEntity, "lacks_skill"), (noSkill.StatusCode, await noSkill.ProblemCodeAsync()));
        Assert.Equal("outside_shift", await tooEarly.ProblemCodeAsync());
        Assert.Equal((HttpStatusCode.Conflict, "slot_taken"), (clash.StatusCode, await clash.ProblemCodeAsync()));
        Assert.Equal(HttpStatusCode.OK, after.StatusCode);
    }

    [Fact]
    public async Task Moving_a_job_within_its_own_slot_is_not_a_clash()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.TechnicianAsync(factory);
        var order = await firm.ScheduledAsync(factory, ali);

        var moved = await firm.ScheduleAsync(order.Id, ali, order.ScheduledStart!.Value.AddMinutes(30));

        Assert.Equal(HttpStatusCode.OK, moved.StatusCode);
        Assert.Equal(order.ScheduledStart.Value.AddMinutes(30), (await moved.ReadAsync<WorkOrderDto>()).ScheduledStart);
    }

    [Fact]
    public async Task Two_dispatchers_booking_the_same_slot_at_once_cannot_both_win()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.TechnicianAsync(factory);
        var start = TestApi.At(factory.NextWorkday(), 14);
        var orders = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => firm.OrderAsync()));

        var results = await Task.WhenAll(orders.Select(o => firm.ScheduleAsync(o.Id, ali, start)));

        Assert.Equal(1, results.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(5, results.Count(r => r.StatusCode == HttpStatusCode.Conflict));
    }

    [Fact]
    public async Task The_board_shows_each_technicians_day_and_the_pile()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.TechnicianAsync(factory, "Ali");
        using var veli = await firm.TechnicianAsync(factory, "Veli");
        var day = factory.NextWorkday();
        var morning = await firm.OrderAsync(TestApi.OrderBody(firm, "Sabah işi"));
        var afternoon = await firm.OrderAsync(TestApi.OrderBody(firm, "Öğleden sonra"));
        var waiting = await firm.OrderAsync(TestApi.OrderBody(firm, "Bekleyen", "Urgent"));
        (await firm.ScheduleAsync(afternoon.Id, ali, TestApi.At(day, 14))).EnsureSuccessStatusCode();
        (await firm.ScheduleAsync(morning.Id, ali, TestApi.At(day, 9))).EnsureSuccessStatusCode();

        var board = await (await firm.Owner.Http.GetAsync($"{firm.Path}/board?date={day:yyyy-MM-dd}")).ReadAsync<BoardDto>();
        var other = await (await firm.Owner.Http.GetAsync($"{firm.Path}/board?date={day.AddDays(1):yyyy-MM-dd}")).ReadAsync<BoardDto>();

        Assert.Equal(("Europe/Istanbul", day), (board.TimeZone, board.Date));
        Assert.Equal(["Ali", "Veli"], board.Technicians.Select(t => t.DisplayName));
        var aliDay = board.Technicians[0];
        Assert.Equal(["Sabah işi", "Öğleden sonra"], aliDay.Jobs.Select(j => j.Title));
        Assert.Equal((180, 33, TestApi.At(day, 8, 30)), (aliDay.BookedMinutes, aliDay.LoadPercent, aliDay.ShiftStart!.Value));
        Assert.Equal(41.3335, aliDay.Jobs[0].Latitude);
        Assert.Equal([waiting.Code], board.Unscheduled.Select(j => j.Code));
        Assert.Empty(other.Technicians.SelectMany(t => t.Jobs));
    }

    [Fact]
    public async Task The_board_is_for_dispatchers()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.TechnicianAsync(factory);

        Assert.Equal(HttpStatusCode.Forbidden, (await ali.Http.GetAsync($"{firm.Path}/board")).StatusCode);
    }

    [Fact]
    public async Task Sla_lists_overdue_and_at_risk_jobs()
    {
        var firm = await factory.FirmAsync();
        var late = await firm.OrderAsync(TestApi.OrderBody(firm, "Acil", "Urgent"));
        factory.Clock.Advance(TimeSpan.FromHours(1));
        var close = await firm.OrderAsync(TestApi.OrderBody(firm, "Acil, sonra gelen", "Urgent"));
        await firm.OrderAsync(TestApi.OrderBody(firm, "Düşük", "Low"));

        // Urgent jobs have 4 hours; the last hour of an unplanned one is at risk.
        factory.Clock.Advance(TimeSpan.FromHours(3.5));
        var sla = await (await firm.Owner.Http.GetAsync($"{firm.Path}/sla")).ReadAsync<SlaDto>();

        Assert.Equal((3, 1, 1, 1), (sla.Open, sla.OnTrack, sla.AtRisk, sla.Overdue));
        Assert.Equal([late.Code], sla.Overdues.Select(o => o.Code));
        Assert.Equal([close.Code], sla.AtRisks.Select(o => o.Code));
        Assert.Equal("AtRisk", sla.AtRisks[0].Sla);
    }

    [Fact]
    public async Task Suggestions_rank_the_team_for_a_job()
    {
        var firm = await factory.FirmAsync();
        // Ali starts next door to the customer, Veli across town, Can has no heating skill.
        using var ali = await firm.TechnicianAsync(factory, "Ali", TestApi.ProfileBody(latitude: 41.3335, longitude: 36.2714));
        using var veli = await firm.TechnicianAsync(factory, "Veli", TestApi.ProfileBody(latitude: 41.2867, longitude: 36.33));
        using var can = await firm.TechnicianAsync(factory, "Can", TestApi.ProfileBody(["electrical"]));
        var order = await firm.OrderAsync(TestApi.OrderBody(firm, priority: "Low"));
        var day = factory.NextWorkday(1);

        var result = await (await firm.Owner.Http.GetAsync($"{firm.Path}/work-orders/{order.Id}/suggestions?date={day:yyyy-MM-dd}")).ReadAsync<SuggestionsDto>();

        Assert.Equal(["Ali", "Veli", "Can"], result.Technicians.Select(t => t.DisplayName));
        Assert.Equal((true, TestApi.At(day, 8, 30), 0.0), (result.Technicians[0].Eligible, result.Technicians[0].Start!.Value, result.Technicians[0].DistanceKm!.Value));
        Assert.Equal((TestApi.At(day, 8, 50), 20), (result.Technicians[1].Start!.Value, result.Technicians[1].TravelMinutes!.Value));
        Assert.False(result.Technicians[2].Eligible);
        Assert.Equal(["No \"hvac\" skill"], result.Technicians[2].Reasons);
    }

    [Fact]
    public async Task Taking_a_suggestion_books_the_slot()
    {
        var firm = await factory.FirmAsync();
        using var ali = await firm.TechnicianAsync(factory);
        var order = await firm.OrderAsync();
        var day = factory.NextWorkday();
        var best = (await (await firm.Owner.Http.GetAsync($"{firm.Path}/work-orders/{order.Id}/suggestions?date={day:yyyy-MM-dd}")).ReadAsync<SuggestionsDto>()).Technicians[0];

        var booked = await firm.ScheduleAsync(order.Id, ali, best.Start!.Value);

        Assert.Equal(HttpStatusCode.OK, booked.StatusCode);
        var again = await (await firm.Owner.Http.GetAsync($"{firm.Path}/work-orders/{(await firm.OrderAsync()).Id}/suggestions?date={day:yyyy-MM-dd}")).ReadAsync<SuggestionsDto>();
        Assert.Equal(best.End!.Value, again.Technicians[0].Start!.Value);
    }
}
