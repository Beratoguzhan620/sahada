namespace Sahada.Core.WorkOrders;

/// <summary>How a job stands against its deadline.</summary>
public enum SlaState
{
    /// <summary>Cancelled jobs have no deadline to keep.</summary>
    None = 0,
    OnTrack = 1,

    /// <summary>Still open and likely to miss: planned to end late, or unplanned with under a quarter of its window left.</summary>
    AtRisk = 2,
    Overdue = 3,
    Met = 4,
    Breached = 5,
}

public static class SlaRules
{
    public static SlaState Evaluate(
        Priority priority,
        WorkOrderStatus status,
        DateTimeOffset dueAt,
        DateTimeOffset? scheduledStart,
        int estimatedMinutes,
        DateTimeOffset? completedAt,
        DateTimeOffset now)
    {
        switch (status)
        {
            case WorkOrderStatus.Cancelled:
                return SlaState.None;
            case WorkOrderStatus.Completed:
                return completedAt <= dueAt ? SlaState.Met : SlaState.Breached;
        }

        if (now > dueAt)
        {
            return SlaState.Overdue;
        }

        var planned = status is WorkOrderStatus.Scheduled or WorkOrderStatus.EnRoute or WorkOrderStatus.InProgress && scheduledStart is not null;
        if (planned)
        {
            return scheduledStart!.Value.AddMinutes(estimatedMinutes) > dueAt ? SlaState.AtRisk : SlaState.OnTrack;
        }

        return dueAt - now < Sla.For(priority) / 4 ? SlaState.AtRisk : SlaState.OnTrack;
    }

    public static SlaState Evaluate(WorkOrder order, DateTimeOffset now) =>
        Evaluate(order.Priority, order.Status, order.DueAt, order.ScheduledStart, order.EstimatedMinutes, order.CompletedAt, now);
}
