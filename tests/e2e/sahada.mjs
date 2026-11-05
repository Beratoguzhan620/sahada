// A dispatcher at a desk and a technician on a phone, in separate browsers, against the whole Compose stack.
// The phone loses its signal halfway through the job and syncs when it comes back.
// Usage: AUTH_ATTEMPTS_PER_MINUTE=1000 docker compose up --build -d
//        cd tests/e2e && npm install && npx playwright install chromium && node sahada.mjs
import { chromium } from "playwright";
import { mkdirSync, readFileSync } from "node:fs";

const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const OUT = new URL("./output/", import.meta.url).pathname;
const PHOTO = new URL("./fixtures/valve.jpg", import.meta.url).pathname;
mkdirSync(OUT, { recursive: true });

const run = Date.now().toString(36);
const owner = { name: "Deniz Yılmaz", email: `deniz.${run}@example.com`, password: "e2e-test-password" };
const tech = { name: "Ali Usta", email: `ali.${run}@example.com`, password: "e2e-test-password" };
const title = `Kombi su kaçırıyor ${run}`;

/** The next weekday after today in Istanbul, as YYYY-MM-DD: technicians work Monday to Friday. */
function nextWorkday() {
  const d = new Date(Date.now() + 3 * 3_600_000);
  do d.setUTCDate(d.getUTCDate() + 1); while (d.getUTCDay() === 0 || d.getUTCDay() === 6);
  return d.toISOString().slice(0, 10);
}
const day = nextWorkday();

const browser = await chromium.launch();
const errors = [];
let failures = 0;

async function step(name, action) {
  try {
    await action();
    console.log(`ok   ${name}`);
  } catch (error) {
    failures++;
    console.log(`FAIL ${name}\n     ${String(error.message ?? error).split("\n")[0]}`);
    for (const [label, page] of [["desk", desk], ["phone", phone]]) await page?.screenshot({ path: `${OUT}${label}-${failures}.png`, fullPage: true }).catch(() => {});
  }
}

async function open(viewport, mobile = false) {
  const context = await browser.newContext({ viewport, locale: "en-GB", timezoneId: "Europe/Istanbul", isMobile: mobile, hasTouch: mobile });
  const page = await context.newPage();
  page.on("pageerror", (error) => errors.push(error.message));
  page.on("console", (message) => {
    if (message.type() === "error" && !/Failed to load resource|net::ERR_INTERNET_DISCONNECTED|tile\.openstreetmap/.test(message.text())) errors.push(message.text());
  });
  return page;
}

async function api(page, method, path, body) {
  return page.evaluate(async ({ method, path, body }) => {
    const session = JSON.parse(localStorage.getItem("sahada.session"));
    const response = await fetch(path, { method, headers: { "Content-Type": "application/json", Authorization: `Bearer ${session.accessToken}` }, body: body ? JSON.stringify(body) : undefined });
    if (!response.ok) throw new Error(`${method} ${path}: ${response.status} ${await response.text()}`);
    return response.status === 204 ? null : response.json();
  }, { method, path, body });
}

const desk = await open({ width: 1366, height: 900 });
const phone = await open({ width: 390, height: 844 }, true);
let companyPath = "";
let orderUrl = "";
let orderId = "";
let otherId = "";

await step("the technician has an account", async () => {
  const response = await fetch(`${BASE}/api/auth/register`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify({ displayName: tech.name, email: tech.email, password: tech.password }) });
  if (!response.ok) throw new Error(`register: ${response.status}`);
});

await step("an owner signs up, creates a company and puts the technician on the board", async () => {
  await desk.goto(`${BASE}/`);
  await desk.getByRole("heading", { name: "Sign in" }).waitFor();
  await desk.getByRole("link", { name: "Create an account" }).click();
  await desk.getByLabel("Your name").fill(owner.name);
  await desk.getByLabel("Email").fill(owner.email);
  await desk.getByLabel("Password").fill(owner.password);
  await desk.getByRole("button", { name: "Create account" }).click();
  await desk.getByRole("heading", { name: "Your companies" }).waitFor();
  await desk.getByLabel("Name").fill("Kuzey Klima Servis");
  await desk.getByRole("button", { name: "Create company" }).click();
  await desk.getByRole("heading", { name: "Team" }).waitFor();
  companyPath = new URL(desk.url()).pathname.replace(/\/team$/, "");
  await desk.getByLabel("Email of their Sahada account").fill(tech.email);
  await desk.getByRole("button", { name: "Add", exact: true }).click();
  const dialog = desk.getByRole("dialog", { name: `${tech.name} on the board` });
  await dialog.getByLabel("Skills").fill("hvac, plumbing");
  await dialog.getByRole("button", { name: "Save" }).click();
  await desk.getByText("08:30–17:30 · Mo Tu We Th Fr · Depot").waitFor();
});

await step("they add a customer with a site, a boiler and a part", async () => {
  await desk.getByRole("link", { name: "Customers" }).first().click();
  await desk.getByRole("button", { name: "New customer" }).click();
  let dialog = desk.getByRole("dialog", { name: "New customer" });
  await dialog.getByLabel("Name").fill("Atakum Apartmanı Yönetimi");
  await dialog.getByLabel("Phone").fill("0362 000 00 00");
  await dialog.getByRole("button", { name: "Add customer" }).click();
  await desk.getByRole("heading", { name: "Atakum Apartmanı Yönetimi" }).waitFor();
  await desk.getByRole("button", { name: "Add site" }).click();
  dialog = desk.getByRole("dialog", { name: "New site" });
  await dialog.getByLabel("Label").fill("Kazan dairesi");
  await dialog.getByLabel("Address", { exact: true }).fill("Cumhuriyet Cd. 12, Atakum");
  await dialog.getByLabel("Latitude").fill("41.3335");
  await dialog.getByLabel("Longitude").fill("36.2714");
  await dialog.getByRole("button", { name: "Add site" }).click();
  await desk.getByText("Cumhuriyet Cd. 12, Atakum, Samsun").waitFor();
  await desk.getByRole("button", { name: "Equipment" }).click();
  dialog = desk.getByRole("dialog");
  await dialog.getByLabel("Kind").fill("Kombi");
  await dialog.getByLabel("Brand").fill("Demirdöküm");
  await dialog.getByLabel("Serial number").fill("DD-2291");
  await dialog.getByRole("button", { name: "Add equipment" }).click();
  await desk.getByText("DD-2291").waitFor();

  await desk.goto(`${BASE}${companyPath}/parts`);
  await desk.getByRole("button", { name: "New part" }).click();
  dialog = desk.getByRole("dialog", { name: "New part" });
  await dialog.getByLabel("SKU").fill("vlv-22");
  await dialog.getByLabel("Price (TRY)").fill("249.90");
  await dialog.getByLabel("Name").fill("Emniyet ventili");
  await dialog.getByRole("button", { name: "Add part" }).click();
  await desk.getByRole("cell", { name: "VLV-22", exact: true }).waitFor();
});

await step("they open a work order with a checklist", async () => {
  await desk.goto(`${BASE}${companyPath}/customers`);
  await desk.getByRole("link", { name: "Atakum Apartmanı Yönetimi" }).click();
  await desk.getByRole("link", { name: "New work order" }).click();
  await desk.getByText("Atakum Apartmanı Yönetimi").first().waitFor();
  await desk.getByLabel("Title").fill(title);
  await desk.getByLabel("What the customer reported").fill("Alttan damlıyor, basınç düşüyor.");
  await desk.getByLabel("Priority").selectOption("Normal");
  await desk.getByLabel("Skill needed").fill("hvac");
  await desk.getByLabel("Step 1", { exact: true }).fill("Basıncı ölç");
  await desk.getByRole("button", { name: "Add a step" }).click();
  await desk.getByLabel("Step 2", { exact: true }).fill("Gaz kaçağı testi");
  await desk.getByRole("button", { name: "Open work order" }).click();
  await desk.getByRole("heading", { name: title }).waitFor();
  await desk.getByText("SO-01001").waitFor();
  orderUrl = desk.url();
  orderId = orderUrl.split("/").pop();
});

await step("the board suggests the technician and books the slot", async () => {
  await desk.goto(`${BASE}${companyPath}/board?date=${day}`);
  await desk.getByRole("heading", { name: "To plan" }).waitFor();
  await desk.getByRole("button", { name: "Plan" }).first().click();
  const dialog = desk.getByRole("dialog", { name: "Plan SO-01001" });
  await dialog.getByText(tech.name, { exact: true }).first().waitFor();
  await dialog.getByText('Has "hvac"').waitFor();
  await dialog.getByRole("button", { name: /^Book \d\d:\d\d$/ }).first().click();
  await desk.getByRole("listitem").filter({ hasText: title }).first().waitFor();
  await desk.getByText("Everything is planned").waitFor();
});

await step("a second job is planned for later the same day", async () => {
  const other = await api(desk, "POST", `/api${companyPath.replace("/c/", "/companies/")}/work-orders`, {
    customerId: (await api(desk, "GET", `/api${companyPath.replace("/c/", "/companies/")}/customers`)).items[0].id,
    siteId: (await api(desk, "GET", `/api${companyPath.replace("/c/", "/companies/")}/work-orders/${orderId}`)).site.id,
    title: "Yıllık bakım", priority: "Low", estimatedMinutes: 60, requiredSkill: "hvac", checklist: [],
  });
  const me = await api(desk, "GET", `/api${companyPath.replace("/c/", "/companies/")}/technicians`);
  await api(desk, "POST", `/api${companyPath.replace("/c/", "/companies/")}/work-orders/${other.id}/schedule`, { technicianId: me[0].userId, start: `${day}T15:00:00+03:00` });
  otherId = other.id;
});

await step("the technician signs in on the phone and gets both jobs", async () => {
  await phone.goto(`${BASE}/tech`);
  await phone.getByRole("heading", { name: "Sign in" }).waitFor();
  await phone.getByLabel("Email").fill(tech.email);
  await phone.getByLabel("Password").fill(tech.password);
  await phone.getByRole("button", { name: "Sign in" }).click();
  await phone.getByRole("heading", { name: "My jobs" }).waitFor();
  await phone.getByRole("link", { name: new RegExp(title) }).waitFor();
  await phone.getByRole("link", { name: /Yıllık bakım/ }).waitFor();
  await phone.getByText(/^Synced/).waitFor();
});

await step("the signal drops; the job goes on and the work queues up on the phone", async () => {
  await phone.context().setOffline(true);
  await phone.getByText(/^Offline/).waitFor();
  await phone.getByRole("link", { name: new RegExp(title) }).click();
  await phone.getByRole("button", { name: "On my way" }).click();
  await phone.getByRole("button", { name: "Start work" }).click();
  await phone.getByRole("button", { name: "Basıncı ölç" }).click();
  await phone.getByRole("button", { name: "Gaz kaçağı testi" }).click();
  await phone.getByLabel("Part").selectOption({ label: "VLV-22 · Emniyet ventili" });
  await phone.getByRole("button", { name: "Add", exact: true }).first().click();
  await phone.getByText("1 × Emniyet ventili").waitFor();
  await phone.locator('input[type="file"]').setInputFiles(PHOTO);
  await phone.locator(".photo img").first().waitFor();
  await phone.getByLabel("Note").fill("Eski ventil paslanmış, değiştirildi.");
  await phone.getByRole("button", { name: "Add", exact: true }).last().click();
  await phone.getByLabel("Work done").fill("Emniyet ventili değişti, basınç 1,5 bar.");
  await phone.getByRole("button", { name: "Ask the customer to sign" }).click();
  await phone.getByLabel("Customer's name").fill("Hasan Yılmaz");
  const pad = await phone.locator("canvas.pad").boundingBox();
  await phone.mouse.move(pad.x + 30, pad.y + 110);
  await phone.mouse.down();
  for (const [x, y] of [[80, 60], [130, 120], [180, 50], [240, 110], [300, 70]]) await phone.mouse.move(pad.x + x, pad.y + y, { steps: 6 });
  await phone.mouse.up();
  await phone.getByRole("button", { name: "Save signature" }).click();
  await phone.getByText(/Hasan Yılmaz, /).waitFor();
  await phone.getByRole("button", { name: "Complete" }).click();
  await phone.getByText("Completed", { exact: true }).first().waitFor();
  await phone.getByText(/^Offline · \d+ waiting$/).waitFor();
});

await step("meanwhile the office cancels the second job, which the phone does not know yet", async () => {
  await api(desk, "POST", `/api${companyPath.replace("/c/", "/companies/")}/work-orders/${otherId}/cancel`, { reason: "Müşteri erteledi" });
  await phone.getByRole("link", { name: "My jobs" }).click();
  await phone.getByRole("link", { name: /Yıllık bakım/ }).click();
  await phone.getByLabel("Note").fill("Kapıdayım, kimse açmadı.");
  await phone.getByRole("button", { name: "Add", exact: true }).last().click();
  await phone.getByRole("button", { name: "On my way" }).click();
});

await step("the signal comes back: everything syncs, the late work on the cancelled job is kept for review", async () => {
  await phone.context().setOffline(false);
  await phone.getByText(/^Synced/).waitFor({ timeout: 20_000 });
  await phone.getByText("The job was cancelled meanwhile.", { exact: false }).first().waitFor();
  await phone.getByText("kept for the office to review").waitFor();
  await phone.getByText("This job was cancelled: Müşteri erteledi").waitFor();
  await phone.getByRole("link", { name: "My jobs" }).click();
  await phone.getByRole("link", { name: new RegExp(title) }).getByText("Completed").waitFor();
});

await step("the dispatcher sees the finished job with parts, photo and signature, and gets the report", async () => {
  await desk.goto(orderUrl);
  await desk.getByText("Completed", { exact: true }).first().waitFor();
  await desk.getByText("Emniyet ventili değişti, basınç 1,5 bar.").first().waitFor();
  await desk.getByText("₺249,90").first().waitFor();
  await desk.locator(".signed").getByText("Hasan Yılmaz").waitFor();
  await desk.locator(".photo img").first().waitFor();
  const [download] = await Promise.all([desk.waitForEvent("download"), desk.getByRole("button", { name: "Service report" }).click()]);
  const pdf = readFileSync(await download.path());
  if (pdf.subarray(0, 4).toString() !== "%PDF") throw new Error("the report is not a PDF");
});

await step("the cancelled job shows the phone's note marked for review", async () => {
  await desk.goto(`${BASE}${companyPath}/orders/${otherId}`);
  await desk.getByText("1 for review").waitFor();
  await desk.getByText("Kapıdayım, kimse açmadı.").waitFor();
});

await step("deadlines, the map and the work order list load", async () => {
  await desk.goto(`${BASE}${companyPath}/deadlines`);
  await desk.getByRole("heading", { name: "Deadlines" }).waitFor();
  await desk.goto(`${BASE}${companyPath}/map`);
  await desk.locator(".leaflet-container").waitFor();
  await desk.goto(`${BASE}${companyPath}/orders?view=all`);
  await desk.getByRole("link", { name: title }).waitFor();
});

await step("nobody else can see the company", async () => {
  const response = await fetch(`${BASE}/api${companyPath.replace("/c/", "/companies/")}/work-orders`);
  if (response.status !== 401) throw new Error(`expected 401, got ${response.status}`);
});

if (errors.length > 0) {
  failures++;
  console.log(`FAIL the browsers reported errors\n     ${[...new Set(errors)].join("\n     ")}`);
}

await browser.close();
console.log(failures === 0 ? "\nAll steps passed." : `\n${failures} step(s) failed.`);
process.exit(failures === 0 ? 0 : 1);
