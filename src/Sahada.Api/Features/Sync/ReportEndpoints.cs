using System.Security.Claims;
using Sahada.Api.Features.Companies;
using Sahada.Api.Features.Dispatch;
using Sahada.Api.Features.WorkOrders;
using Sahada.Core.Companies;
using Sahada.Core.WorkOrders;
using Sahada.Infrastructure.Persistence;
using Sahada.Infrastructure.Reports;
using Sahada.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Sahada.Api.Features.Sync;

internal static class ReportEndpoints
{
    public static IEndpointRouteBuilder MapReportEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/companies/{companyId:guid}/work-orders/{workOrderId:guid}/report", Report)
            .WithTags("Work orders")
            .RequireAuthorization()
            .WithSummary("The service report as a PDF: the job, the work done, parts, photos and the customer's signature")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf");
        return app;
    }

    private static async Task<FileContentHttpResult> Report(
        Guid companyId, Guid workOrderId, ClaimsPrincipal user, SahadaDbContext db, IFileStore files, TimeProvider clock, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Technician, ct);
        await WorkOrderEndpoints.FindAsync(db, companyId, workOrderId, membership, ct);
        var now = clock.GetUtcNow();
        var detail = await WorkOrderQueries.DetailAsync(db, workOrderId, now, ct);
        var company = await db.Companies.AsNoTracking().Where(c => c.Id == companyId).Select(c => c.Name).SingleAsync(ct);
        var zone = await DispatchData.ZoneAsync(db, companyId, ct);
        DateTime Local(DateTimeOffset value) => TimeZoneInfo.ConvertTime(value, zone).DateTime;

        var photos = new List<ReportPhoto>();
        foreach (var photo in detail.Photos.Where(p => p.ContentType == "image/jpeg").Take(4))
        {
            var key = await db.Attachments.AsNoTracking().Where(a => a.Id == photo.Id).Select(a => a.StorageKey).SingleAsync(ct);
            if (await files.GetAsync(key, ct) is { } bytes)
            {
                photos.Add(new ReportPhoto(bytes, photo.Caption));
            }
        }

        var equipment = detail.Equipment is { } e
            ? string.Join(" ", new[] { e.Kind, e.Brand, e.Model }.Where(x => x.Length > 0)) + (e.SerialNumber.Length > 0 ? $" · S/N {e.SerialNumber}" : string.Empty)
            : null;
        var parts = detail.Parts.Where(p => !p.Removed)
            .Select(p => new ReportPart(p.Sku, p.Name, p.Quantity, (long)(p.UnitPrice * 100), (long)(p.Total * 100)))
            .ToList();

        var pdf = ServiceReport.Render(new ServiceReportDocument(
            company,
            detail.Code,
            detail.Title,
            detail.Status switch { WorkOrderStatus.EnRoute => "En route", WorkOrderStatus.InProgress => "In progress", WorkOrderStatus.OnHold => "On hold", var s => s.ToString() },
            detail.Priority.ToString(),
            detail.Customer.Name,
            detail.Customer.Phone,
            detail.Site.Label,
            $"{detail.Site.Address}, {detail.Site.City}",
            equipment,
            detail.TechnicianName,
            Local(detail.CreatedAt),
            detail.StartedAt is { } started ? Local(started) : null,
            detail.CompletedAt is { } completed ? Local(completed) : null,
            detail.Description,
            detail.Checklist.Select(i => (i.Text, i.Done)).ToList(),
            detail.Resolution,
            parts,
            parts.Sum(p => p.TotalMinor),
            detail.Signature?.SignedBy ?? string.Empty,
            detail.Signature is { } signature ? Local(signature.SignedAt) : null,
            detail.Signature?.Strokes.Select(s => (IReadOnlyList<SignaturePoint>)s.Select(p => new SignaturePoint(p[0], p[1])).ToList()).ToList() ?? [],
            photos,
            Local(now)));
        return TypedResults.File(pdf, "application/pdf", $"{detail.Code}.pdf");
    }
}
