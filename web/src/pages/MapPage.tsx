import { useMemo, useState } from "react";
import { api } from "../api";
import { PriorityBadge, SlaBadge, StatusBadge } from "../components/Badges";
import { MapView, type Pin } from "../components/MapView";
import { Failed, Loading } from "../components/States";
import { useCompany } from "../context";
import { initials, relative } from "../format";
import { useLoad } from "../hooks";
import { linkProps, navigate } from "../router";

/** Every open job and every technician's base on one map. */
export function MapPage() {
  const company = useCompany();
  const [orders, reload] = useLoad(() => api.orders(company.id, { status: ["New", "Scheduled", "EnRoute", "InProgress", "OnHold"], sort: "due" }), [company.id]);
  const [team] = useLoad(() => api.technicians(company.id), [company.id]);
  const [focus, setFocus] = useState<string | null>(null);

  const pins = useMemo<Pin[]>(() => [
    ...(team.data ?? []).filter((t) => t.active).map((t) => ({ id: t.userId, latitude: t.baseLatitude, longitude: t.baseLongitude, label: `${t.displayName} · ${t.baseLabel}`, text: initials(t.displayName), tone: "ink" as const })),
    ...(orders.data?.items ?? []).map((o) => ({
      id: o.id, latitude: o.latitude, longitude: o.longitude, label: `${o.code} · ${o.title}`, text: o.technicianName ? initials(o.technicianName) : "?",
      tone: (o.sla === "Overdue" || o.sla === "AtRisk" ? "danger" : o.technicianId ? "accent" : "muted") as Pin["tone"],
      onClick: () => setFocus(o.id),
    })),
  ], [orders.data, team.data]);

  if (orders.state === "failed" && !orders.data) return <Failed message={orders.error} onRetry={reload} />;
  if (!orders.data) return <Loading />;
  const selected = orders.data.items.find((o) => o.id === focus);

  return (
    <div className="stack">
      <div className="page-head">
        <div><h1>Map</h1><p className="muted">Open jobs by who has them; dark pins are where technicians start their day.</p></div>
        <ul className="legend">
          <li><span className="dot" style={{ background: "#8A5A00" }} />Planned</li>
          <li><span className="dot" style={{ background: "#8A867F" }} />Not planned</li>
          <li><span className="dot" style={{ background: "#9E2B25" }} />Late or at risk</li>
          <li><span className="dot" style={{ background: "#1F2A37" }} />Technician base</li>
        </ul>
      </div>
      <section className="card map-page">
        <MapView pins={pins} height={560} />
        {selected && (
          <div className="map-popup card">
            <div className="row between"><span className="mono faint">{selected.code}</span><button className="link small" onClick={() => setFocus(null)}>Close</button></div>
            <strong>{selected.title}</strong>
            <span className="small muted">{selected.customerName} · {selected.siteLabel}, {selected.city}</span>
            <div className="row wrap gap-4"><StatusBadge status={selected.status} /><PriorityBadge priority={selected.priority} /><SlaBadge sla={selected.sla} /></div>
            <span className="small">{selected.technicianName ?? "Not planned"} · due {relative(selected.dueAt)}</span>
            <a className="button small" {...linkProps(`/c/${company.id}/orders/${selected.id}`)} onClick={(e) => { e.preventDefault(); navigate(`/c/${company.id}/orders/${selected.id}`); }}>Open</a>
          </div>
        )}
      </section>
    </div>
  );
}
