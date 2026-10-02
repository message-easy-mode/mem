import { formatMigrationDateTime } from "../components/migration-time"
import {
  AlertTriangle,
  ArrowLeft,
  ExternalLink,
  History,
  PackageOpen,
  RefreshCw,
  ShieldCheck,
} from "lucide-react"
import { Link, Navigate, useParams } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { PageBreadcrumbs } from "@/components/layout/page-breadcrumbs"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import type { MigrationWorkspaceStage } from "@/features/operator/migrations/api/migration-workspace"
import type {
  MigrationSessionDetail,
  MigrationSessionFinding,
  MigrationSessionLinkedObject,
} from "@/features/operator/migrations/api/migration-sessions"
import { MigrationAcceptanceWorkspace } from "@/features/operator/migrations/components/migration-acceptance-workspace"
import { MigrationCutoverCompatibilityPanel } from "@/features/operator/migrations/components/migration-cutover-compatibility-panel"
import { MigrationFinalPackageHandoff } from "@/features/operator/migrations/components/migration-final-package-handoff"
import { MigrationFinishWorkspace } from "@/features/operator/migrations/components/migration-finish-workspace"
import { MigrationGuidedWorkspace } from "@/features/operator/migrations/components/migration-guided-workspace"
import { MigrationLifecycle, MigrationStatusBadge, migrationPhaseLabel } from "@/features/operator/migrations/components/migration-session-display"
import { MigrationSessionLifecyclePanel } from "@/features/operator/migrations/components/migration-session-lifecycle-panel"
import { MigrationGoLiveWorkspace } from "@/features/operator/migrations/components/migration-go-live-workspace"
import { MigrationPackageWorkspace } from "@/features/operator/migrations/components/migration-package-workspace"
import { MigrationPrepareAndTestWorkspace } from "@/features/operator/migrations/components/migration-prepare-and-test-workspace"
import { MigrationPrivateServerCreationWorkspace } from "@/features/operator/migrations/components/migration-private-server-creation-workspace"
import { MigrationProductionAdoptionWorkspace } from "@/features/operator/migrations/components/migration-production-adoption-workspace"
import { MigrationSourceAssessment } from "@/features/operator/migrations/components/migration-source-assessment"
import { MigrationTwoServerQualificationWorkspace } from "@/features/operator/migrations/components/migration-two-server-qualification-workspace"
import { useMigrationSessionLifecycle } from "@/features/operator/migrations/hooks/use-migration-session-lifecycle"
import { useMigrationWorkspace } from "@/features/operator/migrations/hooks/use-migration-workspace"

import { MigrationCancelAction } from "../components/migration-cancel-action"

import { MigrationGuidedStateProvider } from "../components/migration-guided-state-context"

export function MigrationSessionPage() {
  const { migrationId } = useParams()
  const { t } = useI18n()
  const workspaceQuery = useMigrationWorkspace(migrationId)

  if (!migrationId) return <Navigate to="/migrations" replace />
  if (workspaceQuery.isLoading) return <MigrationSessionLoading />
  // Never reconstruct progression from independently cached specialist reads.
  // A missing/older contract fails closed until the matching server is available.
  if (!workspaceQuery.data?.guided || workspaceQuery.data.schemaVersion !== 2) {
    return <MigrationSessionError migrationId={migrationId} onRefresh={() => void workspaceQuery.refetch()} />
  }

  const workspace = workspaceQuery.data
  const detail = workspace.guided.detail
  const session = detail.session
  const finalPackageValidated = workspace.guided.productionAuthorityType === "final-frozen"
  const productionAuthorityType = workspace.guided.productionAuthorityType
  const productionAuthorized = workspace.guided.productionAuthorized
  const refreshWorkspace = async () => { await workspaceQuery.refetch() }

  const renderStage = (stage: MigrationWorkspaceStage) => {
    if (stage.code === "create-and-upload-package") {
      return <MigrationPackageWorkspace detail={detail} onChanged={refreshWorkspace} />
    }

    if (stage.code === "review-old-server") {
      return <MigrationSourceAssessment detail={detail} onChanged={refreshWorkspace} />
    }

    if (stage.code === "prepare-and-test") {
      return (
        <MigrationPrepareAndTestWorkspace
          detail={detail}
          onChanged={refreshWorkspace}
        />
      )
    }

    if (stage.code === "create-new-server") {
      return (
        <MigrationPrivateServerCreationWorkspace
          migrationId={session.migrationId}
          matrixServerName={workspace.source.matrixServerName}
          assuranceMode={productionAuthorityType === "final-frozen" ? "final-frozen" : "simplified"}
          onChanged={refreshWorkspace}
        />
      )
    }

    if (stage.code === "make-new-server-live") {
      return (
        <MigrationGoLiveWorkspace
          migrationId={session.migrationId}
          assuranceMode={productionAuthorityType === "final-frozen" ? "final-frozen" : "simplified"}
          onChanged={refreshWorkspace}
        />
      )
    }

    if (stage.code === "finish-migration") {
      return (
        <MigrationFinishWorkspace
          migrationId={session.migrationId}
          onChanged={refreshWorkspace}
        />
      )
    }

    return null
  }

  return (
    <MigrationGuidedStateProvider key={migrationId} workspace={workspace} refresh={refreshWorkspace} observationAvailable={!workspaceQuery.isError}>
    <div className="min-w-0 space-y-4">
      <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div className="min-w-0 flex-1">
          <PageBreadcrumbs
            items={[
              { label: t("migrationWorkspace.breadcrumb"), to: "/migrations" },
              { label: workspace.migration.displayName },
            ]}
          />
          <div className="flex flex-wrap items-center gap-3">
            <h1 className="break-words text-2xl font-semibold tracking-tight">
              {workspace.migration.displayName}
            </h1>
            <MigrationStatusBadge status={workspace.migration.status} t={t} />
          </div>
          <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">
            {t("migrationWorkspace.guide.description")}
          </p>
        </div>
        <div className="flex shrink-0 flex-wrap gap-2 md:max-w-72">
          <MigrationCancelAction />
          <Button variant="outline" asChild>
            <Link to="/migrations">
              <ArrowLeft className="mr-2 h-4 w-4" aria-hidden="true" />
              {t("migrationWorkspace.detail.back")}
            </Link>
          </Button>
          <Button
            variant="outline"
            onClick={() => void refreshWorkspace()}
            disabled={workspaceQuery.isFetching}
          >
            <RefreshCw
              className={
                workspaceQuery.isFetching
                  ? "mr-2 h-4 w-4 animate-spin"
                  : "mr-2 h-4 w-4"
              }
              aria-hidden="true"
            />
            {t("migrationWorkspace.refresh")}
          </Button>
        </div>
      </div>

      {session.historicalCompatibility.usesLegacyNeutralImportContract ||
      session.historicalCompatibility.usesCatalogRestorePath ? (
        <Alert>
          <History className="h-4 w-4" />
          <AlertTitle>{t("migrationWorkspace.detail.historicalTitle")}</AlertTitle>
          <AlertDescription>
            {t("migrationWorkspace.detail.historicalDescription", {
              catalogCount: session.historicalCompatibility.catalogEntryCount,
              restoreCount: session.historicalCompatibility.restoreSessionCount,
            })}
          </AlertDescription>
        </Alert>
      ) : null}

      <MigrationGuidedWorkspace
        workspace={workspace}
        renderStage={renderStage}
        evidenceDetails={<MigrationEvidenceDetails detail={detail} />}
        advancedContent={
          <MigrationAdvancedDetails
            detail={detail}
            finalPackageValidated={finalPackageValidated}
            accepted={Boolean(
              workspace.advancedTools.find(
                (tool) => tool.code === "two-server-qualification",
              )?.availability === "available",
            )}
            productionAuthorized={productionAuthorized}
            productionAuthorityType={productionAuthorityType}
            onChanged={refreshWorkspace}
          />
        }
      />
    </div>
    </MigrationGuidedStateProvider>
  )
}

function MigrationEvidenceDetails({ detail }: { detail: MigrationSessionDetail }) {
  const { t } = useI18n()

  return (
    <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_22rem]">
      <Card>
        <CardHeader>
          <CardTitle>{t("migrationWorkspace.detail.findings")}</CardTitle>
        </CardHeader>
        <CardContent>
          {detail.findings.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              {t("migrationWorkspace.detail.noFindings")}
            </p>
          ) : (
            <div className="space-y-3">
              {detail.findings.map((finding) => (
                <Finding key={`${finding.code}-${finding.createdAtUtc}`} finding={finding} />
              ))}
            </div>
          )}
        </CardContent>
      </Card>

      <Card>
        <CardHeader>
          <CardTitle className="flex items-center gap-2">
            <PackageOpen className="h-5 w-5" aria-hidden="true" />
            {t("migrationWorkspace.detail.linkedObjects")}
          </CardTitle>
        </CardHeader>
        <CardContent>
          {detail.linkedObjects.length === 0 ? (
            <p className="text-sm text-muted-foreground">
              {t("migrationWorkspace.detail.noLinkedObjects")}
            </p>
          ) : (
            <div className="space-y-3">
              {detail.linkedObjects.map((linkedObject) => (
                <LinkedObject
                  key={`${linkedObject.kind}-${linkedObject.id}`}
                  linkedObject={linkedObject}
                />
              ))}
            </div>
          )}
        </CardContent>
      </Card>
    </div>
  )
}

function MigrationLifecycleTools({ migrationId }: { migrationId: string }) {
  const query = useMigrationSessionLifecycle(migrationId)
  const { t } = useI18n()
  if (query.data && !query.isError) return <MigrationSessionLifecyclePanel lifecycle={query.data} />
  return <Alert><AlertTitle>{t("migrationWorkspace.lifecycle.panelTitle")}</AlertTitle>
    <AlertDescription>{t(query.isError ? "migrationWorkspace.guided.evidenceUnavailable" : "migrationWorkspace.detail.loading")}</AlertDescription>
    <Button variant="outline" onClick={() => void query.refetch()}>{t("migrationWorkspace.refresh")}</Button>
  </Alert>
}

function MigrationAdvancedDetails({
  detail,
  finalPackageValidated,
  accepted,
  productionAuthorized,
  productionAuthorityType,
  onChanged,
}: {
  detail: MigrationSessionDetail
  finalPackageValidated: boolean
  accepted: boolean
  productionAuthorized: boolean
  productionAuthorityType: string | null
  onChanged: () => Promise<unknown>
}) {
  const { intlLocale, t } = useI18n()
  const session = detail.session
  const catalogEntry = detail.linkedObjects.find(
    (linkedObject) => linkedObject.kind === "backup-catalog-entry",
  )

  return (
    <div className="space-y-5">
      <MigrationLifecycleTools migrationId={session.migrationId} />
      <Card>
        <CardHeader>
          <CardTitle>{t("migrationWorkspace.detail.lifecycle")}</CardTitle>
          <p className="text-sm text-muted-foreground">
            {t("migrationWorkspace.advanced.lifecycleDescription")}
          </p>
        </CardHeader>
        <CardContent className="space-y-5">
          <MigrationLifecycle phase={session.phase} t={t} />
          <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            <Fact label={t("migrationWorkspace.detail.id")} value={session.migrationId} mono />
            <Fact
              label={t("migrationWorkspace.detail.phase")}
              value={migrationPhaseLabel(t, session.phase)}
            />
            <Fact
              label={t("migrationWorkspace.detail.created")}
              value={formatDate(session.createdAtUtc, intlLocale)}
            />
            <Fact
              label={t("migrationWorkspace.detail.updated")}
              value={formatDate(session.updatedAtUtc, intlLocale)}
            />
          </div>
        </CardContent>
      </Card>

      {!finalPackageValidated && !accepted ? (
        <div className="space-y-4">
          <Alert>
            <ShieldCheck className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.assurance.advancedSummary")}</AlertTitle>
            <AlertDescription>
              {t("migrationWorkspace.assurance.advancedDescription")}
            </AlertDescription>
          </Alert>
          <MigrationFinalPackageHandoff detail={detail} onChanged={onChanged} />
        </div>
      ) : null}

      {detail.session.historicalCompatibility.usesCatalogRestorePath && catalogEntry ? (
        <MigrationCutoverCompatibilityPanel
          catalogEntryId={catalogEntry.id}
          payloadAvailable={
            catalogEntry.status !== "removed" && catalogEntry.status !== "not-found"
          }
        />
      ) : null}

      {productionAuthorized ? (
        <details className="rounded-xl border bg-muted/10">
          <summary className="cursor-pointer px-4 py-3 text-sm font-medium">
            {t("migrationWorkspace.goLive.advancedTitle")}
          </summary>
          <div className="border-t p-4">
            <MigrationProductionAdoptionWorkspace
              detail={detail}
              productionAuthorityType={productionAuthorityType}
            />
          </div>
        </details>
      ) : null}

      {productionAuthorized ? (
        <details className="rounded-xl border bg-muted/10">
          <summary className="cursor-pointer px-4 py-3 text-sm font-medium">
            {t("migrationWorkspace.finish.advancedTitle")}
          </summary>
          <div className="border-t p-4">
            <MigrationAcceptanceWorkspace migrationId={session.migrationId} />
          </div>
        </details>
      ) : null}

      {accepted && !detail.session.historicalCompatibility.usesCatalogRestorePath ? (
        <MigrationTwoServerQualificationWorkspace migrationId={session.migrationId} />
      ) : null}
    </div>
  )
}

function MigrationSessionError({
  migrationId,
  onRefresh,
}: {
  migrationId: string
  onRefresh: () => void
}) {
  const { t } = useI18n()
  return (
    <div className="space-y-6">
      <PageBreadcrumbs
        items={[
          { label: t("migrationWorkspace.breadcrumb"), to: "/migrations" },
          { label: migrationId },
        ]}
      />
      <Alert variant="destructive">
        <AlertTriangle className="h-4 w-4" />
        <AlertTitle>{t("migrationWorkspace.detail.errorTitle")}</AlertTitle>
        <AlertDescription>{t("migrationWorkspace.detail.errorDescription")}</AlertDescription>
      </Alert>
      <div className="flex gap-2">
        <Button variant="outline" asChild>
          <Link to="/migrations">
            <ArrowLeft className="mr-2 h-4 w-4" aria-hidden="true" />
            {t("migrationWorkspace.detail.back")}
          </Link>
        </Button>
        <Button onClick={onRefresh}>
          <RefreshCw className="mr-2 h-4 w-4" aria-hidden="true" />
          {t("migrationWorkspace.refresh")}
        </Button>
      </div>
    </div>
  )
}

function MigrationSessionLoading() {
  const { t } = useI18n()
  return (
    <div className="space-y-4" aria-label={t("migrationWorkspace.detail.loading")}>
      <div className="h-8 w-72 animate-pulse rounded bg-muted" />
      <div className="h-24 animate-pulse rounded bg-muted" />
      <div className="h-56 animate-pulse rounded bg-muted" />
    </div>
  )
}

function Fact({
  label,
  value,
  mono = false,
}: {
  label: string
  value: string
  mono?: boolean
}) {
  return (
    <div className="rounded-lg border p-3">
      <div className="text-xs text-muted-foreground">{label}</div>
      <div
        className={
          mono
            ? "mt-1 break-all font-mono text-sm font-medium"
            : "mt-1 break-words text-sm font-medium"
        }
      >
        {value}
      </div>
    </div>
  )
}

function Finding({ finding }: { finding: MigrationSessionFinding }) {
  const { t } = useI18n()
  const label =
    finding.severity === "blocker"
      ? t("migrationWorkspace.finding.blocker")
      : finding.severity === "warning"
        ? t("migrationWorkspace.finding.warning")
        : t("migrationWorkspace.finding.advisory")

  return (
    <div className="rounded-lg border p-3 text-sm">
      <div className="flex flex-wrap items-center gap-2">
        <Badge variant={finding.severity === "blocker" ? "destructive" : "outline"}>
          {label}
        </Badge>
        <span className="font-mono text-xs text-muted-foreground">{finding.code}</span>
      </div>
      <p className="mt-2">{finding.message}</p>
    </div>
  )
}

function LinkedObject({ linkedObject }: { linkedObject: MigrationSessionLinkedObject }) {
  const { t } = useI18n()
  const target =
    linkedObject.kind === "backup-catalog-entry"
      ? `/backups/catalog/${encodeURIComponent(linkedObject.id)}`
      : linkedObject.kind === "restore-session"
        ? `/restores/${encodeURIComponent(linkedObject.id)}`
        : null

  return (
    <div className="rounded-lg border p-3 text-sm">
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="font-medium">{linkedObject.displayName}</div>
          <div className="mt-1 break-all font-mono text-xs text-muted-foreground">
            {linkedObject.id}
          </div>
          <div className="mt-2 text-xs text-muted-foreground">
            {linkedObject.relationship} · {linkedObject.status}
          </div>
        </div>
        {linkedObject.historical ? (
          <Badge variant="outline">{t("migrationWorkspace.historical")}</Badge>
        ) : null}
      </div>
      {target ? (
        <Button className="mt-3" size="sm" variant="outline" asChild>
          <Link to={target}>
            {t("migrationWorkspace.detail.openLinkedObject")}
            <ExternalLink className="ml-2 h-3.5 w-3.5" aria-hidden="true" />
          </Link>
        </Button>
      ) : null}
    </div>
  )
}

function formatDate(value: string, locale: string) {
  return formatMigrationDateTime(value, locale)
}
