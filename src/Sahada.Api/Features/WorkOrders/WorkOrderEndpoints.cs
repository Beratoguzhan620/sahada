using System.Security.Claims;
using Sahada.Api.Common;
using Sahada.Api.Features.Companies;
using Sahada.Api.Features.Customers;
using Sahada.Api.Features.Dispatch;
using Sahada.Core;
using Sahada.Core.Companies;
using Sahada.Core.Dispatching;
using Sahada.Core.WorkOrders;
using Sahada.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Sahada.Api.Features.WorkOrders;

internal static class WorkOrderEndpoints
{
    public static IEndpointRouteBuilder MapWorkOrderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/companies/{companyId:guid}/work-orders").WithTags("Work orders").RequireAuthorization();
        group.MapGet("/", List).WithSummary("Work orders, filtered and paged")
            .WithDescription("""
                Filters combine: `status` (repeat for several), `priority`, `customerId`, `technicianId`, `overdue`,
                `q` (code, title or customer). `sort` is `due` (default, soonest deadline first), `newest` or `number`.
                Technicians only ever see the jobs assigned to them.
                """);
        group.MapPost("/", Create).WithSummary("Open a work order (dispatchers)")
            .WithDescription("The deadline follows the priority: Urgent 4 hours, High 24 hours, Normal 72 hours, Low 7 days.");
        group.MapGet("/{workOrderId:guid}", Get).WithSummary("A work order with its checklist and history");
        group.MapPut("/{workOrderId:guid}", Edit).WithSummary("Change what the job is about (dispatchers)");
        group.MapPost("/{workOrderId:guid}/checklist", AddChecklistItem).WithSummary("Add a checklist item (dispatchers)");
        group.MapPut("/{workOrderId:guid}/checklist/{itemId:guid}", SetChecklistItem).WithSummary("Tick or untick a checklist item");
        group.MapPost("/{workOrderId:guid}/schedule", Schedule).WithSummary("Put the job in a technician's day (dispatchers)")
            .WithDescription("The technician needs the required skill, and the job has to fit in their shift without overlapping another one.");
        group.MapPost("/{workOrderId:guid}/unschedule", Unschedule).WithSummary("Take the job out of the schedule (dispatchers)");
        group.MapPost("/{workOrderId:guid}/status", ChangeStatus).WithSummary("Move the job along: EnRoute, InProgress, OnHold or Completed")
            .WithDescription("The assigned technician or a dispatcher. OnHold needs a `reason`; Completed needs a `resolution` and every checklist item ticked.");
        group.MapPost("/{workOrderId:guid}/cancel", Cancel).WithSummary("Cancel the job (dispatchers)");
        group.MapPost("/{workOrderId:guid}/notes", AddNote).WithSummary("Add a note to the history");
        return app;
    }

    private static async Task<Results<Ok<Page<WorkOrderSummary>>, ValidationProblem>> List(
        Guid companyId,
        string[]? status,
        Priority? priority,
        Guid? customerId,
        Guid? technicianId,
        bool? overdue,
        string? q,
        string? sort,
        int? page,
        int? pageSize,
        ClaimsPrincipal user,
        SahadaDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Technician, ct);
        var statuses = new List<WorkOrderStatus>();
        var validation = new Validation();
        foreach (var value in status ?? [])
        {
            foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                validation.Require(Enum.TryParse<WorkOrderStatus>(part, true, out var parsed) && Enum.IsDefined(parsed), "status", $"'{part}' is not a status.");
                statuses.Add(parsed);
            }
        }

        validation.Require(sort is null or "due" or "newest" or "number", "sort", "Sort by due, newest or number.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var now = clock.GetUtcNow();
        var (number, size) = Paging.Clamp(page, pageSize);
        var orders = db.WorkOrders.AsNoTracking().Where(w => w.CompanyId == companyId);
        if (!membership.Can(Role.Dispatcher))
        {
            orders = orders.Where(w => w.TechnicianId == membership.UserId);
        }

        if (statuses.Count > 0)
        {
            orders = orders.Where(w => statuses.Contains(w.Status));
        }

        if (priority is { } p)
        {
            orders = orders.Where(w => w.Priority == p);
        }

        if (customerId is { } customer)
        {
            orders = orders.Where(w => w.CustomerId == customer);
        }

        if (technicianId is { } technician)
        {
            orders = orders.Where(w => w.TechnicianId == technician);
        }

        if (overdue is { } late)
        {
            orders = late
                ? orders.Where(w => w.Status != WorkOrderStatus.Completed && w.Status != WorkOrderStatus.Cancelled && w.DueAt < now)
                : orders.Where(w => w.Status == WorkOrderStatus.Completed || w.Status == WorkOrderStatus.Cancelled || w.DueAt >= now);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var text = q.Trim();
            var pattern = $"%{Like.Escape(text)}%";
            var code = text.StartsWith("SO-", StringComparison.OrdinalIgnoreCase) ? text[3..] : text;
            var byNumber = int.TryParse(code, out var n) ? n : -1;
            orders = orders.Where(w => w.Number == byNumber
                || EF.Functions.ILike(w.Title, pattern, "\\")
                || db.Customers.Any(c => c.Id == w.CustomerId && EF.Functions.ILike(c.Name, pattern, "\\")));
        }

        var total = await orders.CountAsync(ct);
        orders = sort switch
        {
            "newest" => orders.OrderByDescending(w => w.CreatedAt).ThenByDescending(w => w.Number),
            "number" => orders.OrderBy(w => w.Number),
            _ => orders.OrderBy(w => w.DueAt).ThenBy(w => w.Number),
        };
        var items = await WorkOrderQueries.Summaries(db, orders.Skip((number - 1) * size).Take(size), now).ToListAsync(ct);
        return TypedResults.Ok(new Page<WorkOrderSummary>(items, total, number, size));
    }

    private static async Task<Created<WorkOrderResponse>> Create(
        Guid companyId, CreateWorkOrderRequest request, ClaimsPrincipal user, SahadaDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        await CustomerEndpoints.FindAsync(db, companyId, request.CustomerId, ct);
        if (!await db.Sites.AnyAsync(s => s.Id == request.SiteId && s.CustomerId == request.CustomerId, ct))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "The site does not belong to this customer.");
        }

        if (request.EquipmentId is { } equipmentId && !await db.Equipment.AnyAsync(e => e.Id == equipmentId && e.SiteId == request.SiteId, ct))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "The equipment is not at this site.");
        }

        if (!Enum.IsDefined(request.Priority))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A priority is Low, Normal, High or Urgent.");
        }

        var now = clock.GetUtcNow();
        var userId = user.UserId();
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // Numbers count up per company without gaps; the row lock this takes also orders two orders opened at once.
        var number = (await db.Database
            .SqlQuery<int>($"UPDATE companies SET next_work_order_number = next_work_order_number + 1 WHERE id = {companyId} RETURNING next_work_order_number - 1 AS \"Value\"")
            .ToListAsync(ct)).Single();
        var order = WorkOrder.Create(
            companyId, number, request.CustomerId, request.SiteId, request.EquipmentId, request.Title, request.Description,
            request.Priority, request.RequiredSkill, request.EstimatedMinutes, request.Checklist, userId, now);
        db.WorkOrders.Add(order);
        db.WorkOrderEvents.Add(WorkOrderEvent.Record(order, WorkOrderEventKind.Created, userId, now, now, $"{order.Priority} priority, due {order.DueAt:u}", to: order.Status));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.Created($"/api/companies/{companyId}/work-orders/{order.Id}", await WorkOrderQueries.DetailAsync(db, order.Id, now, ct));
    }

    private static async Task<Ok<WorkOrderResponse>> Get(Guid companyId, Guid workOrderId, ClaimsPrincipal user, SahadaDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Technician, ct);
        await FindAsync(db, companyId, workOrderId, membership, ct);
        return TypedResults.Ok(await WorkOrderQueries.DetailAsync(db, workOrderId, clock.GetUtcNow(), ct));
    }

    private static async Task<Ok<WorkOrderResponse>> Edit(
        Guid companyId, Guid workOrderId, EditWorkOrderRequest request, ClaimsPrincipal user, SahadaDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var order = await FindAsync(db, companyId, workOrderId, membership, ct);
        if (!Enum.IsDefined(request.Priority))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A priority is Low, Normal, High or Urgent.");
        }

        var now = clock.GetUtcNow();
        var before = (order.Title, order.Description, order.Priority, order.RequiredSkill, order.EstimatedMinutes);
        order.Edit(request.Title, request.Description, request.Priority, request.RequiredSkill, request.EstimatedMinutes, now);
        var changes = new List<string>();
        if (before.Title != order.Title) changes.Add("title");
        if (before.Description != order.Description) changes.Add("description");
        if (before.Priority != order.Priority) changes.Add($"priority {before.Priority} → {order.Priority}");
        if (before.RequiredSkill != order.RequiredSkill) changes.Add("required skill");
        if (before.EstimatedMinutes != order.EstimatedMinutes) changes.Add($"estimate {before.EstimatedMinutes} → {order.EstimatedMinutes} min");
        if (changes.Count > 0)
        {
            db.WorkOrderEvents.Add(WorkOrderEvent.Record(order, WorkOrderEventKind.Edited, membership.UserId, now, now, "Changed " + string.Join(", ", changes)));
        }

        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await WorkOrderQueries.DetailAsync(db, workOrderId, now, ct));
    }

    private static async Task<Ok<WorkOrderResponse>> AddChecklistItem(
        Guid companyId, Guid workOrderId, ChecklistItemRequest request, ClaimsPrincipal user, SahadaDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var order = await FindAsync(db, companyId, workOrderId, membership, ct, withChecklist: true);
        var now = clock.GetUtcNow();
        var item = order.AddChecklistItem(request.Text, now);
        db.WorkOrderEvents.Add(WorkOrderEvent.Record(order, WorkOrderEventKind.ChecklistChanged, membership.UserId, now, now, $"Added \"{item.Text}\""));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            // Someone added an item at the same moment and took this position.
            throw new DbUpdateConcurrencyException(ex.Message, ex);
        }
        return TypedResults.Ok(await WorkOrderQueries.DetailAsync(db, workOrderId, now, ct));
    }

    private static async Task<Ok<WorkOrderResponse>> SetChecklistItem(
        Guid companyId, Guid workOrderId, Guid itemId, SetChecklistItemRequest request, ClaimsPrincipal user, SahadaDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Technician, ct);
        var order = await FindAsync(db, companyId, workOrderId, membership, ct, withChecklist: true);
        RequireActor(order, membership);
        var now = clock.GetUtcNow();
        if (order.SetChecklistItem(itemId, request.Done, membership.UserId, now, now))
        {
            var item = order.Checklist.Single(i => i.Id == itemId);
            db.WorkOrderEvents.Add(WorkOrderEvent.Record(order, WorkOrderEventKind.ChecklistChanged, membership.UserId, now, now, $"{(item.Done ? "Ticked" : "Unticked")} \"{item.Text}\""));
            await db.SaveChangesAsync(ct);
        }

        return TypedResults.Ok(await WorkOrderQueries.DetailAsync(db, workOrderId, now, ct));
    }

    private static async Task<Ok<WorkOrderResponse>> Schedule(
        Guid companyId, Guid workOrderId, ScheduleRequest request, ClaimsPrincipal user, SahadaDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var technician = await db.Memberships.AsNoTracking()
            .Where(m => m.CompanyId == companyId && m.UserId == request.TechnicianId)
            .Join(db.Users, m => m.UserId, u => u.Id, (m, u) => u.DisplayName)
            .SingleOrDefaultAsync(ct)
            ?? throw new DomainException(ErrorCodes.ValidationFailed, "The technician is not on this company's team.");
        var profile = await db.TechnicianProfiles.AsNoTracking().SingleOrDefaultAsync(t => t.CompanyId == companyId && t.UserId == request.TechnicianId, ct)
            ?? throw new DomainException(ErrorCodes.NotInField, $"{technician} is not on the board yet; set up their technician profile first.");
        var zone = await DispatchData.ZoneAsync(db, companyId, ct);
        var start = request.Start.ToUniversalTime();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        // One booking per technician at a time: two dispatchers filling the same afternoon cannot both win it.
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"technician:" + request.TechnicianId}, 0))", ct);
        var order = await FindAsync(db, companyId, workOrderId, membership, ct);
        var bookings = await DispatchData.BookingsAsync(db, companyId, [request.TechnicianId], start, start.AddMinutes(order.EstimatedMinutes), order.Id, ct);
        if (Planner.Check(profile, bookings[request.TechnicianId].ToList(), order.RequiredSkill, start, order.EstimatedMinutes, zone) is { } problem)
        {
            throw problem;
        }

        var now = clock.GetUtcNow();
        var from = order.Status;
        order.Schedule(request.TechnicianId, start, now);
        var local = TimeZoneInfo.ConvertTime(start, zone);
        db.WorkOrderEvents.Add(WorkOrderEvent.Record(order, WorkOrderEventKind.Scheduled, membership.UserId, now, now, $"{technician}, {local:yyyy-MM-dd HH:mm}", from, order.Status));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.Ok(await WorkOrderQueries.DetailAsync(db, workOrderId, now, ct));
    }

    private static async Task<Ok<WorkOrderResponse>> Unschedule(
        Guid companyId, Guid workOrderId, ClaimsPrincipal user, SahadaDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var order = await FindAsync(db, companyId, workOrderId, membership, ct);
        var now = clock.GetUtcNow();
        order.Unschedule(now);
        db.WorkOrderEvents.Add(WorkOrderEvent.Record(order, WorkOrderEventKind.Unscheduled, membership.UserId, now, now, null, WorkOrderStatus.Scheduled, order.Status));
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await WorkOrderQueries.DetailAsync(db, workOrderId, now, ct));
    }

    private static async Task<Ok<WorkOrderResponse>> ChangeStatus(
        Guid companyId, Guid workOrderId, ChangeStatusRequest request, ClaimsPrincipal user, SahadaDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Technician, ct);
        var order = await FindAsync(db, companyId, workOrderId, membership, ct, withChecklist: true);
        RequireActor(order, membership);
        var now = clock.GetUtcNow();
        var from = order.Status;
        string? detail = null;
        switch (request.Status)
        {
            case WorkOrderStatus.EnRoute:
                order.SetOut(now);
                break;
            case WorkOrderStatus.InProgress:
                order.Start(now, now);
                break;
            case WorkOrderStatus.OnHold:
                order.Hold(request.Reason ?? string.Empty, now);
                detail = order.HoldReason;
                break;
            case WorkOrderStatus.Completed:
                order.Complete(request.Resolution ?? string.Empty, now, now);
                detail = order.Resolution;
                break;
            default:
                throw new DomainException(ErrorCodes.ValidationFailed, "Use schedule, unschedule or cancel for that; this moves a job to EnRoute, InProgress, OnHold or Completed.");
        }

        db.WorkOrderEvents.Add(WorkOrderEvent.Record(order, WorkOrderEventKind.StatusChanged, membership.UserId, now, now, detail, from, order.Status));
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await WorkOrderQueries.DetailAsync(db, workOrderId, now, ct));
    }

    private static async Task<Ok<WorkOrderResponse>> Cancel(
        Guid companyId, Guid workOrderId, CancelRequest request, ClaimsPrincipal user, SahadaDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var order = await FindAsync(db, companyId, workOrderId, membership, ct);
        var now = clock.GetUtcNow();
        var from = order.Status;
        order.Cancel(request.Reason, now);
        db.WorkOrderEvents.Add(WorkOrderEvent.Record(order, WorkOrderEventKind.StatusChanged, membership.UserId, now, now, order.CancelReason, from, order.Status));
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await WorkOrderQueries.DetailAsync(db, workOrderId, now, ct));
    }

    private static async Task<Ok<WorkOrderResponse>> AddNote(
        Guid companyId, Guid workOrderId, NoteRequest request, ClaimsPrincipal user, SahadaDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Technician, ct);
        var order = await FindAsync(db, companyId, workOrderId, membership, ct);
        RequireActor(order, membership);
        var now = clock.GetUtcNow();
        order.Touch(now);
        db.WorkOrderEvents.Add(WorkOrderEvent.Note(order, membership.UserId, request.Text, now, now));
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await WorkOrderQueries.DetailAsync(db, workOrderId, now, ct));
    }

    /// <summary>
    /// Finds an order of the company. Technicians only see their own jobs; for anyone else's the answer is
    /// "not found", the same as for an id that does not exist.
    /// </summary>
    internal static async Task<WorkOrder> FindAsync(SahadaDbContext db, Guid companyId, Guid workOrderId, Membership membership, CancellationToken ct, bool withChecklist = false)
    {
        IQueryable<WorkOrder> orders = withChecklist ? db.WorkOrders.Include(w => w.Checklist) : db.WorkOrders;
        var order = await orders.SingleOrDefaultAsync(w => w.Id == workOrderId && w.CompanyId == companyId, ct);
        if (order is null || (!membership.Can(Role.Dispatcher) && order.TechnicianId != membership.UserId))
        {
            throw new DomainException(ErrorCodes.NotFound, "There is no work order with this id.");
        }

        return order;
    }

    /// <summary>Doing the work is for the technician it is assigned to; dispatchers may act on their behalf.</summary>
    private static void RequireActor(WorkOrder order, Membership membership)
    {
        if (!membership.Can(Role.Dispatcher) && order.TechnicianId != membership.UserId)
        {
            throw new DomainException(ErrorCodes.NotAssigned, "This job is assigned to someone else.");
        }
    }
}
