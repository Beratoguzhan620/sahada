import type {
  Board, Company, Customer, CustomerInput, CustomerSummary, Equipment, EquipmentInput, Me, Member, Operation, Page, Part, PartInput,
  Priority, PullResult, PushResult, Role, Session, Site, SiteInput, Sla, Status, Suggestions, Technician, TechnicianInput, WorkOrder,
  WorkOrderInput, WorkOrderSummary,
} from "./types";

/** An API error with the stable code from the problem response, such as "slot_taken". */
export class ApiError extends Error {
  constructor(
    message: string,
    public readonly status: number,
    public readonly code: string | null,
    public readonly fields: Record<string, string[]> = {},
  ) {
    super(message);
  }

  /** The request never reached the server: the device is offline or the server is down. */
  get offline() {
    return this.status === 0;
  }
}

let token: string | null = null;
let onUnauthorized: () => void = () => {};

export function setToken(value: string | null, unauthorized: () => void) {
  token = value;
  onUnauthorized = unauthorized;
}

async function send(path: string, init: RequestInit = {}): Promise<Response> {
  const headers = new Headers(init.headers);
  if (typeof init.body === "string") headers.set("Content-Type", "application/json");
  if (token && !headers.has("Authorization")) headers.set("Authorization", `Bearer ${token}`);

  let response: Response;
  try {
    response = await fetch(path, { ...init, headers });
  } catch (e) {
    if ((e as Error).name === "AbortError") throw e;
    throw new ApiError("The server cannot be reached. Check your connection.", 0, null);
  }

  if (response.status === 401 && token) onUnauthorized();
  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    const fields = (problem?.errors ?? {}) as Record<string, string[]>;
    const fallback = response.status === 429 ? "Too many requests. Wait a minute and try again." : "Something went wrong. Try again.";
    const detail = problem?.detail ?? Object.values(fields).flat()[0] ?? fallback;
    throw new ApiError(detail, response.status, problem?.code ?? null, fields);
  }

  return response;
}

async function request<T>(path: string, init: RequestInit = {}): Promise<T> {
  const response = await send(path, init);
  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}

const json = (body: unknown) => JSON.stringify(body);
const post = (body: unknown = {}): RequestInit => ({ method: "POST", body: json(body) });
const put = (body: unknown): RequestInit => ({ method: "PUT", body: json(body) });
const del: RequestInit = { method: "DELETE" };
const co = (id: string) => `/api/companies/${id}`;
const wo = (companyId: string, id: string) => `${co(companyId)}/work-orders/${id}`;

export function query(params: Record<string, string | number | boolean | string[] | undefined | null>): string {
  const search = new URLSearchParams();
  for (const [key, value] of Object.entries(params)) {
    if (Array.isArray(value)) value.forEach((v) => search.append(key, v));
    else if (value !== undefined && value !== null && String(value).trim() !== "") search.set(key, String(value).trim());
  }
  const text = search.toString();
  return text ? `?${text}` : "";
}

export interface OrderFilters {
  status?: Status[]; priority?: Priority | ""; technicianId?: string; customerId?: string; overdue?: boolean; q?: string; sort?: string; page?: number;
}

export const api = {
  register: (body: { email: string; displayName: string; password: string }) => request<Session>("/api/auth/register", post(body)),
  login: (email: string, password: string) => request<Session>("/api/auth/login", post({ email, password })),
  me: () => request<Me>("/api/me"),

  createCompany: (name: string) => request<Company>("/api/companies", post({ name })),
  members: (c: string) => request<Member[]>(`${co(c)}/members`),
  addMember: (c: string, email: string, role: Role) => request<Member>(`${co(c)}/members`, post({ email, role })),
  changeRole: (c: string, userId: string, role: Role) => request<Member>(`${co(c)}/members/${userId}`, { method: "PATCH", body: json({ role }) }),
  removeMember: (c: string, userId: string) => request<void>(`${co(c)}/members/${userId}`, del),
  technicians: (c: string) => request<Technician[]>(`${co(c)}/technicians`),
  saveTechnician: (c: string, userId: string, body: TechnicianInput) => request<Technician>(`${co(c)}/technicians/${userId}`, put(body)),

  customers: (c: string, params: { q?: string; page?: number; pageSize?: number }) => request<Page<CustomerSummary>>(`${co(c)}/customers${query(params)}`),
  customer: (c: string, id: string) => request<Customer>(`${co(c)}/customers/${id}`),
  createCustomer: (c: string, body: CustomerInput) => request<Customer>(`${co(c)}/customers`, post(body)),
  updateCustomer: (c: string, id: string, body: CustomerInput) => request<Customer>(`${co(c)}/customers/${id}`, put(body)),
  deleteCustomer: (c: string, id: string) => request<void>(`${co(c)}/customers/${id}`, del),
  addSite: (c: string, customerId: string, body: SiteInput) => request<Site>(`${co(c)}/customers/${customerId}/sites`, post(body)),
  updateSite: (c: string, customerId: string, id: string, body: SiteInput) => request<Site>(`${co(c)}/customers/${customerId}/sites/${id}`, put(body)),
  deleteSite: (c: string, customerId: string, id: string) => request<void>(`${co(c)}/customers/${customerId}/sites/${id}`, del),
  addEquipment: (c: string, customerId: string, siteId: string, body: EquipmentInput) =>
    request<Equipment>(`${co(c)}/customers/${customerId}/sites/${siteId}/equipment`, post(body)),
  deleteEquipment: (c: string, customerId: string, siteId: string, id: string) =>
    request<void>(`${co(c)}/customers/${customerId}/sites/${siteId}/equipment/${id}`, del),

  parts: (c: string, params: { q?: string; includeRetired?: boolean; page?: number }) => request<Page<Part>>(`${co(c)}/parts${query({ ...params, pageSize: 50 })}`),
  createPart: (c: string, body: PartInput) => request<Part>(`${co(c)}/parts`, post(body)),
  updatePart: (c: string, id: string, body: PartInput) => request<Part>(`${co(c)}/parts/${id}`, put(body)),

  orders: (c: string, f: OrderFilters) => request<Page<WorkOrderSummary>>(`${co(c)}/work-orders${query({ ...f, overdue: f.overdue ? true : undefined, pageSize: 25 })}`),
  order: (c: string, id: string) => request<WorkOrder>(wo(c, id)),
  createOrder: (c: string, body: WorkOrderInput) => request<WorkOrder>(`${co(c)}/work-orders`, post(body)),
  editOrder: (c: string, id: string, body: { title: string; description: string; priority: Priority; requiredSkill: string; estimatedMinutes: number }) =>
    request<WorkOrder>(wo(c, id), put(body)),
  addChecklistItem: (c: string, id: string, text: string) => request<WorkOrder>(`${wo(c, id)}/checklist`, post({ text })),
  schedule: (c: string, id: string, technicianId: string, start: string) => request<WorkOrder>(`${wo(c, id)}/schedule`, post({ technicianId, start })),
  unschedule: (c: string, id: string) => request<WorkOrder>(`${wo(c, id)}/unschedule`, post()),
  cancel: (c: string, id: string, reason: string) => request<WorkOrder>(`${wo(c, id)}/cancel`, post({ reason })),
  note: (c: string, id: string, text: string) => request<WorkOrder>(`${wo(c, id)}/notes`, post({ text })),
  suggestions: (c: string, id: string, date: string) => request<Suggestions>(`${wo(c, id)}/suggestions${query({ date })}`),
  photo: async (c: string, id: string, photoId: string) => (await send(`${wo(c, id)}/photos/${photoId}`)).blob(),
  report: async (c: string, id: string) => (await send(`${wo(c, id)}/report`)).blob(),

  board: (c: string, date?: string) => request<Board>(`${co(c)}/board${query({ date })}`),
  sla: (c: string) => request<Sla>(`${co(c)}/sla`),

  pull: (c: string, cursor: string | null) => request<PullResult>(`${co(c)}/sync/pull${query({ cursor })}`),
  push: (c: string, operations: Operation[]) => request<PushResult>(`${co(c)}/sync/push`, post({ operations })),
  uploadPhoto: (c: string, orderId: string, photoId: string, blob: Blob) =>
    send(`${wo(c, orderId)}/photos/${photoId}`, { method: "PUT", body: blob, headers: { "Content-Type": blob.type || "image/jpeg" } }),
};
