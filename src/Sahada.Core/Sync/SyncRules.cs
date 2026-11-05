using Sahada.Core.WorkOrders;

namespace Sahada.Core.Sync;

/// <summary>What a device did while it may have been offline.</summary>
public enum OperationType
{
    StatusChanged = 0,
    NoteAdded = 1,
    ChecklistToggled = 2,
    PartUsed = 3,
    PartRemoved = 4,
    ResolutionSet = 5,
    Signature = 6,
    PhotoAdded = 7,
}

public enum OperationOutcome
{
    /// <summary>Done as asked.</summary>
    Applied = 0,

    /// <summary>Kept, but the job was cancelled or moved to someone else meanwhile, so a dispatcher should look.</summary>
    Flagged = 1,

    /// <summary>A checklist tick older than the one already recorded; the newer one stands.</summary>
    Stale = 2,

    /// <summary>Not done; the message says why. The device drops it.</summary>
    Rejected = 3,
}

/// <summary>
/// How offline work meets what changed on the server meanwhile. Two kinds of operation are told apart:
/// <list type="bullet">
/// <item>Changes to the job's state (status, checklist, resolution) only count while the job is still the device user's
/// and still open; otherwise they are rejected, because the office has moved on.</item>
/// <item>Records of what happened on site (notes, parts, photos, the signature) are facts: they are always kept, and
/// flagged when they arrive for a job that was cancelled or reassigned.</item>
/// </list>
/// Checklist ticks are last-write-wins by the device's clock. Device times are clamped so a wrong phone clock cannot
/// put work in the future or before the job existed.
/// </summary>
public static class SyncRules
{
    /// <summary>Work older than this is dated to this far back.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(7);

    public static bool IsRecord(OperationType type) =>
        type is OperationType.NoteAdded or OperationType.PartUsed or OperationType.PartRemoved or OperationType.PhotoAdded or OperationType.Signature;

    public static DateTimeOffset Clamp(DateTimeOffset deviceAt, DateTimeOffset createdAt, DateTimeOffset now)
    {
        var earliest = createdAt > now - MaxAge ? createdAt : now - MaxAge;
        return deviceAt < earliest ? earliest : deviceAt > now ? now : deviceAt;
    }

    /// <summary>Whether an operation may go ahead, should be kept but flagged, or is refused, before the job's own rules run.</summary>
    public static (OperationOutcome Outcome, string? Reason) Admit(OperationType type, WorkOrder order, Guid userId, bool dispatcher)
    {
        var mine = dispatcher || order.TechnicianId == userId;
        var wasMine = mine || order.PreviousTechnicianId == userId;
        if (!wasMine)
        {
            return (OperationOutcome.Rejected, "This job is not yours.");
        }

        var cancelled = order.Status == WorkOrderStatus.Cancelled;
        if (IsRecord(type))
        {
            return cancelled || !mine
                ? (OperationOutcome.Flagged, cancelled ? "The job was cancelled meanwhile; kept for the office to review." : "The job was given to someone else meanwhile; kept for the office to review.")
                : (OperationOutcome.Applied, null);
        }

        if (cancelled)
        {
            return (OperationOutcome.Rejected, "The job was cancelled meanwhile.");
        }

        if (!mine)
        {
            return (OperationOutcome.Rejected, "The job was given to someone else meanwhile.");
        }

        if (order.Status == WorkOrderStatus.Completed)
        {
            return (OperationOutcome.Rejected, "The job is already completed.");
        }

        return (OperationOutcome.Applied, null);
    }
}

/// <summary>An operation the server has seen, so a retry gets the same answer instead of doing it twice.</summary>
public sealed class SyncOperation
{
    public const int MessageMaxLength = 500;

    private SyncOperation()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid UserId { get; private set; }

    public Guid WorkOrderId { get; private set; }

    public OperationType Type { get; private set; }

    /// <summary>The time the device gave, as sent.</summary>
    public DateTimeOffset DeviceAt { get; private set; }

    public DateTimeOffset ReceivedAt { get; private set; }

    public OperationOutcome Outcome { get; private set; }

    public string Message { get; private set; } = string.Empty;

    public static SyncOperation Record(Guid id, Guid companyId, Guid userId, Guid workOrderId, OperationType type, DateTimeOffset deviceAt, DateTimeOffset now, OperationOutcome outcome, string? message) =>
        new()
        {
            Id = id,
            CompanyId = companyId,
            UserId = userId,
            WorkOrderId = workOrderId,
            Type = type,
            DeviceAt = deviceAt,
            ReceivedAt = now,
            Outcome = outcome,
            Message = message is null ? string.Empty : message.Length <= MessageMaxLength ? message : message[..MessageMaxLength],
        };
}
