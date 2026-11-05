import { describe, expect, it } from "vitest";
import type { Operation, Part, PullResult, WorkOrder } from "../types";
import { agenda, applyLocal, completionBlocker, mergePull, nextSteps, thin, view } from "./local";

const me = { id: "u1", name: "Ali Usta" };
const valve: Part = { id: "p1", sku: "VLV-22", name: "Safety valve", unitPrice: 249.9, active: true };

function order(overrides: Partial<WorkOrder> = {}): WorkOrder {
  return {
    id: "o1", number: 1001, code: "SO-01001", title: "Boiler leaking", description: "", priority: "High", status: "Scheduled", requiredSkill: "hvac",
    estimatedMinutes: 90, dueAt: "2026-11-06T09:00:00Z", overdue: false, sla: "OnTrack",
    customer: { id: "c1", name: "Atakum Apartmanı", phone: "" },
    site: { id: "s1", label: "Boiler room", address: "Cumhuriyet Cd. 12", city: "Samsun", latitude: 41.33, longitude: 36.27, accessNotes: "" },
    equipment: null, technicianId: "u1", technicianName: "Ali Usta", scheduledStart: "2026-11-05T07:00:00Z", scheduledEnd: "2026-11-05T08:30:00Z",
    startedAt: null, completedAt: null, holdReason: "", cancelReason: "", resolution: "",
    checklist: [{ id: "i1", position: 1, text: "Measure pressure", done: false, changedAt: null }],
    parts: [], partsTotal: 0, photos: [], signature: null, flagged: 0, history: [], createdAt: "2026-11-05T06:00:00Z", updatedAt: "2026-11-05T06:00:00Z",
    ...overrides,
  };
}

const op = (o: Partial<Operation> & Pick<Operation, "type">): Operation => ({ id: crypto.randomUUID(), workOrderId: "o1", at: "2026-11-05T07:30:00.000Z", ...o });

describe("applyLocal", () => {
  it("moves the job along and keeps the first start time", () => {
    let o = applyLocal(order(), op({ type: "StatusChanged", status: "InProgress" }), me, []);
    o = applyLocal(o, op({ type: "StatusChanged", status: "OnHold", reason: "Waiting for a part", at: "2026-11-05T08:00:00.000Z" }), me, []);
    o = applyLocal(o, op({ type: "StatusChanged", status: "InProgress", at: "2026-11-05T09:00:00.000Z" }), me, []);
    expect(o.status).toBe("InProgress");
    expect(o.startedAt).toBe("2026-11-05T07:30:00.000Z");
    expect(o.holdReason).toBe("");
    expect(o.history.map((h) => h.toStatus)).toEqual(["InProgress", "OnHold", "InProgress"]);
  });

  it("completes with the resolution written while working", () => {
    let o = applyLocal(order({ status: "InProgress" }), op({ type: "ResolutionSet", text: "Valve replaced." }), me, []);
    o = applyLocal(o, op({ type: "StatusChanged", status: "Completed" }), me, []);
    expect(o.resolution).toBe("Valve replaced.");
    expect(o.completedAt).toBe("2026-11-05T07:30:00.000Z");
  });

  it("ticks checklist items, last change by time wins", () => {
    let o = applyLocal(order(), op({ type: "ChecklistToggled", itemId: "i1", done: true, at: "2026-11-05T07:40:00.000Z" }), me, []);
    o = applyLocal(o, op({ type: "ChecklistToggled", itemId: "i1", done: false, at: "2026-11-05T07:35:00.000Z" }), me, []);
    expect(o.checklist[0]!.done).toBe(true);
  });

  it("prices parts from the catalog and drops taken-back ones from the total", () => {
    let o = applyLocal(order(), op({ type: "PartUsed", lineId: "l1", partId: "p1", quantity: 2 }), me, [valve]);
    o = applyLocal(o, op({ type: "PartUsed", lineId: "l1", partId: "p1", quantity: 2 }), me, [valve]);
    expect(o.parts).toHaveLength(1);
    expect(o.partsTotal).toBe(499.8);
    o = applyLocal(o, op({ type: "PartRemoved", lineId: "l1" }), me, [valve]);
    expect(o.partsTotal).toBe(0);
    expect(o.parts[0]!.removed).toBe(true);
  });

  it("ignores a part that is not in the catalog", () => {
    expect(applyLocal(order(), op({ type: "PartUsed", lineId: "l1", partId: "missing" }), me, [valve]).parts).toEqual([]);
  });

  it("adds notes, photos and the signature", () => {
    let o = applyLocal(order(), op({ type: "NoteAdded", text: "Rusty valve" }), me, []);
    o = applyLocal(o, op({ type: "PhotoAdded", attachmentId: "a1", caption: "Before" }), me, []);
    o = applyLocal(o, op({ type: "Signature", signedBy: "Hasan", strokes: [[[0, 0], [1, 1]]] }), me, []);
    expect(o.history.at(-1)).toMatchObject({ kind: "Note", detail: "Rusty valve", actorName: "Ali Usta" });
    expect(o.history.at(-1)!.id).toBeLessThan(0);
    expect(o.photos.map((p) => p.caption)).toEqual(["Before"]);
    expect(o.signature?.signedBy).toBe("Hasan");
  });
});

describe("the device's copy", () => {
  it("lays queued work over the server's copy of each job, in order", () => {
    const queue = [op({ type: "StatusChanged", status: "InProgress" }), op({ type: "ChecklistToggled", itemId: "i1", done: true }), op({ type: "NoteAdded", workOrderId: "other", text: "x" })];
    const [o] = view([order()], queue, me, []);
    expect(o!.status).toBe("InProgress");
    expect(o!.checklist[0]!.done).toBe(true);
    expect(o!.history).toHaveLength(1);
  });

  it("replaces changed jobs, adds new ones and drops reassigned ones unless work for them is waiting", () => {
    const pull = (orders: WorkOrder[], assigned: string[]): PullResult => ({ cursor: "9", serverTime: "", orders, assigned, parts: null });
    const base = [order({ id: "a" }), order({ id: "b" }), order({ id: "c" })];
    const merged = mergePull(base, pull([order({ id: "a", title: "Changed" }), order({ id: "d" })], ["a", "d"]), new Set(["c"]));
    expect(merged.map((o) => o.id)).toEqual(["a", "c", "d"]);
    expect(merged[0]!.title).toBe("Changed");
  });

  it("lists open jobs by start time before finished ones", () => {
    const list = agenda([
      order({ id: "done", status: "Completed", scheduledStart: "2026-11-05T05:00:00Z" }),
      order({ id: "late", scheduledStart: "2026-11-05T12:00:00Z" }),
      order({ id: "early", scheduledStart: "2026-11-05T06:00:00Z" }),
    ]);
    expect(list.map((o) => o.id)).toEqual(["early", "late", "done"]);
  });
});

describe("rules on the device", () => {
  it("offers the next steps for each status", () => {
    expect(nextSteps("Scheduled")).toEqual(["EnRoute", "InProgress"]);
    expect(nextSteps("InProgress")).toEqual(["Completed", "OnHold"]);
    expect(nextSteps("Completed")).toEqual([]);
  });

  it("says why a job cannot be completed yet", () => {
    expect(completionBlocker(order(), "Done it")).toBe("One checklist item is not done yet.");
    const ticked = order({ checklist: [{ id: "i1", position: 1, text: "x", done: true, changedAt: null }] });
    expect(completionBlocker(ticked, " ")).toBe("Say in a few words what was done.");
    expect(completionBlocker(ticked, "Valve replaced")).toBeNull();
  });

  it("thins signature strokes and rounds them", () => {
    const points = [[0.1, 0.1], [0.1001, 0.1001], [0.2, 0.2], [0.20001, 0.2]];
    expect(thin(points)).toEqual([[0.1, 0.1], [0.2, 0.2]]);
    expect(thin([[0.5, 0.5], [0.5, 0.5]])).toEqual([[0.5, 0.5], [0.5, 0.5]]);
  });
});
