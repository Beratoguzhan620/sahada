import { CircleCheck } from "lucide-react";
import { api } from "../api";
import { PriorityBadge, StatusBadge } from "../components/Badges";
import { Empty, Failed, Loading } from "../components/States";
import { useCompany } from "../context";
import { formatDayTime, relative } from "../format";
import { useLoad } from "../hooks";
import { linkProps } from "../router";
import type { WorkOrderSummary } from "../types";

function List({ title, hint, orders, tone }: { title: string; hint: string; orders: WorkOrderSummary[]; tone: "danger" | "info" }) {
  const company = useCompany();
  return (
    <section className="card table-card">
      <div className="section-head"><h2>{title}</h2><span className={`badge ${tone}`}>{orders.length}</span></div>
      <p className="small muted section-hint">{hint}</p>
      {orders.length === 0 ? <Empty title="Nothing here" icon={<CircleCheck size={22} />} /> : (
        <div className="table-wrap">
          <table className="table">
            <thead><tr><th>Job</th><th>Status</th><th>Technician</th><th>Due</th></tr></thead>
            <tbody>
              {orders.map((o) => (
                <tr key={o.id}>
                  <td><a className="strong-link" {...linkProps(`/c/${company.id}/orders/${o.id}`)}>{o.title}</a><span className="block small muted"><span className="mono">{o.code}</span> · {o.customerName} · <PriorityBadge priority={o.priority} /></span></td>
                  <td><StatusBadge status={o.status} /></td>
                  <td>{o.technicianName ?? <span className="faint">Not planned</span>}{o.scheduledStart && <span className="block small muted">{formatDayTime(o.scheduledStart)}</span>}</td>
                  <td className="nowrap"><span className={`block${tone === "danger" ? " danger-text" : ""}`}>{relative(o.dueAt)}</span><span className="small muted">{formatDayTime(o.dueAt)}</span></td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </section>
  );
}

/** Jobs past their deadline, and jobs about to miss it: planned to end late, or unplanned with little time left. */
export function Deadlines() {
  const company = useCompany();
  const [sla, reload] = useLoad(() => api.sla(company.id), [company.id]);
  if (sla.state === "failed" && !sla.data) return <Failed message={sla.error} onRetry={reload} />;
  if (!sla.data) return <Loading />;
  const s = sla.data;
  const share = (n: number) => (s.open === 0 ? 0 : Math.round((100 * n) / s.open));
  return (
    <div className="stack">
      <div className="page-head"><div><h1>Deadlines</h1><p className="muted">Urgent jobs have 4 hours, high 24, normal 3 days, low a week, counted from when the call came in.</p></div></div>
      <div className="stats">
        <div className="card stat"><span className="small muted">Open jobs</span><strong>{s.open}</strong></div>
        <div className="card stat"><span className="small muted">On track</span><strong>{s.onTrack}</strong><span className="small faint">{share(s.onTrack)}%</span></div>
        <div className="card stat"><span className="small muted">At risk</span><strong className="accent">{s.atRisk}</strong><span className="small faint">{share(s.atRisk)}%</span></div>
        <div className="card stat"><span className="small muted">Overdue</span><strong className="danger-text">{s.overdue}</strong><span className="small faint">{share(s.overdue)}%</span></div>
      </div>
      <div className="meter large sla-meter" aria-hidden="true">
        <span className="ok-fill" style={{ width: `${share(s.onTrack)}%` }} /><span className="risk-fill" style={{ width: `${share(s.atRisk)}%` }} /><span className="late-fill" style={{ width: `${share(s.overdue)}%` }} />
      </div>
      <List title="Overdue" hint="Past the deadline and still open." orders={s.overdues} tone="danger" />
      <List title="At risk" hint="Planned to finish after the deadline, or not planned with less than a quarter of the time left." orders={s.atRisks} tone="info" />
    </div>
  );
}
