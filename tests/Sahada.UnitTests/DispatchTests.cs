using Sahada.Core;
using Sahada.Core.Dispatching;
using Sahada.Core.WorkOrders;

namespace Sahada.UnitTests;

public sealed class DispatchTests
{
    private static readonly TimeZoneInfo Istanbul = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    // Monday 2 November 2026; Istanbul is UTC+3 all year.
    private static readonly DateOnly Monday = new(2026, 11, 2);

    // Atakum and the city centre in Samsun, about 7 km apart.
    private const double AtakumLat = 41.3335, AtakumLon = 36.2714, CentreLat = 41.2867, CentreLon = 36.33;

    private static DateTimeOffset Local(int hour, int minute = 0, DateOnly? day = null) =>
        new DateTimeOffset((day ?? Monday).ToDateTime(new TimeOnly(hour, minute)), TimeSpan.FromHours(3)).ToUniversalTime();

    private static TechnicianProfile Tech(string[]? skills = null, bool active = true, double lat = CentreLat, double lon = CentreLon)
    {
        var tech = TechnicianProfile.Create(Guid.NewGuid(), Guid.NewGuid(), Builders.Now);
        tech.Update(skills ?? ["hvac"], "Depo", lat, lon, new TimeOnly(8, 30), new TimeOnly(17, 30),
            [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday], active, Builders.Now);
        return tech;
    }

    private static Booking Job(int fromHour, int toHour, double lat = AtakumLat, double lon = AtakumLon) =>
        new(Guid.NewGuid(), Local(fromHour), Local(toHour), lat, lon);

    [Fact]
    public void Distances_are_great_circle_kilometres()
    {
        Assert.Equal(0, Geo.DistanceKm(AtakumLat, AtakumLon, AtakumLat, AtakumLon), 6);
        Assert.InRange(Geo.DistanceKm(AtakumLat, AtakumLon, CentreLat, CentreLon), 7, 8);
        // Samsun to Ankara is about 330 km as the crow flies.
        Assert.InRange(Geo.DistanceKm(41.2867, 36.33, 39.9334, 32.8597), 320, 340);
    }

    [Theory]
    [InlineData(0.1, 0)]
    [InlineData(1, 5)]
    [InlineData(7.4, 20)]
    [InlineData(20, 45)]
    public void Travel_time_is_rounded_up_to_five_minutes(double km, int minutes) =>
        Assert.Equal(minutes, Geo.TravelMinutes(km));

    [Fact]
    public void Shifts_are_wall_clock_times_in_the_company_zone()
    {
        var shift = Tech().ShiftOn(Monday, Istanbul);

        Assert.Equal((Local(8, 30), Local(17, 30)), shift);
        Assert.Null(Tech().ShiftOn(Monday.AddDays(5), Istanbul));
    }

    [Fact]
    public void Profiles_are_checked_and_tidied()
    {
        var tech = Tech(["HVAC", " electrical ", "hvac", ""]);

        Assert.Equal(["electrical", "hvac"], tech.Skills);
        Assert.Throws<DomainException>(() => tech.Update([], null, 41, 36, new TimeOnly(17, 0), new TimeOnly(9, 0), [DayOfWeek.Monday], true, Builders.Now));
        Assert.Throws<DomainException>(() => tech.Update([], null, 41, 36, new TimeOnly(9, 0), new TimeOnly(9, 30), [DayOfWeek.Monday], true, Builders.Now));
        Assert.Throws<DomainException>(() => tech.Update([], null, 41, 36, new TimeOnly(9, 0), new TimeOnly(17, 0), [], true, Builders.Now));
        Assert.Throws<DomainException>(() => tech.Update([], null, 99, 36, new TimeOnly(9, 0), new TimeOnly(17, 0), [DayOfWeek.Monday], true, Builders.Now));
    }

    [Fact]
    public void A_job_that_fits_passes_the_checks() =>
        Assert.Null(Planner.Check(Tech(), [Job(9, 11)], "hvac", Local(11), 90, Istanbul));

    [Theory]
    [InlineData(8, 0, 60, ErrorCodes.OutsideShift)]
    [InlineData(16, 30, 90, ErrorCodes.OutsideShift)]
    [InlineData(10, 30, 60, ErrorCodes.SlotTaken)]
    [InlineData(8, 30, 31, ErrorCodes.SlotTaken)]
    public void A_job_has_to_fit_the_shift_and_the_gaps(int hour, int minute, int minutes, string code) =>
        Assert.Equal(code, Planner.Check(Tech(), [Job(9, 11)], "hvac", Local(hour, minute), minutes, Istanbul)?.Code);

    [Fact]
    public void Skills_days_off_and_inactive_profiles_are_refused()
    {
        Assert.Equal(ErrorCodes.LacksSkill, Planner.Check(Tech(["electrical"]), [], "hvac", Local(10), 60, Istanbul)?.Code);
        Assert.Equal(ErrorCodes.OutsideShift, Planner.Check(Tech(), [], "hvac", Local(10, day: Monday.AddDays(6)), 60, Istanbul)?.Code);
        Assert.Equal(ErrorCodes.NotInField, Planner.Check(Tech(active: false), [], "", Local(10), 60, Istanbul)?.Code);
        Assert.Null(Planner.Check(Tech(["electrical"]), [], "", Local(10), 60, Istanbul));
    }

    [Fact]
    public void An_empty_day_starts_after_the_drive_from_base()
    {
        var slot = Planner.FirstFreeSlot(Tech(), [], Monday, Istanbul, Local(7), 60, AtakumLat, AtakumLon);

        Assert.Equal(new Slot(Local(8, 50), Local(9, 50), 7.1, 20), slot);
    }

    [Fact]
    public void Nothing_starts_in_the_past()
    {
        var slot = Planner.FirstFreeSlot(Tech(), [], Monday, Istanbul, Local(13, 2), 60, AtakumLat, AtakumLon);

        Assert.Equal(Local(13, 5), slot!.Start);
    }

    [Fact]
    public void A_gap_has_to_hold_the_drive_there_the_job_and_the_drive_on()
    {
        // 08:30–09:00 is free, but 20 minutes there, 60 minutes of work and 20 minutes on to the 09:00 job do not fit.
        var bookings = new[] { Job(9, 11, CentreLat + 0.0001, CentreLon), Job(12, 13) };

        var slot = Planner.FirstFreeSlot(Tech(lat: CentreLat, lon: CentreLon), bookings, Monday, Istanbul, Local(7), 60, AtakumLat, AtakumLon);

        // After the 09:00–11:00 job in the centre: drive 20 minutes, 11:20–12:20 clashes with 12:00, so after the 12:00–13:00 job next door.
        Assert.Equal(new Slot(Local(13), Local(14), 0, 0), slot);
    }

    [Fact]
    public void A_full_day_has_no_slot()
    {
        Assert.Null(Planner.FirstFreeSlot(Tech(), [Job(8, 18)], Monday, Istanbul, Local(7), 30, AtakumLat, AtakumLon));
        Assert.Null(Planner.FirstFreeSlot(Tech(), [], Monday, Istanbul, Local(18), 30, AtakumLat, AtakumLon));
    }

    [Fact]
    public void Suggestions_put_the_nearest_free_technician_first_and_say_why()
    {
        var near = Tech(lat: AtakumLat, lon: AtakumLon);
        var far = Tech();
        var busy = Tech(lat: AtakumLat, lon: AtakumLon);
        var unskilled = Tech(["electrical"], lat: AtakumLat, lon: AtakumLon);
        var away = Tech(active: false);

        var result = Planner.Suggest(
            [(far, []), (busy, [Job(8, 16)]), (unskilled, []), (near, []), (away, [])],
            "hvac", 60, AtakumLat, AtakumLon, Local(12), Monday, Istanbul, Local(8));

        Assert.Equal([near.UserId, far.UserId, busy.UserId], result.Take(3).Select(s => s.TechnicianId));
        Assert.True(result[0].Score > result[1].Score);
        Assert.Equal(["Has \"hvac\"", "Free at 08:30", "Already there", "Day 0% booked", "Finishes before the deadline"], result[0].Reasons);
        Assert.Equal((Local(16), true, false), (result[2].Slot!.Start, result[2].Eligible, result[2].MeetsDeadline));
        Assert.Equal(83, result[2].LoadPercent);
        Assert.Equal(["No \"hvac\" skill", "Not on the board"], result.Skip(3).SelectMany(s => s.Reasons).Order());
        Assert.All(result.Skip(3), s => Assert.False(s.Eligible));
    }

    [Theory]
    [InlineData(WorkOrderStatus.New, null, 1, SlaState.OnTrack)]
    [InlineData(WorkOrderStatus.New, null, 19, SlaState.AtRisk)]
    [InlineData(WorkOrderStatus.Scheduled, 10, 17, SlaState.OnTrack)]
    [InlineData(WorkOrderStatus.Scheduled, 23, 1, SlaState.AtRisk)]
    [InlineData(WorkOrderStatus.InProgress, 2, 25, SlaState.Overdue)]
    [InlineData(WorkOrderStatus.Cancelled, null, 30, SlaState.None)]
    public void Sla_state_follows_the_plan_and_the_clock(WorkOrderStatus status, int? startHours, int nowHours, SlaState expected)
    {
        // High priority: 24 hours from 09:00, so due at 09:00 the next day; at risk in the last 6 hours unplanned.
        var due = Builders.Now.AddHours(24);

        var state = SlaRules.Evaluate(Priority.High, status, due, startHours is { } h ? Builders.Now.AddHours(h) : null, 90, null, Builders.Now.AddHours(nowHours));

        Assert.Equal(expected, state);
    }

    [Fact]
    public void Finished_jobs_met_or_breached_their_deadline()
    {
        var due = Builders.Now.AddHours(4);

        Assert.Equal(SlaState.Met, SlaRules.Evaluate(Priority.Urgent, WorkOrderStatus.Completed, due, null, 60, due, due.AddDays(1)));
        Assert.Equal(SlaState.Breached, SlaRules.Evaluate(Priority.Urgent, WorkOrderStatus.Completed, due, null, 60, due.AddMinutes(1), due.AddDays(1)));
    }
}
