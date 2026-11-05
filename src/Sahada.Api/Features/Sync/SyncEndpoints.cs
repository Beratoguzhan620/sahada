using System.Security.Claims;
using Sahada.Api.Common;
using Sahada.Api.Features.Companies;
using Sahada.Api.Features.Parts;
using Sahada.Api.Features.WorkOrders;
using Sahada.Core;
using Sahada.Core.Companies;
using Sahada.Core.WorkOrders;
using Sahada.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Sahada.Api.Features.Sync;

internal static class SyncEndpoints
{
    public const int MaxOperations = 200;

    /// <summary>Done jobs stay on the device this long, so the technician can look back at the week.</summary>
    public static readonly TimeSpan KeepDone = TimeSpan.FromDays(14);

    public static IEndpointRouteBuilder MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/companies/{companyId:guid}/sync").WithTags("Sync").RequireAuthorization();
        group.MapGet("/pull", Pull).WithSummary("Your jobs that changed since the cursor")
            .WithDescription("""
                Without a cursor: every open job assigned to you and the ones finished in the last 14 days, plus the parts
                catalog. With one: only the jobs that changed since. `assigned` always lists every job the device should
                hold; drop the others, they went to someone else. The cursor is an opaque string; keep the latest one.
                """);
        group.MapPost("/push", Push).WithSummary("Send what was done offline, in the order it was done")
            .WithDescription("""
                Up to 200 operations. Each is applied once, however often it is sent: a retry gets the first answer back.
                Status, checklist and resolution changes are rejected for a job that was cancelled or reassigned meanwhile;
                notes, parts, photos and signatures are kept and flagged for the office. Checklist ticks are
                last-write-wins by the device's clock. Device times are clamped to the past week, and never the future.
                """);
        return app;
    }

    private static async Task<Results<Ok<PullResponse>, ValidationProblem>> Pull(
        Guid companyId, string? cursor, ClaimsPrincipal user, SahadaDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Technician, ct);
        if (cursor is not null && !ulong.TryParse(cursor, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _))
        {
            return new Validation().Require(false, "cursor", "Pass the cursor exactly as the last pull returned it.").ToProblem();
        }

        var now = clock.GetUtcNow();
        var userId = membership.UserId;

        // Every transaction older than this one has finished, so everything stamped below it is visible now and
        // nothing below it can still appear. Changes at or above it come next time.
        var horizon = (await db.Database.SqlQuery<string>($"SELECT pg_snapshot_xmin(pg_current_snapshot())::text AS \"Value\"").ToListAsync(ct)).Single();

        var since = now - KeepDone;
        var assigned = await db.WorkOrders.AsNoTracking()
            .Where(w => w.CompanyId == companyId && w.TechnicianId == userId
                && (w.Status != WorkOrderStatus.Completed && w.Status != WorkOrderStatus.Cancelled || w.UpdatedAt >= since))
            .OrderBy(w => w.ScheduledStart).ThenBy(w => w.Number)
            .Select(w => w.Id)
            .ToListAsync(ct);

        var changed = cursor is null
            ? assigned
            : (await db.Database.SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM work_orders
                WHERE company_id = {companyId} AND technician_id = {userId}
                  AND change_xid >= {cursor}::xid8 AND change_xid < {horizon}::xid8
                """).ToListAsync(ct)).Intersect(assigned).ToList();

        var orders = new List<WorkOrderResponse>(changed.Count);
        foreach (var id in changed)
        {
            orders.Add(await WorkOrderQueries.DetailAsync(db, id, now, ct));
        }

        List<PartResponse>? parts = null;
        if (cursor is null)
        {
            parts = (await db.Parts.AsNoTracking().Where(p => p.CompanyId == companyId && p.Active).OrderBy(p => p.Sku).Take(2000).ToListAsync(ct))
                .Select(PartEndpoints.ToResponse).ToList();
        }

        return TypedResults.Ok(new PullResponse(horizon, now, orders, assigned, parts));
    }

    private static async Task<Results<Ok<PushResponse>, ValidationProblem>> Push(
        Guid companyId, PushRequest request, ClaimsPrincipal user, SahadaDbContext db, SyncService sync, TimeProvider clock, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Technician, ct);
        var operations = request.Operations ?? [];
        var validation = new Validation()
            .Require(operations.Count <= MaxOperations, "operations", $"Send at most {MaxOperations} operations at a time.")
            .Require(operations.Select(o => o.Id).Distinct().Count() == operations.Count, "operations", "Operation ids are unique within a push.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var results = new List<OperationResult>(operations.Count);
        foreach (var operation in operations)
        {
            results.Add(await sync.ApplyAsync(companyId, membership, operation, ct));
        }

        return TypedResults.Ok(new PushResponse(results, clock.GetUtcNow()));
    }
}
