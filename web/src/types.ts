export type Role = "Technician" | "Dispatcher" | "Owner";
export type Priority = "Low" | "Normal" | "High" | "Urgent";
export type Status = "New" | "Scheduled" | "EnRoute" | "InProgress" | "OnHold" | "Completed" | "Cancelled";
export type SlaState = "None" | "OnTrack" | "AtRisk" | "Overdue" | "Met" | "Breached";
export type DayOfWeek = "Sunday" | "Monday" | "Tuesday" | "Wednesday" | "Thursday" | "Friday" | "Saturday";

export interface User { id: string; email: string; displayName: string }
export interface Session { accessToken: string; expiresAt: string; user: User }

export interface Company {
  id: string; name: string; role: Role; timeZone: string; members: number; customers: number; openWorkOrders: number; createdAt: string;
}
export interface Me { user: User; companies: Company[] }
export interface Member { userId: string; displayName: string; email: string; role: Role; joinedAt: string }
export interface Page<T> { items: T[]; total: number; pageNumber: number; pageSize: number }

export interface CustomerSummary { id: string; name: string; phone: string; email: string; sites: number; openWorkOrders: number; createdAt: string }
export interface Equipment {
  id: string; siteId: string; kind: string; brand: string; model: string; serialNumber: string; installedOn: string | null; warrantyUntil: string | null;
}
export interface Site {
  id: string; customerId: string; label: string; address: string; city: string; latitude: number; longitude: number; accessNotes: string; equipment: Equipment[];
}
export interface Customer { id: string; name: string; phone: string; email: string; notes: string; sites: Site[]; createdAt: string }
export interface CustomerInput { name: string; phone: string; email: string; notes: string }
export interface SiteInput { label: string; address: string; city: string; latitude: number; longitude: number; accessNotes: string }
export interface EquipmentInput {
  kind: string; brand: string; model: string; serialNumber: string; installedOn: string | null; warrantyUntil: string | null;
}

export interface Part { id: string; sku: string; name: string; unitPrice: number; active: boolean }
export interface PartInput { sku: string; name: string; unitPrice: number; active: boolean }

export interface WorkOrderSummary {
  id: string; number: number; code: string; title: string; priority: Priority; status: Status; customerId: string; customerName: string;
  siteId: string; siteLabel: string; city: string; latitude: number; longitude: number; requiredSkill: string; technicianId: string | null;
  technicianName: string | null; scheduledStart: string | null; estimatedMinutes: number; dueAt: string; overdue: boolean; sla: SlaState; createdAt: string;
}
export interface ChecklistItem { id: string; position: number; text: string; done: boolean; changedAt: string | null }
export interface PartLine {
  id: string; partId: string; sku: string; name: string; quantity: number; unitPrice: number; total: number; addedAt: string; removed: boolean; flagged: boolean;
}
export interface Photo { id: string; contentType: string; sizeBytes: number; caption: string; uploadedAt: string; flagged: boolean }
export interface Signature { signedBy: string; signedAt: string; strokes: number[][][] }
export type EventKind =
  | "Created" | "Edited" | "StatusChanged" | "Scheduled" | "Unscheduled" | "Note" | "ChecklistChanged" | "PartUsed" | "PartRemoved" | "PhotoAdded"
  | "Signed" | "ResolutionSet";
export interface HistoryEntry {
  id: number; kind: EventKind; actorId: string; actorName: string; at: string; fromStatus: Status | null; toStatus: Status | null; detail: string; flagged: boolean;
}
export interface WorkOrder {
  id: string; number: number; code: string; title: string; description: string; priority: Priority; status: Status; requiredSkill: string;
  estimatedMinutes: number; dueAt: string; overdue: boolean; sla: SlaState;
  customer: { id: string; name: string; phone: string };
  site: { id: string; label: string; address: string; city: string; latitude: number; longitude: number; accessNotes: string };
  equipment: { id: string; kind: string; brand: string; model: string; serialNumber: string; warrantyUntil: string | null; underWarranty: boolean } | null;
  technicianId: string | null; technicianName: string | null; scheduledStart: string | null; scheduledEnd: string | null;
  startedAt: string | null; completedAt: string | null; holdReason: string; cancelReason: string; resolution: string;
  checklist: ChecklistItem[]; parts: PartLine[]; partsTotal: number; photos: Photo[]; signature: Signature | null; flagged: number;
  history: HistoryEntry[]; createdAt: string; updatedAt: string;
}
export interface WorkOrderInput {
  customerId: string; siteId: string; equipmentId: string | null; title: string; description: string; priority: Priority;
  requiredSkill: string; estimatedMinutes: number; checklist: string[];
}

export interface Technician {
  userId: string; displayName: string; role: Role; skills: string[]; baseLabel: string; baseLatitude: number; baseLongitude: number;
  shiftStart: string; shiftEnd: string; workDays: DayOfWeek[]; active: boolean;
}
export interface TechnicianInput {
  skills: string[]; baseLabel: string; baseLatitude: number; baseLongitude: number; shiftStart: string; shiftEnd: string; workDays: DayOfWeek[]; active: boolean;
}
export interface BoardTechnician {
  userId: string; displayName: string; skills: string[]; baseLabel: string; baseLatitude: number; baseLongitude: number;
  shiftStart: string | null; shiftEnd: string | null; bookedMinutes: number; loadPercent: number; jobs: WorkOrderSummary[];
}
export interface Board { date: string; timeZone: string; technicians: BoardTechnician[]; unscheduled: WorkOrderSummary[] }
export interface Sla { open: number; onTrack: number; atRisk: number; overdue: number; overdues: WorkOrderSummary[]; atRisks: WorkOrderSummary[] }
export interface Suggestion {
  technicianId: string; displayName: string; eligible: boolean; start: string | null; end: string | null; distanceKm: number | null;
  travelMinutes: number | null; loadPercent: number; meetsDeadline: boolean; score: number; reasons: string[];
}
export interface Suggestions { workOrderId: string; date: string; technicians: Suggestion[] }

export type OperationType = "StatusChanged" | "NoteAdded" | "ChecklistToggled" | "PartUsed" | "PartRemoved" | "ResolutionSet" | "Signature" | "PhotoAdded";
export type Outcome = "Applied" | "Flagged" | "Stale" | "Rejected";

/** One thing done on the device, waiting to be sent. Field use depends on the type, as in the API. */
export interface Operation {
  id: string; workOrderId: string; type: OperationType; at: string;
  status?: Status; reason?: string; resolution?: string; text?: string; itemId?: string; done?: boolean;
  lineId?: string; partId?: string; quantity?: number; attachmentId?: string; caption?: string; signedBy?: string; strokes?: number[][][];
}
export interface OperationResult { id: string; outcome: Outcome; message: string | null; duplicate: boolean }
export interface PushResult { results: OperationResult[]; serverTime: string }
export interface PullResult { cursor: string; serverTime: string; orders: WorkOrder[]; assigned: string[]; parts: Part[] | null }
