using Sahada.Core;
using Sahada.Core.Companies;
using Sahada.Core.Sync;
using Sahada.Core.WorkOrders;
using Sahada.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Sahada.Api.Features.Sync;

/// <summary>Applies operations from devices one at a time, each in its own transaction, each exactly once.</summary>
internal sealed partial class SyncService(SahadaDbContext db, TimeProvider clock, ILogger<SyncService> logger)
{
    public async Task<OperationResult> ApplyAsync(Guid companyId, Membership membership, SyncOperationRequest op, CancellationToken ct)
    {
        if (op.Id == Guid.Empty || !Enum.IsDefined(op.Type))
        {
            return new OperationResult(op.Id, OperationOutcome.Rejected, "An operation needs an id and a known type.", false);
        }

        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // The job's row is locked, so a dispatcher's change and this one, or a retry and the first try, go one after the other.
        var locked = await db.Database
            .SqlQuery<Guid>($"SELECT id AS \"Value\" FROM work_orders WHERE id = {op.WorkOrderId} AND company_id = {companyId} FOR UPDATE")
            .ToListAsync(ct);
        if (locked.Count == 0)
        {
            return new OperationResult(op.Id, OperationOutcome.Rejected, "There is no work order with this id.", false);
        }

        if (await SeenAsync(op.Id, membership.UserId, ct) is { } seen)
        {
            return seen;
        }

        var order = await db.WorkOrders.Include(w => w.Checklist).SingleAsync(w => w.Id == op.WorkOrderId, ct);
        var now = clock.GetUtcNow();
        var at = SyncRules.Clamp(op.At.ToUniversalTime(), order.CreatedAt, now);
        var (outcome, message) = SyncRules.Admit(op.Type, order, membership.UserId, membership.Can(Role.Dispatcher));
        if (outcome != OperationOutcome.Rejected)
        {
            try
            {
                var applied = await ApplyAsync(order, membership.UserId, op, at, now, outcome == OperationOutcome.Flagged, ct);
                if (applied is not null)
                {
                    (outcome, message) = applied.Value;
                }
            }
            catch (DomainException ex)
            {
                // The job's own rules said no: nothing of it is saved, only the answer.
                db.ChangeTracker.Clear();
                (outcome, message) = (OperationOutcome.Rejected, ex.Message);
            }
        }

        db.SyncOperations.Add(SyncOperation.Record(op.Id, companyId, membership.UserId, order.Id, op.Type, op.At.ToUniversalTime(), now, outcome, message));
        try
        {
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // The same operation arrived twice at once (a retry racing the first try); the first one's answer stands.
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return await SeenAsync(op.Id, membership.UserId, ct) ?? throw new InvalidOperationException("A duplicate operation left no record.", ex);
        }

        LogApplied(logger, op.Type, order.Id, outcome);
        return new OperationResult(op.Id, outcome, message, false);
    }

    private async Task<OperationResult?> SeenAsync(Guid id, Guid userId, CancellationToken ct)
    {
        var seen = await db.SyncOperations.AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct);
        return seen is null
            ? null
            : seen.UserId == userId
                ? new OperationResult(id, seen.Outcome, seen.Message.Length > 0 ? seen.Message : null, true)
                : new OperationResult(id, OperationOutcome.Rejected, "This operation id is already taken.", true);
    }

    /// <summary>Does the work. Returns a different outcome when the operation turned out stale or already done.</summary>
    private async Task<(OperationOutcome, string?)?> ApplyAsync(WorkOrder order, Guid userId, SyncOperationRequest op, DateTimeOffset at, DateTimeOffset now, bool flagged, CancellationToken ct)
    {
        switch (op.Type)
        {
            case OperationType.StatusChanged:
            {
                var from = order.Status;
                string? detail = null;
                switch (op.Status)
                {
                    case WorkOrderStatus.EnRoute:
                        order.SetOut(now);
                        break;
                    case WorkOrderStatus.InProgress:
                        order.Start(at, now);
                        break;
                    case WorkOrderStatus.OnHold:
                        order.Hold(op.Reason ?? string.Empty, now);
                        detail = order.HoldReason;
                        break;
                    case WorkOrderStatus.Completed:
                        order.Complete(op.Resolution, at, now);
                        detail = order.Resolution;
                        break;
                    default:
                        throw new DomainException(ErrorCodes.ValidationFailed, "From a device a job moves to EnRoute, InProgress, OnHold or Completed.");
                }

                db.WorkOrderEvents.Add(WorkOrderEvent.Record(order, WorkOrderEventKind.StatusChanged, userId, at, now, detail, from, order.Status));
                return null;
            }

            case OperationType.NoteAdded:
                db.WorkOrderEvents.Add(WorkOrderEvent.Note(order, userId, op.Text, at, now, flagged));
                order.Touch(now);
                return null;

            case OperationType.ChecklistToggled:
            {
                var item = order.Checklist.SingleOrDefault(i => i.Id == op.ItemId)
                    ?? throw new DomainException(ErrorCodes.NotFound, "This work order has no checklist item with this id.");
                if (item.ChangedAt is { } changed && at <= changed)
                {
                    return (OperationOutcome.Stale, $"A later change to \"{item.Text}\" is already recorded.");
                }

                var done = op.Done ?? throw new DomainException(ErrorCodes.ValidationFailed, "Say whether the item is done.");
                if (order.SetChecklistItem(item.Id, done, userId, at, now))
                {
                    db.WorkOrderEvents.Add(WorkOrderEvent.Record(order, WorkOrderEventKind.ChecklistChanged, userId, at, now, $"{(done ? "Ticked" : "Unticked")} \"{item.Text}\""));
                }

                return null;
            }

            case OperationType.PartUsed:
            {
                var lineId = op.LineId is { } l && l != Guid.Empty ? l : throw new DomainException(ErrorCodes.ValidationFailed, "A part line needs an id.");
                if (await db.PartLines.AnyAsync(x => x.Id == lineId, ct))
                {
                    return (OperationOutcome.Applied, "This line was already recorded.");
                }

                var part = await db.Parts.SingleOrDefaultAsync(p => p.Id == op.PartId && p.CompanyId == order.CompanyId, ct)
                    ?? throw new DomainException(ErrorCodes.NotFound, "There is no part with this id.");
                var line = PartLine.Create(lineId, order, part, op.Quantity ?? 1, userId, at, flagged);
                db.PartLines.Add(line);
                db.WorkOrderEvents.Add(WorkOrderEvent.Record(order, WorkOrderEventKind.PartUsed, userId, at, now, $"{line.Quantity:0.##} × {line.Sku} {line.Name}", flagged: flagged));
                order.Touch(now);
                return null;
            }

            case OperationType.PartRemoved:
            {
                var line = await db.PartLines.SingleOrDefaultAsync(x => x.Id == op.LineId && x.WorkOrderId == order.Id, ct)
                    ?? throw new DomainException(ErrorCodes.NotFound, "This work order has no part line with this id.");
                if (line.Remove(at, flagged))
                {
                    db.WorkOrderEvents.Add(WorkOrderEvent.Record(order, WorkOrderEventKind.PartRemoved, userId, at, now, $"{line.Sku} {line.Name}", flagged: flagged));
                    order.Touch(now);
                }

                return null;
            }

            case OperationType.ResolutionSet:
                order.SetResolution(op.Text, now);
                return null;

            case OperationType.Signature:
            {
                var strokes = (op.Strokes ?? []).Select(s => (IReadOnlyList<SignaturePoint>)s.Select(p => p is [var x, var y]
                    ? new SignaturePoint(x, y)
                    : throw new DomainException(ErrorCodes.ValidationFailed, "A signature point is [x, y].")).ToList()).ToList();
                order.Sign(op.SignedBy ?? string.Empty, strokes, at, now);
                db.WorkOrderEvents.Add(WorkOrderEvent.Record(order, WorkOrderEventKind.Signed, userId, at, now, $"Signed by {order.SignedBy}", flagged: flagged));
                return null;
            }

            case OperationType.PhotoAdded:
            {
                var photo = await db.Attachments.SingleOrDefaultAsync(a => a.Id == op.AttachmentId && a.WorkOrderId == order.Id, ct)
                    ?? throw new DomainException(ErrorCodes.NotFound, "Upload the photo before sending this.");
                photo.SetCaption(op.Caption, flagged);
                db.WorkOrderEvents.Add(WorkOrderEvent.Record(order, WorkOrderEventKind.PhotoAdded, userId, at, now, photo.Caption, flagged: flagged));
                order.Touch(now);
                return null;
            }

            default:
                throw new DomainException(ErrorCodes.ValidationFailed, "Unknown operation.");
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sync {Type} on {WorkOrder}: {Outcome}")]
    private static partial void LogApplied(ILogger logger, OperationType type, Guid workOrder, OperationOutcome outcome);
}
