import { ArrowLeft, Cpu, MapPin, Pencil, Plus, ShieldCheck, Trash2 } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api, ApiError } from "../api";
import { StatusBadge } from "../components/Badges";
import { Dialog } from "../components/Dialog";
import { MapView } from "../components/MapView";
import { Failed, Loading } from "../components/States";
import { canDispatch, useCompany } from "../context";
import { relative } from "../format";
import { useLoad } from "../hooks";
import { linkProps, navigate } from "../router";
import type { Customer, CustomerInput, EquipmentInput, Site, SiteInput } from "../types";
import { CustomerFields } from "./Customers";

const emptySite: SiteInput = { label: "", address: "", city: "Samsun", latitude: 41.2867, longitude: 36.33, accessNotes: "" };
const emptyEquipment: EquipmentInput = { kind: "", brand: "", model: "", serialNumber: "", installedOn: null, warrantyUntil: null };

function SiteDialog({ customer, site, onClose, onSaved }: { customer: Customer; site: Site | null; onClose: () => void; onSaved: () => void }) {
  const company = useCompany();
  const [form, setForm] = useState<SiteInput>(site ? { ...site } : emptySite);
  const [error, setError] = useState<string | null>(null);
  const set = <K extends keyof SiteInput>(key: K, value: SiteInput[K]) => setForm({ ...form, [key]: value });

  async function submit(e: FormEvent) {
    e.preventDefault();
    try {
      if (site) await api.updateSite(company.id, customer.id, site.id, form);
      else await api.addSite(company.id, customer.id, form);
      onSaved();
    } catch (err) {
      setError((err as Error).message);
    }
  }

  return (
    <Dialog title={site ? `Edit ${site.label}` : "New site"} onClose={onClose} wide>
      <form className="stack" onSubmit={submit}>
        <div className="grid-2">
          <label className="field">Label<input className="input" value={form.label} onChange={(e) => set("label", e.target.value)} placeholder="Boiler room" required autoFocus /></label>
          <label className="field">City<input className="input" value={form.city} onChange={(e) => set("city", e.target.value)} required /></label>
        </div>
        <label className="field">Address<input className="input" value={form.address} onChange={(e) => set("address", e.target.value)} placeholder="Cumhuriyet Cd. 12, Atakum" required /></label>
        <div className="field">
          <span>Location <span className="hint">Click the map to place the pin.</span></span>
          <MapView pins={[{ id: "site", latitude: form.latitude, longitude: form.longitude, label: form.label || "Site", text: "", tone: "accent" }]} height={220}
            onPick={(latitude, longitude) => setForm((f) => ({ ...f, latitude, longitude }))} />
          <div className="grid-2">
            <input className="input mono-input" type="number" step="0.000001" value={form.latitude} onChange={(e) => set("latitude", Number(e.target.value))} aria-label="Latitude" />
            <input className="input mono-input" type="number" step="0.000001" value={form.longitude} onChange={(e) => set("longitude", Number(e.target.value))} aria-label="Longitude" />
          </div>
        </div>
        <label className="field">Access notes<textarea className="textarea short" value={form.accessNotes} onChange={(e) => set("accessNotes", e.target.value)} placeholder="Key with the caretaker, park in the back" /></label>
        {error && <p className="form-error" role="alert">{error}</p>}
        <div className="dialog-actions"><button type="button" className="button" onClick={onClose}>Close</button><button className="button primary">{site ? "Save" : "Add site"}</button></div>
      </form>
    </Dialog>
  );
}

function EquipmentDialog({ customer, site, onClose, onSaved }: { customer: Customer; site: Site; onClose: () => void; onSaved: () => void }) {
  const company = useCompany();
  const [form, setForm] = useState<EquipmentInput>(emptyEquipment);
  const [error, setError] = useState<string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    try {
      await api.addEquipment(company.id, customer.id, site.id, form);
      onSaved();
    } catch (err) {
      setError((err as Error).message);
    }
  }

  return (
    <Dialog title={`Equipment at ${site.label}`} onClose={onClose}>
      <form className="stack" onSubmit={submit}>
        <div className="grid-2">
          <label className="field">Kind<input className="input" value={form.kind} onChange={(e) => setForm({ ...form, kind: e.target.value })} placeholder="Boiler" required autoFocus /></label>
          <label className="field">Serial number<input className="input mono-input" value={form.serialNumber} onChange={(e) => setForm({ ...form, serialNumber: e.target.value })} /></label>
          <label className="field">Brand<input className="input" value={form.brand} onChange={(e) => setForm({ ...form, brand: e.target.value })} /></label>
          <label className="field">Model<input className="input" value={form.model} onChange={(e) => setForm({ ...form, model: e.target.value })} /></label>
          <label className="field">Installed<input className="input" type="date" value={form.installedOn ?? ""} onChange={(e) => setForm({ ...form, installedOn: e.target.value || null })} /></label>
          <label className="field">Warranty until<input className="input" type="date" value={form.warrantyUntil ?? ""} onChange={(e) => setForm({ ...form, warrantyUntil: e.target.value || null })} /></label>
        </div>
        {error && <p className="form-error" role="alert">{error}</p>}
        <div className="dialog-actions"><button type="button" className="button" onClick={onClose}>Close</button><button className="button primary">Add equipment</button></div>
      </form>
    </Dialog>
  );
}

export function CustomerPage({ customerId }: { customerId: string }) {
  const company = useCompany();
  const dispatcher = canDispatch(company);
  const [customer, reload] = useLoad(() => api.customer(company.id, customerId), [company.id, customerId]);
  const [orders] = useLoad(() => api.orders(company.id, { customerId, sort: "newest" }), [company.id, customerId]);
  const [dialog, setDialog] = useState<{ kind: "edit" } | { kind: "site"; site: Site | null } | { kind: "equipment"; site: Site } | null>(null);
  const [form, setForm] = useState<CustomerInput | null>(null);
  const [error, setError] = useState<ApiError | null>(null);
  const [problem, setProblem] = useState<string | null>(null);

  if (customer.state === "failed" && !customer.data) return <Failed message={customer.error} onRetry={reload} />;
  if (!customer.data) return <Loading />;
  const c = customer.data;
  const done = () => { setDialog(null); reload(); };
  const attempt = (work: Promise<unknown>) => work.then(reload, (e: Error) => setProblem(e.message));

  return (
    <div className="stack customer-page">
      <a className="back" {...linkProps(`/c/${company.id}/customers`)}><ArrowLeft size={14} />Customers</a>
      <div className="page-head">
        <div><h1>{c.name}</h1><p className="muted">{[c.phone, c.email].filter(Boolean).join(" · ") || "No contact details"}</p></div>
        {dispatcher && (
          <div className="row gap-6">
            <button className="button" onClick={() => { setForm({ name: c.name, phone: c.phone, email: c.email, notes: c.notes }); setDialog({ kind: "edit" }); }}><Pencil size={15} />Edit</button>
            <a className="button primary" {...linkProps(`/c/${company.id}/orders/new?customer=${c.id}`)}><Plus size={16} />New work order</a>
          </div>
        )}
      </div>
      {c.notes && <p className="notice">{c.notes}</p>}
      {problem && <p className="form-error" role="alert">{problem}</p>}

      <div className="two-col">
        <section className="stack">
          <div className="row between"><h2>Sites</h2>{dispatcher && <button className="button small" onClick={() => setDialog({ kind: "site", site: null })}><Plus size={14} />Add site</button>}</div>
          {c.sites.length === 0 && <p className="card section muted small">No sites yet. A work order needs a site to send someone to.</p>}
          {c.sites.map((s) => (
            <article key={s.id} className="card section site-card">
              <div className="row between">
                <span className="row gap-6"><MapPin size={16} className="accent" /><strong>{s.label}</strong></span>
                {dispatcher && (
                  <span className="row gap-4">
                    <button className="icon-button" onClick={() => setDialog({ kind: "site", site: s })} aria-label={`Edit ${s.label}`}><Pencil size={15} /></button>
                    <button className="icon-button" onClick={() => attempt(api.deleteSite(company.id, c.id, s.id))} aria-label={`Remove ${s.label}`}><Trash2 size={15} /></button>
                  </span>
                )}
              </div>
              <span className="small">{s.address}, {s.city}</span>
              {s.accessNotes && <span className="small muted">{s.accessNotes}</span>}
              <ul className="equipment">
                {s.equipment.map((e) => {
                  const warranty = e.warrantyUntil && new Date(e.warrantyUntil) >= new Date(new Date().toDateString());
                  return (
                    <li key={e.id}>
                      <Cpu size={14} className="faint" />
                      <span className="grow min0"><span className="block ellipsis">{[e.kind, e.brand, e.model].filter(Boolean).join(" ")}</span>{e.serialNumber && <span className="mono faint">{e.serialNumber}</span>}</span>
                      {warranty && <span className="badge ok"><ShieldCheck size={12} />Warranty</span>}
                      {dispatcher && <button className="icon-button" onClick={() => attempt(api.deleteEquipment(company.id, c.id, s.id, e.id))} aria-label={`Remove ${e.kind}`}><Trash2 size={14} /></button>}
                    </li>
                  );
                })}
              </ul>
              {dispatcher && <div><button className="link small" onClick={() => setDialog({ kind: "equipment", site: s })}><Plus size={13} />Equipment</button></div>}
            </article>
          ))}
        </section>
        <section className="stack">
          <h2>Work orders</h2>
          <div className="card">
            {!orders.data ? <Loading /> : orders.data.items.length === 0 ? <p className="section muted small">No work orders yet.</p> : (
              <ul className="order-list">
                {orders.data.items.map((o) => (
                  <li key={o.id}><a {...linkProps(`/c/${company.id}/orders/${o.id}`)}>
                    <span className="grow min0"><strong className="block ellipsis">{o.title}</strong><span className="small muted"><span className="mono">{o.code}</span> · {relative(o.createdAt)}</span></span>
                    <StatusBadge status={o.status} />
                  </a></li>
                ))}
              </ul>
            )}
          </div>
          {dispatcher && c.sites.length === 0 && orders.data?.items.length === 0 && (
            <button className="button danger small" onClick={() => api.deleteCustomer(company.id, c.id).then(() => navigate(`/c/${company.id}/customers`), (e: Error) => setProblem(e.message))}><Trash2 size={14} />Remove customer</button>
          )}
        </section>
      </div>

      {dialog?.kind === "edit" && form && (
        <Dialog title={`Edit ${c.name}`} onClose={() => setDialog(null)}>
          <form className="stack" onSubmit={(e) => { e.preventDefault(); api.updateCustomer(company.id, c.id, form).then(done, (err: ApiError) => setError(err)); }}>
            <CustomerFields value={form} onChange={setForm} error={error} />
            <div className="dialog-actions"><button type="button" className="button" onClick={() => setDialog(null)}>Close</button><button className="button primary">Save</button></div>
          </form>
        </Dialog>
      )}
      {dialog?.kind === "site" && <SiteDialog customer={c} site={dialog.site} onClose={() => setDialog(null)} onSaved={done} />}
      {dialog?.kind === "equipment" && <EquipmentDialog customer={c} site={dialog.site} onClose={() => setDialog(null)} onSaved={done} />}
    </div>
  );
}
