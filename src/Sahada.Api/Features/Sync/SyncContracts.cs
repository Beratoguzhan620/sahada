using Sahada.Api.Features.Parts;
using Sahada.Api.Features.WorkOrders;
using Sahada.Core.Sync;
using Sahada.Core.WorkOrders;

namespace Sahada.Api.Features.Sync;

/// <summary>
/// One thing a technician did, recorded on the device with an id it chose and the time by its clock. Which fields
/// are used depends on <c>type</c>:
/// StatusChanged: status, reason (OnHold), resolution (Completed) · NoteAdded: text · ChecklistToggled: itemId, done ·
/// PartUsed: lineId, partId, quantity · PartRemoved: lineId · ResolutionSet: text · Signature: signedBy, strokes ·
/// PhotoAdded: attachmentId, caption.
/// </summary>
public sealed record SyncOperationRequest(
    Guid Id,
    Guid WorkOrderId,
    OperationType Type,
    DateTimeOffset At,
    WorkOrderStatus? Status = null,
    string? Reason = null,
    string? Resolution = null,
    string? Text = null,
    Guid? ItemId = null,
    bool? Done = null,
    Guid? LineId = null,
    Guid? PartId = null,
    decimal? Quantity = null,
    Guid? AttachmentId = null,
    string? Caption = null,
    string? SignedBy = null,
    IReadOnlyList<IReadOnlyList<double[]>>? Strokes = null);

public sealed record PushRequest(IReadOnlyList<SyncOperationRequest> Operations);

/// <summary>What became of an operation. <c>duplicate</c> means it had been sent before; the answer is the one given then.</summary>
public sealed record OperationResult(Guid Id, OperationOutcome Outcome, string? Message, bool Duplicate);

public sealed record PushResponse(IReadOnlyList<OperationResult> Results, DateTimeOffset ServerTime);

/// <summary>
/// The jobs that changed since <c>cursor</c>, the ids of every job the device should hold (drop the rest), and on the
/// first pull the parts catalog. Pass the returned cursor next time.
/// </summary>
public sealed record PullResponse(string Cursor, DateTimeOffset ServerTime, IReadOnlyList<WorkOrderResponse> Orders, IReadOnlyList<Guid> Assigned, IReadOnlyList<PartResponse>? Parts);
