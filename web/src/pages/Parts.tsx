import { Package, Pencil, Plus, Search } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api } from "../api";
import { Dialog } from "../components/Dialog";
import { Empty, Failed, SkeletonRows } from "../components/States";
import { canDispatch, useCompany } from "../context";
import { formatMoney } from "../format";
import { useDebounced, useLoad } from "../hooks";
import type { Part, PartInput } from "../types";

function PartDialog({ part, onClose, onSaved }: { part: Part | null; onClose: () => void; onSaved: () => void }) {
  const company = useCompany();
  const [form, setForm] = useState<PartInput>(part ? { sku: part.sku, name: part.name, unitPrice: part.unitPrice, active: part.active } : { sku: "", name: "", unitPrice: 0, active: true });
  const [error, setError] = useState<string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    try {
      if (part) await api.updatePart(company.id, part.id, form);
      else await api.createPart(company.id, form);
      onSaved();
    } catch (err) {
      setError((err as Error).message);
    }
  }

  return (
    <Dialog title={part ? `Edit ${part.sku}` : "New part"} onClose={onClose}>
      <form className="stack" onSubmit={submit}>
        <div className="grid-2">
          <label className="field">SKU<input className="input mono-input" value={form.sku} onChange={(e) => setForm({ ...form, sku: e.target.value.toUpperCase() })} placeholder="VLV-22" required autoFocus /></label>
          <label className="field">Price (TRY)<input className="input" type="number" min={0} step="0.01" value={form.unitPrice} onChange={(e) => setForm({ ...form, unitPrice: Number(e.target.value) })} required /></label>
        </div>
        <label className="field">Name<input className="input" value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} placeholder="Safety valve 1/2&quot;" required /></label>
        {part && <label className="check small"><input type="checkbox" checked={form.active} onChange={(e) => setForm({ ...form, active: e.target.checked })} />In use (retired parts stay on old jobs but cannot be added to new ones)</label>}
        {error && <p className="form-error" role="alert">{error}</p>}
        <div className="dialog-actions"><button type="button" className="button" onClick={onClose}>Close</button><button className="button primary">{part ? "Save" : "Add part"}</button></div>
      </form>
    </Dialog>
  );
}

export function Parts() {
  const company = useCompany();
  const [q, setQ] = useState("");
  const [retired, setRetired] = useState(false);
  const search = useDebounced(q);
  const [parts, reload] = useLoad(() => api.parts(company.id, { q: search, includeRetired: retired }), [company.id, search, retired]);
  const [editing, setEditing] = useState<Part | null | undefined>(undefined);
  const dispatcher = canDispatch(company);

  return (
    <div className="stack">
      <div className="page-head">
        <div><h1>Parts</h1><p className="muted">What technicians fit, at the price the customer pays. Technicians pick from this list on site, even offline.</p></div>
        {dispatcher && <button className="button primary" onClick={() => setEditing(null)}><Plus size={16} />New part</button>}
      </div>
      <div className="card table-card">
        <div className="toolbar">
          <label className="search"><Search size={16} /><input className="input" value={q} onChange={(e) => setQ(e.target.value)} placeholder="SKU or name" aria-label="Search parts" /></label>
          <label className="check small"><input type="checkbox" checked={retired} onChange={(e) => setRetired(e.target.checked)} />Show retired</label>
        </div>
        {parts.state === "failed" && !parts.data ? <Failed message={parts.error} onRetry={reload} /> : (
          <div className="table-wrap">
            <table className="table">
              <thead><tr><th>SKU</th><th>Name</th><th className="right">Price</th><th /></tr></thead>
              {!parts.data ? <SkeletonRows columns={4} /> : parts.data.items.length === 0 ? (
                <tbody><tr><td colSpan={4}><Empty title={q ? "No part matches" : "No parts yet"} icon={<Package size={24} />} /></td></tr></tbody>
              ) : (
                <tbody>
                  {parts.data.items.map((p) => (
                    <tr key={p.id} className={p.active ? "" : "removed"}>
                      <td className="mono">{p.sku}</td>
                      <td>{p.name} {!p.active && <span className="badge plain">Retired</span>}</td>
                      <td className="right nowrap">{formatMoney(p.unitPrice)}</td>
                      <td className="actions">{dispatcher && <button className="icon-button" onClick={() => setEditing(p)} aria-label={`Edit ${p.sku}`}><Pencil size={15} /></button>}</td>
                    </tr>
                  ))}
                </tbody>
              )}
            </table>
          </div>
        )}
      </div>
      {editing !== undefined && <PartDialog part={editing} onClose={() => setEditing(undefined)} onSaved={() => { setEditing(undefined); reload(); }} />}
    </div>
  );
}
