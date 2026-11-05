import { AlertTriangle, ChevronLeft, ChevronRight, Inbox, UserRoundX } from "lucide-react";
import { useMemo, useState } from "react";
import { api } from "../api";
import { PriorityBadge, SlaBadge } from "../components/Badges";
import { MapView, type Pin } from "../components/MapView";
import { PlanDialog } from "../components/PlanDialog";
import { Empty, Failed, Loading } from "../components/States";
import { useCompany } from "../context";
import { addDays, formatDate, formatTime, initials, localMinutes, minutes, relative, statusLabel, today } from "../format";
import { useLoad } from "../hooks";
import { linkProps, navigate } from "../router";
import type { BoardTechnician, WorkOrderSummary } from "../types";

const DAY_START = 7 * 60;
const DAY_END = 20 * 60;
const span = DAY_END - DAY_START;
const pct = (m: number) => `${(100 * (Math.min(DAY_END, Math.max(DAY_START, m)) - DAY_START)) / span}%`;

function Timeline({ tech, companyId }: { tech: BoardTechnician; companyId: string }) {
  const shiftFrom = tech.shiftStart ? localMinutes(tech.shiftStart) : null;
  const shiftTo = tech.shiftEnd ? localMinutes(tech.shiftEnd) : null;
  return (
    <div className="lane" role="list" aria-label={`${tech.displayName}'s day`}>
      {shiftFrom !== null && shiftTo !== null
        ? <span className="shift" style={{ left: pct(shiftFrom), width: `calc(${pct(shiftTo)} - ${pct(shiftFrom)})` }} />
        : <span className="day-off">Day off</span>}
      {tech.jobs.map((job) => {
        const from = localMinutes(job.scheduledStart!);
        const tone = job.sla === "Overdue" || job.sla === "AtRisk" ? " late" : "";
        return (
          <a key={job.id} role="listitem" className={`job job-${job.status}${tone}`} {...linkProps(`/c/${companyId}/orders/${job.id}`)}
            style={{ left: pct(from), width: `calc(${pct(from + job.estimatedMinutes)} - ${pct(from)})` }}
            title={`${job.code} · ${job.title} · ${formatTime(job.scheduledStart!)} · ${statusLabel[job.status]}`}>
            <strong>{formatTime(job.scheduledStart!)}</strong> <span className="ellipsis">{job.title}</span>
          </a>
        );
      })}
    </div>
  );
}

function PileCard({ job, onPlan }: { job: WorkOrderSummary; onPlan: () => void }) {
  return (
    <li className="pile-card">
      <div className="row between gap-6">
        <span className="mono faint">{job.code}</span>
        <span className="row gap-4"><SlaBadge sla={job.sla} /><PriorityBadge priority={job.priority} /></span>
      </div>
      <strong className="pile-title">{job.title}</strong>
      <span className="small muted ellipsis">{job.customerName} · {job.city}</span>
      <div className="row between">
        <span className={`small${job.overdue ? " danger-text" : " muted"}`}>Due {relative(job.dueAt)} · {minutes(job.estimatedMinutes)}</span>
        <button className="button small" onClick={onPlan}>Plan</button>
      </div>
    </li>
  );
}

export function Board({ date: requested }: { date?: string }) {
  const company = useCompany();
  const date = requested ?? today();
  const [board, reload] = useLoad(() => api.board(company.id, date), [company.id, date]);
  const [sla, reloadSla] = useLoad(() => api.sla(company.id), [company.id]);
  const [planning, setPlanning] = useState<WorkOrderSummary | null>(null);
  const go = (d: string) => navigate(`/c/${company.id}/board?date=${d}`);

  const pins = useMemo<Pin[]>(() => {
    if (!board.data) return [];
    const techs = board.data.technicians;
    return [
      ...techs.map((t) => ({ id: `base-${t.userId}`, latitude: t.baseLatitude, longitude: t.baseLongitude, label: `${t.displayName} · ${t.baseLabel}`, text: initials(t.displayName), tone: "ink" as const })),
      ...techs.flatMap((t) => t.jobs.map((j, i) => ({
        id: j.id, latitude: j.latitude, longitude: j.longitude, label: `${formatTime(j.scheduledStart!)} ${j.code} · ${j.title} (${t.displayName})`, text: String(i + 1),
        tone: (j.sla === "Overdue" || j.sla === "AtRisk" ? "danger" : "accent") as Pin["tone"],
        onClick: () => navigate(`/c/${company.id}/orders/${j.id}`),
      }))),
      ...board.data.unscheduled.map((j) => ({ id: j.id, latitude: j.latitude, longitude: j.longitude, label: `${j.code} · ${j.title} (not planned)`, text: "?", tone: "muted" as const, onClick: () => setPlanning(j) })),
    ];
  }, [board.data, company.id]);

  const routes = useMemo(() => board.data?.technicians.filter((t) => t.jobs.length > 0).map((t) => ({
    points: [[t.baseLatitude, t.baseLongitude] as [number, number], ...t.jobs.map((j) => [j.latitude, j.longitude] as [number, number])],
    tone: "accent" as const,
  })) ?? [], [board.data]);

  const hours = Array.from({ length: (DAY_END - DAY_START) / 60 }, (_, i) => DAY_START / 60 + i);

  return (
    <div className="stack">
      <div className="page-head">
        <div>
          <h1>Board</h1>
          <div className="row gap-6 date-nav">
            <button className="icon-button" onClick={() => go(addDays(date, -1))} aria-label="Previous day"><ChevronLeft size={18} /></button>
            <strong>{formatDate(date)}</strong>
            <button className="icon-button" onClick={() => go(addDays(date, 1))} aria-label="Next day"><ChevronRight size={18} /></button>
            {date !== today() && <button className="link small" onClick={() => go(today())}>Today</button>}
            <input className="input date-input" type="date" value={date} onChange={(e) => e.target.value && go(e.target.value)} aria-label="Pick a day" />
          </div>
        </div>
        {sla.data && (sla.data.overdue > 0 || sla.data.atRisk > 0) && (
          <a className="sla-strip" {...linkProps(`/c/${company.id}/deadlines`)}>
            <AlertTriangle size={16} />
            {sla.data.overdue > 0 && <span><strong>{sla.data.overdue}</strong> overdue</span>}
            {sla.data.atRisk > 0 && <span><strong>{sla.data.atRisk}</strong> at risk</span>}
          </a>
        )}
      </div>

      {board.state === "failed" && !board.data ? <Failed message={board.error} onRetry={reload} /> : !board.data ? <Loading label="Loading the day" /> : (
        <div className="board">
          <section className="card pile" aria-label="Not planned yet">
            <div className="pile-head"><h2>To plan</h2><span className="badge plain">{board.data.unscheduled.length}</span></div>
            {board.data.unscheduled.length === 0
              ? <Empty title="Everything is planned" icon={<Inbox size={22} />}><span className="small">New jobs show up here.</span></Empty>
              : <ul className="pile-list">{board.data.unscheduled.map((j) => <PileCard key={j.id} job={j} onPlan={() => setPlanning(j)} />)}</ul>}
          </section>

          <section className="card timeline" aria-label="Technicians' day">
            {board.data.technicians.length === 0 ? (
              <Empty title="Nobody is on the board" icon={<UserRoundX size={22} />}>
                <span className="small muted">Give team members a technician profile with skills and a shift.</span>
                <a className="button small" {...linkProps(`/c/${company.id}/team`)}>Set up the team</a>
              </Empty>
            ) : (
              <div className="timeline-scroll">
                <div className="timeline-grid">
                  <div className="lane-head" />
                  <div className="hours" aria-hidden="true">{hours.map((h) => <span key={h} style={{ left: pct(h * 60) }}>{String(h).padStart(2, "0")}</span>)}</div>
                  {board.data.technicians.map((t) => (
                    <div className="lane-row" key={t.userId}>
                      <div className="lane-head">
                        <span className="avatar small-avatar">{initials(t.displayName)}</span>
                        <span className="min0">
                          <strong className="block ellipsis">{t.displayName}</strong>
                          <span className="small muted">{t.jobs.length} jobs · {t.loadPercent}%</span>
                        </span>
                      </div>
                      <Timeline tech={t} companyId={company.id} />
                    </div>
                  ))}
                </div>
              </div>
            )}
          </section>

          <section className="card board-map" aria-label="The day on the map">
            <MapView pins={pins} routes={routes} height={360} />
          </section>
        </div>
      )}

      {planning && <PlanDialog order={planning} date={date} onClose={() => setPlanning(null)} onDone={() => { setPlanning(null); reload(); reloadSla(); }} />}
    </div>
  );
}
