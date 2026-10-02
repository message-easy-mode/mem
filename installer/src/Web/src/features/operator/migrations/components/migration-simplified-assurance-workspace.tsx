import { formatMigrationDateTime } from "./migration-time"
import { useMemo, useState } from "react"
import {
  AlertTriangle,
  CheckCircle2,
  Clock3,
  Loader2,
  RefreshCw,
  ShieldCheck,
} from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import {
  isMigrationProductionAuthorityStepUpRequired,
  type CreateOperatorAttestedSnapshotAuthorityRequest,
  type MigrationProductionAuthorityState,
} from "@/features/operator/migrations/api/migration-production-authority"
import type { MigrationSessionDetail } from "@/features/operator/migrations/api/migration-sessions"
import { useCreateOperatorAttestedSnapshotAuthority } from "@/features/operator/migrations/hooks/use-migration-production-authority"
import { useMigrationStagingRuns } from "@/features/operator/migrations/hooks/use-migration-staging"

const acknowledgementKeys = [
  "acknowledgeUsersWereInstructedNotToUseSource",
  "acknowledgePostCaptureWritesWillNotMigrate",
  "acknowledgeSelectedSnapshotBecomesAuthoritative",
  "acknowledgeSourceWillBeRetainedUntilVerification",
  "acknowledgeNoFormalSourceFreezeEvidence",
  "acknowledgeReducedRollbackAssurance",
] as const

type AcknowledgementKey = typeof acknowledgementKeys[number]
type Acknowledgements = Record<AcknowledgementKey, boolean>

const initialAcknowledgements: Acknowledgements = {
  acknowledgeUsersWereInstructedNotToUseSource: false,
  acknowledgePostCaptureWritesWillNotMigrate: false,
  acknowledgeSelectedSnapshotBecomesAuthoritative: false,
  acknowledgeSourceWillBeRetainedUntilVerification: false,
  acknowledgeNoFormalSourceFreezeEvidence: false,
  acknowledgeReducedRollbackAssurance: false,
}

type Props = {
  detail: MigrationSessionDetail
  authorityState: MigrationProductionAuthorityState | undefined
  authorityLoading: boolean
  authorityError: unknown
  authorityFetching: boolean
  onRefresh: () => void
}

export function MigrationSimplifiedAssuranceWorkspace({
  detail,
  authorityState,
  authorityLoading,
  authorityError,
  authorityFetching,
  onRefresh,
}: Props) {
  const { intlLocale, t } = useI18n()
  const migrationId = detail.session.migrationId
  const stagingQuery = useMigrationStagingRuns(migrationId, true)
  const createMutation = useCreateOperatorAttestedSnapshotAuthority(migrationId)
  const [acknowledgements, setAcknowledgements] = useState<Acknowledgements>(initialAcknowledgements)
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const [pendingCreation, setPendingCreation] = useState(false)
  const [actionError, setActionError] = useState<string | null>(null)

  const activeAuthority = authorityState?.authorizesProduction
    ? authorityState.authority
    : null
  const simplifiedAuthorityActive =
    activeAuthority?.authorityType === "operator-attested-snapshot" &&
    activeAuthority.status === "active"

  const previewRevision = [...(detail.packageRevisions ?? [])]
    .filter((revision) =>
      revision.active &&
      revision.purpose === "preview" &&
      revision.status === "package-validated")
    .sort((left, right) => right.revisionNumber - left.revisionNumber)[0]
  const retainedVerifiedStaging = (stagingQuery.data ?? []).find((run) =>
    run.status === "verified" &&
    run.destroyedAtUtc === null &&
    run.privateOnly &&
    !run.publicRoutesCreated &&
    run.databaseImportSucceeded &&
    run.synapseHealthPassed &&
    run.elementConfigPresent &&
    run.elementContainerStarted &&
    run.elementHealthPassed &&
    run.elementSynapseConnectivityPassed &&
    run.elementNetworkAttached)
  const source = detail.sources.find((candidate) => Boolean(candidate.capturedAtUtc)) ?? detail.sources[0]
  const capturedAtUtc = activeAuthority?.capturedAtUtc ?? source?.capturedAtUtc ?? null
  const usersCount = activeAuthority?.usersCount ?? retainedVerifiedStaging?.usersCount ?? null
  const roomsCount = activeAuthority?.roomsCount ?? retainedVerifiedStaging?.roomsCount ?? null
  const eventsCount = activeAuthority?.eventsCount ?? retainedVerifiedStaging?.eventsCount ?? null
  const matrixServerName = activeAuthority?.matrixServerName ?? retainedVerifiedStaging?.matrixServerName ?? null
  const allAcknowledged = useMemo(
    () => acknowledgementKeys.every((key) => acknowledgements[key]),
    [acknowledgements],
  )
  const snapshotReady = Boolean(previewRevision && retainedVerifiedStaging)

  async function createAuthority() {
    try {
      setActionError(null)
      const request: CreateOperatorAttestedSnapshotAuthorityRequest = { ...acknowledgements }
      await createMutation.mutateAsync(request)
      setPendingCreation(false)
    } catch (caught) {
      if (isMigrationProductionAuthorityStepUpRequired(caught)) {
        setPendingCreation(true)
        setStepUpOpen(true)
        return
      }
      setActionError(caught instanceof Error ? caught.message : String(caught))
    }
  }

  return (
    <Card className="border-emerald-500/35 bg-emerald-500/[0.025]">
      <CardHeader>
        <div className="flex flex-col gap-3 sm:flex-row sm:items-start sm:justify-between">
          <div>
            <CardTitle className="flex items-center gap-2">
              <ShieldCheck className="h-5 w-5 text-emerald-500" aria-hidden="true" />
              {simplifiedAuthorityActive
                ? t("migrationWorkspace.assurance.activeTitle")
                : t("migrationWorkspace.assurance.title")}
            </CardTitle>
            <p className="mt-1 text-sm text-muted-foreground">
              {simplifiedAuthorityActive
                ? t("migrationWorkspace.assurance.activeDescription")
                : t("migrationWorkspace.assurance.description")}
            </p>
          </div>
          <Button
            variant="outline"
            size="sm"
            onClick={onRefresh}
            disabled={authorityFetching || createMutation.isPending}
          >
            <RefreshCw
              className={authorityFetching ? "mr-2 h-4 w-4 animate-spin" : "mr-2 h-4 w-4"}
              aria-hidden="true"
            />
            {t("migrationWorkspace.refresh")}
          </Button>
        </div>
      </CardHeader>
      <CardContent className="space-y-5">
        {authorityLoading ? (
          <div className="flex items-center gap-2 text-sm text-muted-foreground">
            <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
            {t("migrationWorkspace.assurance.loading")}
          </div>
        ) : null}

        {authorityError ? (
          <Alert variant="destructive">
            <AlertTriangle className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.assurance.loadErrorTitle")}</AlertTitle>
            <AlertDescription>
              {authorityError instanceof Error
                ? authorityError.message
                : t("migrationWorkspace.assurance.loadErrorDescription")}
            </AlertDescription>
          </Alert>
        ) : null}

        {actionError ? (
          <Alert variant="destructive">
            <AlertTriangle className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.assurance.actionErrorTitle")}</AlertTitle>
            <AlertDescription>{actionError}</AlertDescription>
          </Alert>
        ) : null}

        {simplifiedAuthorityActive && activeAuthority ? (
          <>
            <Alert>
              <CheckCircle2 className="h-4 w-4" />
              <AlertTitle>{t("migrationWorkspace.assurance.authorizedTitle")}</AlertTitle>
              <AlertDescription>{authorityState?.detail}</AlertDescription>
            </Alert>
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant="secondary">{t("migrationWorkspace.assurance.simplifiedBadge")}</Badge>
              <span className="font-mono text-xs text-muted-foreground">
                {activeAuthority.productionAuthorityId}
              </span>
            </div>
            <SnapshotFacts
              capturedAtUtc={capturedAtUtc}
              locale={intlLocale}
              usersCount={usersCount}
              roomsCount={roomsCount}
              eventsCount={eventsCount}
              matrixServerName={matrixServerName}
            />
            <Alert>
              <AlertTriangle className="h-4 w-4" />
              <AlertTitle>{t("migrationWorkspace.assurance.reducedTitle")}</AlertTitle>
              <AlertDescription>{t("migrationWorkspace.assurance.reducedDescription")}</AlertDescription>
            </Alert>
            <div className="grid gap-3 sm:grid-cols-2">
              <Fact label={t("migrationWorkspace.assurance.packageRevision")} value={activeAuthority.packageRevisionId} mono />
              <Fact label={t("migrationWorkspace.assurance.stagingRun")} value={activeAuthority.stagingRunId} mono />
              <Fact label={t("migrationWorkspace.assurance.created")} value={formatDate(activeAuthority.createdAtUtc, intlLocale)} />
              <Fact label={t("migrationWorkspace.assurance.evidenceSha")} value={activeAuthority.evidenceSha256} mono />
            </div>
          </>
        ) : activeAuthority ? (
          <Alert>
            <CheckCircle2 className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.assurance.otherAuthorityTitle")}</AlertTitle>
            <AlertDescription>{authorityState?.detail}</AlertDescription>
          </Alert>
        ) : (
          <>
            <Alert>
              <Clock3 className="h-4 w-4" />
              <AlertTitle>{t("migrationWorkspace.assurance.snapshotTitle")}</AlertTitle>
              <AlertDescription>{t("migrationWorkspace.assurance.snapshotDescription")}</AlertDescription>
            </Alert>
            <SnapshotFacts
              capturedAtUtc={capturedAtUtc}
              locale={intlLocale}
              usersCount={usersCount}
              roomsCount={roomsCount}
              eventsCount={eventsCount}
              matrixServerName={matrixServerName}
            />
            {!snapshotReady && !stagingQuery.isLoading ? (
              <Alert variant="destructive">
                <AlertTriangle className="h-4 w-4" />
                <AlertTitle>{t("migrationWorkspace.assurance.notReadyTitle")}</AlertTitle>
                <AlertDescription>{t("migrationWorkspace.assurance.notReadyDescription")}</AlertDescription>
              </Alert>
            ) : null}
            <Alert variant="destructive">
              <AlertTriangle className="h-4 w-4" />
              <AlertTitle>{t("migrationWorkspace.assurance.riskTitle")}</AlertTitle>
              <AlertDescription>{t("migrationWorkspace.assurance.riskDescription")}</AlertDescription>
            </Alert>
            <Acknowledgements
              values={acknowledgements}
              disabled={createMutation.isPending}
              onChange={(key, checked) =>
                setAcknowledgements((current) => ({ ...current, [key]: checked }))}
            />
            <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
              <Button
                onClick={() => void createAuthority()}
                disabled={!snapshotReady || !allAcknowledged || createMutation.isPending}
              >
                {createMutation.isPending ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden="true" />
                ) : (
                  <ShieldCheck className="mr-2 h-4 w-4" aria-hidden="true" />
                )}
                {t("migrationWorkspace.assurance.authorize")}
              </Button>
              <p className="text-xs text-muted-foreground">
                {t("migrationWorkspace.assurance.stepUpDescription")}
              </p>
            </div>
          </>
        )}
      </CardContent>

      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={(open) => {
          setStepUpOpen(open)
          if (!open) setPendingCreation(false)
        }}
        onVerified={() => {
          setStepUpOpen(false)
          if (pendingCreation) {
            setPendingCreation(false)
            void createAuthority()
          }
        }}
      />
    </Card>
  )
}

function Acknowledgements({
  values,
  disabled,
  onChange,
}: {
  values: Acknowledgements
  disabled: boolean
  onChange: (key: AcknowledgementKey, checked: boolean) => void
}) {
  const { t } = useI18n()
  const labels: Record<AcknowledgementKey, string> = {
    acknowledgeUsersWereInstructedNotToUseSource: t("migrationWorkspace.assurance.ackUsersQuiet"),
    acknowledgePostCaptureWritesWillNotMigrate: t("migrationWorkspace.assurance.ackPostCaptureWrites"),
    acknowledgeSelectedSnapshotBecomesAuthoritative: t("migrationWorkspace.assurance.ackAuthoritative"),
    acknowledgeSourceWillBeRetainedUntilVerification: t("migrationWorkspace.assurance.ackRetainSource"),
    acknowledgeNoFormalSourceFreezeEvidence: t("migrationWorkspace.assurance.ackNoFreezeEvidence"),
    acknowledgeReducedRollbackAssurance: t("migrationWorkspace.assurance.ackReducedRollback"),
  }
  return (
    <div className="space-y-2 rounded-lg border p-4">
      <div className="font-medium">{t("migrationWorkspace.assurance.confirmationsTitle")}</div>
      {acknowledgementKeys.map((key) => (
        <label key={key} className="flex items-start gap-2 text-sm">
          <input
            type="checkbox"
            className="mt-1"
            checked={values[key]}
            disabled={disabled}
            onChange={(event) => onChange(key, event.target.checked)}
          />
          <span>{labels[key]}</span>
        </label>
      ))}
    </div>
  )
}

function SnapshotFacts({
  capturedAtUtc,
  locale,
  usersCount,
  roomsCount,
  eventsCount,
  matrixServerName,
}: {
  capturedAtUtc: string | null
  locale: string
  usersCount: number | null
  roomsCount: number | null
  eventsCount: number | null
  matrixServerName: string | null
}) {
  const { t } = useI18n()
  return (
    <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-5">
      <Fact label={t("migrationWorkspace.assurance.captured")} value={capturedAtUtc ? formatDate(capturedAtUtc, locale) : "—"} />
      <Fact label={t("migrationWorkspace.staging.users")} value={String(usersCount ?? "—")} />
      <Fact label={t("migrationWorkspace.staging.rooms")} value={String(roomsCount ?? "—")} />
      <Fact label={t("migrationWorkspace.staging.events")} value={String(eventsCount ?? "—")} />
      <Fact label={t("migrationWorkspace.assurance.matrixServer")} value={matrixServerName ?? "—"} />
    </div>
  )
}

function Fact({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="rounded-lg border p-3">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div className={mono ? "mt-1 break-all font-mono text-sm font-medium" : "mt-1 break-words text-sm font-medium"}>
        {value}
      </div>
    </div>
  )
}

function formatDate(value: string, locale: string) {
  return formatMigrationDateTime(value, locale)
}
