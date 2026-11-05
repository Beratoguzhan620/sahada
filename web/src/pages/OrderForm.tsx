import { ArrowLeft, Plus, Trash2 } from "lucide-react";
import { useEffect, useState, type FormEvent } from "react";
import { api, ApiError } from "../api";
import { useCompany } from "../context";
import { useDebounced, useLoad } from "../hooks";
import { linkProps, navigate } from "../router";
import type { Customer, Priority } from "../types";

const windows: Record<Priority, string> = { Urgent: "4 hours", High: "24 hours", Normal: "3 days", Low: "7 days" };

/** Opens a work order: who, where, what is wrong, how urgent, and the steps to tick off on site. */
export function OrderForm({ customerId: initialCustomer }: { customerId?: string }) {
  const company = useCompany();
  const [q, setQ] = useState("");
  const search = useDebounced(q);
  const [matches] = useLoad(() => api.customers(company.id, { q: search, pageSize: 8 }), [company.id, search]);
  const [customer, setCustomer] = useState<Customer | null>(null);
  const [siteId, setSiteId] = useState("");
  const [equipmentId, setEquipmentId] = useState("");
  const [form, setForm] = useState({ title: "", description: "", priority: "Normal" as Priority, requiredSkill: "", estimatedMinutes: 60 });
  const [checklist, setChecklist] = useState<string[]>([""]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<ApiError | null>(null);

  const pick = async (id: string) => {
    const c = await api.customer(company.id, id);
    setCustomer(c);
    setSiteId(c.sites[0]?.id ?? "");
    setEquipmentId("");
  };

  useEffect(() => { if (initialCustomer) void pick(initialCustomer); }, [initialCustomer]); // eslint-disable-line react-hooks/exhaustive-deps

  const site = customer?.sites.find((s) => s.id === siteId);
  const set = <K extends keyof typeof form>(key: K, value: (typeof form)[K]) => setForm({ ...form, [key]: value });
  const fieldError = (name: string) => error?.fields[name]?.[0];

  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!customer || !siteId) return;
    setBusy(true);
    setError(null);
    try {
      const created = await api.createOrder(company.id, {
        ...form, customerId: customer.id, siteId, equipmentId: equipmentId || null, checklist: checklist.map((c) => c.trim()).filter(Boolean),
      });
      navigate(`/c/${company.id}/orders/${created.id}`, true);
    } catch (err) {
      setError(err as ApiError);
      setBusy(false);
    }
  }

  return (
    <form className="narrow-page" onSubmit={submit}>
      <a className="back" {...linkProps(`/c/${company.id}/orders`)}><ArrowLeft size={14} />Work orders</a>
      <h1>New work order</h1>

      <section className="card section">
        <h2>Customer and site</h2>
        {customer ? (
          <div className="stack-8">
            <div className="row between"><strong>{customer.name}</strong><button type="button" className="link small" onClick={() => setCustomer(null)}>Change</button></div>
            {customer.sites.length === 0 ? (
              <p className="form-error">This customer has no site yet. <a {...linkProps(`/c/${company.id}/customers/${customer.id}`)}>Add one</a> first.</p>
            ) : (
              <div className="grid-2">
                <label className="field">Site
                  <select className="select" value={siteId} onChange={(e) => { setSiteId(e.target.value); setEquipmentId(""); }}>
                    {customer.sites.map((s) => <option key={s.id} value={s.id}>{s.label} · {s.address}, {s.city}</option>)}
                  </select>
                </label>
                <label className="field">Equipment
                  <select className="select" value={equipmentId} onChange={(e) => setEquipmentId(e.target.value)}>
                    <option value="">None in particular</option>
                    {site?.equipment.map((eq) => <option key={eq.id} value={eq.id}>{[eq.kind, eq.brand, eq.model].filter(Boolean).join(" ")}{eq.serialNumber && ` · ${eq.serialNumber}`}</option>)}
                  </select>
                </label>
              </div>
            )}
          </div>
        ) : (
          <div className="stack-8">
            <input className="input" value={q} onChange={(e) => setQ(e.target.value)} placeholder="Search customers by name or phone" aria-label="Search customers" autoFocus />
            <ul className="pick-list">
              {matches.data?.items.map((c) => (
                <li key={c.id}><button type="button" onClick={() => pick(c.id)}><strong>{c.name}</strong><span className="small muted">{c.phone || c.email} · {c.sites} sites</span></button></li>
              ))}
              {matches.data?.items.length === 0 && <li className="small muted">No customer matches. <a {...linkProps(`/c/${company.id}/customers`)}>Add a customer</a>.</li>}
            </ul>
          </div>
        )}
      </section>

      <section className="card section">
        <h2>The job</h2>
        <label className="field">Title<input className="input" value={form.title} onChange={(e) => set("title", e.target.value)} placeholder="Boiler leaking from below" maxLength={120} required />
          {fieldError("title") && <span className="field-error">{fieldError("title")}</span>}</label>
        <label className="field">What the customer reported<textarea className="textarea" value={form.description} onChange={(e) => set("description", e.target.value)} maxLength={4000} /></label>
        <div className="grid-3">
          <label className="field">Priority
            <select className="select" value={form.priority} onChange={(e) => set("priority", e.target.value as Priority)}>
              {(["Urgent", "High", "Normal", "Low"] as Priority[]).map((p) => <option key={p}>{p}</option>)}
            </select>
            <span className="hint">Due within {windows[form.priority]}.</span>
          </label>
          <label className="field">Skill needed<input className="input" value={form.requiredSkill} onChange={(e) => set("requiredSkill", e.target.value.toLowerCase().replace(/\s/g, ""))} placeholder="hvac" maxLength={40} />
            <span className="hint">Leave empty if anyone can do it.</span></label>
          <label className="field">Estimate (minutes)<input className="input" type="number" min={15} max={960} step={15} value={form.estimatedMinutes} onChange={(e) => set("estimatedMinutes", Number(e.target.value))} /></label>
        </div>
      </section>

      <section className="card section">
        <h2>Checklist</h2>
        <p className="small muted">Steps the technician ticks off on site. Every one has to be done before the job can be completed.</p>
        {checklist.map((item, i) => (
          <div className="row" key={i}>
            <input className="input" value={item} onChange={(e) => setChecklist(checklist.map((c, j) => (j === i ? e.target.value : c)))} placeholder={i === 0 ? "Measure the pressure" : "Another step"} aria-label={`Step ${i + 1}`} maxLength={200} />
            <button type="button" className="icon-button" onClick={() => setChecklist(checklist.filter((_, j) => j !== i))} aria-label={`Remove step ${i + 1}`} disabled={checklist.length === 1}><Trash2 size={16} /></button>
          </div>
        ))}
        <div><button type="button" className="button small" onClick={() => setChecklist([...checklist, ""])}><Plus size={14} />Add a step</button></div>
      </section>

      {error && Object.keys(error.fields).length === 0 && <p className="form-error" role="alert">{error.message}</p>}
      <div className="sticky-actions"><button className="button primary" disabled={busy || !customer || !siteId || form.title.trim().length < 3}>{busy ? "Opening…" : "Open work order"}</button></div>
    </form>
  );
}
