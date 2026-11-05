import type { HistoryEntry, Operation, Part, PullResult, Status, WorkOrder } from "../types";

export interface Me { id: string; name: string }

/** The next steps a technician can take from a status, in the order the buttons show them. */
export function nextSteps(status: Status): Status[] {
  switch (status) {
    case "Scheduled": return ["EnRoute", "InProgress"];
    case "EnRoute": return ["InProgress"];
    case "InProgress": return ["Completed", "OnHold"];
    case "OnHold": return ["InProgress"];
    default: return [];
  }
}

/** Why the job cannot be completed yet, or null when it can. */
export function completionBlocker(order: WorkOrder, resolution: string): string | null {
  const open = order.checklist.filter((i) => !i.done).length;
  if (open > 0) return open === 1 ? "One checklist item is not done yet." : `${open} checklist items are not done yet.`;
  if ((resolution.trim() || order.resolution.trim()).length < 3) return "Say in a few words what was done.";
  return null;
}

const round2 = (n: number) => Math.round(n * 100) / 100;

function withTotals(order: WorkOrder): WorkOrder {
  return { ...order, partsTotal: round2(order.parts.filter((p) => !p.removed).reduce((sum, p) => sum + p.total, 0)) };
}

function entry(op: Operation, me: Me, kind: HistoryEntry["kind"], detail: string, from: Status | null = null, to: Status | null = null): HistoryEntry {
  // Negative ids mark lines that exist only on this device until the next sync.
  return { id: -Date.parse(op.at) - Math.random(), kind, actorId: me.id, actorName: me.name, at: op.at, fromStatus: from, toStatus: to, detail, flagged: false };
}

/**
 * What a job looks like on the device after an operation, before the server has seen it. The server's answer
 * replaces this on the next sync, so anything it refused simply disappears.
 */
export function applyLocal(order: WorkOrder, op: Operation, me: Me, catalog: Part[]): WorkOrder {
  switch (op.type) {
    case "StatusChanged": {
      const to = op.status!;
      const next: WorkOrder = { ...order, status: to, history: [...order.history, entry(op, me, "StatusChanged", op.reason ?? op.resolution ?? "", order.status, to)] };
      if (to === "InProgress") { next.startedAt = order.startedAt ?? op.at; next.holdReason = ""; }
      if (to === "OnHold") next.holdReason = op.reason ?? "";
      if (to === "Completed") { next.completedAt = op.at; next.resolution = op.resolution?.trim() || order.resolution; }
      return next;
    }
    case "NoteAdded":
      return { ...order, history: [...order.history, entry(op, me, "Note", op.text ?? "")] };
    case "ChecklistToggled":
      return {
        ...order,
        checklist: order.checklist.map((i) => (i.id === op.itemId && (!i.changedAt || Date.parse(i.changedAt) < Date.parse(op.at)) ? { ...i, done: !!op.done, changedAt: op.at } : i)),
      };
    case "PartUsed": {
      if (order.parts.some((p) => p.id === op.lineId)) return order;
      const part = catalog.find((p) => p.id === op.partId);
      if (!part) return order;
      const quantity = op.quantity ?? 1;
      const line = { id: op.lineId!, partId: part.id, sku: part.sku, name: part.name, quantity, unitPrice: part.unitPrice, total: round2(quantity * part.unitPrice), addedAt: op.at, removed: false, flagged: false };
      return withTotals({ ...order, parts: [...order.parts, line] });
    }
    case "PartRemoved":
      return withTotals({ ...order, parts: order.parts.map((p) => (p.id === op.lineId ? { ...p, removed: true } : p)) });
    case "ResolutionSet":
      return { ...order, resolution: op.text ?? "" };
    case "Signature":
      return { ...order, signature: { signedBy: op.signedBy ?? "", signedAt: op.at, strokes: op.strokes ?? [] } };
    case "PhotoAdded":
      if (order.photos.some((p) => p.id === op.attachmentId)) return order;
      return { ...order, photos: [...order.photos, { id: op.attachmentId!, contentType: "image/jpeg", sizeBytes: 0, caption: op.caption ?? "", uploadedAt: op.at, flagged: false }] };
  }
}

/** The jobs as the device shows them: the server's copy with the operations not yet sent laid on top, in order. */
export function view(base: WorkOrder[], queue: Operation[], me: Me, catalog: Part[]): WorkOrder[] {
  return base.map((order) => queue.filter((op) => op.workOrderId === order.id).reduce((o, op) => applyLocal(o, op, me, catalog), order));
}

/**
 * Folds a pull into the device's copy: changed jobs are replaced, jobs no longer assigned are dropped (unless work on
 * them is still waiting to be sent, so it is not lost before the server has seen it).
 */
export function mergePull(base: WorkOrder[], pull: PullResult, pending: Set<string>): WorkOrder[] {
  const changed = new Map(pull.orders.map((o) => [o.id, o]));
  const assigned = new Set(pull.assigned);
  const kept = base.filter((o) => assigned.has(o.id) || pending.has(o.id)).map((o) => changed.get(o.id) ?? o);
  const known = new Set(kept.map((o) => o.id));
  return [...kept, ...pull.orders.filter((o) => !known.has(o.id))];
}

/** Today's jobs first, by start time; then the rest; finished ones last. */
export function agenda(orders: WorkOrder[]): WorkOrder[] {
  const rank = (o: WorkOrder) => (o.status === "Completed" || o.status === "Cancelled" ? 1 : 0);
  return [...orders].sort((a, b) => rank(a) - rank(b) || (a.scheduledStart ?? "9").localeCompare(b.scheduledStart ?? "9") || a.number - b.number);
}

/** Drops points closer than `min` to the previous one: a signature stays smooth at a fraction of the size. */
export function thin(points: number[][], min = 0.004): number[][] {
  const out: number[][] = [];
  for (const p of points) {
    const last = out[out.length - 1];
    if (!last || Math.hypot(p[0]! - last[0]!, p[1]! - last[1]!) >= min) out.push([Math.round(p[0]! * 1000) / 1000, Math.round(p[1]! * 1000) / 1000]);
  }
  const final = points[points.length - 1];
  if (final && out.length === 1) out.push([Math.round(final[0]! * 1000) / 1000, Math.round(final[1]! * 1000) / 1000]);
  return out;
}

export const newId = () => crypto.randomUUID();
