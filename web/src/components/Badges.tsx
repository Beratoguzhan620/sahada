import { AlertTriangle, Flag } from "lucide-react";
import { priorityTone, slaLabel, slaTone, statusLabel, statusTone } from "../format";
import type { Priority, SlaState, Status } from "../types";

export const StatusBadge = ({ status }: { status: Status }) => <span className={`badge ${statusTone[status]}`}>{statusLabel[status]}</span>;

export const PriorityBadge = ({ priority }: { priority: Priority }) => <span className={`badge ${priorityTone[priority]}`}>{priority}</span>;

export function SlaBadge({ sla }: { sla: SlaState }) {
  if (sla === "None" || sla === "OnTrack") return null;
  return (
    <span className={`badge ${slaTone[sla]}`}>
      {(sla === "Overdue" || sla === "AtRisk") && <AlertTriangle size={12} />}
      {slaLabel[sla]}
    </span>
  );
}

export const FlagBadge = ({ count, label = "for review" }: { count: number; label?: string }) =>
  count > 0 ? <span className="badge danger" title="Sent from a device after the job was cancelled or reassigned"><Flag size={12} />{count} {label}</span> : null;
