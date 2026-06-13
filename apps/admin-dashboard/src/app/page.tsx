import { Activity, Monitor, ShieldAlert, Users } from "lucide-react";
import { StatCard } from "@/components/stat-card";

const rows = [
  { type: "SESSION_REQUESTED", actor: "technician@company.test", target: "Finance-PC", at: "2026-06-11 14:15", severity: "info" },
  { type: "PAIRING_CODE_FAILED", actor: "unknown", target: "Support-Host-01", at: "2026-06-11 14:08", severity: "warning" },
  { type: "SESSION_APPROVED", actor: "host-user@company.test", target: "Ops-Laptop", at: "2026-06-11 13:58", severity: "info" }
];

export default function DashboardPage() {
  return (
    <main className="min-h-screen">
      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto flex max-w-7xl items-center justify-between px-6 py-4">
          <div>
            <h1 className="text-xl font-semibold text-slate-950">SecureRemoteDesk Admin</h1>
            <p className="text-sm text-slate-500">Users, devices, sessions and security events</p>
          </div>
          <button className="rounded-md bg-primary px-4 py-2 text-sm font-medium text-white">Admin</button>
        </div>
      </header>
      <div className="mx-auto grid max-w-7xl gap-6 px-6 py-6">
        <section className="grid gap-4 md:grid-cols-4">
          <StatCard label="Total users" value="0" icon={Users} tone="bg-blue-50 text-primary" />
          <StatCard label="Online devices" value="0" icon={Monitor} tone="bg-emerald-50 text-success" />
          <StatCard label="Active sessions" value="0" icon={Activity} tone="bg-amber-50 text-warning" />
          <StatCard label="Security events 24h" value="0" icon={ShieldAlert} tone="bg-red-50 text-danger" />
        </section>
        <section className="rounded-md border border-slate-200 bg-white">
          <div className="flex items-center justify-between border-b border-slate-200 px-4 py-3">
            <h2 className="text-base font-semibold">Audit log</h2>
            <input className="h-9 rounded-md border border-slate-300 px-3 text-sm" placeholder="Search logs" />
          </div>
          <div className="overflow-x-auto">
            <table className="w-full min-w-[760px] border-collapse text-left text-sm">
              <thead className="bg-slate-50 text-slate-500">
                <tr>
                  <th className="px-4 py-3 font-medium">Action</th>
                  <th className="px-4 py-3 font-medium">Actor</th>
                  <th className="px-4 py-3 font-medium">Target</th>
                  <th className="px-4 py-3 font-medium">Time</th>
                  <th className="px-4 py-3 font-medium">Severity</th>
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={`${row.type}-${row.at}`} className="border-t border-slate-100">
                    <td className="px-4 py-3 font-medium text-slate-900">{row.type}</td>
                    <td className="px-4 py-3 text-slate-600">{row.actor}</td>
                    <td className="px-4 py-3 text-slate-600">{row.target}</td>
                    <td className="px-4 py-3 text-slate-600">{row.at}</td>
                    <td className="px-4 py-3 text-slate-600">{row.severity}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      </div>
    </main>
  );
}
