namespace Sahada.Core.Parts;

/// <summary>Something a technician fits and the customer pays for: a valve, a filter, a fuse.</summary>
public sealed class Part
{
    public const int SkuMaxLength = 30;
    public const int NameMaxLength = 100;

    private Part()
    {
    }

    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>In kuruş, so sums are exact.</summary>
    public long UnitPriceMinor { get; private set; }

    /// <summary>Retired parts stay on old work orders but cannot be added to new ones.</summary>
    public bool Active { get; private set; }

    public static Part Create(Guid companyId, string sku, string name, decimal unitPrice, DateTimeOffset now)
    {
        var part = new Part { Id = Guid.CreateVersion7(now), CompanyId = companyId, Active = true };
        part.Update(sku, name, unitPrice, true);
        return part;
    }

    public void Update(string sku, string name, decimal unitPrice, bool active)
    {
        var clean = (sku ?? string.Empty).Trim().ToUpperInvariant();
        if (clean.Length is 0 or > SkuMaxLength || !clean.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.'))
        {
            throw new DomainException(ErrorCodes.ValidationFailed, $"A SKU has 1 to {SkuMaxLength} letters, digits, '-', '_' or '.'.");
        }

        if (unitPrice is < 0 or > 1_000_000 || decimal.Round(unitPrice, 2) != unitPrice)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A price is 0 to 1,000,000 TRY, to the kuruş.");
        }

        Sku = clean;
        Name = Text.Clean(name, 2, NameMaxLength, "A part's name");
        UnitPriceMinor = decimal.ToInt64(unitPrice * 100);
        Active = active;
    }
}
