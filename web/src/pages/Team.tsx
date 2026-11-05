import { HardHat, Pencil, UserPlus } from "lucide-react";
import { useState, type FormEvent } from "react";
import { api } from "../api";
import { Dialog } from "../components/Dialog";
import { MapView } from "../components/MapView";
import { Failed, SkeletonRows } from "../components/States";
import { canDispatch, useAccount, useCompany } from "../context";
import { initials, roleHint } from "../format";
import { useLoad } from "../hooks";
import { navigate } from "../router";
import type { DayOfWeek, Member, Role, Technician, TechnicianInput } from "../types";

const roles: Role[] = ["Technician", "Dispatcher", "Owner"];
const week: DayOfWeek[] = ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday"];

function ProfileDialog({ member, profile, onClose, onSaved }: { member: Member; profile: Technician | undefined; onClose: () => void; onSaved: () => void }) {
  const company = useCompany();
  const [form, setForm] = useState<TechnicianInput>(profile
    ? { skills: profile.skills, baseLabel: profile.baseLabel, baseLatitude: profile.baseLatitude, baseLongitude: profile.baseLongitude, shiftStart: profile.shiftStart.slice(0, 5), shiftEnd: profile.shiftEnd.slice(0, 5), workDays: profile.workDays, active: profile.active }
    : { skills: [], baseLabel: "Depot", baseLatitude: 41.2867, baseLongitude: 36.33, shiftStart: "08:30", shiftEnd: "17:30", workDays: ["Monday", "Tuesday", "Wednesday", "Thursday", "Friday"], active: true });
  const [skills, setSkills] = useState(form.skills.join(", "));
  const [error, setError] = useState<string | null>(null);

  async function submit(e: FormEvent) {
    e.preventDefault();
    try {
      await api.saveTechnician(company.id, member.userId, { ...form, skills: skills.split(/[,\s]+/).map((s) => s.trim()).filter(Boolean) });
      onSaved();
    } catch (err) {
      setError((err as Error).message);
    }
  }

  const toggle = (day: DayOfWeek) => setForm({ ...form, workDays: form.workDays.includes(day) ? form.workDays.filter((d) => d !== day) : [...form.workDays, day] });

  return (
    <Dialog title={`${member.displayName} on the board`} onClose={onClose} wide>
      <form className="stack" onSubmit={submit}>
        <label className="field">Skills<input className="input" value={skills} onChange={(e) => setSkills(e.target.value)} placeholder="hvac, electrical, plumbing" />
          <span className="hint">Jobs that need a skill are only offered to people who have it.</span></label>
        <div className="grid-3">
          <label className="field">Shift starts<input className="input" type="time" value={form.shiftStart} onChange={(e) => setForm({ ...form, shiftStart: e.target.value })} /></label>
          <label className="field">Shift ends<input className="input" type="time" value={form.shiftEnd} onChange={(e) => setForm({ ...form, shiftEnd: e.target.value })} /></label>
          <label className="field">Base<input className="input" value={form.baseLabel} onChange={(e) => setForm({ ...form, baseLabel: e.target.value })} /></label>
        </div>
        <fieldset className="field"><legend className="section-legend">Working days</legend>
          <div className="row wrap gap-6">{week.map((d) => <button type="button" key={d} className={`chip${form.workDays.includes(d) ? " on" : ""}`} aria-pressed={form.workDays.includes(d)} onClick={() => toggle(d)}>{d.slice(0, 3)}</button>)}</div>
        </fieldset>
        <div className="field"><span>Where the day starts <span className="hint">Click the map to move it.</span></span>
          <MapView pins={[{ id: "base", latitude: form.baseLatitude, longitude: form.baseLongitude, label: form.baseLabel, text: initials(member.displayName), tone: "ink" }]} height={200}
            onPick={(baseLatitude, baseLongitude) => setForm((f) => ({ ...f, baseLatitude, baseLongitude }))} />
        </div>
        <label className="check small"><input type="checkbox" checked={form.active} onChange={(e) => setForm({ ...form, active: e.target.checked })} />On the board (untick for leave or sick days)</label>
        {error && <p className="form-error" role="alert">{error}</p>}
        <div className="dialog-actions"><button type="button" className="button" onClick={onClose}>Close</button><button className="button primary">Save</button></div>
      </form>
    </Dialog>
  );
}

export function Team() {
  const company = useCompany();
  const { session, refresh } = useAccount();
  const [members, reload] = useLoad(() => api.members(company.id), [company.id]);
  const [profiles, reloadProfiles] = useLoad(() => api.technicians(company.id), [company.id]);
  const [email, setEmail] = useState("");
  const [role, setRole] = useState<Role>("Technician");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [editing, setEditing] = useState<Member | null>(null);
  const owner = company.role === "Owner";
  const dispatcher = canDispatch(company);

  async function add(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    try {
      const member = await api.addMember(company.id, email, role);
      setEmail("");
      reload();
      if (role === "Technician") setEditing(member);
    } catch (err) {
      setError((err as Error).message);
    } finally {
      setBusy(false);
    }
  }

  async function change(userId: string, next: Role) {
    setError(null);
    try {
      await api.changeRole(company.id, userId, next);
      reload();
      if (userId === session.user.id) await refresh();
    } catch (err) {
      setError((err as Error).message);
    }
  }

  async function remove(userId: string) {
    setError(null);
    try {
      await api.removeMember(company.id, userId);
      if (userId === session.user.id) {
        await refresh();
        navigate("/");
      } else {
        reload();
        reloadProfiles();
      }
    } catch (err) {
      setError((err as Error).message);
    }
  }

  const profileOf = (userId: string) => profiles.data?.find((p) => p.userId === userId);

  return (
    <div className="stack">
      <div className="page-head"><div><h1>Team</h1><p className="muted">Technicians do the jobs, dispatchers plan them, owners also manage the team.</p></div></div>
      {owner && (
        <form className="card section" onSubmit={add}>
          <div className="row wrap gap-10">
            <label className="field grow">Email of their Sahada account<input className="input" type="email" value={email} onChange={(e) => setEmail(e.target.value)} required placeholder="ali@example.com" /></label>
            <label className="field">Role<select className="select" value={role} onChange={(e) => setRole(e.target.value as Role)}>{roles.map((r) => <option key={r}>{r}</option>)}</select></label>
            <button className="button primary align-end" disabled={busy}><UserPlus size={16} />Add</button>
          </div>
          <span className="hint">{roleHint[role]}.</span>
        </form>
      )}
      {error && <p className="form-error" role="alert">{error}</p>}
      <div className="card table-card">
        {members.state === "failed" && !members.data ? <Failed message={members.error} onRetry={reload} /> : (
          <div className="table-wrap">
            <table className="table">
              <thead><tr><th>Member</th><th>Role</th><th>In the field</th><th /></tr></thead>
              {!members.data ? <SkeletonRows columns={4} rows={3} /> : (
                <tbody>
                  {members.data.map((m) => {
                    const p = profileOf(m.userId);
                    return (
                      <tr key={m.userId}>
                        <td><div className="row gap-10"><span className="avatar">{initials(m.displayName)}</span><span className="min0"><strong className="block ellipsis">{m.displayName}{m.userId === session.user.id && <span className="muted"> (you)</span>}</strong><span className="small muted ellipsis block">{m.email}</span></span></div></td>
                        <td>
                          {owner ? (
                            <select className="select narrow" value={m.role} onChange={(e) => change(m.userId, e.target.value as Role)} aria-label={`Role of ${m.displayName}`}>{roles.map((r) => <option key={r}>{r}</option>)}</select>
                          ) : <span className="badge plain">{m.role}</span>}
                        </td>
                        <td>
                          {p ? (
                            <span className="stack-8 profile-cell">
                              <span className="row wrap gap-4">{p.active ? null : <span className="badge plain">Off the board</span>}{p.skills.map((s) => <span key={s} className="badge info">{s}</span>)}{p.skills.length === 0 && <span className="small faint">No skills</span>}</span>
                              <span className="small muted">{p.shiftStart.slice(0, 5)}–{p.shiftEnd.slice(0, 5)} · {p.workDays.map((d) => d.slice(0, 2)).join(" ")} · {p.baseLabel}</span>
                            </span>
                          ) : <span className="small faint">Not on the board</span>}
                        </td>
                        <td className="actions">
                          <span className="row gap-4">
                            {dispatcher && <button className="button small" onClick={() => setEditing(m)}>{p ? <Pencil size={14} /> : <HardHat size={14} />}{p ? "Profile" : "Put on board"}</button>}
                            {(owner || m.userId === session.user.id) && <button className="button small danger" onClick={() => remove(m.userId)}>{m.userId === session.user.id ? "Leave" : "Remove"}</button>}
                          </span>
                        </td>
                      </tr>
                    );
                  })}
                </tbody>
              )}
            </table>
          </div>
        )}
      </div>
      {editing && <ProfileDialog member={editing} profile={profileOf(editing.userId)} onClose={() => setEditing(null)} onSaved={() => { setEditing(null); reloadProfiles(); }} />}
    </div>
  );
}
