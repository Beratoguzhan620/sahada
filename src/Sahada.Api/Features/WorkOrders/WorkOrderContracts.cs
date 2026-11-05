using Sahada.Core.WorkOrders;

namespace Sahada.Api.Features.WorkOrders;

public sealed record CreateWorkOrderRequest(
    Guid CustomerId,
    Guid SiteId,
    Guid? EquipmentId,
    string Title,
    string? Description,
    Priority Priority,
    string? RequiredSkill,
    int EstimatedMinutes,
    IReadOnlyList<string>? Checklist);

public sealed record EditWorkOrderRequest(string Title, string? Description, Priority Priority, string? RequiredSkill, int EstimatedMinutes);

public sealed record ChecklistItemRequest(string Text);

public sealed record SetChecklistItemRequest(bool Done);

public sealed record ScheduleRequest(Guid TechnicianId, DateTimeOffset Start);

/// <summary>Moves the job along. <c>reason</c> goes with OnHold, <c>resolution</c> with Completed.</summary>
public sealed record ChangeStatusRequest(WorkOrderStatus Status, string? Reason, string? Resolution);

public sealed record CancelRequest(string Reason);

public sealed record NoteRequest(string Text);

public sealed record WorkOrderSummary(
    Guid Id,
    int Number,
    string Code,
    string Title,
    Priority Priority,
    WorkOrderStatus Status,
    Guid CustomerId,
    string CustomerName,
    Guid SiteId,
    string SiteLabel,
    string City,
    string RequiredSkill,
    Guid? TechnicianId,
    string? TechnicianName,
    DateTimeOffset? ScheduledStart,
    int EstimatedMinutes,
    DateTimeOffset DueAt,
    bool Overdue,
    DateTimeOffset CreatedAt);

public sealed record WorkOrderResponse(
    Guid Id,
    int Number,
    string Code,
    string Title,
    string Description,
    Priority Priority,
    WorkOrderStatus Status,
    string RequiredSkill,
    int EstimatedMinutes,
    DateTimeOffset DueAt,
    bool Overdue,
    CustomerRef Customer,
    SiteRef Site,
    EquipmentRef? Equipment,
    Guid? TechnicianId,
    string? TechnicianName,
    DateTimeOffset? ScheduledStart,
    DateTimeOffset? ScheduledEnd,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    string HoldReason,
    string CancelReason,
    string Resolution,
    IReadOnlyList<ChecklistItemResponse> Checklist,
    IReadOnlyList<WorkOrderEventResponse> History,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record CustomerRef(Guid Id, string Name, string Phone);

public sealed record SiteRef(Guid Id, string Label, string Address, string City, double Latitude, double Longitude, string AccessNotes);

public sealed record EquipmentRef(Guid Id, string Kind, string Brand, string Model, string SerialNumber, DateOnly? WarrantyUntil, bool UnderWarranty);

public sealed record ChecklistItemResponse(Guid Id, int Position, string Text, bool Done, DateTimeOffset? ChangedAt);

public sealed record WorkOrderEventResponse(long Id, WorkOrderEventKind Kind, Guid ActorId, string ActorName, DateTimeOffset At, WorkOrderStatus? FromStatus, WorkOrderStatus? ToStatus, string Detail);
