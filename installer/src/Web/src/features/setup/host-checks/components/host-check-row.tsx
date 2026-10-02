import { AlertTriangle } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { hostCheckStatusTranslationKey } from "@/features/setup/setup-status-i18n"
import { localizedHostCheckTitle } from "../host-check-copy-i18n"

import {
  getHostCheckStatusClassName,
  type HostCheckResult,
} from "../api/host-checks.types"

export function HostCheckRow({
  check,
  compact = false,
}: {
  check: HostCheckResult
  compact?: boolean
}) {
  const { t } = useI18n()
  const needsAttention = check.status !== "Pass"
  const hasTechnicalDetails =
    Boolean(check.whyItMatters) ||
    Boolean(check.details) ||
    Boolean(check.evidence?.length)

  return (
    <div className="space-y-3 rounded-xl border border-border bg-background p-4">
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <h3 className="font-medium">{localizedHostCheckTitle(check.key, t, check.title)}</h3>

            {check.blocking && check.status !== "Pass" && (
              <span className="rounded-full border border-red-500/30 bg-red-500/10 px-2 py-0.5 text-xs font-medium text-red-300">
                {t("setup.common.blocksInstall")}
              </span>
            )}
          </div>

          <p className="mt-1 text-sm text-muted-foreground">{check.summary}</p>
        </div>

        <span
          className={[
            "w-fit shrink-0 rounded-full border px-2.5 py-1 text-xs font-medium",
            getHostCheckStatusClassName(check.status),
          ].join(" ")}
        >
          {t(hostCheckStatusTranslationKey(check.status))}
        </span>
      </div>

      {needsAttention && check.recommendedAction ? (
        <div className="rounded-lg border border-amber-500/20 bg-amber-500/5 p-3">
          <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
            {t("setup.common.recommendedAction")}
          </div>
          <p className="mt-1 text-sm">{check.recommendedAction}</p>
        </div>
      ) : null}

      {hasTechnicalDetails ? (
        <details className="rounded-lg border border-border bg-muted/20 p-3" open={false}>
          <summary className="cursor-pointer text-xs font-medium uppercase tracking-wide text-muted-foreground">
            {compact ? t("setup.common.details") : t("common.technicalDetails")}
          </summary>

          <div className="mt-3 space-y-3">
            {check.whyItMatters ? (
              <div>
                <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                  {t("setup.common.whyItMatters")}
                </div>
                <p className="mt-1 text-sm">{check.whyItMatters}</p>
              </div>
            ) : null}

            {!needsAttention && check.recommendedAction ? (
              <div>
                <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                  {t("setup.common.recommendedAction")}
                </div>
                <p className="mt-1 text-sm">{check.recommendedAction}</p>
              </div>
            ) : null}

            {check.details ? (
              <Alert>
                <AlertTriangle className="h-4 w-4" />
                <AlertTitle>{t("setup.common.details")}</AlertTitle>
                <AlertDescription className="whitespace-pre-wrap">{check.details}</AlertDescription>
              </Alert>
            ) : null}

            {check.evidence && check.evidence.length > 0 ? (
              <details className="rounded-lg border border-border bg-background p-3">
                <summary className="cursor-pointer text-xs font-medium uppercase tracking-wide text-muted-foreground">
                  {t("setup.common.rawEvidence")}
                </summary>

                <div className="mt-3 space-y-2">
                  {check.evidence.map((item) => (
                    <div
                      key={`${item.kind}-${item.label}`}
                      className="rounded-lg border border-border bg-muted/20 p-3 text-sm"
                    >
                      <div className="font-medium">{item.label}</div>
                      <pre className="mt-2 max-h-64 overflow-auto whitespace-pre-wrap text-xs text-muted-foreground">
                        {item.sensitive
                          ? t("setup.common.hiddenSensitive")
                          : truncateEvidence(item.value, t("setup.common.truncatedEvidence"))}
                      </pre>
                    </div>
                  ))}
                </div>
              </details>
            ) : null}
          </div>
        </details>
      ) : null}
    </div>
  )
}

function truncateEvidence(value: string, truncationTemplate: string, maxLength = 4000) {
  if (value.length <= maxLength) {
    return value
  }

  return `${value.slice(0, maxLength)}\n\n${truncationTemplate.replace("{{count}}", String(value.length - maxLength))}`
}
