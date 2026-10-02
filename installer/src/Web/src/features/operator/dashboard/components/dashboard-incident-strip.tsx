import { CircleAlert, TriangleAlert } from "lucide-react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import { useDiagnosticsAttention } from "@/features/operator/diagnostics/hooks/use-diagnostics-attention"
import { cn } from "@/lib/utils"

export function DashboardIncidentStrip() {
  const { t } = useI18n()
  const query = useDiagnosticsAttention()
  const attention = query.data

  if (
    !attention ||
    attention.total <= 0 ||
    (attention.state !== "warning" && attention.state !== "attention")
  ) {
    return null
  }

  const urgent = attention.state === "attention"
  const stale = query.isRefetchError || attention.partial
  const latest = attention.items[0]?.summary?.trim()
  const title = t(
    urgent
      ? "diagnostics.attention.errorLabel"
      : "diagnostics.attention.warningLabel",
    { count: attention.total },
  )

  return (
    <section
      role="status"
      aria-label={title}
      data-dashboard-incident-state={urgent ? "attention" : "warning"}
      data-dashboard-incident-stale={stale ? "true" : undefined}
      className={cn(
        "rounded-xl border px-4 py-3",
        urgent
          ? "border-red-500/25 bg-red-500/10"
          : "border-amber-500/25 bg-amber-500/10",
      )}
    >
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex min-w-0 gap-2.5">
          {urgent ? (
            <CircleAlert className="mt-0.5 h-4 w-4 shrink-0 text-red-300" aria-hidden="true" />
          ) : (
            <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0 text-amber-300" aria-hidden="true" />
          )}
          <div className="min-w-0">
            <div className="text-sm font-semibold text-foreground">{title}</div>
            <p className="mt-0.5 text-sm text-muted-foreground">
              {latest
                ? t("dashboard.incidents.latest", { summary: latest })
                : t("dashboard.incidents.description")}
            </p>
            {stale ? (
              <p className="mt-1 text-xs text-muted-foreground">
                {t("diagnostics.attention.stale")}
              </p>
            ) : null}
          </div>
        </div>

        <Button variant="outline" size="sm" asChild className="shrink-0">
          <Link to="/diagnostics/logs?tab=incidents">
            {t("dashboard.incidents.review")}
          </Link>
        </Button>
      </div>
    </section>
  )
}
