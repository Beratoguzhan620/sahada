using System.Security.Claims;
using Sahada.Api.Common;
using Sahada.Core.Companies;
using Sahada.Core.WorkOrders;
using Sahada.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Sahada.Api.Features.Companies;

public sealed record CreateCompanyRequest(string Name);

public sealed record CompanyResponse(Guid Id, string Name, Role Role, string TimeZone, int Members, int Customers, int OpenWorkOrders, DateTimeOffset CreatedAt);

internal static class CompanyEndpoints
{
    public static IEndpointRouteBuilder MapCompanyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/companies").WithTags("Companies").RequireAuthorization();
        group.MapGet("/", List).WithSummary("The companies you belong to");
        group.MapPost("/", Create).WithSummary("Create a company")
            .WithDescription("You become its owner and can add dispatchers and technicians.");
        group.MapGet("/{companyId:guid}", Get).WithSummary("One of your companies");
        group.MapPatch("/{companyId:guid}", Rename).WithSummary("Rename (owners)");
        return app;
    }

    internal static async Task<List<CompanyResponse>> ListForAsync(SahadaDbContext db, Guid userId, CancellationToken ct) =>
        await Query(db, userId, null).ToListAsync(ct);

    private static async Task<Ok<List<CompanyResponse>>> List(ClaimsPrincipal user, SahadaDbContext db, CancellationToken ct) =>
        TypedResults.Ok(await ListForAsync(db, user.UserId(), ct));

    private static async Task<Results<Created<CompanyResponse>, ValidationProblem>> Create(
        CreateCompanyRequest request, ClaimsPrincipal user, SahadaDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var validation = new Validation().Require(
            request.Name?.Trim().Length is >= Company.NameMinLength and <= Company.NameMaxLength,
            "name", $"A name has {Company.NameMinLength} to {Company.NameMaxLength} characters.");
        if (!validation.IsValid)
        {
            return validation.ToProblem();
        }

        var (company, owner) = Company.Create(request.Name!, user.UserId(), clock.GetUtcNow());
        db.Companies.Add(company);
        db.Memberships.Add(owner);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/companies/{company.Id}",
            new CompanyResponse(company.Id, company.Name, Role.Owner, company.TimeZone, 1, 0, 0, company.CreatedAt));
    }

    private static async Task<Ok<CompanyResponse>> Get(Guid companyId, ClaimsPrincipal user, SahadaDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Technician, ct);
        return TypedResults.Ok(await Query(db, user.UserId(), companyId).SingleAsync(ct));
    }

    private static async Task<Ok<CompanyResponse>> Rename(
        Guid companyId, CreateCompanyRequest request, ClaimsPrincipal user, SahadaDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Owner, ct);
        var company = await db.Companies.SingleAsync(c => c.Id == companyId, ct);
        company.Rename(request.Name);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await Query(db, user.UserId(), companyId).SingleAsync(ct));
    }

    private static IQueryable<CompanyResponse> Query(SahadaDbContext db, Guid userId, Guid? companyId) =>
        from m in db.Memberships
        join c in db.Companies on m.CompanyId equals c.Id
        where m.UserId == userId && (companyId == null || c.Id == companyId)
        orderby c.Name
        select new CompanyResponse(
            c.Id,
            c.Name,
            m.Role,
            c.TimeZone,
            db.Memberships.Count(x => x.CompanyId == c.Id),
            db.Customers.Count(x => x.CompanyId == c.Id),
            db.WorkOrders.Count(w => w.CompanyId == c.Id && w.Status != WorkOrderStatus.Completed && w.Status != WorkOrderStatus.Cancelled),
            c.CreatedAt);
}
