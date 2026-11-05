import { Building2, ChevronRight, Plus, Smartphone } from "lucide-react";
import { useEffect, useState, type FormEvent } from "react";
import { api } from "../api";
import { Empty } from "../components/States";
import { useAccount } from "../context";
import { initials, plural } from "../format";
import { linkProps, navigate } from "../router";

/** Where everyone lands: dispatchers go to their board, technicians to their app; anyone else starts a company. */
export function Home() {
  const { me, refresh } = useAccount();
  const [name, setName] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (me.companies.length !== 1) return;
    const only = me.companies[0]!;
    navigate(only.role === "Technician" ? "/tech" : `/c/${only.id}/board`, true);
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  async function create(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const created = await api.createCompany(name);
      await refresh();
      navigate(`/c/${created.id}/team`);
    } catch (err) {
      setError((err as Error).message);
      setBusy(false);
    }
  }

  return (
    <div className="narrow-page">
      <div className="page-head"><div><h1>Your companies</h1><p className="muted">Plan the day, send the right technician, and close jobs with a signed report.</p></div></div>
      {me.companies.length === 0 ? (
        <section className="card section">
          <Empty title="Nothing here yet" icon={<Building2 size={26} />}>
            <span className="muted measure">Create a company to start, or ask its owner to add you to the team with this account's email.</span>
          </Empty>
        </section>
      ) : (
        <ul className="org-list card">
          {me.companies.map((c) => (
            <li key={c.id}>
              <a {...linkProps(c.role === "Technician" ? "/tech" : `/c/${c.id}/board`)}>
                <span className="org-avatar">{initials(c.name)}</span>
                <span className="grow"><strong className="block">{c.name}</strong><span className="small muted">{c.role} · {plural(c.openWorkOrders, "open job")} · {plural(c.members, "member")}</span></span>
                {c.role === "Technician" ? <Smartphone size={18} className="muted" /> : <ChevronRight size={18} className="muted" />}
              </a>
            </li>
          ))}
        </ul>
      )}
      <form className="card section" onSubmit={create}>
        <h2>New company</h2>
        <label className="field">Name<input className="input" value={name} onChange={(e) => setName(e.target.value)} placeholder="Kuzey Klima Servis" required minLength={2} maxLength={80} /></label>
        {error && <p className="form-error" role="alert">{error}</p>}
        <div><button className="button primary" disabled={busy || name.trim().length < 2}><Plus size={16} />Create company</button></div>
      </form>
    </div>
  );
}
