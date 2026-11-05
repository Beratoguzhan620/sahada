using Sahada.Core.WorkOrders;

namespace Sahada.Core.Dispatching;

/// <summary>What the planner needs to know about someone who goes out on jobs: what they can do, where they start, when they work.</summary>
public sealed class TechnicianProfile
{
    public const int MaxSkills = 12;
    public const int BaseLabelMaxLength = 60;

    private TechnicianProfile()
    {
    }

    public Guid CompanyId { get; private set; }

    public Guid UserId { get; private set; }

    /// <summary>Lower-case tags, matched against a work order's required skill.</summary>
    public List<string> Skills { get; private set; } = [];

    /// <summary>Where the day starts: home, the depot.</summary>
    public string BaseLabel { get; private set; } = string.Empty;

    public double BaseLatitude { get; private set; }

    public double BaseLongitude { get; private set; }

    /// <summary>Wall-clock times in the company's time zone.</summary>
    public TimeOnly ShiftStart { get; private set; }

    public TimeOnly ShiftEnd { get; private set; }

    /// <summary>One bit per <see cref="DayOfWeek"/>.</summary>
    public int WorkDays { get; private set; }

    /// <summary>Off the board while away (leave, sick days) without losing the profile.</summary>
    public bool Active { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public static TechnicianProfile Create(Guid companyId, Guid userId, DateTimeOffset now) =>
        new()
        {
            CompanyId = companyId,
            UserId = userId,
            BaseLabel = "Depot",
            ShiftStart = new TimeOnly(8, 30),
            ShiftEnd = new TimeOnly(17, 30),
            WorkDays = Mask([DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday]),
            Active = true,
            UpdatedAt = now,
        };

    public void Update(
        IEnumerable<string>? skills,
        string? baseLabel,
        double baseLatitude,
        double baseLongitude,
        TimeOnly shiftStart,
        TimeOnly shiftEnd,
        IEnumerable<DayOfWeek> workDays,
        bool active,
        DateTimeOffset now)
    {
        var clean = (skills ?? []).Select(WorkOrders.Skills.Normalize).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (clean.Count > MaxSkills)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A technician has at most {MaxSkills} skills.");
        }

        if (baseLatitude is < -90 or > 90 || baseLongitude is < -180 or > 180 || !double.IsFinite(baseLatitude) || !double.IsFinite(baseLongitude))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "The base has to be on the map.");
        }

        var length = shiftEnd - shiftStart;
        if (shiftEnd <= shiftStart || length < TimeSpan.FromHours(1) || length > TimeSpan.FromHours(14))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A shift starts and ends on the same day and lasts 1 to 14 hours.");
        }

        var days = workDays.ToList();
        if (days.Count == 0 || days.Any(d => !Enum.IsDefined(d)))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Pick at least one working day.");
        }

        Skills = clean;
        BaseLabel = string.IsNullOrWhiteSpace(baseLabel) ? "Depot" : Text.Clean(baseLabel, 1, BaseLabelMaxLength, "A base's name");
        BaseLatitude = baseLatitude;
        BaseLongitude = baseLongitude;
        ShiftStart = shiftStart;
        ShiftEnd = shiftEnd;
        WorkDays = Mask(days);
        Active = active;
        UpdatedAt = now;
    }

    public IReadOnlyList<DayOfWeek> Days => Enum.GetValues<DayOfWeek>().Where(d => (WorkDays & (1 << (int)d)) != 0).ToList();

    public bool Has(string requiredSkill) => requiredSkill.Length == 0 || Skills.Contains(requiredSkill);

    /// <summary>The shift on a calendar day, as instants; null on a day off.</summary>
    public (DateTimeOffset Start, DateTimeOffset End)? ShiftOn(DateOnly date, TimeZoneInfo zone)
    {
        if ((WorkDays & (1 << (int)date.DayOfWeek)) == 0)
        {
            return null;
        }

        return (Instant(date, ShiftStart, zone), Instant(date, ShiftEnd, zone));
    }

    internal static DateTimeOffset Instant(DateOnly date, TimeOnly time, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        // A wall-clock time that falls in a DST gap is moved past it.
        while (zone.IsInvalidTime(local))
        {
            local = local.AddMinutes(30);
        }

        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    private static int Mask(IEnumerable<DayOfWeek> days) => days.Aggregate(0, (mask, d) => mask | (1 << (int)d));
}
