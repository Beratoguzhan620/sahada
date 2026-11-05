using Sahada.Core.Dispatching;
using Sahada.Core.WorkOrders;
using Sahada.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Sahada.Api.Features.Dispatch;

/// <summary>Reads what the planner works with: the company's clock, the team's profiles and the jobs already in their days.</summary>
internal static class DispatchData
{
    /// <summary>Statuses that hold a place in a technician's day. A job on hold gives its slot back.</summary>
    public static readonly WorkOrderStatus[] Booked = [WorkOrderStatus.Scheduled, WorkOrderStatus.EnRoute, WorkOrderStatus.InProgress];

    public static async Task<TimeZoneInfo> ZoneAsync(SahadaDbContext db, Guid companyId, CancellationToken ct)
    {
        var id = await db.Companies.Where(c => c.Id == companyId).Select(c => c.TimeZone).SingleAsync(ct);
        return TimeZoneInfo.FindSystemTimeZoneById(id);
    }

    public static DateOnly Today(TimeZoneInfo zone, DateTimeOffset now) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);

    /// <summary>The day as instants in the company's zone, from local midnight to the next.</summary>
    public static (DateTimeOffset From, DateTimeOffset To) DayBounds(DateOnly date, TimeZoneInfo zone)
    {
        var from = new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(date.ToDateTime(TimeOnly.MinValue))).ToUniversalTime();
        var nextDay = date.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var to = new DateTimeOffset(nextDay, zone.GetUtcOffset(nextDay)).ToUniversalTime();
        return (from, to);
    }

    /// <summary>Jobs booked for these technicians that touch the window, leaving one order out (the one being moved).</summary>
    public static async Task<ILookup<Guid, Booking>> BookingsAsync(
        SahadaDbContext db, Guid companyId, IReadOnlyCollection<Guid> technicians, DateTimeOffset from, DateTimeOffset to, Guid? except, CancellationToken ct)
    {
        // Jobs are at most 16 hours long, so anything that starts this long before the window cannot reach into it.
        var earliest = from.AddMinutes(-WorkOrder.MaxEstimatedMinutes);
        var rows = await (
            from w in db.WorkOrders.AsNoTracking()
            join s in db.Sites on w.SiteId equals s.Id
            where w.CompanyId == companyId
                && w.TechnicianId != null && technicians.Contains(w.TechnicianId.Value)
                && Booked.Contains(w.Status)
                && w.ScheduledStart >= earliest && w.ScheduledStart < to
                && (except == null || w.Id != except)
            select new { TechnicianId = w.TechnicianId!.Value, w.Id, Start = w.ScheduledStart!.Value, w.EstimatedMinutes, s.Latitude, s.Longitude })
            .ToListAsync(ct);
        return rows
            .Select(r => (r.TechnicianId, Booking: new Booking(r.Id, r.Start, r.Start.AddMinutes(r.EstimatedMinutes), r.Latitude, r.Longitude)))
            .Where(r => r.Booking.End > from)
            .ToLookup(r => r.TechnicianId, r => r.Booking);
    }
}
