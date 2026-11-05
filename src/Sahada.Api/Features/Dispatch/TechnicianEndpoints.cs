using System.Security.Claims;
using Sahada.Api.Common;
using Sahada.Api.Features.Companies;
using Sahada.Core;
using Sahada.Core.Companies;
using Sahada.Core.Dispatching;
using Sahada.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Sahada.Api.Features.Dispatch;

public sealed record TechnicianRequest(
    IReadOnlyList<string>? Skills,
    string? BaseLabel,
    double BaseLatitude,
    double BaseLongitude,
    TimeOnly ShiftStart,
    TimeOnly ShiftEnd,
    IReadOnlyList<DayOfWeek>? WorkDays,
    bool? Active);

public sealed record TechnicianResponse(
    Guid UserId,
    string DisplayName,
    Role Role,
    IReadOnlyList<string> Skills,
    string BaseLabel,
    double BaseLatitude,
    double BaseLongitude,
    TimeOnly ShiftStart,
    TimeOnly ShiftEnd,
    IReadOnlyList<DayOfWeek> WorkDays,
    bool Active);

internal static class TechnicianEndpoints
{
    public static IEndpointRouteBuilder MapTechnicianEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/companies/{companyId:guid}/technicians").WithTags("Dispatch").RequireAuthorization();
        group.MapGet("/", List).WithSummary("Everyone who goes out on jobs, with skills, base and shift");
        group.MapPut("/{userId:guid}", Save).WithSummary("Put a team member on the board, or change their profile (dispatchers)")
            .WithDescription("Shift times are wall-clock times in the company's time zone. `active: false` takes someone off the board for a while.");
        return app;
    }

    private static async Task<Ok<List<TechnicianResponse>>> List(Guid companyId, ClaimsPrincipal user, SahadaDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Technician, ct);
        var rows = await (
            from t in db.TechnicianProfiles.AsNoTracking()
            join m in db.Memberships on new { t.CompanyId, t.UserId } equals new { m.CompanyId, m.UserId }
            join u in db.Users on t.UserId equals u.Id
            where t.CompanyId == companyId
            select new { Profile = t, u.DisplayName, m.Role })
            .ToListAsync(ct);
        return TypedResults.Ok(rows.OrderBy(r => r.DisplayName, StringComparer.CurrentCulture).Select(r => ToResponse(r.Profile, r.DisplayName, r.Role)).ToList());
    }

    private static async Task<Ok<TechnicianResponse>> Save(
        Guid companyId, Guid userId, TechnicianRequest request, ClaimsPrincipal user, SahadaDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var member = await (
            from m in db.Memberships.AsNoTracking()
            join u in db.Users on m.UserId equals u.Id
            where m.CompanyId == companyId && m.UserId == userId
            select new { m.Role, u.DisplayName })
            .SingleOrDefaultAsync(ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "This person is not on the team.");

        var now = clock.GetUtcNow();
        var profile = await db.TechnicianProfiles.SingleOrDefaultAsync(t => t.CompanyId == companyId && t.UserId == userId, ct);
        if (profile is null)
        {
            profile = TechnicianProfile.Create(companyId, userId, now);
            db.TechnicianProfiles.Add(profile);
        }

        profile.Update(
            request.Skills, request.BaseLabel, request.BaseLatitude, request.BaseLongitude, request.ShiftStart, request.ShiftEnd,
            request.WorkDays ?? profile.Days, request.Active ?? true, now);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            throw new DomainException(ErrorCodes.Conflict, "Someone else set up this profile at the same time. Reload and try again.");
        }

        return TypedResults.Ok(ToResponse(profile, member.DisplayName, member.Role));
    }

    private static TechnicianResponse ToResponse(TechnicianProfile t, string name, Role role) =>
        new(t.UserId, name, role, t.Skills, t.BaseLabel, t.BaseLatitude, t.BaseLongitude, t.ShiftStart, t.ShiftEnd, t.Days, t.Active);
}
