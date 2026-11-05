using Sahada.Core.Customers;
using Sahada.Core.WorkOrders;
using Sahada.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Sahada.Api.Features.WorkOrders;

/// <summary>What the lists and the detail page show; reads only.</summary>
internal static class WorkOrderQueries
{
    public static IQueryable<WorkOrderSummary> Summaries(SahadaDbContext db, IQueryable<WorkOrder> orders, DateTimeOffset now) =>
        from w in orders
        join c in db.Customers on w.CustomerId equals c.Id
        join s in db.Sites on w.SiteId equals s.Id
        select new WorkOrderSummary(
            w.Id,
            w.Number,
            WorkOrder.Format(w.Number),
            w.Title,
            w.Priority,
            w.Status,
            c.Id,
            c.Name,
            s.Id,
            s.Label,
            s.City,
            s.Latitude,
            s.Longitude,
            w.RequiredSkill,
            w.TechnicianId,
            db.Users.Where(u => u.Id == w.TechnicianId).Select(u => u.DisplayName).FirstOrDefault(),
            w.ScheduledStart,
            w.EstimatedMinutes,
            w.DueAt,
            w.Status != WorkOrderStatus.Completed && w.Status != WorkOrderStatus.Cancelled && w.DueAt < now,
            SlaRules.Evaluate(w.Priority, w.Status, w.DueAt, w.ScheduledStart, w.EstimatedMinutes, w.CompletedAt, now),
            w.CreatedAt);

    public static async Task<WorkOrderResponse> DetailAsync(SahadaDbContext db, Guid workOrderId, DateTimeOffset now, CancellationToken ct)
    {
        var order = await db.WorkOrders.AsNoTracking().Include(w => w.Checklist).SingleAsync(w => w.Id == workOrderId, ct);
        var customer = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == order.CustomerId, ct);
        var site = await db.Sites.AsNoTracking().SingleAsync(s => s.Id == order.SiteId, ct);
        var equipment = order.EquipmentId is { } equipmentId
            ? await db.Equipment.AsNoTracking().SingleOrDefaultAsync(e => e.Id == equipmentId, ct)
            : null;
        var technician = order.TechnicianId is { } technicianId
            ? await db.Users.AsNoTracking().Where(u => u.Id == technicianId).Select(u => u.DisplayName).SingleOrDefaultAsync(ct)
            : null;
        var history = await (
            from e in db.WorkOrderEvents.AsNoTracking()
            join u in db.Users on e.ActorId equals u.Id
            where e.WorkOrderId == workOrderId
            orderby e.Id
            select new WorkOrderEventResponse(e.Id, e.Kind, e.ActorId, u.DisplayName, e.At, e.FromStatus, e.ToStatus, e.Detail, e.Flagged))
            .ToListAsync(ct);
        var parts = await db.PartLines.AsNoTracking().Where(l => l.WorkOrderId == workOrderId).OrderBy(l => l.AddedAt).ThenBy(l => l.Id).ToListAsync(ct);
        var photos = await db.Attachments.AsNoTracking().Where(a => a.WorkOrderId == workOrderId).OrderBy(a => a.UploadedAt).ThenBy(a => a.Id).ToListAsync(ct);
        // Everything a device sends for a job it no longer had leaves a flagged line in the history.
        var flagged = history.Count(h => h.Flagged);

        return new WorkOrderResponse(
            order.Id,
            order.Number,
            order.Code,
            order.Title,
            order.Description,
            order.Priority,
            order.Status,
            order.RequiredSkill,
            order.EstimatedMinutes,
            order.DueAt,
            order.IsOverdue(now),
            SlaRules.Evaluate(order, now),
            new CustomerRef(customer.Id, customer.Name, customer.Phone),
            new SiteRef(site.Id, site.Label, site.Address, site.City, site.Latitude, site.Longitude, site.AccessNotes),
            equipment is null ? null : ToRef(equipment, now),
            order.TechnicianId,
            technician,
            order.ScheduledStart,
            order.ScheduledEnd,
            order.StartedAt,
            order.CompletedAt,
            order.HoldReason,
            order.CancelReason,
            order.Resolution,
            order.Checklist.OrderBy(i => i.Position).Select(i => new ChecklistItemResponse(i.Id, i.Position, i.Text, i.Done, i.ChangedAt)).ToList(),
            parts.Select(l => new PartLineResponse(l.Id, l.PartId, l.Sku, l.Name, l.Quantity, l.UnitPriceMinor / 100m, l.TotalMinor / 100m, l.AddedAt, l.RemovedAt is not null, l.Flagged)).ToList(),
            parts.Sum(l => l.TotalMinor) / 100m,
            photos.Select(a => new AttachmentResponse(a.Id, a.ContentType, a.SizeBytes, a.Caption, a.UploadedAt, a.Flagged)).ToList(),
            order.SignedAt is { } signedAt
                ? new SignatureResponse(order.SignedBy, signedAt, SignatureStrokes.Decode(order.Signature).Select(s => (IReadOnlyList<double[]>)s.Select(p => new[] { p.X, p.Y }).ToList()).ToList())
                : null,
            flagged,
            history,
            order.CreatedAt,
            order.UpdatedAt);
    }

    private static EquipmentRef ToRef(Equipment e, DateTimeOffset now) =>
        new(e.Id, e.Kind, e.Brand, e.Model, e.SerialNumber, e.WarrantyUntil, e.UnderWarranty(DateOnly.FromDateTime(now.UtcDateTime)));
}
