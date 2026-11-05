import type { Operation, Part, WorkOrder } from "../types";

/** What a sync left behind that the technician should know: work the server refused or flagged. */
export interface Notice { id: string; workOrderId: string; outcome: string; message: string; at: string }

export interface Meta {
  companyId: string | null;
  userId: string | null;
  cursor: string | null;
  lastSync: string | null;
  parts: Part[];
  notices: Notice[];
}

const NAME = "sahada-tech";
const empty: Meta = { companyId: null, userId: null, cursor: null, lastSync: null, parts: [], notices: [] };

let opening: Promise<IDBDatabase> | null = null;

function open(): Promise<IDBDatabase> {
  opening ??= new Promise((resolve, reject) => {
    const request = indexedDB.open(NAME, 1);
    request.onupgradeneeded = () => {
      const db = request.result;
      db.createObjectStore("orders", { keyPath: "id" });
      db.createObjectStore("queue", { keyPath: "seq", autoIncrement: true });
      db.createObjectStore("photos");
      db.createObjectStore("meta");
    };
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
  return opening;
}

function done(tx: IDBTransaction): Promise<void> {
  return new Promise((resolve, reject) => {
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
    tx.onabort = () => reject(tx.error);
  });
}

function all<T>(store: IDBObjectStore): Promise<T[]> {
  return new Promise((resolve, reject) => {
    const request = store.getAll();
    request.onsuccess = () => resolve(request.result as T[]);
    request.onerror = () => reject(request.error);
  });
}

function get<T>(store: IDBObjectStore, key: IDBValidKey): Promise<T | undefined> {
  return new Promise((resolve, reject) => {
    const request = store.get(key);
    request.onsuccess = () => resolve(request.result as T | undefined);
    request.onerror = () => reject(request.error);
  });
}

/** The device's copy of the technician's jobs and the work waiting to be sent, kept in IndexedDB. */
export const local = {
  async load(): Promise<{ orders: WorkOrder[]; queue: (Operation & { seq: number })[]; meta: Meta }> {
    const db = await open();
    const tx = db.transaction(["orders", "queue", "meta"], "readonly");
    const [orders, rows, meta] = await Promise.all([
      all<WorkOrder>(tx.objectStore("orders")),
      all<{ seq: number; op: Operation }>(tx.objectStore("queue")),
      get<Meta>(tx.objectStore("meta"), "meta"),
    ]);
    return { orders, queue: rows.sort((a, b) => a.seq - b.seq).map((r) => ({ ...r.op, seq: r.seq })), meta: { ...empty, ...meta } };
  },

  async enqueue(op: Operation, photo?: Blob) {
    const db = await open();
    const tx = db.transaction(["queue", "photos"], "readwrite");
    tx.objectStore("queue").add({ op });
    if (photo && op.attachmentId) tx.objectStore("photos").put(photo, op.attachmentId);
    await done(tx);
  },

  async photo(id: string): Promise<Blob | undefined> {
    const db = await open();
    return get<Blob>(db.transaction("photos").objectStore("photos"), id);
  },

  async forgetPhoto(id: string) {
    const db = await open();
    const tx = db.transaction("photos", "readwrite");
    tx.objectStore("photos").delete(id);
    await done(tx);
  },

  async settle(seqs: number[]) {
    const db = await open();
    const tx = db.transaction("queue", "readwrite");
    for (const seq of seqs) tx.objectStore("queue").delete(seq);
    await done(tx);
  },

  async saveOrders(orders: WorkOrder[]) {
    const db = await open();
    const tx = db.transaction("orders", "readwrite");
    const store = tx.objectStore("orders");
    store.clear();
    for (const order of orders) store.put(order);
    await done(tx);
  },

  async saveMeta(meta: Meta) {
    const db = await open();
    const tx = db.transaction("meta", "readwrite");
    tx.objectStore("meta").put(meta, "meta");
    await done(tx);
  },

  /** Signing out on a shared device leaves nothing of the previous person behind. */
  async clear() {
    const db = await open();
    const tx = db.transaction(["orders", "queue", "photos", "meta"], "readwrite");
    for (const name of ["orders", "queue", "photos", "meta"]) tx.objectStore(name).clear();
    await done(tx);
  },
};
