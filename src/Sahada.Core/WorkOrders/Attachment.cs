namespace Sahada.Core.WorkOrders;

/// <summary>A photo taken on a job. The bytes live in object storage under <see cref="StorageKey"/>.</summary>
public sealed class Attachment
{
    public const long MaxBytes = 5 * 1024 * 1024;
    public const int CaptionMaxLength = 200;

    public static readonly IReadOnlyDictionary<string, string> Extensions = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["image/jpeg"] = "jpg",
        ["image/png"] = "png",
        ["image/webp"] = "webp",
    };

    private Attachment()
    {
    }

    /// <summary>Chosen by the device, so an upload that is retried lands once.</summary>
    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid WorkOrderId { get; private set; }

    public string ContentType { get; private set; } = string.Empty;

    public long SizeBytes { get; private set; }

    /// <summary>Hex SHA-256 of the bytes; a retry with different bytes under the same id is refused.</summary>
    public string Sha256 { get; private set; } = string.Empty;

    public string StorageKey { get; private set; } = string.Empty;

    public string Caption { get; private set; } = string.Empty;

    public Guid UploadedBy { get; private set; }

    public DateTimeOffset UploadedAt { get; private set; }

    public bool Flagged { get; private set; }

    public static Attachment Create(Guid id, WorkOrder order, string contentType, long size, string sha256, Guid by, DateTimeOffset now, bool flagged)
    {
        if (!Extensions.TryGetValue(contentType, out var extension))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "Photos are JPEG, PNG or WebP.");
        }

        if (size is <= 0 or > MaxBytes)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A photo is at most {MaxBytes / 1024 / 1024} MB.");
        }

        return new Attachment
        {
            Id = id,
            CompanyId = order.CompanyId,
            WorkOrderId = order.Id,
            ContentType = contentType,
            SizeBytes = size,
            Sha256 = sha256,
            StorageKey = $"companies/{order.CompanyId:N}/work-orders/{order.Id:N}/{id:N}.{extension}",
            UploadedBy = by,
            UploadedAt = now,
            Flagged = flagged,
        };
    }

    public void SetCaption(string? caption, bool flagged)
    {
        Caption = string.IsNullOrWhiteSpace(caption) ? string.Empty : Text.Clean(caption, 1, CaptionMaxLength, "A caption");
        Flagged |= flagged;
    }

    /// <summary>Recognises the formats by their first bytes rather than trusting the declared type.</summary>
    public static string? Sniff(ReadOnlySpan<byte> head) => head switch
    {
        [0xFF, 0xD8, 0xFF, ..] => "image/jpeg",
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => "image/png",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => "image/webp",
        _ => null,
    };
}
