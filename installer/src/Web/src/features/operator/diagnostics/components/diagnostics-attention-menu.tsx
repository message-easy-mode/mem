import { Bell, BellOff, CircleAlert, TriangleAlert } from "lucide-react"
import { Link } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu"
import type {
  DiagnosticsAttentionItem,
  DiagnosticsAttentionResponse,
} from "@/features/operator/diagnostics/api/diagnostics.types"
import { useDiagnosticsAttention } from "@/features/operator/diagnostics/hooks/use-diagnostics-attention"
export { diagnosticsAttentionPollingInterval } from "@/features/operator/diagnostics/hooks/use-diagnostics-attention"
import { cn } from "@/lib/utils"

const severityKeys = {
  warning: "diagnostics.severity.warning",
  error: "diagnostics.severity.error",
  critical: "diagnostics.severity.critical",
} as const satisfies Readonly<Record<string, TranslationKey>>

function safeIncidentHref(value: string): string | null {
  if (!value.startsWith("/diagnostics/logs?")) {
    return null
  }

  try {
    const parsed = new URL(value, window.location.origin)
    const incidentId = parsed.searchParams.get("incident")
    if (
      parsed.origin !== window.location.origin ||
      parsed.pathname !== "/diagnostics/logs" ||
      !incidentId ||
      incidentId.length > 80 ||
      !/^inc_[A-Za-z0-9_.-]+$/.test(incidentId)
    ) {
      return null
    }

    return `/diagnostics/logs?incident=${encodeURIComponent(incidentId)}`
  } catch {
    return null
  }
}

function formatRelativeTime(value: string, locale: string): string {
  const timestamp = Date.parse(value)
  if (!Number.isFinite(timestamp)) {
    return value
  }

  const seconds = (timestamp - Date.now()) / 1000
  const absoluteSeconds = Math.abs(seconds)
  const [amount, unit] = absoluteSeconds < 60
    ? [Math.round(seconds), "second" as const]
    : absoluteSeconds < 3600
      ? [Math.round(seconds / 60), "minute" as const]
      : absoluteSeconds < 86400
        ? [Math.round(seconds / 3600), "hour" as const]
        : [Math.round(seconds / 86400), "day" as const]

  return new Intl.RelativeTimeFormat(locale, { numeric: "auto" }).format(amount, unit)
}

function severityBadgeVariant(severity: string) {
  const normalized = severity.toLowerCase()
  return normalized === "critical" || normalized === "error"
    ? "destructive" as const
    : normalized === "warning"
      ? "secondary" as const
      : "outline" as const
}

function AttentionItem({ item }: { item: DiagnosticsAttentionItem }) {
  const { intlLocale, t } = useI18n()
  const normalizedSeverity = item.severity.toLowerCase()
  const severityKey = severityKeys[normalizedSeverity as keyof typeof severityKeys]
  const severity = severityKey ? t(severityKey) : item.severity
  const href = safeIncidentHref(item.href)
  const content = (
    <div className="min-w-0 flex-1 space-y-1">
      <div className="flex min-w-0 items-center gap-2">
        <Badge variant={severityBadgeVariant(item.severity)}>{severity}</Badge>
        <span className="truncate text-xs text-muted-foreground">{item.feature}</span>
      </div>
      <div className="line-clamp-2 text-sm leading-5 text-foreground">{item.summary}</div>
      <div className="text-xs text-muted-foreground">
        {formatRelativeTime(item.lastSeenAtUtc, intlLocale)}
      </div>
    </div>
  )

  return href ? (
    <DropdownMenuItem asChild className="items-start px-3 py-2.5">
      <Link to={href}>{content}</Link>
    </DropdownMenuItem>
  ) : (
    <div className="px-3 py-2.5">{content}</div>
  )
}

function attentionState(
  data: DiagnosticsAttentionResponse | undefined,
  unavailable: boolean,
  pending: boolean,
) {
  if (pending) {
    return "loading"
  }

  if (unavailable || !data) {
    return "unavailable"
  }

  return data.state === "warning" || data.state === "attention"
    ? data.state
    : data.state === "ready"
      ? "ready"
      : "unavailable"
}

export function DiagnosticsAttentionMenu() {
  const { intlLocale, t } = useI18n()
  const query = useDiagnosticsAttention()

  const unavailable = query.isError && !query.data
  const stale = (query.isRefetchError && Boolean(query.data)) || Boolean(query.data?.partial)
  const state = attentionState(query.data, unavailable, query.isPending)
  const total = query.data?.total ?? 0
  const visibleItems = query.data?.items.slice(0, 5) ?? []
  const label = query.isPending
    ? t("diagnostics.attention.loading")
    : state === "ready"
      ? t("diagnostics.attention.readyLabel")
      : state === "warning"
        ? t("diagnostics.attention.warningLabel", { count: total })
        : state === "attention"
          ? t("diagnostics.attention.errorLabel", { count: total })
          : t("diagnostics.attention.unavailableLabel")
  const Icon = state === "attention"
    ? CircleAlert
    : state === "warning"
      ? TriangleAlert
      : state === "unavailable"
        ? BellOff
        : Bell

  return (
    <DropdownMenu>
      <DropdownMenuTrigger asChild>
        <Button
          type="button"
          variant="outline"
          size="icon-sm"
          aria-label={label}
          title={label}
          data-attention-state={state}
          data-attention-stale={stale ? "true" : undefined}
          className={cn(
            "relative",
            state === "warning" && "border-amber-500/40 text-amber-700 dark:text-amber-300",
            state === "attention" && "border-destructive/40 text-destructive",
            state === "unavailable" && "border-muted-foreground/30 text-muted-foreground",
          )}
        >
          <Icon className="h-4 w-4" aria-hidden="true" />
          {total > 0 ? (
            <span
              aria-hidden="true"
              className={cn(
                "absolute -right-1.5 -top-1.5 flex min-h-4 min-w-4 items-center justify-center rounded-full px-1 text-[0.6rem] font-bold leading-none shadow-sm ring-2 ring-background",
                state === "attention"
                  ? "bg-destructive text-white"
                  : "bg-amber-500 text-black",
              )}
            >
              {total > 99 ? "99+" : total}
            </span>
          ) : null}
        </Button>
      </DropdownMenuTrigger>

      <DropdownMenuContent align="end" className="w-[min(24rem,calc(100vw-2rem))] p-0">
        <DropdownMenuLabel className="space-y-1 px-3 py-2.5">
          <div className="text-sm font-semibold text-foreground">
            {t("diagnostics.attention.title")}
          </div>
          <div className="text-xs font-normal text-muted-foreground">
            {stale
              ? t("diagnostics.attention.stale")
              : query.data
                ? t("diagnostics.attention.updated", {
                    time: formatRelativeTime(query.data.observedAtUtc, intlLocale),
                  })
                : t("diagnostics.attention.description")}
          </div>
        </DropdownMenuLabel>

        <DropdownMenuSeparator />

        {state === "loading" ? (
          <div className="px-3 py-3 text-sm text-muted-foreground">
            {t("diagnostics.attention.loading")}
          </div>
        ) : state === "unavailable" ? (
          <div className="space-y-1 px-3 py-3">
            <div className="text-sm font-medium text-foreground">
              {t("diagnostics.attention.unavailableTitle")}
            </div>
            <div className="text-xs leading-5 text-muted-foreground">
              {t("diagnostics.attention.unavailableDescription")}
            </div>
          </div>
        ) : query.data && visibleItems.length > 0 ? (
          <div className="py-1">
            {visibleItems.map((item) => (
              <AttentionItem key={item.incidentId} item={item} />
            ))}
            {query.data.total > visibleItems.length ? (
              <div className="px-3 py-2 text-xs text-muted-foreground">
                {t("diagnostics.attention.more", {
                  count: query.data.total - visibleItems.length,
                })}
              </div>
            ) : null}
          </div>
        ) : total > 0 ? (
          <div className="space-y-1 px-3 py-3">
            <div className="text-sm font-medium text-foreground">
              {t("diagnostics.attention.unavailableTitle")}
            </div>
            <div className="text-xs leading-5 text-muted-foreground">
              {t("diagnostics.attention.unavailableDescription")}
            </div>
          </div>
        ) : (
          <div className="space-y-1 px-3 py-3">
            <div className="text-sm font-medium text-foreground">
              {t("diagnostics.attention.clearTitle")}
            </div>
            <div className="text-xs leading-5 text-muted-foreground">
              {t("diagnostics.attention.clearDescription")}
            </div>
          </div>
        )}

        <DropdownMenuSeparator />
        <DropdownMenuItem asChild className="px-3 py-2.5">
          <Link to="/diagnostics/logs?tab=incidents">
            {t("diagnostics.attention.viewAll")}
          </Link>
        </DropdownMenuItem>
      </DropdownMenuContent>
    </DropdownMenu>
  )
}
