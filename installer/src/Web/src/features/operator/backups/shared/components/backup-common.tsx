import type { ReactNode } from "react"
import { AlertTriangle } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Card, CardContent } from "@/components/ui/card"
import { cn } from "@/lib/utils"

export function OverviewCard({
  icon,
  label,
  value,
  detail,
  onClick,
  active = false,
}: {
  icon: ReactNode
  label: string
  value: string
  detail: string
  onClick?: () => void
  active?: boolean
}) {
  const content = (
    <>
      <span className="flex items-center gap-2 text-sm text-muted-foreground">
        {icon}
        {label}
      </span>
      <span className="text-2xl font-semibold tracking-tight">{value}</span>
      <span className="text-xs text-muted-foreground">{detail}</span>
    </>
  )

  if (onClick) {
    return (
      <button
        type="button"
        onClick={onClick}
        aria-pressed={active}
        className={cn(
          "flex min-h-full w-full flex-col gap-2 rounded-xl bg-card px-4 py-3 text-left text-card-foreground ring-1 ring-foreground/10 transition hover:bg-accent/35 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring",
          active && "bg-accent/35 ring-primary/55",
        )}
      >
        {content}
      </button>
    )
  }

  return (
    <Card>
      <CardContent className="space-y-2 py-2">{content}</CardContent>
    </Card>
  )
}

export function Info({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <div className="text-xs uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className="mt-1 break-words text-foreground">{value}</div>
    </div>
  )
}

export function PathInfo({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <div className="text-xs uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className="mt-1 break-all font-mono text-xs text-foreground">{value}</div>
    </div>
  )
}

export function WarningList({
  warnings,
  className,
}: {
  warnings: string[]
  className?: string
}) {
  const { t } = useI18n()

  return (
    <div className={`rounded-lg border border-amber-500/20 bg-amber-500/10 p-3 text-sm text-amber-100 ${className ?? ""}`}>
      <div className="flex items-center gap-2 font-medium">
        <AlertTriangle className="h-4 w-4" />
        {t("common.warningListTitle")}
      </div>
      <ul className="mt-2 list-disc space-y-1 pl-5">
        {warnings.map((warning) => (
          <li key={warning}>{warning}</li>
        ))}
      </ul>
    </div>
  )
}
