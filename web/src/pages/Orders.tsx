import { ClipboardList, Flag, Plus, Search } from "lucide-react";
import { useState } from "react";
import { api } from "../api";
import { PriorityBadge, SlaBadge, StatusBadge } from "../components/Badges";
import { Empty, Failed, SkeletonRows } from "../components/States";
import { useCompany } from "../context";
import { formatDayTime, relative } from "../format";
import { useDebounced, useLoad } from "../hooks";
import { linkProps, navigate, setQuery } from "../router";
import type { Priority, Status } from "../types";

const views: { key: string; label: string; status: Status[] }[] = [
  { key: "open", label: "Open", status: ["New", "Scheduled", "EnRoute", "InProgress", "OnHold"] },
  { key: "unplanned", label: "To plan", status: ["New", "OnHold"] },
  { key: "active", label: "In the field", status: ["EnRoute", "InProgress"] },
  { key: "done", label: "Completed", status: ["Completed"] },
  { key: "cancelled", label: "Cancelled", status: ["Cancelled"] },
  { key: "all", label: "All", status: [] },
];

export function Orders() {
  const company = useCompany();
  const params = new URLSearchParams(location.search);
  const [view, setView] = useState(params.get("view") ?? "open");
  const [q, setQ] = useState(params.get("q") ?? "");
  const [priority, setPriority] = useState<Priority | "">((params.get("priority") as Priority) ?? "");
  const [overdue, setOverdue] = useState(params.get("overdue") === "true");
  const [page, setPage] = useState(1);
  const search = useDebounced(q);
  const status = views.find((v) => v.key === view)?.status ?? [];
  const sort = view === "done" || view === "cancelled" || view === "all" ? "newest" : "due";

  const [orders, reload] = useLoad(() => {
    setQuery({ view: view === "open" ? undefined : view, q: search || undefined, priority: priority || undefined, overdue: overdue ? "true" : undefined });
    return api.orders(company.id, { status, q: search, priority, overdue, sort, page });
  }, [company.id, view, search, priority, overdue, page]);

  const reset = () => setPage(1);
  const data = orders.data;
  const pages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

  return (
    <div className="stack">
      <div className="page-head">
        <div><h1>Work orders</h1><p className="muted">Every job, its deadline and who has it.</p></div>
        <a className="button primary" {...linkProps(`/c/${company.id}/orders/new`)}><Plus size={16} />New work order</a>
      </div>
      <div className="card table-card">
        <div className="toolbar">
          <div className="tabs" role="tablist">
            {views.map((v) => <button key={v.key} role="tab" aria-selected={view === v.key} className={`tab${view === v.key ? " active" : ""}`} onClick={() => { setView(v.key); reset(); }}>{v.label}</button>)}
          </div>
          <label className="search"><Search size={16} /><input className="input" value={q} onChange={(e) => { setQ(e.target.value); reset(); }} placeholder="SO-01001, title or customer" aria-label="Search work orders" /></label>
          <select className="select" value={priority} onChange={(e) => { setPriority(e.target.value as Priority | ""); reset(); }} aria-label="Priority">
            <option value="">Any priority</option>
            {(["Urgent", "High", "Normal", "Low"] as Priority[]).map((p) => <option key={p}>{p}</option>)}
          </select>
          <label className="check small"><input type="checkbox" checked={overdue} onChange={(e) => { setOverdue(e.target.checked); reset(); }} />Overdue only</label>
        </div>
        {orders.state === "failed" && !data ? <Failed message={orders.error} onRetry={reload} /> : (
          <div className="table-wrap">
            <table className="table">
              <thead><tr><th>Job</th><th>Customer</th><th>Status</th><th>Technician</th><th>Due</th></tr></thead>
              {!data ? <SkeletonRows columns={5} /> : data.items.length === 0 ? (
                <tbody><tr><td colSpan={5}><Empty title="No work orders here" icon={<ClipboardList size={24} />}><span className="small">Try another view or filter.</span></Empty></td></tr></tbody>
              ) : (
                <tbody>
                  {data.items.map((o) => (
                    <tr key={o.id} className="clickable" onClick={() => navigate(`/c/${company.id}/orders/${o.id}`)}>
                      <td>
                        <a className="strong-link" {...linkProps(`/c/${company.id}/orders/${o.id}`)} onClick={(e) => e.stopPropagation()}>{o.title}</a>
                        <span className="block small muted"><span className="mono">{o.code}</span> · <PriorityBadge priority={o.priority} /></span>
                      </td>
                      <td><span className="block">{o.customerName}</span><span className="small muted">{o.siteLabel}, {o.city}</span></td>
                      <td><StatusBadge status={o.status} /></td>
                      <td>{o.technicianName ?? <span className="faint">—</span>}{o.scheduledStart && <span className="block small muted">{formatDayTime(o.scheduledStart)}</span>}</td>
                      <td className="nowrap"><span className="block">{relative(o.dueAt)}</span><SlaBadge sla={o.sla} /></td>
                    </tr>
                  ))}
                </tbody>
              )}
            </table>
          </div>
        )}
        {data && data.total > data.pageSize && (
          <div className="pager">
            <span>{data.total} work orders</span>
            <div className="row gap-6">
              <button className="button small" disabled={page <= 1} onClick={() => setPage(page - 1)}>Previous</button>
              <span>{page} / {pages}</span>
              <button className="button small" disabled={page >= pages} onClick={() => setPage(page + 1)}>Next</button>
            </div>
          </div>
        )}
      </div>
      <p className="small faint row gap-4"><Flag size={12} />Jobs with work sent from a device after they were cancelled or reassigned are marked for review on their page.</p>
    </div>
  );
}
