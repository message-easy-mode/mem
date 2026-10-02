import { CircleAlert, Info, RefreshCw, RotateCcw, ShieldCheck } from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { ConfirmationDialog } from "@/components/ui/confirmation-dialog"

import type {
  FederationMode,
  RuntimeStackFederationReview,
} from "../api/federation.types"

type FederationReviewDialogProps = {
  open: boolean
  review: RuntimeStackFederationReview | null
  onOpenChange: (open: boolean) => void
  onConfirm: () => void
}

export function FederationReviewDialog({
  open,
  review,
  onOpenChange,
  onConfirm,
}: FederationReviewDialogProps) {
  const { t } = useI18n()
  if (!review) return null

  return (
    <ConfirmationDialog
      open={open}
      onOpenChange={onOpenChange}
      title={t("federation.review.title")}
      description={t("federation.review.description")}
      confirmLabel={t("federation.review.confirm")}
      cancelLabel={t("federation.review.cancel")}
      confirmDisabled={review.noChange}
      onConfirm={onConfirm}
      className="max-h-[calc(100vh-2rem)] max-w-2xl overflow-y-auto"
    >
      <div className="grid gap-3 sm:grid-cols-2">
        <ReviewFact
          label={t("federation.review.currentMode")}
          value={modeLabel(review.currentMode, t)}
        />
        <ReviewFact
          label={t("federation.review.proposedMode")}
          value={modeLabel(review.proposedMode, t)}
        />
      </div>

      <div className="grid gap-3 md:grid-cols-2">
        <DomainList
          title={t("federation.review.currentDomains")}
          domains={review.currentAllowlist}
          empty={t("federation.review.noDomains")}
        />
        <DomainList
          title={t("federation.review.proposedDomains")}
          domains={review.canonicalAllowlist}
          empty={t("federation.review.noDomains")}
        />
      </div>

      {(review.addedDomains.length > 0 || review.removedDomains.length > 0) ? (
        <div className="grid gap-3 md:grid-cols-2">
          <DomainList
            title={t("federation.review.addedDomains")}
            domains={review.addedDomains}
            empty={t("federation.review.noneAdded")}
          />
          <DomainList
            title={t("federation.review.removedDomains")}
            domains={review.removedDomains}
            empty={t("federation.review.noneRemoved")}
          />
        </div>
      ) : null}

      <div className="grid gap-3 sm:grid-cols-2">
        <ReviewBoolean
          icon={RefreshCw}
          label={t("federation.review.restart")}
          value={review.restartRequired
            ? t("federation.review.required")
            : t("federation.review.notRequired")}
        />
        <ReviewBoolean
          icon={ShieldCheck}
          label={t("federation.review.ingress")}
          value={review.ingressChangeRequired
            ? t("federation.review.willChange")
            : t("federation.review.willNotChange")}
        />
      </div>

      {review.restartRequired ? (
        <p className="text-sm text-muted-foreground">
          {t("federation.review.restartImpact")}
        </p>
      ) : null}

      {review.noChange ? (
        <Alert>
          <Info className="h-4 w-4" />
          <AlertTitle>{t("federation.review.noChangeTitle")}</AlertTitle>
          <AlertDescription>{t("federation.review.noChangeDescription")}</AlertDescription>
        </Alert>
      ) : null}

      {review.warnings.map((warning) => (
        <Alert key={warning.code} className="border-amber-500/30 bg-amber-500/10">
          <CircleAlert className="h-4 w-4" />
          <AlertTitle>{reviewWarningTitle(warning.code, t)}</AlertTitle>
          <AlertDescription>{reviewWarningDescription(warning.code, warning.detail, t)}</AlertDescription>
        </Alert>
      ))}

      <Alert>
        <RotateCcw className="h-4 w-4" />
        <AlertTitle>{t("federation.review.rollbackTitle")}</AlertTitle>
        <AlertDescription>{t("federation.review.rollbackDescription")}</AlertDescription>
      </Alert>

      <p className="text-sm text-muted-foreground">
        {t("federation.review.historyRetained")}
      </p>
      <p className="text-sm font-medium">
        {review.proposedMode === "public"
          ? t("federation.review.confirmationPublic")
          : review.proposedMode === "local_only"
            ? t("federation.review.confirmationLocalOnly")
            : t("federation.review.confirmationRestricted")}
      </p>
    </ConfirmationDialog>
  )
}

function reviewWarningTitle(
  code: string,
  t: (key: TranslationKey) => string,
) {
  switch (code) {
    case "federation_local_only_client_access_remains_public":
      return t("federation.review.localOnlyClientTitle")
    case "federation_historical_state_retained":
      return t("federation.review.historyImpactTitle")
    default:
      return t("federation.review.roomImpactTitle")
  }
}

function reviewWarningDescription(
  code: string,
  detail: string,
  t: (key: TranslationKey) => string,
) {
  switch (code) {
    case "federation_existing_rooms_may_be_affected":
      return t("federation.review.roomImpactDescription")
    case "federation_local_only_client_access_remains_public":
      return t("federation.review.localOnlyClientDescription")
    case "federation_historical_state_retained":
      return t("federation.review.historyRetained")
    default:
      return detail
  }
}


function ReviewFact({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border bg-muted/20 p-3">
      <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </p>
      <p className="mt-2 font-medium">{value}</p>
    </div>
  )
}

function ReviewBoolean({
  icon: Icon,
  label,
  value,
}: {
  icon: typeof RefreshCw
  label: string
  value: string
}) {
  return (
    <div className="flex items-center gap-3 rounded-lg border p-3">
      <Icon className="h-4 w-4 shrink-0" aria-hidden="true" />
      <div>
        <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
          {label}
        </p>
        <p className="mt-1 text-sm font-medium">{value}</p>
      </div>
    </div>
  )
}

function DomainList({
  title,
  domains,
  empty,
}: {
  title: string
  domains: string[]
  empty: string
}) {
  return (
    <div className="rounded-lg border p-3">
      <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {title}
      </p>
      {domains.length > 0 ? (
        <ul className="mt-2 space-y-1">
          {domains.map((domain) => (
            <li key={domain} className="font-mono text-sm">{domain}</li>
          ))}
        </ul>
      ) : (
        <p className="mt-2 text-sm text-muted-foreground">{empty}</p>
      )}
    </div>
  )
}

function modeLabel(
  mode: FederationMode,
  t: (key: TranslationKey) => string,
) {
  switch (mode) {
    case "public": return t("federation.mode.public")
    case "restricted": return t("federation.mode.restricted")
    case "local_only": return t("federation.mode.localOnly")
    default: return t("federation.mode.unknown")
  }
}
