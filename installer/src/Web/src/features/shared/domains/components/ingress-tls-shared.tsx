import { cloneElement, useId, type ReactElement } from "react"
import { CheckCircle2 } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Badge } from "@/components/ui/badge"
import { Label } from "@/components/ui/label"
import type { CertificateOperationEvidence } from "@/features/shared/domains/api/domains.types"

export function StatusBadge({ label, ok }: { label: string; ok: boolean }) {
  return (
    <Badge variant={ok ? "secondary" : "destructive"}>
      {ok ? <CheckCircle2 className="mr-1 h-3 w-3" /> : null}
      {label}
    </Badge>
  )
}

export function ReadinessTile({
  label,
  ok,
  value,
}: {
  label: string
  ok: boolean
  value?: string
}) {
  return (
    <div className="rounded-lg border border-border/80 bg-background/40 p-3">
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </div>
      <div className="mt-2">
        <StatusBadge label={value ?? (ok ? "OK" : "No")} ok={ok} />
      </div>
    </div>
  )
}

export function InfoLine({ label, value }: { label: string; value: string }) {
  return (
    <div className="min-w-0">
      <div className="text-[0.7rem] font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </div>
      <div className="mt-0.5 break-all">{value}</div>
    </div>
  )
}

export function FormField({
  label,
  children,
}: {
  label: string
  children: ReactElement<{ id?: string }>
}) {
  const generatedId = useId()
  const controlId = children.props.id ?? generatedId

  return (
    <div className="grid gap-2">
      <Label htmlFor={controlId}>{label}</Label>
      {cloneElement(children, { id: controlId })}
    </div>
  )
}

export function EvidenceRow({ item }: { item: CertificateOperationEvidence }) {
  const { t } = useI18n()
  const status = item.status ?? "Info"
  const statusLabel = operationStatusLabel(status, t)

  return (
    <div className="rounded-lg border border-border/70 bg-muted/30 p-3 text-xs">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="font-medium">{item.key}</div>
        <Badge variant={status === "Failed" ? "destructive" : "outline"}>
          {t("operatorDomains.certificates.result.recordedStatus", { status: statusLabel })}
        </Badge>
      </div>
      <div className="mt-2 break-all text-muted-foreground">
        {item.sensitive
          ? t("operatorDomains.certificates.result.sensitiveValue")
          : item.value}
      </div>
    </div>
  )
}

export function getErrorMessage(error: unknown): string {
  if (error instanceof Error) {
    return error.message
  }

  if (typeof error === "string") {
    return error
  }

  try {
    return JSON.stringify(error)
  } catch {
    return String(error)
  }
}

export function formatDateTime(value: string): string {
  return new Date(value).toLocaleString()
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
    case "info":
      return t("operatorDomains.certificates.result.statusInfo")
    default:
      return status
  }
}
