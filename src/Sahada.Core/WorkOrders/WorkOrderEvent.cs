namespace Sahada.Core.WorkOrders;

public enum WorkOrderEventKind
{
    Created = 0,
    Edited = 1,
    StatusChanged = 2,
    Scheduled = 3,
    Unscheduled = 4,
    Note = 5,
    ChecklistChanged = 6,
}

/// <summary>One line of a work order's history: who did what and when. Notes live here too.</summary>
public sealed class WorkOrderEvent
{
    public const int DetailMaxLength = 2000;

    private WorkOrderEvent()
    {
    }

    public long Id { get; private set; }

    public Guid WorkOrderId { get; private set; }

    public Guid CompanyId { get; private set; }

    public WorkOrderEventKind Kind { get; private set; }

    public Guid ActorId { get; private set; }

    /// <summary>When it happened by the actor's clock; can be earlier than <see cref="RecordedAt"/> for work done offline.</summary>
    public DateTimeOffset At { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public WorkOrderStatus? FromStatus { get; private set; }

    public WorkOrderStatus? ToStatus { get; private set; }

    public string Detail { get; private set; } = string.Empty;

    public static WorkOrderEvent Record(WorkOrder order, WorkOrderEventKind kind, Guid actorId, DateTimeOffset at, DateTimeOffset now, string? detail = null, WorkOrderStatus? from = null, WorkOrderStatus? to = null) =>
        new()
        {
            WorkOrderId = order.Id,
            CompanyId = order.CompanyId,
            Kind = kind,
            ActorId = actorId,
            At = at,
            RecordedAt = now,
            FromStatus = from,
            ToStatus = to,
            Detail = detail is null ? string.Empty : detail.Length <= DetailMaxLength ? detail : detail[..DetailMaxLength],
        };

    public static WorkOrderEvent Note(WorkOrder order, Guid actorId, string text, DateTimeOffset at, DateTimeOffset now) =>
        Record(order, WorkOrderEventKind.Note, actorId, at, now, Text.CleanParagraphs(text, DetailMaxLength, "A note") is { Length: > 0 } clean
            ? clean
            : throw new DomainException(ErrorCodes.ValidationFailed, "A note cannot be empty."));
}
