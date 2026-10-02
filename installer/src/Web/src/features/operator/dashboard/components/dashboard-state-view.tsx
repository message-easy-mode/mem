import { LoaderCircle } from "lucide-react"

import { ApiProblemAlert } from "@/components/operator/api-problem-alert"

import { formatDateTime } from "@/app/formatters"
import { useI18n } from "@/app/i18n/i18n-context"

export function DashboardLoadingState() {
  const { t } = useI18n()

  return (
    <section
      aria-label={t("dashboard.loading.title")}
      role="status"
      className="rounded-2xl border border-border bg-card/95 p-6 shadow-sm"
    >
      <div className="flex items-start gap-3">
        <LoaderCircle className="mt-0.5 h-5 w-5 animate-spin text-muted-foreground" />
        <div>
          <h2 className="font-semibold">{t("dashboard.loading.title")}</h2>
          <p className="mt-1 text-sm text-muted-foreground">
            {t("dashboard.loading.description")}
          </p>
        </div>
      </div>
    </section>
  )
}

type DashboardLoadErrorStateProps = {
  error: unknown
  onRetry: () => void
  retrying: boolean
}

export function DashboardLoadErrorState({
  error,
  onRetry,
  retrying,
}: DashboardLoadErrorStateProps) {
  const { t } = useI18n()

  return (
    <ApiProblemAlert
      error={error}
      title={t("dashboard.loadError.title")}
      fallbackDescription={t("dashboard.loadError.description")}
      onRetry={onRetry}
      retrying={retrying}
      className="items-start"
    />
  )
}

type DashboardRefreshErrorNoticeProps = {
  error: unknown
  generatedAtUtc: string
  onRetry: () => void
  retrying: boolean
}

export function DashboardRefreshErrorNotice({
  error,
  generatedAtUtc,
  onRetry,
  retrying,
}: DashboardRefreshErrorNoticeProps) {
  const { language, t } = useI18n()

  return (
    <ApiProblemAlert
      error={error}
      title={t("dashboard.refreshError.title")}
      fallbackDescription={t("dashboard.refreshError.description", {
        value: formatDateTime(generatedAtUtc, language),
      })}
      onRetry={onRetry}
      retrying={retrying}
      variant="default"
      className="border-amber-500/20 bg-amber-500/10"
    />
  )
}
