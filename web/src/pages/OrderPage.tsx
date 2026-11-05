import {
  ArrowLeft, CalendarClock, CalendarX2, CheckSquare, FileDown, Flag, MapPin, Pencil, Phone, Plus, Square, Wrench, XCircle,
} from "lucide-react";
import { useState, type FormEvent } from "react";
import { api } from "../api";
import { FlagBadge, PriorityBadge, SlaBadge, StatusBadge } from "../components/Badges";
import { Dialog } from "../components/Dialog";
import { MapView } from "../components/MapView";
import { PhotoThumb } from "../components/PhotoThumb";
import { PlanDialog } from "../components/PlanDialog";
import { SignatureView } from "../components/Signature";
import { Failed, Loading } from "../components/States";
import { useCompany } from "../context";
import { formatDayTime, formatMoney, formatTime, isOpen, minutes, relative, statusLabel } from "../format";
import { useLoad } from "../hooks";
import { linkProps } from "../router";
import type { HistoryEntry, Priority, WorkOrder } from "../types";

function describe(h: HistoryEntry): string {
  switch (h.kind) {
    case "Created": return "Opened the work order";
    case "Edited": return h.detail;
    case "Scheduled": return `Planned: ${h.detail}`;
    case "Unscheduled": return "Took it off the board";
    case "StatusChanged": return h.toStatus === "Cancelled" ? `Cancelled: ${h.detail}` : `${statusLabel[h.fromStatus!]} → ${statusLabel[h.toStatus!]}${h.detail ? `: ${h.detail}` : ""}`;
    case "Note": return h.detail;
    case "ChecklistChanged": return h.detail;
    case "PartUsed": return `Used ${h.detail}`;
    case "PartRemoved": return `Took back ${h.detail}`;
    case "PhotoAdded": return h.detail ? `Photo: ${h.detail}` : "Added a photo";
    case "Signed": return h.detail;
    case "ResolutionSet": return "Wrote up the work";
  }
}

function EditDialog({ order, onClose, onSaved }: { order: WorkOrder; onClose: () => void; onSaved: (o: WorkOrder) => void }) {
  const company = useCompany();
  const [form, setForm] = useState({ title: order.title, description: order.description, priority: order.priority, requiredSkill: order.requiredSkill, estimatedMinutes: order.estimatedMinutes });
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    try {
      onSaved(await api.editOrder(company.id, order.id, form));
    } catch (err) {
      setError((err as Error).message);
      setBusy(false);
    }
  }

  return (
    <Dialog title={`Edit ${order.code}`} onClose={onClose}>
      <form className="stack" onSubmit={submit}>
        <label className="field">Title<input className="input" value={form.title} onChange={(e) => setForm({ ...form, title: e.target.value })} /></label>
        <label className="field">Description<textarea className="textarea" value={form.description} onChange={(e) => setForm({ ...form, description: e.target.value })} /></label>
        <div className="grid-3">
          <label className="field">Priority<select className="select" value={form.priority} onChange={(e) => setForm({ ...form, priority: e.target.value as Priority })}>
            {(["Urgent", "High", "Normal", "Low"] as Priority[]).map((p) => <option key={p}>{p}</option>)}</select></label>
          <label className="field">Skill<input className="input" value={form.requiredSkill} onChange={(e) => setForm({ ...form, requiredSkill: e.target.value })} /></label>
          <label className="field">Minutes<input className="input" type="number" min={15} step={15} value={form.estimatedMinutes} onChange={(e) => setForm({ ...form, estimatedMinutes: Number(e.target.value) })} /></label>
        </div>
        {error && <p className="form-error" role="alert">{error}</p>}
        <div className="dialog-actions"><button type="button" className="button" onClick={onClose}>Close</button><button className="button primary" disabled={busy}>Save</button></div>
      </form>
    </Dialog>
  );
}

function CancelDialog({ order, onClose, onDone }: { order: WorkOrder; onClose: () => void; onDone: (o: WorkOrder) => void }) {
  const company = useCompany();
  const [reason, setReason] = useState("");
  const [error, setError] = useState<string | null>(null);
  return (
    <Dialog title={`Cancel ${order.code}?`} onClose={onClose}>
      <p className="muted small">The technician's app drops the job at its next sync. Anything they send for it afterwards is kept and marked for review.</p>
      <label className="field">Reason<input className="input" value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Customer fixed it themselves" autoFocus /></label>
      {error && <p className="form-error" role="alert">{error}</p>}
      <div className="dialog-actions">
        <button className="button" onClick={onClose}>Keep it</button>
        <button className="button danger" disabled={reason.trim().length < 3} onClick={() => api.cancel(company.id, order.id, reason).then(onDone, (e: Error) => setError(e.message))}>Cancel the job</button>
      </div>
    </Dialog>
  );
}

export function OrderPage({ orderId }: { orderId: string }) {
  const company = useCompany();
  const [order, reload, set] = useLoad(() => api.order(company.id, orderId), [company.id, orderId]);
  const [dialog, setDialog] = useState<"plan" | "edit" | "cancel" | null>(null);
  const [note, setNote] = useState("");
  const [step, setStep] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [downloading, setDownloading] = useState(false);

  if (order.state === "failed" && !order.data) return <Failed message={order.error} onRetry={reload} />;
  if (!order.data) return <Loading />;
  const o = order.data;
  const open = isOpen(o.status);

  const act = (work: Promise<WorkOrder>) => work.then(set, (e: Error) => setError(e.message));

  async function report() {
    setDownloading(true);
    try {
      const blob = await api.report(company.id, o.id);
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `${o.code}.pdf`;
      a.click();
      setTimeout(() => URL.revokeObjectURL(url), 1000);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setDownloading(false);
    }
  }

  return (
    <div className="order-page">
      <a className="back" {...linkProps(`/c/${company.id}/orders`)}><ArrowLeft size={14} />Work orders</a>
      <div className="page-head">
        <div className="min0">
          <span className="mono faint">{o.code}</span>
          <h1>{o.title}</h1>
          <div className="row wrap gap-6 head-badges">
            <StatusBadge status={o.status} /><PriorityBadge priority={o.priority} /><SlaBadge sla={o.sla} /><FlagBadge count={o.flagged} />
            <span className="small muted">Due {formatDayTime(o.dueAt)} ({relative(o.dueAt)})</span>
          </div>
        </div>
        <div className="row wrap gap-6">
          {open && o.status !== "EnRoute" && o.status !== "InProgress" && <button className="button primary" onClick={() => setDialog("plan")}><CalendarClock size={16} />{o.status === "Scheduled" ? "Reschedule" : "Plan"}</button>}
          {o.status === "Scheduled" && <button className="button" onClick={() => act(api.unschedule(company.id, o.id))}><CalendarX2 size={16} />Unplan</button>}
          {open && <button className="button" onClick={() => setDialog("edit")}><Pencil size={16} />Edit</button>}
          <button className="button" onClick={report} disabled={downloading}><FileDown size={16} />{downloading ? "Preparing…" : "Service report"}</button>
          {open && <button className="button danger" onClick={() => setDialog("cancel")}><XCircle size={16} />Cancel</button>}
        </div>
      </div>
      {error && <p className="form-error" role="alert">{error}</p>}
      {o.flagged > 0 && (
        <div className="notice warn"><Flag size={16} /><span>{o.flagged === 1 ? "One entry" : `${o.flagged} entries`} came from a device after the job was cancelled or given to someone else. They are kept and marked below.</span></div>
      )}

      <div className="order-layout">
        <div className="stack">
          <section className="card section facts">
            <div><span className="label">Customer</span><strong>{o.customer.name}</strong>{o.customer.phone && <a className="small" href={`tel:${o.customer.phone}`}><Phone size={12} /> {o.customer.phone}</a>}</div>
            <div><span className="label">Site</span><strong>{o.site.label}</strong><span className="small muted">{o.site.address}, {o.site.city}</span>{o.site.accessNotes && <span className="small">{o.site.accessNotes}</span>}</div>
            <div><span className="label">Equipment</span>{o.equipment ? <><strong>{[o.equipment.kind, o.equipment.brand, o.equipment.model].filter(Boolean).join(" ")}</strong><span className="small muted">{o.equipment.serialNumber && `S/N ${o.equipment.serialNumber} · `}{o.equipment.underWarranty ? "Under warranty" : "Out of warranty"}</span></> : <span className="faint">—</span>}</div>
            <div><span className="label">Technician</span>{o.technicianName ? <><strong>{o.technicianName}</strong>{o.scheduledStart && <span className="small muted">{formatDayTime(o.scheduledStart)}–{formatTime(o.scheduledEnd!)}</span>}</> : <span className="faint">Not planned</span>}</div>
            <div><span className="label">Estimate</span><strong>{minutes(o.estimatedMinutes)}</strong>{o.requiredSkill && <span className="small muted">Needs {o.requiredSkill}</span>}</div>
            <div><span className="label">Worked</span>{o.startedAt ? <strong>{formatDayTime(o.startedAt)}{o.completedAt && ` – ${formatTime(o.completedAt)}`}</strong> : <span className="faint">Not started</span>}</div>
          </section>

          {o.description && <section className="card section"><h2>Reported problem</h2><p className="pre">{o.description}</p></section>}
          {o.status === "OnHold" && o.holdReason && <div className="notice"><span>On hold: {o.holdReason}</span></div>}
          {o.status === "Cancelled" && <div className="notice warn"><span>Cancelled: {o.cancelReason}</span></div>}

          <section className="card section">
            <div className="row between"><h2>Checklist</h2><span className="small muted">{o.checklist.filter((i) => i.done).length} of {o.checklist.length} done</span></div>
            {o.checklist.length === 0 ? <p className="small muted">No steps.</p> : (
              <ul className="checklist">
                {o.checklist.map((i) => <li key={i.id} className={i.done ? "done" : ""}>{i.done ? <CheckSquare size={17} /> : <Square size={17} />}<span>{i.text}</span>{i.changedAt && <span className="small faint">{formatTime(i.changedAt)}</span>}</li>)}
              </ul>
            )}
            {open && (
              <form className="row" onSubmit={(e) => { e.preventDefault(); void act(api.addChecklistItem(company.id, o.id, step)).then(() => setStep("")); }}>
                <input className="input" value={step} onChange={(e) => setStep(e.target.value)} placeholder="Add a step" aria-label="New checklist step" />
                <button className="button" disabled={step.trim().length < 2}><Plus size={15} />Add</button>
              </form>
            )}
          </section>

          <section className="card section">
            <h2>Work done</h2>
            {o.resolution ? <p className="pre">{o.resolution}</p> : <p className="small muted">Nothing written up yet.</p>}
          </section>

          <section className="card section">
            <div className="row between"><h2>Parts</h2>{o.parts.length > 0 && <strong>{formatMoney(o.partsTotal)}</strong>}</div>
            {o.parts.length === 0 ? <p className="small muted">No parts used.</p> : (
              <table className="table compact">
                <thead><tr><th>Part</th><th className="right">Qty</th><th className="right">Unit</th><th className="right">Total</th></tr></thead>
                <tbody>
                  {o.parts.map((p) => (
                    <tr key={p.id} className={p.removed ? "removed" : ""}>
                      <td><span className="mono faint">{p.sku}</span> {p.name} {p.flagged && <Flag size={12} className="danger-text" aria-label="For review" />}</td>
                      <td className="right">{p.quantity}</td><td className="right nowrap">{formatMoney(p.unitPrice)}</td><td className="right nowrap">{p.removed ? "Taken back" : formatMoney(p.total)}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </section>

          {(o.photos.length > 0 || o.signature) && (
            <section className="card section">
              <h2>Photos and sign-off</h2>
              {o.photos.length > 0 && <div className="photo-grid">{o.photos.map((p) => <PhotoThumb key={p.id} alt={p.caption} flagged={p.flagged} load={() => api.photo(company.id, o.id, p.id)} />)}</div>}
              {o.signature && (
                <div className="signed">
                  <SignatureView strokes={o.signature.strokes} />
                  <span className="small muted">Signed by <strong>{o.signature.signedBy}</strong>, {formatDayTime(o.signature.signedAt)}</span>
                </div>
              )}
            </section>
          )}
        </div>

        <aside className="stack">
          <section className="card map-card-small">
            <MapView pins={[{ id: o.site.id, latitude: o.site.latitude, longitude: o.site.longitude, label: `${o.site.label}, ${o.site.address}`, text: "", tone: "accent" }]} height={200} />
            <a className="small row gap-4 map-link" href={`https://www.openstreetmap.org/?mlat=${o.site.latitude}&mlon=${o.site.longitude}#map=17/${o.site.latitude}/${o.site.longitude}`} target="_blank" rel="noreferrer"><MapPin size={13} />Open in OpenStreetMap</a>
          </section>
          <section className="card section">
            <h2>History</h2>
            <ol className="history">
              {[...o.history].reverse().map((h) => (
                <li key={h.id} className={`${h.kind === "Note" ? "note" : ""}${h.flagged ? " flagged" : ""}`}>
                  <span className="small faint">{formatDayTime(h.at)} · {h.actorName}{h.flagged && <> · <Flag size={11} /> for review</>}</span>
                  <span className={h.kind === "Note" ? "pre" : ""}>{describe(h)}</span>
                </li>
              ))}
            </ol>
            <form className="stack-8" onSubmit={(e) => { e.preventDefault(); void act(api.note(company.id, o.id, note)).then(() => setNote("")); }}>
              <textarea className="textarea short" value={note} onChange={(e) => setNote(e.target.value)} placeholder="A note for the technician and the file" aria-label="Note" />
              <div><button className="button small" disabled={!note.trim()}><Wrench size={14} />Add note</button></div>
            </form>
          </section>
        </aside>
      </div>

      {dialog === "plan" && <PlanDialog order={o} onClose={() => setDialog(null)} onDone={() => { setDialog(null); reload(); }} />}
      {dialog === "edit" && <EditDialog order={o} onClose={() => setDialog(null)} onSaved={(saved) => { set(saved); setDialog(null); }} />}
      {dialog === "cancel" && <CancelDialog order={o} onClose={() => setDialog(null)} onDone={(saved) => { set(saved); setDialog(null); }} />}
    </div>
  );
}
