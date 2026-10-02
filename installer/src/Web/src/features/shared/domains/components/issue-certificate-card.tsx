import { useEffect, useState } from "react"

import {
  CheckCircle2,
  Circle,
  CircleAlert,
  CircleX,
  Clock3,
  Loader2,
  MinusCircle,
  ShieldCheck,
} from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import type { DomainCertificateIssueOperation } from "@/features/shared/domains/api/domains.types"
import { FormField } from "@/features/shared/domains/components/ingress-tls-shared"
import type {
  IssueCertificateForm,
  IssueFormValues,
} from "@/features/shared/domains/components/types"

type IssuancePhaseId =
  | "prepare"
  | "publish-dns"
  | "wait-dns"
  | "validate-dns"
  | "issue"
  | "store-validate"
  | "renewal-credential"

type IssuancePhaseState =
  | "completed"
  | "current"
  | "pending"
  | "failed"
  | "warning"
  | "not-applicable"

type IssuancePhase = {
  id: IssuancePhaseId
  state: IssuancePhaseState
  detail: string | null
}

const issuancePhaseOrder: IssuancePhaseId[] = [
  "prepare",
  "publish-dns",
  "wait-dns",
  "validate-dns",
  "issue",
  "store-validate",
  "renewal-credential",
]

export function IssueCertificateCard({
  form,
  isSubmitting,
  operation,
  lockDomain = false,
  onSubmit,
}: {
  form: IssueCertificateForm
  isSubmitting: boolean
  operation?: DomainCertificateIssueOperation | null
  lockDomain?: boolean
  onSubmit: (values: IssueFormValues) => void
}) {
  const { t } = useI18n()
  const useStaging = form.watch("useStaging")
  const watchedDomain = form.watch("domain")
  const watchedZone = form.watch("zone")
  const operationActive = isActiveOperation(operation)
  const isBusy = isSubmitting || operationActive
  const [now, setNow] = useState(() => Date.now())
  const [requestMode, setRequestMode] = useState(() => !operation)
  const [showRequestDetails, setShowRequestDetails] = useState(false)

  useEffect(() => {
    if (!operationActive) return

    setNow(Date.now())
    const timer = window.setInterval(() => setNow(Date.now()), 1000)
    return () => window.clearInterval(timer)
  }, [operationActive, operation?.operationId])

  useEffect(() => {
    if (!operation) return
    setRequestMode(false)
    setShowRequestDetails(false)
  }, [operation?.operationId])

  const showRequestForm = !operation || requestMode

  function beginAnotherIssuance() {
    form.setValue("providerToken", "")
    form.clearErrors()
    setShowRequestDetails(false)
    setRequestMode(true)
  }

  if (!showRequestForm && operation) {
    const succeeded = isSucceededOperation(operation)

    return (
      <Card>
        <CardContent className="mx-auto max-w-xl space-y-5 pt-6">
          <div
            className="rounded-2xl border border-border bg-muted/20 p-4"
            data-testid="certificate-request-summary"
          >
            <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
              <div className="min-w-0">
                <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                  {t("operatorDomains.certificates.issue.requestSummaryTitle")}
                </div>
                <div className="mt-1 break-all font-semibold text-foreground">
                  {watchedDomain || t("operatorDomains.certificates.issue.progressDomainUnknown")}
                </div>
                <div className="mt-1 text-sm text-muted-foreground">
                  {operation.useStaging
                    ? t("operatorDomains.common.staging")
                    : t("operatorDomains.common.production")}
                  {" · deSEC"}
                </div>
              </div>
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={() => setShowRequestDetails((current) => !current)}
              >
                {showRequestDetails
                  ? t("operatorDomains.certificates.issue.hideRequestDetails")
                  : t("operatorDomains.certificates.issue.showRequestDetails")}
              </Button>
            </div>

            {showRequestDetails ? (
              <div className="mt-4 grid gap-3 border-t border-border/70 pt-4 text-sm sm:grid-cols-2">
                <SafeRequestFact
                  label={t("operatorDomains.certificates.issue.wildcardDomain")}
                  value={watchedDomain || t("operatorDomains.certificates.issue.progressDomainUnknown")}
                />
                <SafeRequestFact
                  label={t("operatorDomains.common.dnsProvider")}
                  value="deSEC"
                />
                <SafeRequestFact
                  label={t("operatorDomains.common.dnsZone")}
                  value={watchedZone || "—"}
                />
                <SafeRequestFact
                  label={t("operatorDomains.certificates.issue.requestEnvironment")}
                  value={
                    operation.useStaging
                      ? t("operatorDomains.common.staging")
                      : t("operatorDomains.common.production")
                  }
                />
                <div className="sm:col-span-2 text-xs text-muted-foreground">
                  {t("operatorDomains.certificates.issue.requestSecretBoundary")}
                </div>
              </div>
            ) : null}
          </div>

          <CertificateIssuanceProgress
            operation={operation}
            watchedDomain={watchedDomain}
            now={now}
          />

          {operation.isTerminal ? (
            <Button type="button" variant="outline" className="w-full" onClick={beginAnotherIssuance}>
              {succeeded
                ? t("operatorDomains.certificates.issue.issueAnother")
                : t("operatorDomains.certificates.issue.tryAnother")}
            </Button>
          ) : null}
        </CardContent>
      </Card>
    )
  }

  return (
    <Card>
      <CardHeader className="text-center">
        <CardTitle>{t("operatorDomains.certificates.issue.title")}</CardTitle>
        <CardDescription>
          {useStaging
            ? t("operatorDomains.certificates.issue.descriptionStaging")
            : t("operatorDomains.certificates.issue.descriptionProduction")}
        </CardDescription>
      </CardHeader>

      <CardContent className="mx-auto max-w-xl space-y-6">
        <div className="rounded-2xl border border-border bg-muted/20 p-4 text-sm">
          <div className="font-medium text-foreground">
            {t("operatorDomains.certificates.issue.thisStepWill")}
          </div>
          <ul className="mt-2 space-y-1 text-muted-foreground">
            <li>✓ {t("operatorDomains.certificates.issue.willDns01")}</li>
            <li>✓ {t("operatorDomains.certificates.issue.willRequest")}</li>
            <li>✓ {t("operatorDomains.certificates.issue.willStore")}</li>
            <li>— {t("operatorDomains.certificates.issue.willNotChangeProxy")}</li>
          </ul>
        </div>

        <form
          className="space-y-5"
          onSubmit={form.handleSubmit((values) => {
            setShowRequestDetails(false)
            onSubmit(values)
          })}
        >
          <fieldset className="space-y-5" disabled={isBusy}>
            <FormField label={t("operatorDomains.certificates.issue.wildcardDomain")}>
              <Input
                placeholder="*.example.com"
                readOnly={lockDomain}
                autoComplete="off"
                {...form.register("domain")}
              />
            </FormField>

            <FormField label={t("operatorDomains.common.dnsProvider")}>
              <Input value="deSEC" disabled />
            </FormField>
            <p className="-mt-3 text-xs text-muted-foreground">
              {t("operatorDomains.certificates.issue.desecSupported")}
            </p>

            <FormField label={t("operatorDomains.certificates.issue.acmeEmail")}>
              <Input type="email" autoComplete="email" {...form.register("email")} />
            </FormField>

            <FormField label={t("operatorDomains.certificates.issue.desecToken")}>
              <Input
                type="password"
                autoComplete="off"
                placeholder={t("operatorDomains.certificates.issue.tokenPlaceholder")}
                {...form.register("providerToken")}
              />
            </FormField>
            <p className="-mt-3 text-xs text-muted-foreground">
              {useStaging
                ? t("operatorDomains.certificates.issue.tokenHelpStaging")
                : t("operatorDomains.certificates.issue.tokenHelpProduction")}
            </p>

            <details className="rounded-2xl border border-border bg-muted/10 p-4">
              <summary className="cursor-pointer text-sm font-medium text-muted-foreground">
                {t("operatorDomains.certificates.issue.advancedDns")}
              </summary>
              <div className="mt-4">
                <FormField label={t("operatorDomains.common.dnsZone")}>
                  <Input
                    placeholder="example.com"
                    readOnly={lockDomain}
                    autoComplete="off"
                    {...form.register("zone")}
                  />
                </FormField>
                <p className="mt-2 text-xs text-muted-foreground">
                  {t("operatorDomains.certificates.issue.zoneHelp")}
                </p>
              </div>
            </details>

            <label className="flex items-start gap-2 rounded-xl border border-amber-500/20 bg-amber-500/5 p-3 text-sm">
              <input
                className="mt-0.5"
                type="checkbox"
                {...form.register("useStaging")}
              />
              <span>
                <span className="block font-medium">
                  {t("operatorDomains.certificates.issue.useStaging")}
                </span>
                <span className="mt-1 block text-xs text-muted-foreground">
                  {t("operatorDomains.certificates.issue.stagingHelp")}
                </span>
              </span>
            </label>
          </fieldset>

          <Button className="w-full" type="submit" disabled={isBusy}>
            {isBusy ? (
              <Loader2 className="mr-2 h-4 w-4 animate-spin" />
            ) : (
              <ShieldCheck className="mr-2 h-4 w-4" />
            )}
            {isBusy
              ? t("operatorDomains.certificates.issue.issuing")
              : useStaging
                ? t("operatorDomains.certificates.issue.issueStaging")
                : t("operatorDomains.certificates.issue.issueProduction")}
          </Button>
        </form>
      </CardContent>
    </Card>
  )
}

function SafeRequestFact({ label, value }: { label: string; value: string }) {
  return (
    <div className="min-w-0">
      <div className="text-[0.65rem] font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </div>
      <div className="mt-1 break-all text-sm text-foreground">{value}</div>
    </div>
  )
}

function CertificateIssuanceProgress({
  operation,
  watchedDomain,
  now,
}: {
  operation: DomainCertificateIssueOperation
  watchedDomain: string
  now: number
}) {
  const { t } = useI18n()
  const active = isActiveOperation(operation)
  const succeeded = isSucceededOperation(operation)
  const failed = isFailedOperation(operation)
  const elapsedUntil = active
    ? now
    : operation.completedAtUtc
      ? Date.parse(operation.completedAtUtc)
      : now
  const elapsed = formatElapsed(operation.requestedAtUtc, elapsedUntil, t)
  const phases = buildIssuancePhases(operation)

  const panelClass = failed
    ? "border-red-500/30 bg-red-500/5"
    : succeeded
      ? "border-emerald-500/30 bg-emerald-500/5"
      : "border-sky-500/30 bg-sky-500/10"

  return (
    <div
      className={`rounded-2xl border p-4 ${panelClass}`}
      data-testid="certificate-issuance-progress"
      role="status"
      aria-live="polite"
    >
      <div className="flex items-start justify-between gap-4">
        <div className="flex min-w-0 items-start gap-3">
          <OperationIcon active={active} succeeded={succeeded} failed={failed} />
          <div className="min-w-0">
            <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
              {active
                ? t("operatorDomains.certificates.issue.progressLabel")
                : t("operatorDomains.certificates.issue.progressLastOperation")}
            </div>
            <div className="mt-1 font-semibold text-foreground">
              {operation.useStaging
                ? t("operatorDomains.certificates.issue.progressTitleStaging")
                : t("operatorDomains.certificates.issue.progressTitleProduction")}
            </div>
            <div className="mt-1 break-all text-xs text-muted-foreground">
              {watchedDomain || t("operatorDomains.certificates.issue.progressDomainUnknown")}
            </div>
          </div>
        </div>
        <div className="shrink-0 rounded-lg border border-border/70 bg-background/40 px-3 py-2 text-right">
          <div className="text-[0.65rem] font-medium uppercase tracking-wide text-muted-foreground">
            {t("operatorDomains.certificates.issue.elapsed")}
          </div>
          <div className="mt-0.5 font-mono text-sm font-semibold tabular-nums">
            {elapsed}
          </div>
        </div>
      </div>

      <div className="mt-4 rounded-xl border border-border/70 bg-background/30 p-3">
        <div className="font-medium text-foreground">
          {active
            ? currentOperationPhaseLabel(operation, t)
            : succeeded
              ? t("operatorDomains.certificates.issue.progressTerminalSucceeded")
              : failed
                ? t("operatorDomains.certificates.issue.progressTerminalFailed")
                : operation.phaseSummary ?? operation.status}
        </div>
        {active ? (
          <>
            <div className="mt-2 text-sm text-muted-foreground">
              {t("operatorDomains.certificates.issue.progressWaitHelp")}
            </div>
            <div className="mt-2 text-xs text-muted-foreground">
              {t("operatorDomains.certificates.issue.progressSafeToLeave")}
            </div>
          </>
        ) : null}
      </div>

      <div className="mt-4" data-testid="certificate-issuance-timeline">
        <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
          {t("operatorDomains.certificates.issue.progressTimelineTitle")}
        </div>
        <div className="mt-3 space-y-2">
          {phases.map((phase) => (
            <div
              key={phase.id}
              className="flex items-start gap-3 rounded-lg border border-border/60 bg-background/25 px-3 py-2.5"
              data-testid={`certificate-issuance-phase-${phase.id}`}
              data-state={phase.state}
            >
              <PhaseIcon state={phase.state} />
              <div className="min-w-0 flex-1">
                <div className="text-sm font-medium text-foreground">
                  {phaseLabel(phase.id, t)}
                </div>
                {phase.detail ? (
                  <div className="mt-1 text-xs text-muted-foreground">{phase.detail}</div>
                ) : null}
              </div>
              <div className="shrink-0 text-xs text-muted-foreground">
                {phaseStateLabel(phase.state, t)}
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  )
}

function OperationIcon({
  active,
  succeeded,
  failed,
}: {
  active: boolean
  succeeded: boolean
  failed: boolean
}) {
  const className =
    "mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-full border border-border/70 bg-background/40"

  if (active) {
    return (
      <div className={className}>
        <Loader2 className="h-5 w-5 animate-spin text-sky-400" />
      </div>
    )
  }

  if (succeeded) {
    return (
      <div className={className}>
        <CheckCircle2 className="h-5 w-5 text-emerald-400" />
      </div>
    )
  }

  if (failed) {
    return (
      <div className={className}>
        <CircleX className="h-5 w-5 text-red-400" />
      </div>
    )
  }

  return (
    <div className={className}>
      <Clock3 className="h-5 w-5 text-muted-foreground" />
    </div>
  )
}

function PhaseIcon({ state }: { state: IssuancePhaseState }) {
  switch (state) {
    case "completed":
      return <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-emerald-400" />
    case "current":
      return <Loader2 className="mt-0.5 h-4 w-4 shrink-0 animate-spin text-sky-400" />
    case "failed":
      return <CircleX className="mt-0.5 h-4 w-4 shrink-0 text-red-400" />
    case "warning":
      return <CircleAlert className="mt-0.5 h-4 w-4 shrink-0 text-amber-400" />
    case "not-applicable":
      return <MinusCircle className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground" />
    default:
      return <Circle className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground/70" />
  }
}

function buildIssuancePhases(operation: DomainCertificateIssueOperation): IssuancePhase[] {
  const succeeded = isSucceededOperation(operation)
  const failed = isFailedOperation(operation)
  const failurePhaseCode = failed ? lastNonTerminalPhaseCode(operation) : null
  const referencePhaseCode = failed
    ? failurePhaseCode
    : operation.phaseCode ?? lastNonTerminalPhaseCode(operation)
  const resolvedCurrentIndex = phaseIndexForCode(referencePhaseCode)
  const currentIndex = resolvedCurrentIndex >= 0 ? resolvedCurrentIndex : 0

  return issuancePhaseOrder.map((id, index) => {
    if (id === "renewal-credential" && operation.useStaging) {
      return { id, state: "not-applicable", detail: null }
    }

    if (succeeded) {
      if (
        id === "renewal-credential" &&
        operation.result?.errorCode === "RenewalCredentialPersistenceFailed"
      ) {
        return {
          id,
          state: "warning",
          detail: operation.result.errorDetail ?? operation.result.message,
        }
      }

      return { id, state: "completed", detail: null }
    }

    if (failed) {
      if (index < currentIndex) {
        return { id, state: "completed", detail: null }
      }

      if (index === currentIndex) {
        return {
          id,
          state: "failed",
          detail: lastNonTerminalSummary(operation),
        }
      }

      return { id, state: "pending", detail: null }
    }

    if (index < currentIndex) {
      return { id, state: "completed", detail: null }
    }

    if (index === currentIndex) {
      return {
        id,
        state: "current",
        detail: operation.phaseSummary,
      }
    }

    return { id, state: "pending", detail: null }
  })
}

function lastNonTerminalPhaseCode(operation: DomainCertificateIssueOperation) {
  for (let index = operation.progress.length - 1; index >= 0; index -= 1) {
    const phaseCode = operation.progress[index]?.phaseCode
    if (phaseCode && phaseIndexForCode(phaseCode) >= 0) {
      return phaseCode
    }
  }

  return "certificate.queued"
}

function lastNonTerminalSummary(operation: DomainCertificateIssueOperation) {
  for (let index = operation.progress.length - 1; index >= 0; index -= 1) {
    const snapshot = operation.progress[index]
    if (snapshot && phaseIndexForCode(snapshot.phaseCode) >= 0) {
      return snapshot.safeSummary
    }
  }

  return operation.result?.message ?? null
}

function phaseIndexForCode(phaseCode: string | null | undefined) {
  const phaseId = phaseIdForCode(phaseCode)
  return phaseId ? issuancePhaseOrder.indexOf(phaseId) : -1
}

function phaseIdForCode(phaseCode: string | null | undefined): IssuancePhaseId | null {
  switch (phaseCode) {
    case "certificate.queued":
    case "certificate.start":
    case "certificate.acme-account":
    case "certificate.acme-order":
      return "prepare"
    case "certificate.dns-publish":
      return "publish-dns"
    case "certificate.dns-authoritative":
    case "certificate.dns-stability":
      return "wait-dns"
    case "certificate.acme-validation":
    case "certificate.acme-validation-retry":
    case "certificate.acme-dns-recovery":
      return "validate-dns"
    case "certificate.acme-finalize":
    case "certificate.download":
      return "issue"
    case "certificate.store":
    case "certificate.validate":
      return "store-validate"
    case "certificate.renewal-credential":
      return "renewal-credential"
    default:
      return null
  }
}

function currentOperationPhaseLabel(
  operation: DomainCertificateIssueOperation,
  t: ReturnType<typeof useI18n>["t"],
) {
  const phaseId = phaseIdForCode(operation.phaseCode ?? lastNonTerminalPhaseCode(operation))
  return phaseId
    ? phaseLabel(phaseId, t)
    : operation.phaseSummary ?? t("operatorDomains.certificates.issue.progressQueued")
}

function phaseLabel(
  phaseId: IssuancePhaseId,
  t: ReturnType<typeof useI18n>["t"],
) {
  switch (phaseId) {
    case "prepare":
      return t("operatorDomains.certificates.issue.progressPhasePrepare")
    case "publish-dns":
      return t("operatorDomains.certificates.issue.progressPhasePublishDns")
    case "wait-dns":
      return t("operatorDomains.certificates.issue.progressPhaseWaitDns")
    case "validate-dns":
      return t("operatorDomains.certificates.issue.progressPhaseValidateDns")
    case "issue":
      return t("operatorDomains.certificates.issue.progressPhaseIssue")
    case "store-validate":
      return t("operatorDomains.certificates.issue.progressPhaseStoreValidate")
    case "renewal-credential":
      return t("operatorDomains.certificates.issue.progressPhaseRenewalCredential")
  }
}

function phaseStateLabel(
  state: IssuancePhaseState,
  t: ReturnType<typeof useI18n>["t"],
) {
  switch (state) {
    case "completed":
      return t("operatorDomains.certificates.issue.progressPhaseCompleted")
    case "current":
      return t("operatorDomains.certificates.issue.progressPhaseCurrent")
    case "failed":
      return t("operatorDomains.certificates.issue.progressPhaseFailed")
    case "warning":
      return t("operatorDomains.certificates.result.statusWarning")
    case "not-applicable":
      return t("operatorDomains.certificates.issue.progressPhaseNotApplicable")
    default:
      return t("operatorDomains.certificates.issue.progressPhasePending")
  }
}

function isActiveOperation(operation?: DomainCertificateIssueOperation | null) {
  const status = operation?.status?.toLowerCase()
  return status === "queued" || status === "running"
}

function isSucceededOperation(operation: DomainCertificateIssueOperation) {
  const status = operation.status.toLowerCase()
  return operation.isTerminal && (status === "succeeded" || operation.result?.succeeded === true)
}

function isFailedOperation(operation: DomainCertificateIssueOperation) {
  const status = operation.status.toLowerCase()
  return operation.isTerminal && (status === "failed" || operation.result?.succeeded === false)
}

function formatElapsed(
  requestedAtUtc: string | null,
  now: number,
  t: ReturnType<typeof useI18n>["t"],
) {
  const requestedAt = requestedAtUtc ? Date.parse(requestedAtUtc) : Number.NaN
  const seconds = Number.isFinite(requestedAt) && Number.isFinite(now)
    ? Math.max(0, Math.floor((now - requestedAt) / 1000))
    : 0

  if (seconds < 60) {
    return t("operatorDomains.certificates.issue.elapsedSeconds", { count: seconds })
  }

  const minutes = Math.floor(seconds / 60)
  const remainingSeconds = seconds % 60
  return t("operatorDomains.certificates.issue.elapsedMinutesSeconds", {
    minutes,
    seconds: remainingSeconds.toString().padStart(2, "0"),
  })
}
