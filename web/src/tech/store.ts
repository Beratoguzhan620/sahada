import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { api, ApiError } from "../api";
import type { Operation, Part, WorkOrder } from "../types";
import { local, type Meta, type Notice } from "./db";
import { mergePull, newId, view, type Me } from "./local";

export type SyncState = "idle" | "syncing" | "offline" | "failed";

export interface TechStore {
  ready: boolean;
  orders: WorkOrder[];
  parts: Part[];
  pending: number;
  pendingFor: (orderId: string) => number;
  notices: Notice[];
  lastSync: string | null;
  state: SyncState;
  error: string | null;
  /** Records something done on a job: it shows at once, and goes to the server with the next sync. */
  record: (op: Omit<Operation, "id" | "at">, photo?: Blob) => Promise<void>;
  sync: () => Promise<void>;
  dismiss: (id: string) => Promise<void>;
}

type Queued = Operation & { seq: number };

/**
 * The technician's jobs, kept on the device. Everything is read from and written to IndexedDB first; syncing sends
 * the queued work in order (photos before the operations that mention them) and then pulls what changed.
 */
export function useTechStore(companyId: string, me: Me): TechStore {
  const [base, setBase] = useState<WorkOrder[]>([]);
  const [queue, setQueue] = useState<Queued[]>([]);
  const [meta, setMeta] = useState<Meta | null>(null);
  const [state, setState] = useState<SyncState>("idle");
  const [error, setError] = useState<string | null>(null);
  const running = useRef<Promise<void> | null>(null);

  const reload = useCallback(async () => {
    const data = await local.load();
    // Another person's or another company's jobs never show: the device starts over for them.
    if (data.meta.companyId && (data.meta.companyId !== companyId || data.meta.userId !== me.id)) {
      await local.clear();
      setBase([]);
      setQueue([]);
      setMeta({ companyId, userId: me.id, cursor: null, lastSync: null, parts: [], notices: [] });
      return;
    }
    setBase(data.orders);
    setQueue(data.queue);
    setMeta({ ...data.meta, companyId, userId: me.id });
  }, [companyId, me.id]);

  const run = useCallback(async () => {
    const data = await local.load();
    let current: Meta = { ...data.meta, companyId, userId: me.id };
    const notices: Notice[] = [];

    // Photos go up first; an operation about a photo the server has not got would be refused.
    for (const op of data.queue.filter((o) => o.type === "PhotoAdded" && o.attachmentId)) {
      const blob = await local.photo(op.attachmentId!);
      if (!blob) continue;
      try {
        await api.uploadPhoto(companyId, op.workOrderId, op.attachmentId!, blob);
        await local.forgetPhoto(op.attachmentId!);
      } catch (e) {
        if (e instanceof ApiError && e.offline) throw e;
        // The server refused the photo for good; the operation will come back refused and say why.
        await local.forgetPhoto(op.attachmentId!);
      }
    }

    for (let i = 0; i < data.queue.length; i += 200) {
      const chunk = data.queue.slice(i, i + 200);
      const result = await api.push(companyId, chunk.map(({ seq: _seq, ...op }) => op));
      for (const r of result.results) {
        if (r.outcome === "Applied") continue;
        const op = chunk.find((o) => o.id === r.id);
        notices.push({ id: r.id, workOrderId: op?.workOrderId ?? "", outcome: r.outcome, message: r.message ?? r.outcome, at: result.serverTime });
      }
      await local.settle(chunk.map((o) => o.seq));
    }

    const remaining = (await local.load()).queue;
    const pull = await api.pull(companyId, current.cursor);
    const merged = mergePull(data.orders, pull, new Set(remaining.map((o) => o.workOrderId)));
    await local.saveOrders(merged);
    current = {
      ...current,
      cursor: pull.cursor,
      lastSync: pull.serverTime,
      parts: pull.parts ?? current.parts,
      notices: [...notices, ...current.notices].slice(0, 20),
    };
    await local.saveMeta(current);
  }, [companyId, me.id]);

  const sync = useCallback(async () => {
    if (running.current) return running.current;
    setState("syncing");
    setError(null);
    running.current = run()
      .then(() => setState("idle"))
      .catch((e: Error) => {
        const offline = e instanceof ApiError && e.offline;
        setState(offline ? "offline" : "failed");
        if (!offline) setError(e.message);
      })
      .finally(() => {
        running.current = null;
        void reload();
      });
    return running.current;
  }, [run, reload]);

  const record = useCallback(async (op: Omit<Operation, "id" | "at">, photo?: Blob) => {
    await local.enqueue({ ...op, id: newId(), at: new Date().toISOString() } as Operation, photo);
    await reload();
    if (navigator.onLine) void sync();
  }, [reload, sync]);

  const dismiss = useCallback(async (id: string) => {
    const data = await local.load();
    await local.saveMeta({ ...data.meta, notices: data.meta.notices.filter((n) => n.id !== id) });
    await reload();
  }, [reload]);

  useEffect(() => {
    void reload().then(() => (navigator.onLine ? sync() : setState("offline")));
    const online = () => void sync();
    const offline = () => setState("offline");
    window.addEventListener("online", online);
    window.addEventListener("offline", offline);
    // A dispatcher's change reaches the device within a minute while it has a connection.
    const timer = setInterval(() => { if (navigator.onLine) void sync(); }, 60_000);
    return () => {
      window.removeEventListener("online", online);
      window.removeEventListener("offline", offline);
      clearInterval(timer);
    };
  }, [reload, sync]);

  const parts = meta?.parts ?? [];
  const orders = useMemo(() => view(base, queue, me, parts), [base, queue, me, parts]);
  const pendingFor = useCallback((orderId: string) => queue.filter((o) => o.workOrderId === orderId).length, [queue]);

  return {
    ready: meta !== null,
    orders,
    parts,
    pending: queue.length,
    pendingFor,
    notices: meta?.notices ?? [],
    lastSync: meta?.lastSync ?? null,
    state,
    error,
    record,
    sync,
    dismiss,
  };
}
