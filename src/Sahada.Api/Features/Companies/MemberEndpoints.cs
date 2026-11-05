using System.Security.Claims;
using Sahada.Api.Common;
using Sahada.Api.Features.Auth;
using Sahada.Core;
using Sahada.Core.Users;
using Sahada.Core.Companies;
using Sahada.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Sahada.Api.Features.Companies;

public sealed record AddMemberRequest(string Email, Role Role);

public sealed record ChangeRoleRequest(Role Role);

public sealed record MemberResponse(Guid UserId, string DisplayName, string Email, Role Role, DateTimeOffset JoinedAt);

internal static class MemberEndpoints
{
    public static IEndpointRouteBuilder MapMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/companies/{companyId:guid}/members").WithTags("Team").RequireAuthorization();
        group.MapGet("/", List).WithSummary("The team");
        group.MapPost("/", Add).WithSummary("Add someone who has an account (owners)")
            .WithDescription("`role` is `Technician`, `Dispatcher` or `Owner`.");
        group.MapPatch("/{userId:guid}", ChangeRole).WithSummary("Change a member's role (owners)");
        group.MapDelete("/{userId:guid}", Remove).WithSummary("Remove a member (owners), or leave (yourself)")
            .WithDescription("A company always keeps at least one owner.");
        return app;
    }

    private static async Task<Ok<List<MemberResponse>>> List(Guid companyId, ClaimsPrincipal user, SahadaDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Technician, ct);
        var members = await Members(db, companyId, null).ToListAsync(ct);
        return TypedResults.Ok(members.OrderByDescending(m => m.Role).ThenBy(m => m.DisplayName, StringComparer.CurrentCulture).ToList());
    }

    private static async Task<Results<Created<MemberResponse>, ValidationProblem>> Add(
        Guid companyId, AddMemberRequest request, ClaimsPrincipal user, SahadaDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Owner, ct);
        var validation = new Validation()
            .Require(AuthEndpoints.IsEmail(request.Email), "email", "A valid email address is required.")
            .Require(Enum.IsDefined(request.Role), "role", "A role is Technician, Dispatcher or Owner.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var email = User.NormalizeEmail(request.Email);
        var invitee = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Email == email, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "Nobody has an account with this email yet. Ask them to sign up, then add them.");
        if (await db.Memberships.AnyAsync(m => m.CompanyId == companyId && m.UserId == invitee.Id, ct))
        {
            throw new DomainException(ErrorCodes.Conflict, "This person is already on the team.");
        }

        var membership = Membership.Create(companyId, invitee.Id, request.Role, clock.GetUtcNow());
        db.Memberships.Add(membership);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            throw new DomainException(ErrorCodes.Conflict, "This person is already on the team.");
        }

        return TypedResults.Created($"/api/companies/{companyId}/members/{invitee.Id}",
            new MemberResponse(invitee.Id, invitee.DisplayName, invitee.Email, membership.Role, membership.JoinedAt));
    }

    private static async Task<Ok<MemberResponse>> ChangeRole(
        Guid companyId, Guid userId, ChangeRoleRequest request, ClaimsPrincipal user, SahadaDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Owner, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await CompanyAccess.LockAsync(db, companyId, ct);
        var membership = await db.Memberships.SingleOrDefaultAsync(m => m.CompanyId == companyId && m.UserId == userId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "This person is not on the team.");

        var owners = await db.Memberships.CountAsync(m => m.CompanyId == companyId && m.Role == Role.Owner, ct);
        var wasOwner = membership.Role == Role.Owner;
        membership.ChangeRole(request.Role);
        Company.EnsureAnOwnerRemains(owners - (wasOwner ? 1 : 0) + (membership.Role == Role.Owner ? 1 : 0));
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.Ok(await Members(db, companyId, userId).SingleAsync(ct));
    }

    private static async Task<NoContent> Remove(
        Guid companyId, Guid userId, ClaimsPrincipal user, SahadaDbContext db, CancellationToken ct)
    {
        // Anyone may leave; removing someone else takes an owner.
        await CompanyAccess.RequireAsync(db, companyId, user, userId == user.UserId() ? Role.Technician : Role.Owner, ct);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await CompanyAccess.LockAsync(db, companyId, ct);
        var membership = await db.Memberships.SingleOrDefaultAsync(m => m.CompanyId == companyId && m.UserId == userId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "This person is not on the team.");
        if (membership.Role == Role.Owner)
        {
            var owners = await db.Memberships.CountAsync(m => m.CompanyId == companyId && m.Role == Role.Owner, ct);
            Company.EnsureAnOwnerRemains(owners - 1);
        }

        db.Memberships.Remove(membership);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return TypedResults.NoContent();
    }

    private static IQueryable<MemberResponse> Members(SahadaDbContext db, Guid companyId, Guid? userId) =>
        from m in db.Memberships
        join u in db.Users on m.UserId equals u.Id
        where m.CompanyId == companyId && (userId == null || m.UserId == userId)
        select new MemberResponse(u.Id, u.DisplayName, u.Email, m.Role, m.JoinedAt);
}
