namespace Sahada.Core.WorkOrders;

/// <summary>How soon the work has to be done. Each priority comes with a deadline counted from when the order came in.</summary>
public enum Priority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3,
}

/// <summary>
/// Where a job stands. The happy path is New → Scheduled → EnRoute → InProgress → Completed; work can pause
/// (OnHold) and be picked up again, and anything not finished can be cancelled.
/// </summary>
public enum WorkOrderStatus
{
    New = 0,
    Scheduled = 1,
    EnRoute = 2,
    InProgress = 3,
    OnHold = 4,
    Completed = 5,
    Cancelled = 6,
}

public static class Sla
{
    /// <summary>The time a priority gives to finish the work.</summary>
    public static TimeSpan For(Priority priority) => priority switch
    {
        Priority.Urgent => TimeSpan.FromHours(4),
        Priority.High => TimeSpan.FromHours(24),
        Priority.Normal => TimeSpan.FromHours(72),
        Priority.Low => TimeSpan.FromDays(7),
        _ => throw new DomainException(ErrorCodes.ValidationFailed, "A priority is Low, Normal, High or Urgent."),
    };
}

/// <summary>A job for a customer: what is wrong, where, how urgent, and everything that happened to it.</summary>
public sealed class WorkOrder
{
    public const int TitleMaxLength = 120;
    public const int DescriptionMaxLength = 4000;
    public const int SkillMaxLength = 40;
    public const int ReasonMaxLength = 500;
    public const int ResolutionMaxLength = 4000;
    public const int MaxEstimatedMinutes = 16 * 60;
    public const int MaxChecklistItems = 30;

    private readonly List<ChecklistItem> _checklist = [];

    private WorkOrder()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    /// <summary>Counts up per company; shown as <see cref="Code"/>.</summary>
    public int Number { get; private set; }

    public string Code => Format(Number);

    public Guid CustomerId { get; private set; }

    public Guid SiteId { get; private set; }

    public Guid? EquipmentId { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Description { get; private set; } = string.Empty;

    public Priority Priority { get; private set; }

    /// <summary>Lower case, empty when anyone can do the job; matched against technicians' skills when assigning.</summary>
    public string RequiredSkill { get; private set; } = string.Empty;

    public int EstimatedMinutes { get; private set; }

    public WorkOrderStatus Status { get; private set; }

    /// <summary>When the work has to be done by: the time the order came in plus the priority's window.</summary>
    public DateTimeOffset DueAt { get; private set; }

    public Guid? TechnicianId { get; private set; }

    public DateTimeOffset? ScheduledStart { get; private set; }

    public DateTimeOffset? StartedAt { get; private set; }

    public DateTimeOffset? CompletedAt { get; private set; }

    public string HoldReason { get; private set; } = string.Empty;

    public string CancelReason { get; private set; } = string.Empty;

    public string Resolution { get; private set; } = string.Empty;

    public Guid CreatedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyList<ChecklistItem> Checklist => _checklist;

    public bool IsOpen => Status is not (WorkOrderStatus.Completed or WorkOrderStatus.Cancelled);

    public DateTimeOffset? ScheduledEnd => ScheduledStart?.AddMinutes(EstimatedMinutes);

    public static string Format(int number) => $"SO-{number:D5}";

    public static WorkOrder Create(
        Guid companyId,
        int number,
        Guid customerId,
        Guid siteId,
        Guid? equipmentId,
        string title,
        string? description,
        Priority priority,
        string? requiredSkill,
        int estimatedMinutes,
        IEnumerable<string>? checklist,
        Guid createdBy,
        DateTimeOffset now)
    {
        var order = new WorkOrder
        {
            Id = Guid.CreateVersion7(now),
            CompanyId = companyId,
            Number = number > 0 ? number : throw new ArgumentOutOfRangeException(nameof(number)),
            CustomerId = customerId,
            SiteId = siteId,
            EquipmentId = equipmentId,
            Status = WorkOrderStatus.New,
            CreatedBy = createdBy,
            CreatedAt = now,
            UpdatedAt = now,
        };
        order.Edit(title, description, priority, requiredSkill, estimatedMinutes, now);
        foreach (var item in checklist ?? [])
        {
            order.AddChecklistItem(item, now);
        }

        return order;
    }

    /// <summary>Changes what the job is about. Changing the priority moves the deadline, counted from when the order came in.</summary>
    public void Edit(string title, string? description, Priority priority, string? requiredSkill, int estimatedMinutes, DateTimeOffset now)
    {
        EnsureOpen();
        if (estimatedMinutes is < 15 or > MaxEstimatedMinutes)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"An estimate is 15 to {MaxEstimatedMinutes} minutes.");
        }

        Title = Text.Clean(title, 3, TitleMaxLength, "A title");
        Description = Text.CleanParagraphs(description, DescriptionMaxLength, "A description");
        Priority = priority;
        DueAt = CreatedAt + Sla.For(priority);
        RequiredSkill = Skills.Normalize(requiredSkill);
        EstimatedMinutes = estimatedMinutes;
        UpdatedAt = now;
    }

    public ChecklistItem AddChecklistItem(string text, DateTimeOffset now)
    {
        EnsureOpen();
        if (_checklist.Count >= MaxChecklistItems)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A checklist has at most {MaxChecklistItems} items.");
        }

        var item = ChecklistItem.Create(Id, _checklist.Count == 0 ? 1 : _checklist.Max(i => i.Position) + 1, text, now);
        _checklist.Add(item);
        UpdatedAt = now;
        return item;
    }

    /// <summary>Ticks or unticks an item. Returns false when it already was in that state.</summary>
    public bool SetChecklistItem(Guid itemId, bool done, Guid by, DateTimeOffset at, DateTimeOffset now)
    {
        EnsureOpen();
        var item = _checklist.SingleOrDefault(i => i.Id == itemId)
            ?? throw new DomainException(ErrorCodes.NotFound, "This work order has no checklist item with this id.");
        if (!item.Set(done, by, at))
        {
            return false;
        }

        UpdatedAt = now;
        return true;
    }

    /// <summary>Puts the job in a technician's day.</summary>
    public void Schedule(Guid technicianId, DateTimeOffset start, DateTimeOffset now)
    {
        Require("schedule", WorkOrderStatus.New, WorkOrderStatus.Scheduled, WorkOrderStatus.OnHold);
        TechnicianId = technicianId;
        ScheduledStart = start;
        Status = WorkOrderStatus.Scheduled;
        HoldReason = string.Empty;
        UpdatedAt = now;
    }

    /// <summary>Takes the job out of the schedule; it goes back to the pile.</summary>
    public void Unschedule(DateTimeOffset now)
    {
        Require("unschedule", WorkOrderStatus.Scheduled);
        TechnicianId = null;
        ScheduledStart = null;
        Status = WorkOrderStatus.New;
        UpdatedAt = now;
    }

    public void SetOut(DateTimeOffset now)
    {
        Require("set out for", WorkOrderStatus.Scheduled);
        Status = WorkOrderStatus.EnRoute;
        UpdatedAt = now;
    }

    public void Start(DateTimeOffset at, DateTimeOffset now)
    {
        Require("start", WorkOrderStatus.Scheduled, WorkOrderStatus.EnRoute, WorkOrderStatus.OnHold);
        StartedAt ??= at;
        Status = WorkOrderStatus.InProgress;
        HoldReason = string.Empty;
        UpdatedAt = now;
    }

    /// <summary>Pauses the work, for example while a part is ordered.</summary>
    public void Hold(string reason, DateTimeOffset now)
    {
        Require("put on hold", WorkOrderStatus.InProgress);
        HoldReason = Text.Clean(reason, 3, ReasonMaxLength, "A reason");
        Status = WorkOrderStatus.OnHold;
        UpdatedAt = now;
    }

    /// <summary>Records what was done. Every checklist item has to be ticked first.</summary>
    public void Complete(string resolution, DateTimeOffset at, DateTimeOffset now)
    {
        Require("complete", WorkOrderStatus.InProgress);
        var open = _checklist.Count(i => !i.Done);
        if (open > 0)
        {
            throw new DomainException(ErrorCodes.InvalidState, open == 1 ? "One checklist item is not done yet." : $"{open} checklist items are not done yet.");
        }

        Resolution = Text.CleanParagraphs(resolution, ResolutionMaxLength, "A resolution");
        if (Resolution.Length < 3)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Say in a few words what was done.");
        }

        CompletedAt = at;
        Status = WorkOrderStatus.Completed;
        UpdatedAt = now;
    }

    public void Cancel(string reason, DateTimeOffset now)
    {
        EnsureOpen();
        CancelReason = Text.Clean(reason, 3, ReasonMaxLength, "A reason");
        Status = WorkOrderStatus.Cancelled;
        UpdatedAt = now;
    }

    /// <summary>Whether the deadline has passed while the job is still open.</summary>
    public bool IsOverdue(DateTimeOffset now) => IsOpen && now > DueAt;

    private void EnsureOpen()
    {
        if (!IsOpen)
        {
            throw new DomainException(ErrorCodes.InvalidState, $"This work order is {Status.ToString().ToLowerInvariant()} and can no longer change.");
        }
    }

    private void Require(string action, params WorkOrderStatus[] allowed)
    {
        if (!allowed.Contains(Status))
        {
            throw new DomainException(ErrorCodes.InvalidState, $"A work order that is {Describe(Status)} cannot {action}.");
        }
    }

    private static string Describe(WorkOrderStatus status) => status switch
    {
        WorkOrderStatus.EnRoute => "en route",
        WorkOrderStatus.InProgress => "in progress",
        WorkOrderStatus.OnHold => "on hold",
        _ => status.ToString().ToLowerInvariant(),
    };
}

/// <summary>One step a technician ticks off on site.</summary>
public sealed class ChecklistItem
{
    public const int TextMaxLength = 200;

    private ChecklistItem()
    {
    }

    public Guid Id { get; private set; }

    public Guid WorkOrderId { get; private set; }

    public int Position { get; private set; }

    public string Text { get; private set; } = string.Empty;

    public bool Done { get; private set; }

    /// <summary>When it was last ticked or unticked, by the clock of whoever did it.</summary>
    public DateTimeOffset? ChangedAt { get; private set; }

    public Guid? ChangedBy { get; private set; }

    internal static ChecklistItem Create(Guid workOrderId, int position, string text, DateTimeOffset now) =>
        new() { Id = Guid.CreateVersion7(now), WorkOrderId = workOrderId, Position = position, Text = Core.Text.Clean(text, 2, TextMaxLength, "A checklist item") };

    internal bool Set(bool done, Guid by, DateTimeOffset at)
    {
        if (Done == done)
        {
            return false;
        }

        Done = done;
        ChangedAt = at;
        ChangedBy = by;
        return true;
    }
}

public static class Skills
{
    /// <summary>Skills are short lower-case tags such as "hvac" or "electrical".</summary>
    public static string Normalize(string? skill)
    {
        var clean = (skill ?? string.Empty).Trim().ToLowerInvariant();
        if (clean.Length > WorkOrder.SkillMaxLength || !clean.All(c => char.IsLetterOrDigit(c) || c is '-' or '_'))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A skill is a single word of up to {WorkOrder.SkillMaxLength} letters, digits, '-' or '_'.");
        }

        return clean;
    }
}
