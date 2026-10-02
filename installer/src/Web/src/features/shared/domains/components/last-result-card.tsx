import { CheckCircle2, CircleAlert } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { EvidenceRow } from "@/features/shared/domains/components/ingress-tls-shared"
import type { LastResult } from "@/features/shared/domains/components/types"

export function LastResultCard({ result }: { result: LastResult }) {
  const { t } = useI18n()
  const incidentId = result?.evidence.find((item) => item.key === "incidentId")?.value

  if (!result) {
    return null
  }

  return (
    <Card>
      <CardHeader>
        <CardTitle>{t("operatorDomains.certificates.result.title")}</CardTitle>
        <CardDescription>
          {t("operatorDomains.certificates.result.description")}
        </CardDescription>
      </CardHeader>

      <CardContent className="space-y-4">
        <Alert
          variant={
            result.succeeded ||
            result.status === "Checking" ||
            result.status === "Unconfirmed"
              ? "default"
              : "destructive"
          }
        >
          {result.succeeded ? (
            <CheckCircle2 className="h-4 w-4" />
          ) : (
            <CircleAlert className="h-4 w-4" />
          )}
          <AlertTitle>{operationStatusLabel(result.status, t)}</AlertTitle>
          <AlertDescription>
            <div>{result.message}</div>
            {result.errorDetail ? (
              <div className="mt-2 text-xs">{result.errorDetail}</div>
            ) : null}
            {incidentId ? (
              <a
                className="mt-3 inline-flex text-xs font-medium underline underline-offset-4"
                href={`/diagnostics/logs?incident=${encodeURIComponent(incidentId)}`}
              >
                {t("operatorDomains.certificates.result.openDiagnostics")}
              </a>
            ) : null}
          </AlertDescription>
        </Alert>

        <details className="rounded-xl border border-border bg-background p-4">
          <summary className="cursor-pointer text-sm font-medium">
            {t("operatorDomains.certificates.result.evidence", {
              count: result.evidence.length,
            })}
          </summary>

          <div className="mt-3 max-h-[520px] space-y-2 overflow-auto pr-2">
            {result.evidence.map((item, index) => (
              <EvidenceRow key={`${item.key}-${index}`} item={item} />
            ))}
          </div>
        </details>
      </CardContent>
    </Card>
  )
}

function operationStatusLabel(
  status: string,
  t: ReturnType<typeof useI18n>["t"],
) {
  switch (status.toLowerCase()) {
    case "succeeded":
    case "success":
      return t("operatorDomains.certificates.result.statusSucceeded")
    case "failed":
    case "failure":
      return t("operatorDomains.certificates.result.statusFailed")
    case "warning":
      return t("operatorDomains.certificates.result.statusWarning")
    case "checking":
      return t("operatorDomains.certificates.result.statusChecking")
    case "unconfirmed":
      return t("operatorDomains.certificates.result.statusUnconfirmed")
    default:
      return status
  }
}
