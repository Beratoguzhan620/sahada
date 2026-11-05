using System.Security.Claims;
using System.Security.Cryptography;
using Sahada.Api.Common;
using Sahada.Api.Features.Companies;
using Sahada.Api.Features.WorkOrders;
using Sahada.Core;
using Sahada.Core.Companies;
using Sahada.Core.Sync;
using Sahada.Core.WorkOrders;
using Sahada.Infrastructure.Persistence;
using Sahada.Infrastructure.Storage;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Sahada.Api.Features.Sync;

internal static class PhotoEndpoints
{
    public static IEndpointRouteBuilder MapPhotoEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/companies/{companyId:guid}/work-orders/{workOrderId:guid}/photos").WithTags("Sync").RequireAuthorization();
        group.MapPut("/{photoId:guid}", Upload).WithSummary("Upload a photo under an id the device chose")
            .WithDescription("""
                The body is the image itself: JPEG, PNG or WebP, at most 5 MB, recognised by its first bytes. Uploading the
                same bytes again under the same id is a no-op, so retries are safe. Send a `PhotoAdded` operation afterwards
                to caption it and put it in the job's history.
                """)
            .Accepts<Stream>("image/jpeg", "image/png", "image/webp");
        group.MapGet("/{photoId:guid}", Download).WithSummary("A photo's bytes");
        return app;
    }

    private static async Task<Results<Created<AttachmentResponse>, Ok<AttachmentResponse>>> Upload(
        Guid companyId, Guid workOrderId, Guid photoId, HttpRequest request, ClaimsPrincipal user, SahadaDbContext db, IFileStore files, TimeProvider clock, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Technician, ct);
        var order = await FindAsync(db, companyId, workOrderId, membership, ct);
        var (outcome, reason) = SyncRules.Admit(OperationType.PhotoAdded, order, membership.UserId, membership.Can(Role.Dispatcher));
        if (outcome == OperationOutcome.Rejected)
        {
            throw new DomainException(ErrorCodes.NotFound, reason ?? "There is no work order with this id.");
        }

        if (photoId == Guid.Empty)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A photo needs an id.");
        }

        var bytes = await ReadAsync(request.Body, Attachment.MaxBytes, ct);
        var type = Attachment.Sniff(bytes) ?? throw new DomainException(ErrorCodes.ValidationFailed, "Photos are JPEG, PNG or WebP.");
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));

        var existing = await db.Attachments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == photoId, ct);
        if (existing is not null)
        {
            return existing.WorkOrderId == workOrderId && existing.Sha256 == hash
                ? TypedResults.Ok(ToResponse(existing))
                : throw new DomainException(ErrorCodes.Conflict, "Another photo was already uploaded under this id.");
        }

        var now = clock.GetUtcNow();
        var photo = Attachment.Create(photoId, order, type, bytes.Length, hash, membership.UserId, now, outcome == OperationOutcome.Flagged);
        // Bytes first: a row never points at a file that is not there. A file without a row is harmless and is overwritten on retry.
        await files.PutAsync(photo.StorageKey, bytes, type, ct);
        db.Attachments.Add(photo);
        order.Touch(now);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation())
        {
            var raced = await db.Attachments.AsNoTracking().SingleAsync(a => a.Id == photoId, ct);
            return raced.Sha256 == hash ? TypedResults.Ok(ToResponse(raced)) : throw new DomainException(ErrorCodes.Conflict, "Another photo was already uploaded under this id.");
        }

        return TypedResults.Created($"/api/companies/{companyId}/work-orders/{workOrderId}/photos/{photoId}", ToResponse(photo));
    }

    private static async Task<FileContentHttpResult> Download(
        Guid companyId, Guid workOrderId, Guid photoId, ClaimsPrincipal user, SahadaDbContext db, IFileStore files, CancellationToken ct)
    {
        var membership = await CompanyAccess.RequireAsync(db, companyId, user, Role.Technician, ct);
        await FindAsync(db, companyId, workOrderId, membership, ct);
        var photo = await db.Attachments.AsNoTracking().SingleOrDefaultAsync(a => a.Id == photoId && a.WorkOrderId == workOrderId, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "This work order has no photo with this id.");
        var bytes = await files.GetAsync(photo.StorageKey, ct)
            ?? throw new DomainException(ErrorCodes.NotFound, "The photo's file is missing from storage.");
        return TypedResults.File(bytes, photo.ContentType, entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{photo.Sha256}\""));
    }

    /// <summary>The job, for its current technician, the one before (whose photos are kept, flagged) and dispatchers.</summary>
    internal static async Task<WorkOrder> FindAsync(SahadaDbContext db, Guid companyId, Guid workOrderId, Membership membership, CancellationToken ct)
    {
        var order = await db.WorkOrders.SingleOrDefaultAsync(w => w.Id == workOrderId && w.CompanyId == companyId, ct);
        if (order is null || !(membership.Can(Role.Dispatcher) || order.TechnicianId == membership.UserId || order.PreviousTechnicianId == membership.UserId))
        {
            throw new DomainException(ErrorCodes.NotFound, "There is no work order with this id.");
        }

        return order;
    }

    private static async Task<byte[]> ReadAsync(Stream body, long max, CancellationToken ct)
    {
        using var copy = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await body.ReadAsync(buffer, ct)) > 0)
        {
            if (copy.Length + read > max)
            {
                throw new DomainException(ErrorCodes.ValidationFailed, $"A photo is at most {max / 1024 / 1024} MB.");
            }

            copy.Write(buffer, 0, read);
        }

        return copy.Length > 0 ? copy.ToArray() : throw new DomainException(ErrorCodes.ValidationFailed, "The photo is empty.");
    }

    private static AttachmentResponse ToResponse(Attachment a) => new(a.Id, a.ContentType, a.SizeBytes, a.Caption, a.UploadedAt, a.Flagged);
}
