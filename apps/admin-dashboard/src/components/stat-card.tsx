import type { LucideIcon } from "lucide-react";

export function StatCard({ label, value, icon: Icon, tone }: { label: string; value: string; icon: LucideIcon; tone: string }) {
  return (
    <section className="rounded-md border border-slate-200 bg-white p-4">
      <div className="flex items-center justify-between gap-3">
        <div>
          <p className="text-sm text-slate-500">{label}</p>
          <p className="mt-1 text-2xl font-semibold text-slate-950">{value}</p>
        </div>
        <span className={`grid h-10 w-10 place-items-center rounded-md ${tone}`}>
          <Icon className="h-5 w-5" aria-hidden />
        </span>
      </div>
    </section>
  );
}
