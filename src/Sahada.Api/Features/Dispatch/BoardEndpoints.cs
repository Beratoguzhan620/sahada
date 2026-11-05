using System.Security.Claims;
using Sahada.Api.Common;
using Sahada.Api.Features.Companies;
using Sahada.Api.Features.WorkOrders;
using Sahada.Core;
using Sahada.Core.Companies;
using Sahada.Core.Dispatching;
using Sahada.Core.WorkOrders;
using Sahada.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Sahada.Api.Features.Dispatch;

public sealed record BoardTechnician(
    Guid UserId,
    string DisplayName,
    IReadOnlyList<string> Skills,
    string BaseLabel,
    double BaseLatitude,
    double BaseLongitude,
    DateTimeOffset? ShiftStart,
    DateTimeOffset? ShiftEnd,
    int BookedMinutes,
    int LoadPercent,
    IReadOnlyList<WorkOrderSummary> Jobs);

/// <summary>A dispatcher's day: each technician's jobs in order, and the pile of jobs that still need someone.</summary>
public sealed record BoardResponse(DateOnly Date, string TimeZone, IReadOnlyList<BoardTechnician> Technicians, IReadOnlyList<WorkOrderSummary> Unscheduled);

public sealed record SlaResponse(int Open, int OnTrack, int AtRisk, int Overdue, IReadOnlyList<WorkOrderSummary> Overdues, IReadOnlyList<WorkOrderSummary> AtRisks);

public sealed record SuggestionResponse(
    Guid TechnicianId,
    string DisplayName,
    bool Eligible,
    DateTimeOffset? Start,
    DateTimeOffset? End,
    double? DistanceKm,
    int? TravelMinutes,
    int LoadPercent,
    bool MeetsDeadline,
    int Score,
    IReadOnlyList<string> Reasons);

public sealed record SuggestionsResponse(Guid WorkOrderId, DateOnly Date, IReadOnlyList<SuggestionResponse> Technicians);

internal static class BoardEndpoints
{
    public const int ListLimit = 200;

    public static IEndpointRouteBuilder MapBoardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/companies/{companyId:guid}").WithTags("Dispatch").RequireAuthorization();
        group.MapGet("/board", Board).WithSummary("A day on the board: who goes where, and what is still unplanned (dispatchers)")
            .WithDescription("`date` is a calendar day in the company's time zone, today by default. Jobs carry site coordinates for the map.");
        group.MapGet("/sla", Sla).WithSummary("Open jobs past their deadline or about to miss it (dispatchers)")
            .WithDescription("At risk: planned to end after the deadline, or unplanned with less than a quarter of the priority's window left.");
        group.MapGet("/work-orders/{workOrderId:guid}/suggestions", Suggestions).WithSummary("Who should take this job, best first (dispatchers)")
            .WithDescription("""
                For each technician: the earliest slot that day that holds the drive from their previous stop, the job and the
                drive on to their next one. Scored out of 100: finishing before the deadline 40, starting soon 30, a short drive 20,
                a light day 10. Technicians without the skill, off that day or fully booked are listed with the reason.
                """);
        return app;
    }

    private static async Task<Ok<BoardResponse>> Board(Guid companyId, DateOnly? date, ClaimsPrincipal user, SahadaDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var now = clock.GetUtcNow();
        var zone = await DispatchData.ZoneAsync(db, companyId, ct);
        var day = date ?? DispatchData.Today(zone, now);
        var (from, to) = DispatchData.DayBounds(day, zone);

        var team = await (
            from t in db.TechnicianProfiles.AsNoTracking()
            join u in db.Users on t.UserId equals u.Id
            where t.CompanyId == companyId && t.Active
            select new { Profile = t, u.DisplayName })
            .ToListAsync(ct);
        var ids = team.Select(t => t.Profile.UserId).ToList();

        var jobs = await WorkOrderQueries.Summaries(db, db.WorkOrders.AsNoTracking()
                .Where(w => w.CompanyId == companyId && w.TechnicianId != null && ids.Contains(w.TechnicianId.Value)
                    && w.ScheduledStart >= from && w.ScheduledStart < to && w.Status != WorkOrderStatus.Cancelled), now)
            .ToListAsync(ct);
        var unscheduled = await WorkOrderQueries.Summaries(db, db.WorkOrders.AsNoTracking()
                .Where(w => w.CompanyId == companyId && (w.Status == WorkOrderStatus.New || w.Status == WorkOrderStatus.OnHold))
                .OrderBy(w => w.DueAt).ThenBy(w => w.Number).Take(ListLimit), now)
            .ToListAsync(ct);

        var technicians = team
            .OrderBy(t => t.DisplayName, StringComparer.CurrentCulture)
            .Select(t =>
            {
                var shift = t.Profile.ShiftOn(day, zone);
                var mine = jobs.Where(j => j.TechnicianId == t.Profile.UserId).OrderBy(j => j.ScheduledStart).ToList();
                var booked = mine.Where(j => j.Status is not WorkOrderStatus.OnHold).Sum(j => j.EstimatedMinutes);
                var length = shift is { } s ? (int)(s.End - s.Start).TotalMinutes : 0;
                return new BoardTechnician(
                    t.Profile.UserId, t.DisplayName, t.Profile.Skills, t.Profile.BaseLabel, t.Profile.BaseLatitude, t.Profile.BaseLongitude,
                    shift?.Start, shift?.End, booked, length > 0 ? (int)Math.Round(100.0 * booked / length) : 0, mine);
            })
            .ToList();
        return TypedResults.Ok(new BoardResponse(day, zone.Id, technicians, unscheduled.OrderBy(j => j.DueAt).ThenBy(j => j.Number).ToList()));
    }

    private static async Task<Ok<SlaResponse>> Sla(Guid companyId, ClaimsPrincipal user, SahadaDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var now = clock.GetUtcNow();
        var open = await WorkOrderQueries.Summaries(db, db.WorkOrders.AsNoTracking()
                .Where(w => w.CompanyId == companyId && w.Status != WorkOrderStatus.Completed && w.Status != WorkOrderStatus.Cancelled), now)
            .ToListAsync(ct);
        var overdue = open.Where(o => o.Sla == SlaState.Overdue).OrderBy(o => o.DueAt).ThenBy(o => o.Number).ToList();
        var atRisk = open.Where(o => o.Sla == SlaState.AtRisk).OrderBy(o => o.DueAt).ThenBy(o => o.Number).ToList();
        return TypedResults.Ok(new SlaResponse(
            open.Count, open.Count - overdue.Count - atRisk.Count, atRisk.Count, overdue.Count,
            overdue.Take(ListLimit).ToList(), atRisk.Take(ListLimit).ToList()));
    }

    private static async Task<Ok<SuggestionsResponse>> Suggestions(
        Guid companyId, Guid workOrderId, DateOnly? date, ClaimsPrincipal user, SahadaDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var order = await WorkOrderEndpoints.FindAsync(db, companyId, workOrderId, membership, ct);
        if (!order.IsOpen)
        {
            throw new DomainException(ErrorCodes.InvalidState, "This job is finished; there is nobody to send.");
        }

        var site = await db.Sites.AsNoTracking().SingleAsync(s => s.Id == order.SiteId, ct);
        var now = clock.GetUtcNow();
        var zone = await DispatchData.ZoneAsync(db, companyId, ct);
        var day = date ?? DispatchData.Today(zone, now);
        var (from, to) = DispatchData.DayBounds(day, zone);

        var team = await (
            from t in db.TechnicianProfiles.AsNoTracking()
            join u in db.Users on t.UserId equals u.Id
            where t.CompanyId == companyId
            select new { Profile = t, u.DisplayName })
            .ToListAsync(ct);
        var bookings = await DispatchData.BookingsAsync(db, companyId, team.Select(t => t.Profile.UserId).ToList(), from, to, order.Id, ct);
        var names = team.ToDictionary(t => t.Profile.UserId, t => t.DisplayName);

        var suggestions = Planner.Suggest(
            team.Select(t => (t.Profile, (IReadOnlyList<Booking>)bookings[t.Profile.UserId].ToList())),
            order.RequiredSkill, order.EstimatedMinutes, site.Latitude, site.Longitude, order.DueAt, day, zone, now);
        return TypedResults.Ok(new SuggestionsResponse(order.Id, day, suggestions.Select(s => new SuggestionResponse(
            s.TechnicianId, names[s.TechnicianId], s.Eligible, s.Slot?.Start, s.Slot?.End, s.Slot?.DistanceKm, s.Slot?.TravelMinutes,
            s.LoadPercent, s.MeetsDeadline, s.Score, s.Reasons)).ToList()));
    }
}
