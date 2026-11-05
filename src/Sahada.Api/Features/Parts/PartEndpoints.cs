using System.Security.Claims;
using Sahada.Api.Common;
using Sahada.Api.Features.Companies;
using Sahada.Core;
using Sahada.Core.Companies;
using Sahada.Core.Parts;
using Sahada.Infrastructure.Persistence;
using Sahada.Infrastructure.Persistence.Configurations;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Sahada.Api.Features.Parts;

public sealed record PartRequest(string Sku, string Name, decimal UnitPrice, bool? Active);

public sealed record PartResponse(Guid Id, string Sku, string Name, decimal UnitPrice, bool Active);

internal static class PartEndpoints
{
    public static IEndpointRouteBuilder MapPartEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/companies/{companyId:guid}/parts").WithTags("Parts").RequireAuthorization();
        group.MapGet("/", List).WithSummary("The parts catalog, with search by SKU or name")
            .WithDescription("Retired parts are left out unless `includeRetired` is true.");
        group.MapPost("/", Create).WithSummary("Add a part (dispatchers)")
            .WithDescription("`unitPrice` is in TRY, to the kuruş. SKUs are unique within a company.");
        group.MapPut("/{partId:guid}", Update).WithSummary("Change a part, or retire it with `active: false` (dispatchers)");
        return app;
    }

    private static async Task<Ok<Page<PartResponse>>> List(
        Guid companyId, string? q, bool? includeRetired, int? page, int? pageSize, ClaimsPrincipal user, SahadaDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Technician, ct);
        var (number, size) = Paging.Clamp(page, pageSize);
        var parts = db.Parts.AsNoTracking().Where(p => p.CompanyId == companyId);
        if (includeRetired != true)
        {
            parts = parts.Where(p => p.Active);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{Like.Escape(q.Trim())}%";
            parts = parts.Where(p => EF.Functions.ILike(p.Sku, pattern, "\\") || EF.Functions.ILike(p.Name, pattern, "\\"));
        }

        var total = await parts.CountAsync(ct);
        var items = await parts.OrderBy(p => p.Sku).Skip((number - 1) * size).Take(size).ToListAsync(ct);
        return TypedResults.Ok(new Page<PartResponse>(items.Select(ToResponse).ToList(), total, number, size));
    }

    private static async Task<Created<PartResponse>> Create(
        Guid companyId, PartRequest request, ClaimsPrincipal user, SahadaDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var part = Part.Create(companyId, request.Sku, request.Name, request.UnitPrice, clock.GetUtcNow());
        if (request.Active == false)
        {
            part.Update(part.Sku, part.Name, request.UnitPrice, false);
        }

        db.Parts.Add(part);
        await SaveAsync(db, part.Sku, ct);
        return TypedResults.Created($"/api/companies/{companyId}/parts/{part.Id}", ToResponse(part));
    }

    private static async Task<Ok<PartResponse>> Update(
        Guid companyId, Guid partId, PartRequest request, ClaimsPrincipal user, SahadaDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var part = await db.Parts.SingleOrDefaultAsync(p => p.Id == partId && p.CompanyId == companyId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "There is no part with this id.");
        part.Update(request.Sku, request.Name, request.UnitPrice, request.Active ?? part.Active);
        await SaveAsync(db, part.Sku, ct);
        return TypedResults.Ok(ToResponse(part));
    }

    private static async Task SaveAsync(SahadaDbContext db, string sku, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(PartConfiguration.SkuIndex))
        {
            throw new DomainException(ErrorCodes.DuplicateSku, $"Another part already has the SKU {sku}.");
        }
    }

    internal static PartResponse ToResponse(Part p) => new(p.Id, p.Sku, p.Name, p.UnitPriceMinor / 100m, p.Active);
}
