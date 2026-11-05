using System.Security.Claims;
using Sahada.Api.Common;
using Sahada.Api.Features.Companies;
using Sahada.Core;
using Sahada.Core.Companies;
using Sahada.Core.Customers;
using Sahada.Core.WorkOrders;
using Sahada.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Sahada.Api.Features.Customers;

public sealed record CustomerRequest(string Name, string? Phone, string? Email, string? Notes);

public sealed record SiteRequest(string Label, string Address, string City, double Latitude, double Longitude, string? AccessNotes);

public sealed record EquipmentRequest(string Kind, string? Brand, string? Model, string? SerialNumber, DateOnly? InstalledOn, DateOnly? WarrantyUntil);

public sealed record CustomerSummary(Guid Id, string Name, string Phone, string Email, int Sites, int OpenWorkOrders, DateTimeOffset CreatedAt);

public sealed record CustomerResponse(Guid Id, string Name, string Phone, string Email, string Notes, IReadOnlyList<SiteResponse> Sites, DateTimeOffset CreatedAt);

public sealed record SiteResponse(Guid Id, Guid CustomerId, string Label, string Address, string City, double Latitude, double Longitude, string AccessNotes, IReadOnlyList<EquipmentResponse> Equipment);

public sealed record EquipmentResponse(Guid Id, Guid SiteId, string Kind, string Brand, string Model, string SerialNumber, DateOnly? InstalledOn, DateOnly? WarrantyUntil);

internal static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/companies/{companyId:guid}/customers").WithTags("Customers").RequireAuthorization();
        group.MapGet("/", List).WithSummary("Customers, with search by name, phone or email");
        group.MapPost("/", Create).WithSummary("Add a customer (dispatchers)");
        group.MapGet("/{customerId:guid}", Get).WithSummary("A customer with their sites and equipment");
        group.MapPut("/{customerId:guid}", Update).WithSummary("Change a customer's details (dispatchers)");
        group.MapDelete("/{customerId:guid}", Delete).WithSummary("Remove a customer without work orders (dispatchers)");

        group.MapPost("/{customerId:guid}/sites", AddSite).WithSummary("Add a site: an address with map coordinates (dispatchers)");
        group.MapPut("/{customerId:guid}/sites/{siteId:guid}", UpdateSite).WithSummary("Change a site (dispatchers)");
        group.MapDelete("/{customerId:guid}/sites/{siteId:guid}", DeleteSite).WithSummary("Remove a site without work orders (dispatchers)");

        group.MapPost("/{customerId:guid}/sites/{siteId:guid}/equipment", AddEquipment).WithSummary("Register equipment at a site (dispatchers)");
        group.MapPut("/{customerId:guid}/sites/{siteId:guid}/equipment/{equipmentId:guid}", UpdateEquipment).WithSummary("Change equipment (dispatchers)");
        group.MapDelete("/{customerId:guid}/sites/{siteId:guid}/equipment/{equipmentId:guid}", DeleteEquipment).WithSummary("Remove equipment (dispatchers)");
        return app;
    }

    private static async Task<Ok<Page<CustomerSummary>>> List(
        Guid companyId, string? q, int? page, int? pageSize, ClaimsPrincipal user, SahadaDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Technician, ct);
        var (number, size) = Paging.Clamp(page, pageSize);
        var customers = db.Customers.AsNoTracking().Where(c => c.CompanyId == companyId);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var pattern = $"%{Like.Escape(q.Trim())}%";
            customers = customers.Where(c => EF.Functions.ILike(c.Name, pattern, "\\") || EF.Functions.ILike(c.Phone, pattern, "\\") || EF.Functions.ILike(c.Email, pattern, "\\"));
        }

        var total = await customers.CountAsync(ct);
        var items = await customers
            .OrderBy(c => c.Name).ThenBy(c => c.Id)
            .Skip((number - 1) * size).Take(size)
            .Select(c => new CustomerSummary(
                c.Id, c.Name, c.Phone, c.Email,
                db.Sites.Count(s => s.CustomerId == c.Id),
                db.WorkOrders.Count(w => w.CustomerId == c.Id && w.Status != WorkOrderStatus.Completed && w.Status != WorkOrderStatus.Cancelled),
                c.CreatedAt))
            .ToListAsync(ct);
        return TypedResults.Ok(new Page<CustomerSummary>(items, total, number, size));
    }

    private static async Task<Created<CustomerResponse>> Create(
        Guid companyId, CustomerRequest request, ClaimsPrincipal user, SahadaDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var customer = Customer.Create(companyId, request.Name, request.Phone, request.Email, request.Notes, clock.GetUtcNow());
        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/companies/{companyId}/customers/{customer.Id}", await LoadAsync(db, customer.Id, ct));
    }

    private static async Task<Ok<CustomerResponse>> Get(Guid companyId, Guid customerId, ClaimsPrincipal user, SahadaDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Technician, ct);
        await FindAsync(db, companyId, customerId, ct);
        return TypedResults.Ok(await LoadAsync(db, customerId, ct));
    }

    private static async Task<Ok<CustomerResponse>> Update(
        Guid companyId, Guid customerId, CustomerRequest request, ClaimsPrincipal user, SahadaDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var customer = await FindAsync(db, companyId, customerId, ct);
        customer.Update(request.Name, request.Phone, request.Email, request.Notes);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(await LoadAsync(db, customerId, ct));
    }

    private static async Task<NoContent> Delete(Guid companyId, Guid customerId, ClaimsPrincipal user, SahadaDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var customer = await FindAsync(db, companyId, customerId, ct);
        if (await db.WorkOrders.AnyAsync(w => w.CustomerId == customerId, ct))
        {
            throw new DomainException(ErrorCodes.Conflict, "This customer has work orders; their history stays.");
        }

        db.Customers.Remove(customer);
        await SaveReferencedAsync(db, "This customer has work orders; their history stays.", ct);
        return TypedResults.NoContent();
    }

    private static async Task<Created<SiteResponse>> AddSite(
        Guid companyId, Guid customerId, SiteRequest request, ClaimsPrincipal user, SahadaDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var customer = await FindAsync(db, companyId, customerId, ct);
        var site = Site.Create(customer, request.Label, request.Address, request.City, request.Latitude, request.Longitude, request.AccessNotes, clock.GetUtcNow());
        db.Sites.Add(site);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/companies/{companyId}/customers/{customerId}/sites/{site.Id}", ToResponse(site, []));
    }

    private static async Task<Ok<SiteResponse>> UpdateSite(
        Guid companyId, Guid customerId, Guid siteId, SiteRequest request, ClaimsPrincipal user, SahadaDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var site = await FindSiteAsync(db, companyId, customerId, siteId, ct);
        site.Update(request.Label, request.Address, request.City, request.Latitude, request.Longitude, request.AccessNotes);
        await db.SaveChangesAsync(ct);
        var equipment = await db.Equipment.AsNoTracking().Where(e => e.SiteId == siteId).OrderBy(e => e.Kind).ThenBy(e => e.Id).ToListAsync(ct);
        return TypedResults.Ok(ToResponse(site, equipment));
    }

    private static async Task<NoContent> DeleteSite(
        Guid companyId, Guid customerId, Guid siteId, ClaimsPrincipal user, SahadaDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var site = await FindSiteAsync(db, companyId, customerId, siteId, ct);
        if (await db.WorkOrders.AnyAsync(w => w.SiteId == siteId, ct))
        {
            throw new DomainException(ErrorCodes.Conflict, "This site has work orders; its history stays.");
        }

        db.Sites.Remove(site);
        await SaveReferencedAsync(db, "This site has work orders; its history stays.", ct);
        return TypedResults.NoContent();
    }

    private static async Task<Created<EquipmentResponse>> AddEquipment(
        Guid companyId, Guid customerId, Guid siteId, EquipmentRequest request, ClaimsPrincipal user, SahadaDbContext db, TimeProvider clock, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var site = await FindSiteAsync(db, companyId, customerId, siteId, ct);
        var equipment = Equipment.Create(site, request.Kind, request.Brand, request.Model, request.SerialNumber, request.InstalledOn, request.WarrantyUntil, clock.GetUtcNow());
        db.Equipment.Add(equipment);
        await db.SaveChangesAsync(ct);
        return TypedResults.Created($"/api/companies/{companyId}/customers/{customerId}/sites/{siteId}/equipment/{equipment.Id}", ToResponse(equipment));
    }

    private static async Task<Ok<EquipmentResponse>> UpdateEquipment(
        Guid companyId, Guid customerId, Guid siteId, Guid equipmentId, EquipmentRequest request, ClaimsPrincipal user, SahadaDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var equipment = await FindEquipmentAsync(db, companyId, customerId, siteId, equipmentId, ct);
        equipment.Update(request.Kind, request.Brand, request.Model, request.SerialNumber, request.InstalledOn, request.WarrantyUntil);
        await db.SaveChangesAsync(ct);
        return TypedResults.Ok(ToResponse(equipment));
    }

    private static async Task<NoContent> DeleteEquipment(
        Guid companyId, Guid customerId, Guid siteId, Guid equipmentId, ClaimsPrincipal user, SahadaDbContext db, CancellationToken ct)
    {
        await CompanyAccess.RequireAsync(db, companyId, user, Role.Dispatcher, ct);
        var equipment = await FindEquipmentAsync(db, companyId, customerId, siteId, equipmentId, ct);
        // Work orders keep their text; the link to the unit is cleared by the database.
        db.Equipment.Remove(equipment);
        await db.SaveChangesAsync(ct);
        return TypedResults.NoContent();
    }

    internal static async Task<Customer> FindAsync(SahadaDbContext db, Guid companyId, Guid customerId, CancellationToken ct) =>
        await db.Customers.SingleOrDefaultAsync(c => c.Id == customerId && c.CompanyId == companyId, ct)
        ?? throw new DomainException(ErrorCodes.NotFound, "There is no customer with this id.");

    private static async Task<Site> FindSiteAsync(SahadaDbContext db, Guid companyId, Guid customerId, Guid siteId, CancellationToken ct) =>
        await db.Sites.SingleOrDefaultAsync(s => s.Id == siteId && s.CustomerId == customerId && s.CompanyId == companyId, ct)
        ?? throw new DomainException(ErrorCodes.NotFound, "This customer has no site with this id.");

    private static async Task<Equipment> FindEquipmentAsync(SahadaDbContext db, Guid companyId, Guid customerId, Guid siteId, Guid equipmentId, CancellationToken ct)
    {
        await FindSiteAsync(db, companyId, customerId, siteId, ct);
        return await db.Equipment.SingleOrDefaultAsync(e => e.Id == equipmentId && e.SiteId == siteId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "This site has no equipment with this id.");
    }

    private static async Task SaveReferencedAsync(SahadaDbContext db, string message, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsForeignKeyViolation())
        {
            // A work order was opened between the check and the delete.
            throw new DomainException(ErrorCodes.Conflict, message);
        }
    }

    private static async Task<CustomerResponse> LoadAsync(SahadaDbContext db, Guid customerId, CancellationToken ct)
    {
        var customer = await db.Customers.AsNoTracking().SingleAsync(c => c.Id == customerId, ct);
        var sites = await db.Sites.AsNoTracking().Where(s => s.CustomerId == customerId).OrderBy(s => s.Label).ThenBy(s => s.Id).ToListAsync(ct);
        var siteIds = sites.Select(s => s.Id).ToList();
        var equipment = await db.Equipment.AsNoTracking().Where(e => siteIds.Contains(e.SiteId)).OrderBy(e => e.Kind).ThenBy(e => e.Id).ToListAsync(ct);
        return new CustomerResponse(
            customer.Id, customer.Name, customer.Phone, customer.Email, customer.Notes,
            sites.Select(s => ToResponse(s, equipment.Where(e => e.SiteId == s.Id))).ToList(),
            customer.CreatedAt);
    }

    private static SiteResponse ToResponse(Site s, IEnumerable<Equipment> equipment) =>
        new(s.Id, s.CustomerId, s.Label, s.Address, s.City, s.Latitude, s.Longitude, s.AccessNotes, equipment.Select(ToResponse).ToList());

    private static EquipmentResponse ToResponse(Equipment e) =>
        new(e.Id, e.SiteId, e.Kind, e.Brand, e.Model, e.SerialNumber, e.InstalledOn, e.WarrantyUntil);
}
