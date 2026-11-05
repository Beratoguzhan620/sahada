namespace Sahada.Core.Companies;

/// <summary>What a member may do. Each role includes everything the ones before it can do.</summary>
public enum Role
{
    /// <summary>Does the work on site: sees their own jobs, records what they did.</summary>
    Technician = 0,

    /// <summary>Also takes in jobs, schedules and assigns them, keeps customers and parts.</summary>
    Dispatcher = 1,

    /// <summary>Also manages the team.</summary>
    Owner = 2,
}

/// <summary>A field service business: its team, customers, parts and work orders.</summary>
public sealed class Company
{
    public const int NameMinLength = 2;
    public const int NameMaxLength = 80;
    public const string DefaultTimeZone = "Europe/Istanbul";

    private Company()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Shifts, schedules and reports are in this zone.</summary>
    public string TimeZone { get; private set; } = DefaultTimeZone;

    /// <summary>The number the next work order gets; claimed with a single UPDATE … RETURNING.</summary>
    public int NextWorkOrderNumber { get; private set; } = 1001;

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static (Company Company, Membership Owner) Create(string name, Guid ownerId, DateTimeOffset now)
    {
        var company = new Company { Id = Guid.CreateVersion7(now), Name = Text.Clean(name, NameMinLength, NameMaxLength, "A company name"), CreatedBy = ownerId, CreatedAt = now };
        return (company, Membership.Create(company.Id, ownerId, Role.Owner, now));
    }

    public void Rename(string name) => Name = Text.Clean(name, NameMinLength, NameMaxLength, "A company name");

    /// <summary>
    /// Checks that a change to the team leaves somebody who can manage it.
    /// <paramref name="ownersAfter"/> is how many owners there would be after the change.
    /// </summary>
    public static void EnsureAnOwnerRemains(int ownersAfter)
    {
        if (ownersAfter < 1)
        {
            throw new DomainException(ErrorCodes.InvalidState, "A company needs at least one owner. Make someone else an owner first.");
        }
    }
}

public sealed class Membership
{
    private Membership()
    {
    }

    public Guid CompanyId { get; private set; }

    public Guid UserId { get; private set; }

    public Role Role { get; private set; }

    public DateTimeOffset JoinedAt { get; private set; }

    public static Membership Create(Guid companyId, Guid userId, Role role, DateTimeOffset now) =>
        new() { CompanyId = companyId, UserId = userId, Role = Enum.IsDefined(role) ? role : throw Invalid(), JoinedAt = now };

    public bool Can(Role needed) => Role >= needed;

    public void ChangeRole(Role role) => Role = Enum.IsDefined(role) ? role : throw Invalid();

    private static DomainException Invalid() => new(ErrorCodes.ValidationFailed, "A role is Technician, Dispatcher or Owner.");
}
