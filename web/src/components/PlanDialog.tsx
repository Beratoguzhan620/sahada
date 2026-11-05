import { CalendarCheck, ChevronLeft, ChevronRight, MapPin, TriangleAlert } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api } from "../api";
import { useCompany } from "../context";
import { addDays, formatDate, formatTime, minutes, today, zonedInstant } from "../format";
import { useLoad } from "../hooks";
import type { Technician, WorkOrder, WorkOrderSummary } from "../types";
import { Dialog } from "./Dialog";
import { Failed, Loading } from "./States";

/**
 * Plans a job: the suggestions for a day, best first with the reasons, and a way to pick someone and a time by
 * hand. The server checks skill, shift and clashes either way.
 */
export function PlanDialog({ order, date: initial, onClose, onDone }: {
  order: WorkOrderSummary | WorkOrder; date?: string; onClose: () => void; onDone: () => void;
}) {
  const company = useCompany();
  const [date, setDate] = useState(initial ?? today());
  const [suggestions, reload] = useLoad(() => api.suggestions(company.id, order.id, date), [company.id, order.id, date]);
  const [team] = useLoad<Technician[]>(() => api.technicians(company.id), [company.id]);
  const [manual, setManual] = useState({ technicianId: "", time: "09:00" });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function book(technicianId: string, start: string) {
    setBusy(true);
    setError(null);
    try {
      await api.schedule(company.id, order.id, technicianId, start);
      onDone();
    } catch (e) {
      setError((e as Error).message);
      setBusy(false);
      reload();
    }
  }

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (manual.technicianId) void book(manual.technicianId, zonedInstant(date, manual.time));
  };

  return (
    <Dialog title={`Plan ${order.code}`} onClose={onClose} wide>
      <p className="muted small">{order.title} · {minutes(order.estimatedMinutes)}{order.requiredSkill && <> · needs <strong>{order.requiredSkill}</strong></>}</p>
      <div className="row between">
        <button type="button" className="icon-button" onClick={() => setDate(addDays(date, -1))} aria-label="Previous day"><ChevronLeft size={18} /></button>
        <strong>{formatDate(date)}</strong>
        <button type="button" className="icon-button" onClick={() => setDate(addDays(date, 1))} aria-label="Next day"><ChevronRight size={18} /></button>
      </div>
      {error && <p className="form-error" role="alert"><TriangleAlert size={16} />{error}</p>}
      {suggestions.state === "failed" && !suggestions.data ? <Failed message={suggestions.error} onRetry={reload} /> : !suggestions.data ? <Loading label="Working out who fits" /> : (
        <ol className="suggestions" aria-label="Suggested technicians">
          {suggestions.data.technicians.map((s, i) => (
            <li key={s.technicianId} className={s.eligible ? "" : "ineligible"}>
              <div className="suggestion-head">
                <span className={`score${i === 0 && s.eligible ? " best" : ""}`} aria-label={`Score ${s.score} of 100`}>{s.eligible ? s.score : "—"}</span>
                <span className="grow min0">
                  <strong className="block ellipsis">{s.displayName}</strong>
                  <span className="small muted">{s.reasons.join(" · ")}</span>
                </span>
                {s.eligible && s.start && (
                  <button type="button" className={`button small${i === 0 ? " primary" : ""}`} disabled={busy} onClick={() => book(s.technicianId, s.start!)}>
                    <CalendarCheck size={14} />Book {formatTime(s.start)}
                  </button>
                )}
              </div>
              {s.eligible && s.distanceKm !== null && (
                <div className="suggestion-bar" aria-hidden="true">
                  <span className="small faint"><MapPin size={12} />{s.distanceKm.toFixed(1)} km</span>
                  <span className="meter"><span style={{ width: `${Math.min(100, s.loadPercent)}%` }} /></span>
                  <span className="small faint">{s.loadPercent}% booked</span>
                </div>
              )}
            </li>
          ))}
          {suggestions.data.technicians.length === 0 && <li className="muted small">Nobody is on the board yet. Set up technician profiles under Team.</li>}
        </ol>
      )}
      <form className="manual" onSubmit={submit}>
        <span className="section-legend">Or choose yourself</span>
        <div className="row wrap gap-10">
          <select className="select grow" value={manual.technicianId} onChange={(e) => setManual({ ...manual, technicianId: e.target.value })} aria-label="Technician">
            <option value="">Technician…</option>
            {team.data?.filter((t) => t.active).map((t) => <option key={t.userId} value={t.userId}>{t.displayName}</option>)}
          </select>
          <input className="input time-input" type="time" step={300} value={manual.time} onChange={(e) => setManual({ ...manual, time: e.target.value })} aria-label="Start time" />
          <button className="button" disabled={busy || !manual.technicianId}>Book</button>
        </div>
      </form>
    </Dialog>
  );
}
