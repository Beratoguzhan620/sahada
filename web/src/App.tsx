import { Wrench } from "lucide-react";
import { useCallback, useEffect, useMemo, useState } from "react";
import { api, setToken } from "./api";
import { Shell } from "./components/Shell";
import { Empty, Failed, Loading } from "./components/States";
import { AccountCtx, CompanyCtx, canDispatch, type Account } from "./context";
import { Register, SignIn } from "./pages/Auth";
import { Board } from "./pages/Board";
import { CustomerPage } from "./pages/CustomerPage";
import { Customers } from "./pages/Customers";
import { Deadlines } from "./pages/Deadlines";
import { Home } from "./pages/Home";
import { MapPage } from "./pages/MapPage";
import { OrderForm } from "./pages/OrderForm";
import { OrderPage } from "./pages/OrderPage";
import { Orders } from "./pages/Orders";
import { Parts } from "./pages/Parts";
import { Team } from "./pages/Team";
import { companyOf, linkProps, navigate, signInHere, useRoute, type Route } from "./router";
import { loadSession, saveSession } from "./session";
import { TechApp } from "./tech/TechApp";
import type { Me, Session } from "./types";

function NotFound() {
  return (
    <Empty title="This page does not exist" icon={<Wrench size={26} />}>
      <span className="muted">Or it belongs to a company you are not part of.</span>
      <a className="button" {...linkProps("/")}>Home</a>
    </Empty>
  );
}

function CompanyPage({ route }: { route: Route }) {
  switch (route.page) {
    case "board": return <Board key={route.date ?? "today"} date={route.date} />;
    case "orders": return <Orders />;
    case "order-new": return <OrderForm customerId={route.customerId} />;
    case "order": return <OrderPage key={route.orderId} orderId={route.orderId} />;
    case "deadlines": return <Deadlines />;
    case "map": return <MapPage />;
    case "customers": return <Customers />;
    case "customer": return <CustomerPage key={route.customerId} customerId={route.customerId} />;
    case "parts": return <Parts />;
    case "team": return <Team />;
    default: return <NotFound />;
  }
}

export function App() {
  const route = useRoute();
  const [session, setSession] = useState<Session | null>(() => loadSession());
  const [me, setMe] = useState<Me | null>(null);
  const [error, setError] = useState<string | null>(null);

  const signOut = useCallback(() => {
    saveSession(null);
    setToken(null, () => {});
    setSession(null);
    setMe(null);
    navigate("/signin", true);
  }, []);

  if (session) setToken(session.accessToken, signOut);

  const refresh = useCallback(async () => {
    const value = await api.me();
    setMe(value);
    return value;
  }, []);

  useEffect(() => {
    if (!session) return;
    setError(null);
    refresh().catch((e: Error) => setError(e.message));
  }, [session, refresh]);

  // A company in the address that is not among ours yet may be one we were just added to.
  const companyId = companyOf(route);
  useEffect(() => {
    if (me && companyId && !me.companies.some((c) => c.id === companyId)) void refresh();
  }, [companyId]); // eslint-disable-line react-hooks/exhaustive-deps

  useEffect(() => {
    const auth = route.page === "signin" || route.page === "register";
    if (!session && !auth) navigate(signInHere(), true);
    if (session && auth) navigate(route.next ?? "/", true);
  }, [session, route]);

  const signedIn = useCallback((value: Session) => {
    saveSession(value);
    setToken(value.accessToken, signOut);
    setSession(value);
  }, [signOut]);

  const account = useMemo<Account | null>(() => (session && me ? { session, me, refresh, signOut } : null), [session, me, refresh, signOut]);
  const context = useMemo(() => ({ account, signedIn }), [account, signedIn]);

  if (!session) {
    return <AccountCtx.Provider value={context}>{route.page === "register" ? <Register next={route.next} /> : <SignIn next={route.page === "signin" ? route.next : undefined} />}</AccountCtx.Provider>;
  }

  if (!me) {
    // The technician app can open without a connection: it has its own copy of the jobs, so it does not wait for /api/me.
    if (route.page === "tech" && error) {
      const offline: Account = { session, me: { user: session.user, companies: cachedCompanies() }, refresh, signOut };
      return <AccountCtx.Provider value={{ account: offline, signedIn }}><TechApp /></AccountCtx.Provider>;
    }
    return <div className="center">{error ? <Failed message={error} onRetry={() => refresh().catch((e: Error) => setError(e.message))} /> : <Loading />}</div>;
  }
  rememberCompanies(me);

  if (route.page === "tech") return <AccountCtx.Provider value={context}><TechApp /></AccountCtx.Provider>;

  const company = companyId ? me.companies.find((c) => c.id === companyId) ?? null : null;
  let content;
  if (route.page === "home") content = <Home />;
  else if (!company) content = <NotFound />;
  else if (!canDispatch(company)) content = <Empty title="This is for dispatchers"><a className="button primary" {...linkProps("/tech")}>Open my jobs</a></Empty>;
  else content = <CompanyCtx.Provider value={company}><CompanyPage route={route} /></CompanyCtx.Provider>;

  return (
    <AccountCtx.Provider value={context}>
      <Shell route={route} company={company} wide={route.page === "board" || route.page === "map"}>{content}</Shell>
    </AccountCtx.Provider>
  );
}

const COMPANIES = "sahada.companies";

function rememberCompanies(me: Me) {
  try {
    localStorage.setItem(COMPANIES, JSON.stringify(me.companies));
  } catch {
    // Storage may be unavailable; the technician app then needs a connection to start.
  }
}

function cachedCompanies() {
  try {
    return JSON.parse(localStorage.getItem(COMPANIES) ?? "[]");
  } catch {
    return [];
  }
}
