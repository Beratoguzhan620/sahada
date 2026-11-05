// A busy afternoon, measured against the running stack: a crew of technicians coming back online at once and
// sending a day of offline work, retries included, while the office cancels jobs under them.
// Usage: AUTH_ATTEMPTS_PER_MINUTE=100000 docker compose up --build -d
//        node tests/load/load.mjs [technicians=40] [jobsEach=5]
const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const TECHS = Number(process.argv[2] ?? 40);
const JOBS = Number(process.argv[3] ?? 5);
const run = Date.now().toString(36);
const password = "load-test-password";

async function call(path, token, body, method = body ? "POST" : "GET") {
  const started = performance.now();
  const response = await fetch(BASE + path, {
    method,
    headers: { ...(body ? { "Content-Type": "application/json" } : {}), ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await response.text();
  const ms = performance.now() - started;
  let json = null;
  try { json = text ? JSON.parse(text) : null; } catch { json = null; }
  return { status: response.status, json, ms };
}

async function must(path, token, body, method) {
  const r = await call(path, token, body, method);
  if (r.status >= 400) throw new Error(`${method ?? (body ? "POST" : "GET")} ${path}: ${r.status} ${JSON.stringify(r.json)}`);
  return r.json;
}

async function pool(items, limit, task) {
  const results = new Array(items.length);
  let next = 0;
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, async () => {
    while (next < items.length) {
      const i = next++;
      results[i] = await task(items[i], i);
    }
  }));
  return results;
}

function stats(values) {
  const sorted = [...values].sort((a, b) => a - b);
  const at = (p) => sorted[Math.min(sorted.length - 1, Math.floor((p / 100) * sorted.length))];
  return { count: sorted.length, p50: Math.round(at(50)), p95: Math.round(at(95)), p99: Math.round(at(99)), max: Math.round(sorted.at(-1) ?? 0) };
}

const report = (name, data) => console.log(`${name.padEnd(30)} ${JSON.stringify(data)}`);

function nextWorkday() {
  const d = new Date(Date.now() + 3 * 3_600_000);
  do d.setUTCDate(d.getUTCDate() + 1); while (d.getUTCDay() === 0 || d.getUTCDay() === 6);
  return d.toISOString().slice(0, 10);
}

// The company: an owner, a customer with a site, a part, and a crew spread over the city with their day booked.
const owner = await must("/api/auth/register", null, { email: `owner.${run}@example.com`, displayName: "Yük Testi", password });
const token = owner.accessToken;
const company = await must("/api/companies", token, { name: "Yük Testi Servis" });
const c = `/api/companies/${company.id}`;
const customer = await must(`${c}/customers`, token, { name: "Yük Müşterisi" });
const site = await must(`${c}/customers/${customer.id}/sites`, token, { label: "Merkez", address: "Cumhuriyet Cd. 1", city: "Samsun", latitude: 41.2867, longitude: 36.33 });
const part = await must(`${c}/parts`, token, { sku: "FLT-1", name: "Filtre", unitPrice: 85 });
const day = nextWorkday();

const crew = await pool(Array.from({ length: TECHS }, (_, i) => i), 16, async (i) => {
  const session = await must("/api/auth/register", null, { email: `tech${i}.${run}@example.com`, displayName: `Teknisyen ${i + 1}`, password });
  await must(`${c}/members`, token, { email: session.user.email, role: "Technician" });
  await must(`${c}/technicians/${session.user.id}`, token, {
    skills: ["hvac"], baseLabel: "Ev", baseLatitude: 41.2 + (i % 10) * 0.02, baseLongitude: 36.2 + Math.floor(i / 10) * 0.03,
    shiftStart: "08:00", shiftEnd: "18:00", workDays: ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"], active: true,
  }, "PUT");
  return { id: session.user.id, token: session.accessToken, jobs: [] };
});

const setup = performance.now();
await pool(crew.flatMap((t) => Array.from({ length: JOBS }, (_, j) => [t, j])), 24, async ([t, j]) => {
  const order = await must(`${c}/work-orders`, token, {
    customerId: customer.id, siteId: site.id, title: `İş ${j + 1}`, priority: "Normal", requiredSkill: "hvac", estimatedMinutes: 60, checklist: ["Kontrol", "Test"],
  });
  const hour = String(8 + j * 2).padStart(2, "0");
  await must(`${c}/work-orders/${order.id}/schedule`, token, { technicianId: t.id, start: `${day}T${hour}:00:00+03:00` });
  t.jobs.push(order);
});
report("setup", { technicians: TECHS, jobs: TECHS * JOBS, seconds: Math.round((performance.now() - setup) / 100) / 10 });

// Suggestions with the whole crew booked: who could still take one more job that day.
const extra = await must(`${c}/work-orders`, token, { customerId: customer.id, siteId: site.id, title: "Ek iş", priority: "High", requiredSkill: "hvac", estimatedMinutes: 45 });
const suggestionTimes = [];
let suggestions = null;
for (let i = 0; i < 20; i++) {
  const r = await call(`${c}/work-orders/${extra.id}/suggestions?date=${day}`, token);
  suggestionTimes.push(r.ms);
  suggestions = r.json;
}
report("suggestions (whole crew)", { ...stats(suggestionTimes), ranked: suggestions.technicians.length, eligible: suggestions.technicians.filter((s) => s.eligible).length });

// First pulls, as every phone opens the app in the morning.
const firstPulls = await pool(crew, 40, async (t) => {
  const r = await call(`${c}/sync/pull`, t.token);
  t.cursor = r.json.cursor;
  return r;
});
report("first pull", { ...stats(firstPulls.map((r) => r.ms)), jobsEach: firstPulls[0].json.orders.length });

// A day of offline work per technician: 8 operations per job, sent in pushes of 10.
const now = Date.now();
for (const t of crew) {
  t.ops = t.jobs.flatMap((o, j) => {
    const at = (m) => new Date(now - (JOBS - j) * 3_600_000 + m * 60_000).toISOString();
    const op = (type, minute, fields) => ({ id: crypto.randomUUID(), workOrderId: o.id, type, at: at(minute), ...fields });
    return [
      op("StatusChanged", 0, { status: "EnRoute" }),
      op("StatusChanged", 10, { status: "InProgress" }),
      op("ChecklistToggled", 20, { itemId: o.checklist[0].id, done: true }),
      op("ChecklistToggled", 25, { itemId: o.checklist[1].id, done: true }),
      op("PartUsed", 30, { lineId: crypto.randomUUID(), partId: part.id, quantity: 1 }),
      op("NoteAdded", 35, { text: "Filtre değişti." }),
      op("Signature", 40, { signedBy: "Müşteri", strokes: [[[0.1, 0.5], [0.5, 0.2], [0.9, 0.6]]] }),
      op("StatusChanged", 45, { status: "Completed", resolution: "Bakım yapıldı." }),
    ];
  });
}

// While the phones push, the office cancels one job in four.
const toCancel = crew.flatMap((t) => t.jobs.filter((_, j) => j % 4 === 3));
const pushTimes = [];
const outcomes = {};
let duplicates = 0;
let cancelled = 0;
const storm = performance.now();
await Promise.all([
  pool(crew, TECHS, async (t) => {
    for (let i = 0; i < t.ops.length; i += 10) {
      const batch = t.ops.slice(i, i + 10);
      const r = await call(`${c}/sync/push`, t.token, { operations: batch });
      if (r.status !== 200) throw new Error(`push: ${r.status} ${JSON.stringify(r.json)}`);
      pushTimes.push(r.ms);
      for (const result of r.json.results) outcomes[result.outcome] = (outcomes[result.outcome] ?? 0) + 1;
    }
    // A flaky connection: the first push is sent again. Every answer must be the one given before.
    const retry = await call(`${c}/sync/push`, t.token, { operations: t.ops.slice(0, 10) });
    duplicates += retry.json.results.filter((r) => r.duplicate).length;
  }),
  // A job the phone already completed can no longer be cancelled (409); the rest are.
  pool(toCancel, 4, async (o) => { if ((await call(`${c}/work-orders/${o.id}/cancel`, token, { reason: "Müşteri iptal etti" })).status === 200) cancelled++; }),
]);
const seconds = (performance.now() - storm) / 1000;
const total = crew.reduce((n, t) => n + t.ops.length, 0);
report("push storm", { operations: total, pushes: pushTimes.length, ...stats(pushTimes), opsPerSecond: Math.round(total / seconds), outcomes });
report("retries", { sent: TECHS * 10, answeredAsDuplicates: duplicates });

// What the database says afterwards: every job is either completed or cancelled, every part line exists once.
const all = await must(`${c}/work-orders?status=Completed&status=Cancelled&pageSize=1`, token);
const open = await must(`${c}/work-orders?status=New&status=Scheduled&status=EnRoute&status=InProgress&status=OnHold&pageSize=1`, token);
let lines = 0;
let flagged = 0;
await pool(crew.flatMap((t) => t.jobs), 16, async (o) => {
  const detail = await must(`${c}/work-orders/${o.id}`, token);
  lines += detail.parts.length;
  flagged += detail.flagged;
});
report("afterwards", {
  finished: all.total, stillOpen: open.total - 1, partLines: lines, partUsedApplied: TECHS * JOBS, flaggedEntries: flagged, cancelled, cancelAttempts: toCancel.length,
});

// Every phone pulls again with its cursor: only what changed comes back.
const secondPulls = await pool(crew, 40, (t) => call(`${c}/sync/pull?cursor=${t.cursor}`, t.token));
report("pull with cursor", { ...stats(secondPulls.map((r) => r.ms)), changedEach: secondPulls[0].json.orders.length });
const quiet = await pool(crew, 40, async (t, i) => call(`${c}/sync/pull?cursor=${secondPulls[i].json.cursor}`, t.token));
report("pull, nothing new", { ...stats(quiet.map((r) => r.ms)), changed: quiet.reduce((n, r) => n + r.json.orders.length, 0) });
