import type { Priority, SlaState, Status } from "./types";

/** The company works in Istanbul time; screens show its wall clock wherever the browser is. */
export const ZONE = "Europe/Istanbul";

const money = new Intl.NumberFormat("tr-TR", { style: "currency", currency: "TRY", minimumFractionDigits: 2 });
const time = new Intl.DateTimeFormat("en-GB", { hour: "2-digit", minute: "2-digit", timeZone: ZONE });
const day = new Intl.DateTimeFormat("en-GB", { weekday: "short", day: "numeric", month: "short", timeZone: ZONE });
const dayTime = new Intl.DateTimeFormat("en-GB", { day: "numeric", month: "short", hour: "2-digit", minute: "2-digit", timeZone: ZONE });
const longDay = new Intl.DateTimeFormat("en-GB", { weekday: "long", day: "numeric", month: "long", year: "numeric", timeZone: "UTC" });

export const formatMoney = (value: number) => money.format(value);
export const formatTime = (iso: string) => time.format(new Date(iso));
export const formatDay = (iso: string) => day.format(new Date(iso));
export const formatDayTime = (iso: string) => dayTime.format(new Date(iso));
/** A calendar date such as "2026-11-05", which has no time zone. */
export const formatDate = (date: string) => longDay.format(new Date(`${date}T00:00:00Z`));

export function initials(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  return ((words[0]?.[0] ?? "") + (words.length > 1 ? words[words.length - 1]![0] : "")).toLocaleUpperCase("tr-TR") || "?";
}

export function minutes(total: number): string {
  const h = Math.floor(total / 60);
  const m = total % 60;
  return h === 0 ? `${m} min` : m === 0 ? `${h} h` : `${h} h ${m} min`;
}

/** "in 3 h", "2 days ago": how far a moment is from now, roughly. */
export function relative(iso: string, now = Date.now()): string {
  const diff = new Date(iso).getTime() - now;
  const abs = Math.abs(diff);
  const unit = abs < 3_600_000 ? [Math.round(abs / 60_000), "min"] as const : abs < 172_800_000 ? [Math.round(abs / 3_600_000), "h"] as const : [Math.round(abs / 86_400_000), "days"] as const;
  const text = `${Math.max(1, unit[0])} ${unit[1]}`;
  return diff >= 0 ? `in ${text}` : `${text} ago`;
}

/** Today's date in the company's zone, as YYYY-MM-DD. */
export function today(now = new Date()): string {
  return new Intl.DateTimeFormat("en-CA", { timeZone: ZONE }).format(now);
}

export function addDays(date: string, days: number): string {
  const d = new Date(`${date}T12:00:00Z`);
  d.setUTCDate(d.getUTCDate() + days);
  return d.toISOString().slice(0, 10);
}

/** An instant from a wall-clock time in the company's zone. Istanbul has kept UTC+3 all year since 2016. */
export function zonedInstant(date: string, hhmm: string): string {
  return new Date(`${date}T${hhmm}:00+03:00`).toISOString();
}

/** The company's wall-clock time of an instant, as HH:MM, for a time input. */
export function wallClock(iso: string): string {
  return formatTime(iso);
}

export const statusLabel: Record<Status, string> = {
  New: "New", Scheduled: "Scheduled", EnRoute: "En route", InProgress: "In progress", OnHold: "On hold", Completed: "Completed", Cancelled: "Cancelled",
};

export const statusTone: Record<Status, "plain" | "info" | "ok" | "danger" | "accent"> = {
  New: "plain", Scheduled: "accent", EnRoute: "info", InProgress: "info", OnHold: "danger", Completed: "ok", Cancelled: "plain",
};

export const priorityTone: Record<Priority, "plain" | "info" | "danger"> = { Low: "plain", Normal: "plain", High: "info", Urgent: "danger" };

export const slaLabel: Record<SlaState, string> = {
  None: "—", OnTrack: "On track", AtRisk: "At risk", Overdue: "Overdue", Met: "Met", Breached: "Missed",
};

export const slaTone: Record<SlaState, "plain" | "info" | "ok" | "danger"> = {
  None: "plain", OnTrack: "plain", AtRisk: "info", Overdue: "danger", Met: "ok", Breached: "danger",
};

export const isOpen = (status: Status) => status !== "Completed" && status !== "Cancelled";

export const plural = (n: number, word: string, many = `${word}s`) => `${n} ${n === 1 ? word : many}`;

export const roleHint = {
  Technician: "Does the jobs on site with the technician app",
  Dispatcher: "Also takes in jobs, plans the board, keeps customers and parts",
  Owner: "Also manages the team",
} as const;

/** Minutes since midnight on the company's wall clock (UTC+3). */
export function localMinutes(iso: string): number {
  const d = new Date(new Date(iso).getTime() + 3 * 3_600_000);
  return d.getUTCHours() * 60 + d.getUTCMinutes();
}
