import type { DashboardTone } from "../api/dashboard.types"

type Props = {
  children: string
  tone?: DashboardTone
}

export function DashboardStatusPill({ children, tone = "neutral" }: Props) {
  return (
    <span
      className={[
        "inline-flex rounded-full border px-2.5 py-1 text-xs font-medium",
        getToneClasses(tone),
      ].join(" ")}
    >
      {children}
    </span>
  )
}

function getToneClasses(tone: DashboardTone) {
  switch (tone) {
    case "good":
      return "border-emerald-500/20 bg-emerald-500/10 text-emerald-300"
    case "warning":
      return "border-amber-500/20 bg-amber-500/10 text-amber-300"
    case "danger":
      return "border-red-500/20 bg-red-500/10 text-red-300"
    default:
      return "border-border bg-background/60 text-muted-foreground"
  }
}
