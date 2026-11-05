import { Building2, Plus, Search } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api, ApiError } from "../api";
import { Dialog } from "../components/Dialog";
import { Empty, Failed, SkeletonRows } from "../components/States";
import { canDispatch, useCompany } from "../context";
import { useDebounced, useLoad } from "../hooks";
import { linkProps, navigate } from "../router";
import type { CustomerInput } from "../types";

export function CustomerFields({ value, onChange, error }: { value: CustomerInput; onChange: (v: CustomerInput) => void; error: ApiError | null }) {
  const fieldError = (name: string) => error?.fields[name]?.[0];
  return (
    <>
      <label className="field">Name<input className="input" value={value.name} onChange={(e) => onChange({ ...value, name: e.target.value })} placeholder="Atakum Apartmanı Yönetimi" required autoFocus />
        {fieldError("name") && <span className="field-error">{fieldError("name")}</span>}</label>
      <div className="grid-2">
        <label className="field">Phone<input className="input" type="tel" value={value.phone} onChange={(e) => onChange({ ...value, phone: e.target.value })} placeholder="0362 000 00 00" /></label>
        <label className="field">Email<input className="input" type="email" value={value.email} onChange={(e) => onChange({ ...value, email: e.target.value })} /></label>
      </div>
      <label className="field">Notes<textarea className="textarea short" value={value.notes} onChange={(e) => onChange({ ...value, notes: e.target.value })} placeholder="Who to ask for, payment terms" /></label>
    </>
  );
}

export function Customers() {
  const company = useCompany();
  const [q, setQ] = useState("");
  const [page, setPage] = useState(1);
  const search = useDebounced(q);
  const [customers, reload] = useLoad(() => api.customers(company.id, { q: search, page, pageSize: 25 }), [company.id, search, page]);
  const [adding, setAdding] = useState(false);
  const [form, setForm] = useState<CustomerInput>({ name: "", phone: "", email: "", notes: "" });
  const [error, setError] = useState<ApiError | null>(null);

  async function create(e: FormEvent) {
    e.preventDefault();
    try {
      const created = await api.createCustomer(company.id, form);
      navigate(`/c/${company.id}/customers/${created.id}`);
    } catch (err) {
      setError(err as ApiError);
    }
  }

  const data = customers.data;
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;
  return (
    <div className="stack">
      <div className="page-head">
        <div><h1>Customers</h1><p className="muted">The people and businesses you work for, their sites and the equipment there.</p></div>
        {canDispatch(company) && <button className="button primary" onClick={() => setAdding(true)}><Plus size={16} />New customer</button>}
      </div>
      <div className="card table-card">
        <div className="toolbar"><label className="search"><Search size={16} /><input className="input" value={q} onChange={(e) => { setQ(e.target.value); setPage(1); }} placeholder="Name, phone or email" aria-label="Search customers" /></label></div>
        {customers.state === "failed" && !data ? <Failed message={customers.error} onRetry={reload} /> : (
          <div className="table-wrap">
            <table className="table">
              <thead><tr><th>Customer</th><th>Contact</th><th className="right">Sites</th><th className="right">Open jobs</th></tr></thead>
              {!data ? <SkeletonRows columns={4} /> : data.items.length === 0 ? (
                <tbody><tr><td colSpan={4}><Empty title={q ? "No customer matches" : "No customers yet"} icon={<Building2 size={24} />} /></td></tr></tbody>
              ) : (
                <tbody>
                  {data.items.map((c) => (
                    <tr key={c.id} className="clickable" onClick={() => navigate(`/c/${company.id}/customers/${c.id}`)}>
                      <td><a className="strong-link" {...linkProps(`/c/${company.id}/customers/${c.id}`)} onClick={(e) => e.stopPropagation()}>{c.name}</a></td>
                      <td><span className="block">{c.phone || <span className="faint">—</span>}</span><span className="small muted">{c.email}</span></td>
                      <td className="right">{c.sites}</td>
                      <td className="right">{c.openWorkOrders > 0 ? <span className="badge accent-badge">{c.openWorkOrders}</span> : <span className="faint">0</span>}</td>
                    </tr>
                  ))}
                </tbody>
              )}
            </table>
          </div>
        )}
        {data && data.total > data.pageSize && (
          <div className="pager"><span>{data.total} customers</span><div className="row gap-6">
            <button className="button small" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button><span>{page} / {pages}</span>
            <button className="button small" disabled={page >= pages} onClick={() => setPage(page + 1)}>Next</button></div></div>
        )}
      </div>
      {adding && (
        <Dialog title="New customer" onClose={() => setAdding(false)}>
          <form className="stack" onSubmit={create}>
            <CustomerFields value={form} onChange={setForm} error={error} />
            {error && Object.keys(error.fields).length === 0 && <p className="form-error" role="alert">{error.message}</p>}
            <div className="dialog-actions"><button type="button" className="button" onClick={() => setAdding(false)}>Close</button><button className="button primary">Add customer</button></div>
          </form>
        </Dialog>
      )}
    </div>
  );
}
