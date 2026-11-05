namespace Sahada.Core.Dispatching;

public static class Geo
{
    /// <summary>Average speed between jobs in town, parking included.</summary>
    public const double AverageSpeedKmh = 35;

    /// <summary>Great-circle distance in kilometres.</summary>
    public static double DistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double EarthRadiusKm = 6371.0088;
        var dLat = Radians(lat2 - lat1);
        var dLon = Radians(lon2 - lon1);
        var a = Math.Pow(Math.Sin(dLat / 2), 2) + (Math.Cos(Radians(lat1)) * Math.Cos(Radians(lat2)) * Math.Pow(Math.Sin(dLon / 2), 2));
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>Rough driving time, rounded up to five minutes; next door is no travel at all.</summary>
    public static int TravelMinutes(double km) =>
        km < 0.2 ? 0 : (int)(Math.Ceiling(km * 1.3 / AverageSpeedKmh * 60 / 5) * 5);

    private static double Radians(double degrees) => degrees * Math.PI / 180;
}

/// <summary>A job already in someone's day.</summary>
public sealed record Booking(Guid WorkOrderId, DateTimeOffset Start, DateTimeOffset End, double Latitude, double Longitude);

/// <summary>Where a job could go: when, and how far the technician comes from.</summary>
public sealed record Slot(DateTimeOffset Start, DateTimeOffset End, double DistanceKm, int TravelMinutes);

/// <summary>One technician's offer for a job, with the reasons behind its score.</summary>
public sealed record Suggestion(Guid TechnicianId, bool Eligible, Slot? Slot, int LoadPercent, bool MeetsDeadline, int Score, IReadOnlyList<string> Reasons);

/// <summary>Fits jobs into technicians' days: the checks a dispatcher's choice must pass, and the suggestions for who should go.</summary>
public static class Planner
{
    /// <summary>Why a technician cannot take a job at a given time, or null when they can.</summary>
    public static DomainException? Check(TechnicianProfile tech, IReadOnlyList<Booking> bookings, string requiredSkill, DateTimeOffset start, int minutes, TimeZoneInfo zone)
    {
        if (!tech.Active)
        {
            return new DomainException(ErrorCodes.NotInField, "This person is not on the board; turn their technician profile on first.");
        }

        if (!tech.Has(requiredSkill))
        {
            return new DomainException(ErrorCodes.LacksSkill, $"The job needs \"{requiredSkill}\", which this technician does not have.");
        }

        var end = start.AddMinutes(minutes);
        var date = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(start, zone).DateTime);
        if (tech.ShiftOn(date, zone) is not { } shift || start < shift.Start || end > shift.End)
        {
            return new DomainException(ErrorCodes.OutsideShift, $"The job has to fit in the technician's shift, {tech.ShiftStart:HH\\:mm}–{tech.ShiftEnd:HH\\:mm} on their working days.");
        }

        var clash = bookings.Where(b => b.Start < end && start < b.End).OrderBy(b => b.Start).FirstOrDefault();
        return clash is null
            ? null
            : new DomainException(ErrorCodes.SlotTaken, $"The technician already has a job from {TimeZoneInfo.ConvertTime(clash.Start, zone):HH\\:mm} to {TimeZoneInfo.ConvertTime(clash.End, zone):HH\\:mm}.");
    }

    /// <summary>
    /// The earliest gap on a day that holds the drive from the previous stop, the job and the drive on to the next
    /// job. Starts are rounded up to five minutes; nothing starts before <paramref name="notBefore"/>.
    /// </summary>
    public static Slot? FirstFreeSlot(TechnicianProfile tech, IReadOnlyList<Booking> bookings, DateOnly date, TimeZoneInfo zone, DateTimeOffset notBefore, int minutes, double latitude, double longitude)
    {
        if (tech.ShiftOn(date, zone) is not { } shift || shift.End <= notBefore)
        {
            return null;
        }

        var day = bookings.Where(b => b.End > shift.Start && b.Start < shift.End).OrderBy(b => b.Start).ToList();
        // Stops in order: start from base, each booking, then the end of the shift (no drive home counted).
        var freeFrom = shift.Start;
        var fromLat = tech.BaseLatitude;
        var fromLon = tech.BaseLongitude;
        for (var i = 0; i <= day.Count; i++)
        {
            var next = i < day.Count ? day[i] : null;
            var gapEnd = next?.Start ?? shift.End;
            var distance = Geo.DistanceKm(fromLat, fromLon, latitude, longitude);
            var travel = Geo.TravelMinutes(distance);
            var start = RoundUp(Max(freeFrom.AddMinutes(travel), notBefore), 5);
            var end = start.AddMinutes(minutes);
            var onward = next is null ? 0 : Geo.TravelMinutes(Geo.DistanceKm(latitude, longitude, next.Latitude, next.Longitude));
            if (end.AddMinutes(onward) <= gapEnd)
            {
                return new Slot(start, end, Math.Round(distance, 1), travel);
            }

            if (next is not null)
            {
                freeFrom = Max(freeFrom, next.End);
                fromLat = next.Latitude;
                fromLon = next.Longitude;
            }
        }

        return null;
    }

    /// <summary>
    /// Scores every technician for a job on a day, best first. Out of 100: finishing before the deadline 40,
    /// starting soon 30, a short drive 20, a light day 10. Without the skill or a free slot, a technician is
    /// listed as not eligible with the reason.
    /// </summary>
    public static IReadOnlyList<Suggestion> Suggest(
        IEnumerable<(TechnicianProfile Tech, IReadOnlyList<Booking> Bookings)> team,
        string requiredSkill,
        int minutes,
        double latitude,
        double longitude,
        DateTimeOffset dueAt,
        DateOnly date,
        TimeZoneInfo zone,
        DateTimeOffset now)
    {
        var suggestions = new List<Suggestion>();
        foreach (var (tech, bookings) in team)
        {
            var shift = tech.ShiftOn(date, zone);
            var shiftMinutes = shift is { } s ? (s.End - s.Start).TotalMinutes : 0;
            var booked = shift is { } w ? bookings.Sum(b => Overlap(b, w.Start, w.End)) : 0;
            var load = shiftMinutes > 0 ? (int)Math.Round(100 * booked / shiftMinutes) : 0;
            if (!tech.Active)
            {
                suggestions.Add(new(tech.UserId, false, null, load, false, 0, ["Not on the board"]));
                continue;
            }

            if (!tech.Has(requiredSkill))
            {
                suggestions.Add(new(tech.UserId, false, null, load, false, 0, [$"No \"{requiredSkill}\" skill"]));
                continue;
            }

            if (shift is null)
            {
                suggestions.Add(new(tech.UserId, false, null, load, false, 0, ["Day off"]));
                continue;
            }

            var slot = FirstFreeSlot(tech, bookings, date, zone, now, minutes, latitude, longitude);
            if (slot is null)
            {
                suggestions.Add(new(tech.UserId, false, null, load, false, 0, ["No free slot that day"]));
                continue;
            }

            var meets = slot.End <= dueAt;
            var hoursAway = Math.Max(0, (slot.Start - now).TotalHours);
            var score = (meets ? 40 : 0)
                + (int)Math.Round(30 * (1 - (Math.Min(hoursAway, 10) / 10)))
                + (int)Math.Round(20 * (1 - (Math.Min(slot.DistanceKm, 20) / 20)))
                + (int)Math.Round(10 * (1 - (Math.Min(load, 100) / 100.0)));
            var reasons = new List<string>();
            if (requiredSkill.Length > 0)
            {
                reasons.Add($"Has \"{requiredSkill}\"");
            }

            reasons.Add($"Free at {TimeZoneInfo.ConvertTime(slot.Start, zone):HH\\:mm}");
            reasons.Add(slot.DistanceKm < 0.2 ? "Already there" : $"{slot.DistanceKm:0.0} km away, about {slot.TravelMinutes} min");
            reasons.Add($"Day {load}% booked");
            reasons.Add(meets ? "Finishes before the deadline" : "Would finish after the deadline");
            suggestions.Add(new(tech.UserId, true, slot, load, meets, score, reasons));
        }

        return suggestions
            .OrderByDescending(s => s.Eligible)
            .ThenByDescending(s => s.Score)
            .ThenBy(s => s.Slot?.Start)
            .ThenBy(s => s.TechnicianId)
            .ToList();
    }

    private static double Overlap(Booking b, DateTimeOffset from, DateTimeOffset to) =>
        Math.Max(0, (Min(b.End, to) - Max(b.Start, from)).TotalMinutes);

    private static DateTimeOffset RoundUp(DateTimeOffset value, int minutes)
    {
        var step = TimeSpan.FromMinutes(minutes).Ticks;
        var ticks = (value.UtcTicks + step - 1) / step * step;
        return new DateTimeOffset(ticks, TimeSpan.Zero);
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
