// Screenshots and a walkthrough for the README, against the running Compose stack, with a believable company.
// Usage: AUTH_ATTEMPTS_PER_MINUTE=100000 docker compose up --build -d
//        cd tests/e2e && node media.mjs [output folder]
import { chromium } from "playwright";
import { mkdirSync, writeFileSync } from "node:fs";

const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const OUT = process.argv[2] ?? new URL("./output/media/", import.meta.url).pathname;
const PHOTO = new URL("./fixtures/valve.jpg", import.meta.url).pathname;
mkdirSync(`${OUT}frames`, { recursive: true });
const run = Date.now().toString(36);
const password = "media-password-123";

async function call(path, token, body, method = body ? "POST" : "GET") {
  const response = await fetch(BASE + path, {
    method,
    headers: { ...(body ? { "Content-Type": "application/json" } : {}), ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  const text = await response.text();
  if (!response.ok) throw new Error(`${method} ${path}: ${response.status} ${text}`);
  return text ? JSON.parse(text) : null;
}

function nextWorkday() {
  const d = new Date(Date.now() + 3 * 3_600_000);
  do d.setUTCDate(d.getUTCDate() + 1); while (d.getUTCDay() === 0 || d.getUTCDay() === 6);
  return d.toISOString().slice(0, 10);
}
const day = nextWorkday();
const at = (hhmm) => `${day}T${hhmm}:00+03:00`;

// The company.
const owner = await call("/api/auth/register", null, { email: `deniz.${run}@example.com`, displayName: "Deniz Yılmaz", password });
const token = owner.accessToken;
const company = await call("/api/companies", token, { name: "Kuzey Klima Servis" });
const c = `/api/companies/${company.id}`;

const crew = {};
for (const [key, name, skills, lat, lon, label] of [
  ["ali", "Ali Usta", ["hvac", "plumbing"], 41.3335, 36.2714, "Atakum"],
  ["mert", "Mert Kaya", ["electrical", "hvac"], 41.2700, 36.3600, "Canik"],
  ["zeynep", "Zeynep Arslan", ["hvac"], 41.2867, 36.3300, "Depo"],
  ["emre", "Emre Doğan", ["plumbing"], 41.2300, 36.4300, "Tekkeköy"],
]) {
  const session = await call("/api/auth/register", null, { email: `${key}.${run}@example.com`, displayName: name, password });
  await call(`${c}/members`, token, { email: session.user.email, role: "Technician" });
  await call(`${c}/technicians/${session.user.id}`, token, { skills, baseLabel: label, baseLatitude: lat, baseLongitude: lon, shiftStart: "08:30", shiftEnd: "17:30", workDays: ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"], active: true }, "PUT");
  crew[key] = { id: session.user.id, token: session.accessToken, email: session.user.email, name };
}
const dispatcher = await call("/api/auth/register", null, { email: `selin.${run}@example.com`, displayName: "Selin Aydın", password });
await call(`${c}/members`, token, { email: dispatcher.user.email, role: "Dispatcher" });

const parts = {};
for (const [sku, name, price] of [
  ["VLV-22", "Emniyet ventili 1/2\"", 249.9], ["FLT-10", "Klima filtresi", 185], ["PMP-3", "Sirkülasyon pompası", 2450], ["SNS-1", "NTC sensör", 320],
  ["GAS-R32", "Soğutucu gaz R32 (kg)", 890], ["FUSE-16", "Sigorta 16A", 45], ["PIPE-16", "Bakır boru 16 mm (m)", 87.35], ["CAP-35", "Kondansatör 35 µF", 210],
]) parts[sku] = await call(`${c}/parts`, token, { sku, name, unitPrice: price });

const places = {};
for (const [key, name, phone, siteLabel, address, lat, lon, kind, brand, model, serial] of [
  ["atakum", "Atakum Apartmanı Yönetimi", "0362 438 12 40", "Kazan dairesi", "Cumhuriyet Cd. 12, Atakum", 41.3335, 36.2714, "Kombi", "Demirdöküm", "Nitromix", "DD-2291"],
  ["lider", "Lider Market", "0362 431 55 10", "Mağaza", "İstiklal Cd. 48, İlkadım", 41.2905, 36.3312, "Soğuk oda", "Frigoblock", "FK-200", "FB-7731"],
  ["otel", "Karadeniz Otel", "0362 445 20 20", "Teknik oda", "Atatürk Bulvarı 101, İlkadım", 41.2958, 36.3401, "Klima santrali", "Daikin", "D-AHU", "DK-0042"],
  ["eczane", "Deniz Eczanesi", "0362 432 77 01", "Eczane", "Kale Mh. Bankalar Cd. 3", 41.2885, 36.3355, "Split klima", "Mitsubishi", "MSZ-HR35", "MT-5520"],
  ["spor", "Samsun Spor Salonu", "0362 465 09 90", "Duş alanı", "Kurupelit Mh. Üniversite Cd. 5", 41.3610, 36.2050, "Sıcak su kazanı", "Bosch", "GC7000", "BS-1189"],
  ["firin", "Güneş Fırını", "0362 233 41 18", "Fırın", "Canik Bulvarı 22, Canik", 41.2710, 36.3540, "Pano", "Schneider", "Prisma", "SC-3301"],
  ["plaza", "Yıldız Ofis Plaza", "0362 420 60 60", "3. kat", "Çiftlik Cd. 70, İlkadım", 41.2842, 36.3352, "VRF dış ünite", "LG", "Multi V", "LG-9902"],
  ["kafe", "Mavi Kafe", "0362 444 18 18", "Mutfak", "Sahil Yolu 14, Atakum", 41.3420, 36.2580, "Bulaşık makinesi", "Winterhalter", "PT-M", "WH-2210"],
]) {
  const customer = await call(`${c}/customers`, token, { name, phone });
  const site = await call(`${c}/customers/${customer.id}/sites`, token, { label: siteLabel, address, city: "Samsun", latitude: lat, longitude: lon, accessNotes: key === "atakum" ? "Anahtar kapıcıda, arka kapıdan girin." : "" });
  const equipment = await call(`${c}/customers/${customer.id}/sites/${site.id}/equipment`, token, { kind, brand, model, serialNumber: serial, installedOn: "2023-04-01", warrantyUntil: key === "atakum" || key === "plaza" ? "2027-04-01" : "2025-04-01" });
  places[key] = { customer, site, equipment };
}

async function order(key, title, priority, skill, minutes, checklist, description = "") {
  const p = places[key];
  return call(`${c}/work-orders`, token, { customerId: p.customer.id, siteId: p.site.id, equipmentId: p.equipment.id, title, description, priority, requiredSkill: skill, estimatedMinutes: minutes, checklist });
}
const schedule = (o, who, hhmm) => call(`${c}/work-orders/${o.id}/schedule`, token, { technicianId: crew[who].id, start: at(hhmm) });
const push = (who, ops) => call(`${c}/sync/push`, crew[who].token, { operations: ops.map((op) => ({ id: crypto.randomUUID(), at: new Date().toISOString(), ...op })) });

const boiler = await order("atakum", "Kombi su kaçırıyor", "High", "hvac", 90, ["Basıncı ölç", "Gaz kaçağı testi", "Emniyet ventilini kontrol et"], "Kombinin altından su damlıyor, basınç 0,5 bara düşüyor. Yönetici sabah 9'dan sonra burada.");
const gym = await order("spor", "Sıcak su gelmiyor", "Urgent", "plumbing", 60, ["Kazan basıncı", "Sirkülasyon pompası"], "Duşlarda sabahtan beri sıcak su yok.");
const market = await order("lider", "Soğuk oda ısınıyor", "Urgent", "hvac", 120, ["Gaz basıncı", "Fan motoru", "Termostat"], "Soğuk oda 8 °C'ye çıktı, ürünler risk altında.");
const panel = await order("firin", "Pano sigortası atıyor", "High", "electrical", 60, ["Kaçak akım testi", "Sigorta değişimi"]);
const hotel = await order("otel", "Klima santrali yıllık bakım", "Normal", "hvac", 150, ["Filtreler", "Kayışlar", "Drenaj"]);
const plaza = await order("plaza", "VRF arıza kodu E-104", "High", "hvac", 90, ["Arıza kodunu oku", "Sensörleri kontrol et"]);
const pharmacy = await order("eczane", "Klima filtre değişimi", "Low", "hvac", 45, ["Filtre değişimi"]);
const cafe = await order("kafe", "Bulaşık makinesi su almıyor", "Normal", "plumbing", 60, ["Su girişi", "Selenoid valf"]);
await order("plaza", "Toplantı odası kliması ses yapıyor", "Normal", "hvac", 60, ["Fan kontrolü"]);
await order("eczane", "Dış ünite titreşim", "Low", "hvac", 45, []);
const urgentPile = await order("otel", "Mutfak havalandırması durdu", "Urgent", "electrical", 60, ["Motor", "Kontaktör"], "Mutfak dumanla doldu, havalandırma çalışmıyor.");

await schedule(boiler, "ali", "08:50");
await schedule(cafe, "ali", "11:00");
await schedule(gym, "emre", "09:30");
await schedule(panel, "mert", "09:00");
await schedule(hotel, "zeynep", "10:00");
await schedule(market, "zeynep", "13:30");
await schedule(plaza, "mert", "13:00");
await schedule(pharmacy, "ali", "14:30");
await call(`${c}/work-orders/${urgentPile.id}/notes`, token, { text: "Müşteri iki kez aradı." });

// Some of the day is already done on the phones.
const line = crypto.randomUUID();
await push("ali", [
  { workOrderId: boiler.id, type: "StatusChanged", status: "EnRoute" },
  { workOrderId: boiler.id, type: "StatusChanged", status: "InProgress" },
  ...boiler.checklist.map((i) => ({ workOrderId: boiler.id, type: "ChecklistToggled", itemId: i.id, done: true })),
  { workOrderId: boiler.id, type: "PartUsed", lineId: line, partId: parts["VLV-22"].id, quantity: 1 },
  { workOrderId: boiler.id, type: "PartUsed", lineId: crypto.randomUUID(), partId: parts["PIPE-16"].id, quantity: 1.5 },
  { workOrderId: boiler.id, type: "NoteAdded", text: "Eski ventil paslanmış; bağlantı borusu da yenilendi." },
  { workOrderId: boiler.id, type: "Signature", signedBy: "Hasan Yılmaz", strokes: [[[0.08, 0.62], [0.14, 0.38], [0.2, 0.66], [0.27, 0.35], [0.33, 0.64]], [[0.4, 0.55], [0.48, 0.42], [0.57, 0.58], [0.66, 0.4], [0.75, 0.57], [0.92, 0.45]]] },
]);
const photoId = crypto.randomUUID();
await fetch(`${BASE}${c}/work-orders/${boiler.id}/photos/${photoId}`, { method: "PUT", headers: { Authorization: `Bearer ${crew.ali.token}`, "Content-Type": "image/jpeg" }, body: (await import("node:fs")).readFileSync(PHOTO) });
await push("ali", [
  { workOrderId: boiler.id, type: "PhotoAdded", attachmentId: photoId, caption: "Paslı emniyet ventili" },
  { workOrderId: boiler.id, type: "StatusChanged", status: "Completed", resolution: "Emniyet ventili ve 1,5 m bakır boru değişti. Basınç 1,5 bar, kaçak yok." },
]);
await push("mert", [{ workOrderId: panel.id, type: "StatusChanged", status: "EnRoute" }]);
await push("emre", [{ workOrderId: gym.id, type: "StatusChanged", status: "InProgress" }]);
await push("zeynep", [{ workOrderId: hotel.id, type: "StatusChanged", status: "InProgress" }, { workOrderId: hotel.id, type: "ChecklistToggled", itemId: hotel.checklist[0].id, done: true }]);
writeFileSync(`${OUT}report.pdf`, Buffer.from(await (await fetch(`${BASE}${c}/work-orders/${boiler.id}/report`, { headers: { Authorization: `Bearer ${token}` } })).arrayBuffer()));

// The screens.
const browser = await chromium.launch();
async function signIn(email, viewport, mobile = false) {
  const context = await browser.newContext({ viewport, deviceScaleFactor: 2, locale: "en-GB", timezoneId: "Europe/Istanbul", isMobile: mobile, hasTouch: mobile });
  const page = await context.newPage();
  await page.goto(`${BASE}/signin`);
  await page.getByLabel("Email").fill(email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await page.waitForURL((url) => !url.pathname.startsWith("/signin"));
  return page;
}
const settle = async (page) => {
  await page.waitForLoadState("networkidle").catch(() => {});
  await page.waitForTimeout(600);
};
let frame = 0;
const snap = async (page, name, options = {}) => {
  await settle(page);
  await page.screenshot({ path: `${OUT}${name}.png`, ...options });
};
const still = async (page, label) => {
  await settle(page);
  await page.screenshot({ path: `${OUT}frames/${String(++frame).padStart(2, "0")}-${label}.png` });
};

const desk = await signIn(dispatcher.user.email, { width: 1440, height: 900 });
await desk.goto(`${BASE}/c/${company.id}/board?date=${day}`);
await desk.locator(".leaflet-tile-loaded").first().waitFor();
await snap(desk, "board");
await still(desk, "board");

await desk.getByRole("button", { name: "Plan" }).first().click();
await desk.getByRole("dialog").getByRole("button", { name: /^Book/ }).first().waitFor();
await snap(desk, "plan");
await still(desk, "plan");
await desk.getByRole("dialog").getByRole("button", { name: /^Book/ }).first().click();
await desk.getByRole("dialog").waitFor({ state: "detached" });
await still(desk, "booked");

await desk.goto(`${BASE}/c/${company.id}/orders/${boiler.id}`);
await desk.locator(".photo img").first().waitFor();
await snap(desk, "work-order", { fullPage: true });

await desk.goto(`${BASE}/c/${company.id}/deadlines`);
await snap(desk, "deadlines");
await desk.goto(`${BASE}/c/${company.id}/map`);
await desk.locator(".leaflet-tile-loaded").first().waitFor();
await snap(desk, "map");
await desk.goto(`${BASE}/c/${company.id}/orders`);
await snap(desk, "work-orders");
await desk.goto(`${BASE}/c/${company.id}/customers/${places.atakum.customer.id}`);
await snap(desk, "customer");

// The phone: Ali's day, a job in progress, and work queued while offline.
const phone = await signIn(crew.ali.email, { width: 390, height: 844 }, true);
await phone.goto(`${BASE}/tech`);
await phone.getByText(/^Synced/).waitFor();
await snap(phone, "tech-agenda");
await still(phone, "phone-agenda");
await phone.getByRole("link", { name: /Bulaşık makinesi/ }).click();
await phone.getByRole("button", { name: "On my way" }).waitFor();
await phone.context().setOffline(true);
await phone.getByRole("button", { name: "On my way" }).click();
await phone.getByRole("button", { name: "Start work" }).click();
await phone.getByRole("button", { name: "Su girişi" }).click();
await phone.getByLabel("Part").selectOption({ label: "VLV-22 · Emniyet ventili 1/2\"" });
await phone.getByRole("button", { name: "Add", exact: true }).first().click();
await phone.getByText(/^Offline · \d+ waiting$/).waitFor();
await still(phone, "phone-offline");
await snap(phone, "tech-offline");
await phone.getByRole("button", { name: "Selenoid valf" }).click();
await phone.getByLabel("Work done").fill("Selenoid valf temizlendi, su girişi normal.");
await phone.getByLabel("Note").click();
await phone.getByRole("button", { name: "Ask the customer to sign" }).click();
await phone.getByLabel("Customer's name").fill("Ayşe Demir");
const pad = await phone.locator("canvas.pad").boundingBox();
await phone.mouse.move(pad.x + 30, pad.y + 110);
await phone.mouse.down();
for (const [x, y] of [[70, 50], [110, 120], [160, 60], [210, 115], [260, 55], [320, 100]]) await phone.mouse.move(pad.x + x, pad.y + y, { steps: 6 });
await phone.mouse.up();
await phone.getByRole("button", { name: "Save signature" }).click();
await still(phone, "phone-signed");
// A tall phone rather than a full-page capture, which would paint the sticky bar in the middle.
await phone.setViewportSize({ width: 390, height: 1900 });
await phone.evaluate(() => window.scrollTo(0, 0));
await snap(phone, "tech-job");
await phone.setViewportSize({ width: 390, height: 844 });
await phone.getByRole("button", { name: "Complete" }).click();
await phone.context().setOffline(false);
await phone.getByText(/^Synced/).waitFor({ timeout: 20_000 });
await still(phone, "phone-synced");

await desk.goto(`${BASE}/c/${company.id}/board?date=${day}`);
await desk.locator(".leaflet-tile-loaded").first().waitFor();
await still(desk, "board-after");
await desk.goto(`${BASE}/c/${company.id}/orders/${cafe.id}`);
await still(desk, "order-after");

// The sign-in page, for the top of the README.
const anonymous = await (await browser.newContext({ viewport: { width: 1440, height: 900 }, deviceScaleFactor: 2 })).newPage();
await anonymous.goto(`${BASE}/signin`);
await snap(anonymous, "sign-in");

await browser.close();
console.log(`media written to ${OUT} (day ${day})`);
