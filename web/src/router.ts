import { useEffect, useState } from "react";

/** The pages of the app. Ids are in the address, so every page can be bookmarked and shared. */
export type Route =
  | { page: "signin"; next?: string }
  | { page: "register"; next?: string }
  | { page: "home" }
  | { page: "board"; companyId: string; date?: string }
  | { page: "orders"; companyId: string }
  | { page: "order-new"; companyId: string; customerId?: string }
  | { page: "order"; companyId: string; orderId: string }
  | { page: "deadlines"; companyId: string }
  | { page: "map"; companyId: string }
  | { page: "customers"; companyId: string }
  | { page: "customer"; companyId: string; customerId: string }
  | { page: "parts"; companyId: string }
  | { page: "team"; companyId: string }
  | { page: "tech"; orderId?: string }
  | { page: "not-found" };

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const isId = (value: string | undefined): value is string => !!value && guid.test(value);
const isDate = (value: string | null): value is string => !!value && /^\d{4}-\d{2}-\d{2}$/.test(value);

/** Only addresses within the app are followed after signing in. */
function safeNext(value: string | null): string | undefined {
  return value && value.startsWith("/") && !value.startsWith("//") ? value : undefined;
}

export function parseRoute(pathname: string, search = ""): Route {
  const params = new URLSearchParams(search);
  const parts = pathname.split("/").filter(Boolean).map(decodeURIComponent);
  const [a, b, c, d] = parts;
  const n = parts.length;

  if (n === 0) return { page: "home" };
  if (n === 1 && a === "signin") return { page: "signin", next: safeNext(params.get("next")) };
  if (n === 1 && a === "register") return { page: "register", next: safeNext(params.get("next")) };
  if (a === "tech" && n === 1) return { page: "tech" };
  if (a === "tech" && n === 2 && isId(b)) return { page: "tech", orderId: b };
  if (a !== "c" || !isId(b)) return { page: "not-found" };
  if (n === 2 || (n === 3 && c === "board")) return { page: "board", companyId: b, date: isDate(params.get("date")) ? params.get("date")! : undefined };
  if (n === 3 && c === "orders") return { page: "orders", companyId: b };
  if (n === 4 && c === "orders" && d === "new") {
    const customerId = params.get("customer");
    return { page: "order-new", companyId: b, customerId: isId(customerId ?? undefined) ? customerId! : undefined };
  }
  if (n === 4 && c === "orders" && isId(d)) return { page: "order", companyId: b, orderId: d };
  if (n === 3 && c === "deadlines") return { page: "deadlines", companyId: b };
  if (n === 3 && c === "map") return { page: "map", companyId: b };
  if (n === 3 && c === "customers") return { page: "customers", companyId: b };
  if (n === 4 && c === "customers" && isId(d)) return { page: "customer", companyId: b, customerId: d };
  if (n === 3 && c === "parts") return { page: "parts", companyId: b };
  if (n === 3 && c === "team") return { page: "team", companyId: b };
  return { page: "not-found" };
}

export const companyOf = (route: Route): string | undefined => ("companyId" in route ? route.companyId : undefined);

export function navigate(path: string, replace = false) {
  if (replace) history.replaceState(null, "", path);
  else history.pushState(null, "", path);
  window.dispatchEvent(new PopStateEvent("popstate"));
  window.scrollTo({ top: 0 });
}

export function useRoute(): Route {
  const read = () => parseRoute(location.pathname, location.search);
  const [route, setRoute] = useState<Route>(read);
  useEffect(() => {
    const update = () => setRoute(read());
    window.addEventListener("popstate", update);
    return () => window.removeEventListener("popstate", update);
  }, []);
  return route;
}

/** Props for an anchor that moves within the app without reloading the page. */
export function linkProps(href: string) {
  return {
    href,
    onClick: (e: React.MouseEvent) => {
      if (e.metaKey || e.ctrlKey || e.shiftKey || e.button !== 0) return;
      e.preventDefault();
      navigate(href);
    },
  };
}

/** The sign-in page, coming back here afterwards. */
export const signInHere = () => `/signin?next=${encodeURIComponent(location.pathname + location.search)}`;

/** Replaces query parameters of the current address without a new history entry. */
export function setQuery(params: Record<string, string | undefined>) {
  const search = new URLSearchParams(location.search);
  for (const [key, value] of Object.entries(params)) {
    if (value) search.set(key, value);
    else search.delete(key);
  }
  const text = search.toString();
  history.replaceState(null, "", location.pathname + (text ? `?${text}` : ""));
}
