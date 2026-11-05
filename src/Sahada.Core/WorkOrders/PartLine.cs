using Sahada.Core.Parts;

namespace Sahada.Core.WorkOrders;

/// <summary>
/// A part fitted on a job. Lines are only ever added; taking one back marks it removed, so the record of what
/// happened on site stays whole. The name and price are copied from the catalog at the time.
/// </summary>
public sealed class PartLine
{
    private PartLine()
    {
    }

    /// <summary>Chosen by the device, so a line sent twice is recorded once.</summary>
    public Guid Id { get; private set; }

    public Guid CompanyId { get; private set; }

    public Guid WorkOrderId { get; private set; }

    public Guid PartId { get; private set; }

    public string Sku { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    /// <summary>Pieces, metres or litres, to two decimals.</summary>
    public decimal Quantity { get; private set; }

    public long UnitPriceMinor { get; private set; }

    public Guid AddedBy { get; private set; }

    public DateTimeOffset AddedAt { get; private set; }

    public DateTimeOffset? RemovedAt { get; private set; }

    /// <summary>Recorded on a job that was cancelled or moved to someone else in the meantime; a dispatcher should look.</summary>
    public bool Flagged { get; private set; }

    public long TotalMinor => RemovedAt is null ? decimal.ToInt64(decimal.Round(Quantity * UnitPriceMinor, 0, MidpointRounding.AwayFromZero)) : 0;

    public static PartLine Create(Guid id, WorkOrder order, Part part, decimal quantity, Guid by, DateTimeOffset at, bool flagged)
    {
        if (quantity is <= 0 or > 1000 || decimal.Round(quantity, 2) != quantity)
        {
            throw new DomainException(ErrorCodes.ValidationFailed, "A quantity is more than 0 and at most 1000, to two decimals.");
        }

        if (part.CompanyId != order.CompanyId)
        {
            throw new DomainException(ErrorCodes.NotFound, "There is no part with this id.");
        }

        return new PartLine
        {
            Id = id,
            CompanyId = order.CompanyId,
            WorkOrderId = order.Id,
            PartId = part.Id,
            Sku = part.Sku,
            Name = part.Name,
            Quantity = quantity,
            UnitPriceMinor = part.UnitPriceMinor,
            AddedBy = by,
            AddedAt = at,
            Flagged = flagged,
        };
    }

    /// <summary>Returns false when the line was already removed.</summary>
    public bool Remove(DateTimeOffset at, bool flagged)
    {
        if (RemovedAt is not null)
        {
            return false;
        }

        RemovedAt = at;
        Flagged |= flagged;
        return true;
    }
}
