import { AlertTriangle, CheckCircle2, Circle, CircleDot } from "lucide-react"

import type { I18nContextValue } from "@/app/i18n/i18n-context"
import { Badge } from "@/components/ui/badge"
import { cn } from "@/lib/utils"

type Translate = I18nContextValue["t"]

export function migrationPhaseLabel(t: Translate, phase: string): string {
  switch (phase) {
    case "package-transfer": return t("migrationWorkspace.phase.packageTransfer")
    case "source-assessment": return t("migrationWorkspace.phase.sourceAssessment")
    case "legacy-compatibility": return t("migrationWorkspace.phase.legacyCompatibility")
    case "conversion": return t("migrationWorkspace.phase.conversion")
    case "staging": return t("migrationWorkspace.phase.staging")
    case "cutover": return t("migrationWorkspace.phase.cutover")
    case "acceptance": return t("migrationWorkspace.phase.acceptance")
    case "completed": return t("migrationWorkspace.phase.completed")
    case "closed": return t("migrationWorkspace.phase.closed")
    case "manual-review": return t("migrationWorkspace.phase.manualReview")
    default: return t("migrationWorkspace.phase.unknown")
  }
}

export function migrationStatusLabel(t: Translate, status: string): string {
  switch (status) {
    case "awaiting-package": return t("migrationWorkspace.status.awaitingPackage")
    case "package-validated": return t("migrationWorkspace.status.packageValidated")
    case "expired": return t("migrationWorkspace.status.expired")
    case "cancelled": return t("migrationWorkspace.status.cancelled")
    case "legacy-contract-previewed": return t("migrationWorkspace.status.legacyContractPreviewed")
    case "legacy-contract-committed": return t("migrationWorkspace.status.legacyContractCommitted")
    case "legacy-contract-accepted": return t("migrationWorkspace.status.legacyContractAccepted")
    case "legacy-contract-abandoned": return t("migrationWorkspace.status.legacyContractAbandoned")
    case "manual-review-required": return t("migrationWorkspace.status.manualReviewRequired")
    case "conversion-running": return t("migrationWorkspace.status.conversionRunning")
    case "conversion-failed": return t("migrationWorkspace.status.conversionFailed")
    case "staging-ready": return t("migrationWorkspace.status.stagingReady")
    case "staging-running": return t("migrationWorkspace.status.stagingRunning")
    case "staging-verified": return t("migrationWorkspace.status.stagingVerified")
    case "staging-failed": return t("migrationWorkspace.status.stagingFailed")
    case "staging-failed-retained": return t("migrationWorkspace.status.stagingFailedRetained")
    case "staging-destroyed": return t("migrationWorkspace.status.stagingDestroyed")
    case "staging-review-required": return t("migrationWorkspace.status.stagingReviewRequired")
    case "production-verification-passed": return t("migrationWorkspace.status.productionVerificationPassed")
    case "production-verification-expired": return t("migrationWorkspace.status.productionVerificationExpired")
    case "accepted": return t("migrationWorkspace.status.accepted")
    case "accepted-baseline-backup-pending": return t("migrationWorkspace.status.acceptedBaselinePending")
    case "accepted-baseline-backup-failed": return t("migrationWorkspace.status.acceptedBaselineFailed")
    case "migration-completed": return t("migrationWorkspace.status.migrationCompleted")
    default: return status
  }
}

export function migrationNextActionLabel(t: Translate, action: string): string {
  switch (action) {
    case "upload-package": return t("migrationWorkspace.action.uploadPackage")
    case "review-source": return t("migrationWorkspace.action.reviewSource")
    case "create-replacement-intake": return t("migrationWorkspace.action.createReplacementIntake")
    case "resolve-legacy-contract-findings": return t("migrationWorkspace.action.resolveLegacyFindings")
    case "commit-legacy-contract": return t("migrationWorkspace.action.commitLegacyContract")
    case "inspect-historical-artifacts": return t("migrationWorkspace.action.inspectHistoricalArtifacts")
    case "open-historical-proof": return t("migrationWorkspace.action.openHistoricalProof")
    case "review-migration": return t("migrationWorkspace.action.reviewMigration")
    case "review-conversion": return t("migrationWorkspace.action.reviewConversion")
    case "retry-conversion": return t("migrationWorkspace.action.retryConversion")
    case "start-private-staging": return t("migrationWorkspace.action.startPrivateStaging")
    case "review-private-staging": return t("migrationWorkspace.action.reviewPrivateStaging")
    case "retry-private-staging": return t("migrationWorkspace.action.retryPrivateStaging")
    case "destroy-private-staging": return t("migrationWorkspace.action.destroyPrivateStaging")
    case "recreate-private-staging": return t("migrationWorkspace.action.recreatePrivateStaging")
    case "review-production-adoption": return t("migrationWorkspace.action.reviewProductionAdoption")
    case "rerun-production-verification": return t("migrationWorkspace.action.rerunProductionVerification")
    case "accept-migration": return t("migrationWorkspace.action.acceptMigration")
    case "review-retention": return t("migrationWorkspace.action.reviewRetention")
    case "review-baseline-backup": return t("migrationWorkspace.action.reviewBaselineBackup")
    case "retry-baseline-backup": return t("migrationWorkspace.action.retryBaselineBackup")
    case "open-baseline-backup": return t("migrationWorkspace.action.openBaselineBackup")
    case "none": return t("migrationWorkspace.action.none")
    default: return action
  }
}

export function MigrationStatusBadge({ status, t }: { status: string; t: Translate }) {
  const variant =
    status === "expired" || status === "manual-review-required" || status.includes("failed")
      ? "destructive"
      : status === "package-validated" || status === "legacy-contract-accepted" || status === "staging-verified" || status === "production-verification-passed" || status === "accepted" || status === "migration-completed" || status === "accepted-baseline-backup-pending"
        ? "secondary"
        : "outline"

  return <Badge variant={variant}>{migrationStatusLabel(t, status)}</Badge>
}

const lifecycleSteps = ["package", "assessment", "conversion", "staging", "cutover", "acceptance"] as const
type LifecycleStep = (typeof lifecycleSteps)[number]

function lifecycleStepLabel(t: Translate, step: LifecycleStep): string {
  switch (step) {
    case "package": return t("migrationWorkspace.lifecycle.package")
    case "assessment": return t("migrationWorkspace.lifecycle.assessment")
    case "conversion": return t("migrationWorkspace.lifecycle.conversion")
    case "staging": return t("migrationWorkspace.lifecycle.staging")
    case "cutover": return t("migrationWorkspace.lifecycle.cutover")
    case "acceptance": return t("migrationWorkspace.lifecycle.acceptance")
  }
}

function currentLifecycleIndex(phase: string): number {
  switch (phase) {
    case "package-transfer": return 0
    case "source-assessment":
    case "legacy-compatibility":
    case "manual-review": return 1
    case "conversion": return 2
    case "staging": return 3
    case "cutover": return 4
    case "acceptance": return 5
    case "completed": return 6
    default: return -1
  }
}

export function MigrationLifecycle({ phase, t }: { phase: string; t: Translate }) {
  const currentIndex = currentLifecycleIndex(phase)

  return (
    <ol className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
      {lifecycleSteps.map((step, index) => {
        const state = currentIndex < 0 ? "upcoming" : index < currentIndex ? "completed" : index === currentIndex ? "current" : "upcoming"
        const Icon = state === "completed" ? CheckCircle2 : state === "current" ? CircleDot : Circle

        return (
          <li key={step} className={cn("flex items-center gap-3 rounded-lg border p-3", state === "current" && "border-primary/40 bg-primary/5")}>
            <Icon className={cn("h-5 w-5 shrink-0", state === "completed" && "text-primary", state === "current" && "text-primary", state === "upcoming" && "text-muted-foreground")} aria-hidden="true" />
            <div>
              <div className="text-sm font-medium">{lifecycleStepLabel(t, step)}</div>
              <div className="text-xs text-muted-foreground">
                {state === "completed" ? t("migrationWorkspace.lifecycle.completed") : state === "current" ? t("migrationWorkspace.lifecycle.current") : t("migrationWorkspace.lifecycle.upcoming")}
              </div>
            </div>
          </li>
        )
      })}
    </ol>
  )
}

export function MigrationAttention({ needsAttention, t }: { needsAttention: boolean; t: Translate }) {
  return needsAttention ? (
    <span className="inline-flex items-center gap-1 text-sm font-medium text-destructive">
      <AlertTriangle className="h-4 w-4" aria-hidden="true" />
      {t("migrationWorkspace.attention.required")}
    </span>
  ) : (
    <span className="text-sm text-muted-foreground">{t("migrationWorkspace.attention.none")}</span>
  )
}
