import {
  ArrowLeft, Camera, CheckSquare, ChevronRight, CloudOff, Flag, LogOut, MapPin, Navigation, Phone, Plus, RefreshCw, Square, Trash2, X,
} from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { api } from "../api";
import { StatusBadge } from "../components/Badges";
import { PhotoThumb } from "../components/PhotoThumb";
import { Brand } from "../components/Shell";
import { SignaturePad, SignatureView } from "../components/Signature";
import { Empty, Loading } from "../components/States";
import { useAccount } from "../context";
import { formatDay, formatDayTime, formatMoney, formatTime, isOpen, relative, statusLabel } from "../format";
import { useOnline } from "../hooks";
import { linkProps, navigate } from "../router";
import type { Company, Status, WorkOrder } from "../types";
import { local } from "./db";
import { agenda, completionBlocker, newId, nextSteps } from "./local";
import { compress } from "./photo";
import { useTechStore, type TechStore } from "./store";

const stepLabel: Partial<Record<Status, string>> = { EnRoute: "On my way", InProgress: "Start work", OnHold: "Pause", Completed: "Complete" };

function SyncChip({ store }: { store: TechStore }) {
  const online = useOnline();
  const waiting = store.pending > 0 ? ` · ${store.pending} waiting` : "";
  const text = !online || store.state === "offline"
    ? `Offline${waiting}`
    : store.state === "syncing" ? "Syncing…" : store.state === "failed" ? "Sync failed" : store.lastSync ? `Synced ${relative(store.lastSync)}${waiting}` : "Not synced yet";
  return (
    <button className={`sync-chip ${!online || store.state === "offline" ? "off" : store.state}`} onClick={() => void store.sync()} disabled={store.state === "syncing"} aria-label={`${text}. Sync now`}>
      {!online || store.state === "offline" ? <CloudOff size={14} /> : <RefreshCw size={14} className={store.state === "syncing" ? "spin" : ""} />}
      <span>{text}</span>
    </button>
  );
}

function Notices({ store }: { store: TechStore }) {
  if (store.notices.length === 0) return null;
  return (
    <ul className="notices" aria-label="From the last sync">
      {store.notices.map((n) => (
        <li key={n.id} className={n.outcome === "Rejected" ? "bad" : ""}>
          <Flag size={14} />
          <span className="grow small">{store.orders.find((o) => o.id === n.workOrderId)?.code ?? "A job"}: {n.message}</span>
          <button className="icon-button" onClick={() => void store.dismiss(n.id)} aria-label="Dismiss"><X size={15} /></button>
        </li>
      ))}
    </ul>
  );
}

function Agenda({ store }: { store: TechStore }) {
  const orders = agenda(store.orders);
  if (!store.ready) return <Loading />;
  if (orders.length === 0) {
    return <Empty title={store.lastSync ? "No jobs for you" : "Waiting for the first sync"}><span className="small muted">Jobs the office plans for you appear here, and stay here without a connection.</span></Empty>;
  }
  let lastDay = "";
  return (
    <ul className="agenda">
      {orders.flatMap((o) => {
        const day = o.scheduledStart ? formatDay(o.scheduledStart) : "Not scheduled";
        const items = day !== lastDay ? [<li className="agenda-day" key={`d-${o.id}`}>{day}</li>] : [];
        lastDay = day;
        const pending = store.pendingFor(o.id);
        items.push(
          <li key={o.id}>
            <a className={`agenda-card${isOpen(o.status) ? "" : " closed"}`} {...linkProps(`/tech/${o.id}`)}>
              <span className="agenda-time">{o.scheduledStart ? formatTime(o.scheduledStart) : "—"}</span>
              <span className="grow min0">
                <strong className="block ellipsis">{o.title}</strong>
                <span className="small muted block ellipsis">{o.customer.name} · {o.site.address}</span>
                <span className="row gap-4 agenda-badges"><StatusBadge status={o.status} />{pending > 0 && <span className="badge info">{pending} to send</span>}</span>
              </span>
              <ChevronRight size={18} className="faint" />
            </a>
          </li>,
        );
        return items;
      })}
    </ul>
  );
}

function PartsBlock({ order, store, editable }: { order: WorkOrder; store: TechStore; editable: boolean }) {
  const [partId, setPartId] = useState("");
  const [quantity, setQuantity] = useState(1);
  const used = order.parts.filter((p) => !p.removed);
  return (
    <section className="tech-section">
      <div className="row between"><h3>Parts</h3>{used.length > 0 && <strong>{formatMoney(order.partsTotal)}</strong>}</div>
      <ul className="tech-parts">
        {used.map((p) => (
          <li key={p.id}>
            <span className="grow min0"><span className="block ellipsis">{p.quantity} × {p.name}</span><span className="mono faint">{p.sku}</span></span>
            <span className="nowrap">{formatMoney(p.total)}</span>
            {editable && <button className="icon-button" onClick={() => void store.record({ workOrderId: order.id, type: "PartRemoved", lineId: p.id })} aria-label={`Take back ${p.name}`}><Trash2 size={15} /></button>}
          </li>
        ))}
      </ul>
      {editable && (
        <div className="row gap-6">
          <select className="select grow" value={partId} onChange={(e) => setPartId(e.target.value)} aria-label="Part">
            <option value="">Add a part…</option>
            {store.parts.map((p) => <option key={p.id} value={p.id}>{p.sku} · {p.name}</option>)}
          </select>
          <input className="input qty" type="number" min={0.01} step={0.01} value={quantity} onChange={(e) => setQuantity(Number(e.target.value))} aria-label="Quantity" />
          <button className="button" disabled={!partId || quantity <= 0} onClick={() => { void store.record({ workOrderId: order.id, type: "PartUsed", lineId: newId(), partId, quantity }); setPartId(""); setQuantity(1); }}><Plus size={15} />Add</button>
        </div>
      )}
    </section>
  );
}

function LocalPhoto({ id, caption, companyId, orderId }: { id: string; caption: string; companyId: string; orderId: string }) {
  // A photo still on the device shows from there; once sent, from the server.
  return <PhotoThumb alt={caption} load={async () => (await local.photo(id)) ?? api.photo(companyId, orderId, id)} />;
}

function Job({ order, store, companyId }: { order: WorkOrder; store: TechStore; companyId: string }) {
  const [note, setNote] = useState("");
  const [resolution, setResolution] = useState(order.resolution);
  const [holdReason, setHoldReason] = useState("");
  const [holding, setHolding] = useState(false);
  const [signing, setSigning] = useState(false);
  const [signer, setSigner] = useState("");
  const [strokes, setStrokes] = useState<number[][][]>([]);
  const [problem, setProblem] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const online = useOnline();
  const editable = isOpen(order.status);
  const working = order.status === "InProgress" || order.status === "EnRoute" || order.status === "OnHold";
  useEffect(() => setResolution(order.resolution), [order.id]); // eslint-disable-line react-hooks/exhaustive-deps

  const step = async (to: Status) => {
    setProblem(null);
    if (to === "OnHold") return setHolding(true);
    if (to === "Completed") {
      const blocker = completionBlocker(order, resolution);
      if (blocker) return setProblem(blocker);
      if (resolution.trim() !== order.resolution) await store.record({ workOrderId: order.id, type: "ResolutionSet", text: resolution });
      return store.record({ workOrderId: order.id, type: "StatusChanged", status: "Completed", resolution });
    }
    return store.record({ workOrderId: order.id, type: "StatusChanged", status: to });
  };

  const addPhoto = async (file: File | undefined) => {
    if (!file) return;
    setBusy(true);
    try {
      const blob = await compress(file);
      await store.record({ workOrderId: order.id, type: "PhotoAdded", attachmentId: newId(), caption: "" }, blob);
    } catch (e) {
      setProblem((e as Error).message);
    } finally {
      setBusy(false);
    }
  };

  return (
    <article className="job-view">
      <a className="back" {...linkProps("/tech")}><ArrowLeft size={14} />My jobs</a>
      <header className="stack-8">
        <span className="mono faint">{order.code}</span>
        <h1>{order.title}</h1>
        <div className="row wrap gap-6"><StatusBadge status={order.status} />{order.scheduledStart && <span className="small muted">{formatDayTime(order.scheduledStart)}</span>}<span className="small muted">Due {relative(order.dueAt)}</span></div>
      </header>

      {order.status === "Cancelled" && <div className="notice warn">This job was cancelled: {order.cancelReason}</div>}

      <section className="tech-section where">
        <strong>{order.customer.name}</strong>
        <span>{order.site.label} · {order.site.address}, {order.site.city}</span>
        {order.site.accessNotes && <span className="small notice">{order.site.accessNotes}</span>}
        {order.equipment && <span className="small muted">{[order.equipment.kind, order.equipment.brand, order.equipment.model].filter(Boolean).join(" ")}{order.equipment.serialNumber && ` · S/N ${order.equipment.serialNumber}`}{order.equipment.underWarranty && " · under warranty"}</span>}
        <div className="row gap-6">
          {order.customer.phone && <a className="button small" href={`tel:${order.customer.phone}`}><Phone size={14} />Call</a>}
          <a className="button small" href={`https://www.openstreetmap.org/directions?to=${order.site.latitude}%2C${order.site.longitude}`} target="_blank" rel="noreferrer"><Navigation size={14} />Directions</a>
          <a className="button small" href={`https://www.openstreetmap.org/?mlat=${order.site.latitude}&mlon=${order.site.longitude}#map=17/${order.site.latitude}/${order.site.longitude}`} target="_blank" rel="noreferrer"><MapPin size={14} />Map</a>
        </div>
      </section>

      {order.description && <section className="tech-section"><h3>Reported</h3><p className="pre">{order.description}</p></section>}

      {nextSteps(order.status).length > 0 && (
        <section className="tech-actions">
          {nextSteps(order.status).map((s, i) => (
            <button key={s} className={`button wide${i === 0 ? " primary" : ""}`} onClick={() => void step(s)}>{stepLabel[s] ?? statusLabel[s]}</button>
          ))}
          {holding && (
            <form className="hold" onSubmit={(e) => { e.preventDefault(); void store.record({ workOrderId: order.id, type: "StatusChanged", status: "OnHold", reason: holdReason }); setHolding(false); setHoldReason(""); }}>
              <input className="input" value={holdReason} onChange={(e) => setHoldReason(e.target.value)} placeholder="Why? Waiting for a part…" autoFocus aria-label="Reason for pausing" />
              <button className="button" disabled={holdReason.trim().length < 3}>Pause</button>
            </form>
          )}
          {problem && <p className="form-error" role="alert">{problem}</p>}
        </section>
      )}

      <section className="tech-section">
        <h3>Checklist</h3>
        <ul className="tech-checklist">
          {order.checklist.map((i) => (
            <li key={i.id}>
              <button disabled={!editable} onClick={() => void store.record({ workOrderId: order.id, type: "ChecklistToggled", itemId: i.id, done: !i.done })} aria-pressed={i.done}>
                {i.done ? <CheckSquare size={22} className="accent" /> : <Square size={22} className="faint" />}<span>{i.text}</span>
              </button>
            </li>
          ))}
          {order.checklist.length === 0 && <li className="small muted">No steps for this job.</li>}
        </ul>
      </section>

      <PartsBlock order={order} store={store} editable={isOpen(order.status) || order.status === "Completed"} />

      <section className="tech-section">
        <div className="row between"><h3>Photos</h3>
          <label className={`button small${busy ? " disabled" : ""}`}><Camera size={14} />{busy ? "Saving…" : "Take a photo"}
            <input type="file" accept="image/*" capture="environment" className="sr-only" onChange={(e) => { void addPhoto(e.target.files?.[0]); e.target.value = ""; }} disabled={busy} />
          </label>
        </div>
        {order.photos.length > 0 ? <div className="photo-grid">{order.photos.map((p) => <LocalPhoto key={p.id} id={p.id} caption={p.caption} companyId={companyId} orderId={order.id} />)}</div> : <p className="small muted">None yet.</p>}
      </section>

      {(working || order.status === "Completed") && (
        <section className="tech-section">
          <h3>Work done</h3>
          <textarea className="textarea" value={resolution} onChange={(e) => setResolution(e.target.value)} disabled={order.status === "Completed"}
            onBlur={() => { if (working && resolution.trim() !== order.resolution.trim()) void store.record({ workOrderId: order.id, type: "ResolutionSet", text: resolution }); }}
            placeholder="What you found and what you did" aria-label="Work done" />
        </section>
      )}

      {(working || order.status === "Completed") && (
        <section className="tech-section">
          <h3>Customer sign-off</h3>
          {order.signature && !signing ? (
            <div className="signed"><SignatureView strokes={order.signature.strokes} /><span className="small muted">{order.signature.signedBy}, {formatDayTime(order.signature.signedAt)}</span>
              <button className="link small" onClick={() => setSigning(true)}>Sign again</button></div>
          ) : signing ? (
            <div className="stack-8">
              <input className="input" value={signer} onChange={(e) => setSigner(e.target.value)} placeholder="Customer's name" aria-label="Customer's name" />
              <SignaturePad onChange={setStrokes} />
              <div className="row gap-6">
                <button className="button primary" disabled={signer.trim().length < 2 || strokes.length === 0} onClick={() => { void store.record({ workOrderId: order.id, type: "Signature", signedBy: signer, strokes }); setSigning(false); }}>Save signature</button>
                <button className="button" onClick={() => setSigning(false)}>Cancel</button>
              </div>
            </div>
          ) : <button className="button" onClick={() => setSigning(true)}>Ask the customer to sign</button>}
        </section>
      )}

      <section className="tech-section">
        <h3>Notes</h3>
        <ol className="history compact">
          {order.history.filter((h) => h.kind === "Note").map((h) => <li key={h.id} className="note"><span className="small faint">{formatDayTime(h.at)} · {h.actorName}</span><span className="pre">{h.detail}</span></li>)}
        </ol>
        <form className="row gap-6" onSubmit={(e) => { e.preventDefault(); void store.record({ workOrderId: order.id, type: "NoteAdded", text: note }); setNote(""); }}>
          <input className="input" value={note} onChange={(e) => setNote(e.target.value)} placeholder="Add a note" aria-label="Note" />
          <button className="button" disabled={!note.trim()}>Add</button>
        </form>
      </section>

      {online && order.status !== "Scheduled" && (
        <section className="tech-section">
          <button className="button wide" onClick={async () => {
            const blob = await api.report(companyId, order.id);
            window.open(URL.createObjectURL(blob), "_blank");
          }}>Service report (PDF)</button>
        </section>
      )}
    </article>
  );
}

function Workspace({ company }: { company: Company }) {
  const account = useAccount();
  const me = useMemo(() => ({ id: account.session.user.id, name: account.session.user.displayName }), [account.session.user]);
  const store = useTechStore(company.id, me);
  const orderId = location.pathname.split("/")[2];
  const order = orderId ? store.orders.find((o) => o.id === orderId) : undefined;

  const signOut = async () => {
    // Work not yet sent would be lost with the device's copy; say so first.
    if (store.pending > 0 && !confirmLeave(store.pending)) return;
    await local.clear();
    account.signOut();
  };

  return (
    <div className="tech">
      <header className="tech-bar">
        <Brand />
        <div className="grow" />
        <SyncChip store={store} />
        <button className="icon-button" onClick={() => void signOut()} aria-label="Sign out"><LogOut size={18} /></button>
      </header>
      <main className="tech-main">
        <Notices store={store} />
        {orderId ? (order ? <Job order={order} store={store} companyId={company.id} /> : store.ready ? (
          <Empty title="This job is not on your device"><span className="small muted">It may have gone to someone else.</span><a className="button" {...linkProps("/tech")}>My jobs</a></Empty>
        ) : <Loading />) : (
          <>
            <div className="tech-head"><h1>My jobs</h1><span className="small muted">{account.session.user.displayName} · {company.name}</span></div>
            <Agenda store={store} />
          </>
        )}
      </main>
    </div>
  );
}

/** Asks before throwing away work that has not reached the office. */
function confirmLeave(pending: number) {
  return window.confirm(`${pending} change${pending === 1 ? " has" : "s have"} not reached the office yet and will be lost. Sign out anyway?`);
}

export function TechApp() {
  const { me } = useAccount();
  const [chosen, setChosen] = useState<string | null>(() => me.companies[0]?.id ?? null);
  const company = me.companies.find((c) => c.id === chosen);
  if (!company) {
    return (
      <div className="center"><Empty title="You are not on a team yet"><span className="small muted">Ask your company's owner to add {me.user.email}.</span>
        <a className="button" {...linkProps("/")} onClick={(e) => { e.preventDefault(); navigate("/"); }}>Back</a></Empty></div>
    );
  }
  return (
    <>
      {me.companies.length > 1 && (
        <div className="company-switch"><select className="select narrow" value={company.id} onChange={(e) => setChosen(e.target.value)} aria-label="Company">{me.companies.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}</select></div>
      )}
      <Workspace key={company.id} company={company} />
    </>
  );
}
