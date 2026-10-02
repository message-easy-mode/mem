import type { ReactNode } from "react"
import {
  ArrowLeft,
  ClipboardList,
  DatabaseBackup,
  HardDriveDownload,
  LoaderCircle,
  ShieldAlert,
  ShieldCheck,
  Trash2,
  Workflow,
} from "lucide-react"
import { Link, useNavigate, useParams } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import type { TranslationKey, TranslationValues } from "@/app/i18n/messages"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { PageBreadcrumbs } from "@/components/layout/page-breadcrumbs"
import {
  isHostAgentProblemError,
  type HostAgentProblem,
} from "@/features/operator/backups/api/transport/host-agent"
import {
  useBackupCatalogEntry,
  useBackupCatalogLifecycle,
  usePrepareCatalogRestoreSession,
} from "@/features/operator/backups/hooks/use-backup-catalog"
import { DeleteCatalogImportArchiveAction } from "@/features/operator/backups/shared/components/artifacts/delete-catalog-import-archive-action"
import { DeleteCatalogEntryAction } from "@/features/operator/backups/shared/components/artifacts/delete-catalog-payload-action"
import { CatalogPortableExportAction } from "@/features/operator/backups/shared/components/artifacts/catalog-portable-export-action"
import {
  formatBytes,
  formatDate,
} from "@/features/operator/backups/shared/components/backup-formatting"


import {
  BackupCatalogIntegrityBadge,
  BackupCatalogOriginBadge,
  BackupCatalogPayloadBadge,
} from "../components/backup-catalog-status-badge"

const catalogRestoreProblemTranslations: Readonly<
  Record<string, TranslationKey>
> = {
  "backup-catalog.entry.not-found": "backupCatalog.api.entryNotFound",
  "backup-catalog.entry.unavailable": "backupCatalog.api.entryUnavailable",
  "backup-catalog.restore-request.invalid":
    "backupCatalog.api.restoreRequestInvalid",
}

function getCatalogRestoreProblemMessage(problem?: HostAgentProblem): Readonly<{
  key: TranslationKey
  values: TranslationValues
}> | undefined {
  const message = problem?.message
  const catalogEntryId = message?.arguments?.catalogEntryId

  if (!message || typeof catalogEntryId !== "string") {
    return undefined
  }

  const key = catalogRestoreProblemTranslations[message.code]
  if (!key) {
    return undefined
  }

  if (message.code === "backup-catalog.entry.unavailable") {
    const payloadState = message.arguments?.payloadState
    if (typeof payloadState !== "string") {
      return undefined
    }

    const values: TranslationValues = { catalogEntryId, payloadState }

    return {
      key,
      values,
    }
  }

  const values: TranslationValues = { catalogEntryId }

  return {
    key,
    values,
  }
}

export function BackupCatalogEntryRouteBoundary() {
  const { t, language } = useI18n()
  const { catalogEntryId } = useParams()
  const navigate = useNavigate()
  const detailQuery = useBackupCatalogEntry(catalogEntryId)
  const lifecycleQuery = useBackupCatalogLifecycle(catalogEntryId, detailQuery.isSuccess)
  const prepareRestore = usePrepareCatalogRestoreSession()

  if (!catalogEntryId) {
    return <CatalogEntryUnavailable message={t("backupCatalog.detail.missingIdentifier")} />
  }

  if (detailQuery.isLoading) {
    return <CatalogEntryLoadingState />
  }

  if (detailQuery.error) {
    if (isCatalogEntryNotFoundError(detailQuery.error)) {
      return <CatalogEntryNotFound />
    }

    return <CatalogEntryUnavailable message={detailQuery.error.message} />
  }

  const entry = detailQuery.data
  if (!entry) {
    return <CatalogEntryUnavailable message={t("backupCatalog.detail.notReturned")} />
  }

  const lifecycle = lifecycleQuery.data
  const integrityReadinessValue = entry.integrityStatus === "valid"
    ? t("backupCatalog.integrity.valid")
    : entry.integrityStatus === "warning"
      ? t("backupCatalog.integrity.warning")
      : entry.integrityStatus
  const integrityReadinessDetail = entry.warningCount > 0
    ? t("backupCatalog.detail.readiness.integrityWarnings", { count: entry.warningCount })
    : (entry.advisoryCount ?? 0) > 0
      ? t("backupCatalog.detail.readiness.importAdvisories", { count: entry.advisoryCount ?? 0 })
      : t("backupCatalog.detail.readiness.checksPassed")

  return (
    <div className="space-y-6">
      <div className="space-y-3">
        <PageBreadcrumbs
          items={[
            { label: t("backupCatalog.breadcrumb.catalog"), to: "/backups" },
            { label: entry.displayName },
          ]}
        />
        <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
          <div>
            <h1 className="text-2xl font-semibold tracking-tight">{entry.displayName}</h1>
            <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
              {t("backupCatalog.detail.description")}
            </p>
          </div>
          <Button asChild variant="outline" size="sm">
            <Link to="/backups">
              <ArrowLeft className="mr-2 h-4 w-4" />
              {t("backupCatalog.detail.back")}
            </Link>
          </Button>
        </div>
      </div>

      <div className="flex flex-wrap gap-2">
        <BackupCatalogOriginBadge originKind={entry.originKind} />
        <BackupCatalogPayloadBadge payloadState={entry.payloadState} />
        <BackupCatalogIntegrityBadge integrityStatus={entry.integrityStatus} />
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card>
          <CardHeader><CardTitle>{t("backupCatalog.detail.identity.title")}</CardTitle></CardHeader>
          <CardContent>
            <DefinitionList items={[
              [t("backupCatalog.detail.identity.catalogEntry"), entry.catalogEntryId, true],
              [t("backupCatalog.detail.identity.sourceStack"), entry.sourceStackSlug],
              [t("backupCatalog.detail.identity.sourceBackup"), entry.sourceBackupId],
              [t("backupCatalog.detail.identity.created"), formatDate(entry.createdAtUtc, language)],
              [t("backupCatalog.detail.identity.captured"), formatDate(entry.capturedAtUtc, language)],
            ]} />
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle>{t("backupCatalog.detail.provenance.title")}</CardTitle></CardHeader>
          <CardContent>
            <DefinitionList items={[
              [t("backupCatalog.detail.provenance.origin"), originLabel(entry.originKind, t)],
              [t("backupCatalog.detail.provenance.imported"), formatDate(entry.importedAtUtc, language)],
              [t("backupCatalog.detail.provenance.materialised"), formatDate(entry.materialisedAtUtc, language)],
              [t("backupCatalog.detail.provenance.validationUpload"), entry.validationId, true],
              [t("backupCatalog.detail.provenance.memVersion"), entry.memVersion],
            ]} />
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle>{t("backupCatalog.detail.payload.title")}</CardTitle></CardHeader>
          <CardContent className="space-y-4">
            <DefinitionList items={[
              [t("backupCatalog.detail.payload.state"), entry.payloadState],
              [t("backupCatalog.detail.payload.size"), formatBytes(entry.payloadBytes, language)],
              [t("backupCatalog.detail.payload.integrity"), entry.integrityStatus],
              [t("backupCatalog.detail.payload.integrityWarnings"), String(entry.warningCount)],
              [t("backupCatalog.detail.payload.importAdvisories"), String(entry.advisoryCount ?? 0)],
              [t("backupCatalog.detail.payload.removed"), formatDate(entry.payloadRemovedAtUtc, language)],
              [t("backupCatalog.detail.payload.removedBy"), entry.payloadRemovedBy],
            ]} />
            {entry.integritySummary ? (
              <p className="rounded-md bg-muted p-3 text-sm text-muted-foreground">
                {entry.integritySummary}
              </p>
            ) : null}
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle>{t("backupCatalog.detail.services.title")}</CardTitle></CardHeader>
          <CardContent>
            <DefinitionList items={[
              [t("backupCatalog.detail.services.matrixServerName"), entry.matrixServerName],
              [t("backupCatalog.detail.services.matrixHost"), entry.matrixHost],
              [t("backupCatalog.detail.services.elementHost"), entry.elementHost],
              [t("backupCatalog.detail.services.manifestVersion"), entry.manifestVersion === null ? null : String(entry.manifestVersion)],
            ]} />
          </CardContent>
        </Card>
      </div>

      {entry.advisories && entry.advisories.length > 0 ? (
        <Card className="border-sky-500/30 bg-sky-500/[0.03]">
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <ShieldAlert className="h-5 w-5 text-sky-300" />
              {t("backupCatalog.detail.advisories.title")}
            </CardTitle>
            <p className="text-sm text-muted-foreground">
              {t("backupCatalog.detail.advisories.description")}
            </p>
          </CardHeader>
          <CardContent>
            <div className="space-y-3">
              {entry.advisories.map((advisory, index) => (
                <div
                  key={`${advisory.category}-${advisory.title}-${index}`}
                  className="rounded-md border border-sky-500/20 bg-background/30 p-3"
                >
                  <div className="flex flex-wrap items-center gap-2">
                    <span className="text-sm font-medium">{advisory.title}</span>
                    <span className="rounded-full bg-sky-500/10 px-2 py-0.5 text-xs text-sky-200">
                      {advisoryCategoryLabel(advisory.category, t)}
                    </span>
                  </div>
                  <p className="mt-1 text-sm text-muted-foreground">{advisory.message}</p>
                </div>
              ))}
            </div>
          </CardContent>
        </Card>
      ) : null}

      <Card className="border-emerald-500/45 bg-emerald-500/[0.035] shadow-[0_0_0_1px_rgba(16,185,129,0.03)]">
        <CardHeader className="gap-4 pb-3 md:flex-row md:items-start md:justify-between">
          <div className="flex gap-3">
            <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-emerald-500/10 text-emerald-400">
              <ShieldCheck className="h-6 w-6" />
            </div>
            <div>
              <h2 className="text-lg font-medium">{t("backupCatalog.detail.readiness.title")}</h2>
              <p className="mt-1 text-sm text-muted-foreground">
                {lifecycle?.hasActiveRestore
                  ? t("backupCatalog.detail.readiness.activeDescription")
                  : lifecycle?.payloadPresent
                    ? t("backupCatalog.detail.readiness.availableDescription")
                    : t("backupCatalog.detail.readiness.unavailableDescription")}
              </p>
            </div>
          </div>
          <div className="flex flex-wrap gap-2">
            <Button
              disabled={prepareRestore.isPending || !lifecycle?.payloadPresent}
              onClick={() => {
                prepareRestore.mutate(catalogEntryId, {
                  onSuccess: (response) => navigate(`/restores/${encodeURIComponent(response.restoreSessionId)}`),
                })
              }}
            >
              {prepareRestore.isPending ? (
                <LoaderCircle className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <ClipboardList className="mr-2 h-4 w-4" />
              )}
              {prepareRestore.isPending
                ? t("backupCatalog.detail.readiness.preparing")
                : lifecycle?.hasActiveRestore
                  ? t("backupCatalog.detail.readiness.resume")
                  : t("backupCatalog.detail.readiness.restore")}
            </Button>
            <Button asChild variant="outline">
              <Link to="/restores">{t("backupCatalog.detail.readiness.sessions")}</Link>
            </Button>
          </div>
        </CardHeader>
        <CardContent className="space-y-4">
          {lifecycleQuery.isLoading ? (
            <p className="flex items-center gap-2 text-sm text-muted-foreground">
              <LoaderCircle className="h-4 w-4 animate-spin" />
              {t("backupCatalog.detail.readiness.loading")}
            </p>
          ) : null}
          {lifecycleQuery.error ? (
            <Alert variant="destructive">
              <AlertTitle>{t("backupCatalog.detail.readiness.lifecycleError")}</AlertTitle>
              <AlertDescription>{lifecycleQuery.error.message}</AlertDescription>
            </Alert>
          ) : null}
          {lifecycle ? (
            <div className="grid divide-y divide-border/60 rounded-lg border bg-background/20 md:grid-cols-4 md:divide-x md:divide-y-0">
              <ReadinessMetric
                icon={<Workflow className="h-5 w-5" />}
                label={t("backupCatalog.detail.readiness.workspace")}
                value={lifecycle.hasActiveRestore ? t("backupCatalog.detail.readiness.active") : t("backupCatalog.detail.readiness.none")}
                detail={lifecycle.activeRestoreSessionId ?? t("backupCatalog.detail.readiness.noActiveWorkspace")}
                tone={lifecycle.hasActiveRestore ? "emerald" : "muted"}
              />
              <ReadinessMetric
                icon={<HardDriveDownload className="h-5 w-5" />}
                label={t("backupCatalog.detail.readiness.payload")}
                value={lifecycle.payloadPresent ? t("backupCatalog.detail.readiness.available") : t("backupCatalog.detail.readiness.unavailable")}
                detail={formatBytes(entry.payloadBytes, language) ?? t("backupCatalog.detail.readiness.sizeNotRecorded")}
                tone={lifecycle.payloadPresent ? "emerald" : "rose"}
              />
              <ReadinessMetric
                icon={<ShieldAlert className="h-5 w-5" />}
                label={t("backupCatalog.detail.readiness.integrity")}
                value={integrityReadinessValue}
                detail={integrityReadinessDetail}
                tone={entry.integrityStatus === "valid" ? "emerald" : "amber"}
              />
              <ReadinessMetric
                icon={<Trash2 className="h-5 w-5" />}
                label={t("backupCatalog.detail.readiness.permanentDeletion")}
                value={lifecycle.canDelete ? t("backupCatalog.detail.readiness.allowed") : t("backupCatalog.detail.readiness.blocked")}
                detail={lifecycle.deleteBlockReason ?? t("backupCatalog.detail.readiness.noActiveBlockers")}
                tone={lifecycle.canDelete ? "emerald" : "rose"}
              />
            </div>
          ) : null}
          {lifecycle?.activeRestoreSessionId ? (
            <div className="flex flex-wrap items-center justify-between gap-3 rounded-md border border-sky-500/25 bg-sky-500/[0.06] p-3 text-sm text-sky-200">
              <span>{t("backupCatalog.detail.readiness.activeRestoreNotice")}</span>
              <Button asChild size="sm" variant="secondary">
                <Link to={`/restores/${encodeURIComponent(lifecycle.activeRestoreSessionId)}`}>
                  {t("backupCatalog.detail.readiness.openActiveWorkspace")}
                </Link>
              </Button>
            </div>
          ) : null}
          {prepareRestore.error ? (
            <CatalogRestorePreparationError error={prepareRestore.error} />
          ) : null}
          {lifecycle && !lifecycle.payloadPresent ? (
            <p className="text-sm text-muted-foreground">
              {t("backupCatalog.detail.readiness.payloadUnavailable")}
            </p>
          ) : null}
        </CardContent>
      </Card>

      <div className="grid gap-4 lg:grid-cols-2">
        <Card className="border-sky-500/25 bg-sky-500/[0.02]">
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <HardDriveDownload className="h-5 w-5 text-sky-400" />
              {t("backupCatalog.detail.portableExport.title")}
            </CardTitle>
          </CardHeader>
          <CardContent>
            <CatalogPortableExportAction
              catalogEntryId={entry.catalogEntryId}
              payloadPresent={lifecycle?.payloadPresent ?? entry.payloadState === "available"}
            />
          </CardContent>
        </Card>
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <DatabaseBackup className="h-5 w-5 text-emerald-400" />
              {t("backupCatalog.detail.lifecycle.title")}
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            {lifecycle ? (
              <DefinitionList items={[
                [t("backupCatalog.detail.lifecycle.payloadPresent"), lifecycle.payloadPresent ? t("common.yes") : t("common.no")],
                [t("backupCatalog.detail.lifecycle.activeRestore"), lifecycle.hasActiveRestore ? t("common.yes") : t("common.no")],
                [t("backupCatalog.detail.lifecycle.permanentDeletion"), lifecycle.canDelete ? t("backupCatalog.detail.readiness.allowed") : t("backupCatalog.detail.readiness.blocked")],
                [t("backupCatalog.detail.lifecycle.deletionReason"), lifecycle.deleteBlockReason],
              ]} />
            ) : (
              <p className="text-sm text-muted-foreground">{t("backupCatalog.detail.lifecycle.loading")}</p>
            )}
            {lifecycle?.canDelete ? (
              <div className="flex flex-wrap items-center justify-between gap-3 rounded-md border border-destructive/30 bg-destructive/[0.03] p-4">
                <div>
                  <div className="font-medium">{t("backupCatalog.detail.lifecycle.permanentDeleteTitle")}</div>
                  <p className="mt-1 text-sm text-muted-foreground">
                    {t("backupCatalog.detail.lifecycle.permanentDeleteDescription")}
                  </p>
                </div>
                <DeleteCatalogEntryAction
                  catalogEntryId={entry.catalogEntryId}
                  displayName={entry.displayName}
                  payloadBytes={entry.payloadBytes}
                  payloadPresent={lifecycle.payloadPresent}
                  onDeleted={() => navigate("/backups", { replace: true })}
                />
              </div>
            ) : null}
          </CardContent>
        </Card>
        <Card className={lifecycle?.originalArchive && lifecycle.originalArchive.archiveState !== "removed" ? "border-destructive/35 bg-destructive/[0.02]" : undefined}>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Trash2 className="h-5 w-5 text-destructive" />
              {t("backupCatalog.detail.archive.title")}
            </CardTitle>
          </CardHeader>
          <CardContent>
            {lifecycle?.originalArchive && lifecycle.originalArchive.archiveState !== "removed" ? (
              <div className="space-y-3">
                <p className="text-sm text-muted-foreground">
                  {t("backupCatalog.detail.archive.retainedDescription")}
                </p>
                <div className="flex flex-wrap items-center justify-between gap-3 rounded-md border border-destructive/30 p-4">
                  <div>
                    <div className="font-medium">{t("backupCatalog.detail.archive.retainedZip")}</div>
                    <p className="mt-1 text-sm text-muted-foreground">
                      {t("backupCatalog.detail.archive.deleteDescription")}
                    </p>
                  </div>
                  <DeleteCatalogImportArchiveAction
                    validationId={lifecycle.originalArchive.validationId}
                    fileName={lifecycle.originalArchive.originalFileName ?? t("backupCatalog.detail.archive.uploadedZip")}
                    archiveBytes={lifecycle.originalArchive.archiveBytes}
                    onDeleted={() => { void lifecycleQuery.refetch() }}
                  />
                </div>
              </div>
            ) : (
              <p className="text-sm text-muted-foreground">{t("backupCatalog.detail.archive.none")}</p>
            )}
          </CardContent>
        </Card>
      </div>
    </div>
  )
}

function CatalogRestorePreparationError({ error }: { error: Error }) {
  const { t } = useI18n()
  const problem = isHostAgentProblemError(error) ? error.problem : undefined
  const localisedProblem = getCatalogRestoreProblemMessage(problem)

  return (
    <Alert variant="destructive">
      <AlertTitle>{t("backupCatalog.detail.readiness.prepareError")}</AlertTitle>
      <AlertDescription>
        <p>
          {localisedProblem
            ? t(localisedProblem.key, localisedProblem.values)
            : (problem?.detail ?? error.message)}
        </p>
        {localisedProblem && problem?.detail ? (
          <details className="mt-3 rounded-md border border-current/20 bg-background/40 px-3 py-2 text-foreground">
            <summary className="cursor-pointer font-medium">
              {t("common.technicalDetails")}
            </summary>
            <div className="mt-2 break-words whitespace-pre-wrap font-mono text-xs text-muted-foreground">
              {problem.detail}
            </div>
          </details>
        ) : null}
      </AlertDescription>
    </Alert>
  )
}

function CatalogEntryLoadingState() {
  const { t } = useI18n()

  return (
    <div className="space-y-6" aria-label={t("backupCatalog.detail.loadingAria")}>
      <div className="h-8 w-64 animate-pulse rounded bg-muted" />
      <div className="grid gap-4 lg:grid-cols-2">
        <div className="h-56 animate-pulse rounded bg-muted" />
        <div className="h-56 animate-pulse rounded bg-muted" />
      </div>
      <div className="h-48 animate-pulse rounded bg-muted" />
    </div>
  )
}

function isCatalogEntryNotFoundError(error: Error) {
  return isHostAgentProblemError(error) &&
    error.problem?.error === "backup_catalog_entry_not_found"
}

function CatalogEntryNotFound() {
  const { t } = useI18n()

  return (
    <div className="space-y-6">
      <PageBreadcrumbs items={[
        { label: t("backupCatalog.breadcrumb.catalog"), to: "/backups" },
        { label: t("backupCatalog.detail.breadcrumbEntry") },
      ]} />
      <Alert>
        <AlertTitle>{t("backupCatalog.detail.notFoundTitle")}</AlertTitle>
        <AlertDescription>{t("backupCatalog.detail.notFoundDescription")}</AlertDescription>
      </Alert>
      <Button asChild variant="outline">
        <Link to="/backups">
          <ArrowLeft className="mr-2 h-4 w-4" />
          {t("backupCatalog.detail.backToCatalog")}
        </Link>
      </Button>
    </div>
  )
}

function CatalogEntryUnavailable({ message }: { message: string }) {
  const { t } = useI18n()

  return (
    <div className="space-y-6">
      <PageBreadcrumbs items={[
        { label: t("backupCatalog.breadcrumb.catalog"), to: "/backups" },
        { label: t("backupCatalog.detail.breadcrumbEntry") },
      ]} />
      <Alert variant="destructive">
        <AlertTitle>{t("backupCatalog.detail.loadErrorTitle")}</AlertTitle>
        <AlertDescription>{message}</AlertDescription>
      </Alert>
      <Button asChild variant="outline">
        <Link to="/backups">
          <ArrowLeft className="mr-2 h-4 w-4" />
          {t("backupCatalog.detail.backToCatalog")}
        </Link>
      </Button>
    </div>
  )
}

function ReadinessMetric({
  icon,
  label,
  value,
  detail,
  tone,
}: {
  icon: ReactNode
  label: string
  value: string
  detail: string
  tone: "emerald" | "amber" | "rose" | "muted"
}) {
  const tones = {
    emerald: "text-emerald-400",
    amber: "text-amber-400",
    rose: "text-rose-400",
    muted: "text-muted-foreground",
  }

  return (
    <div className="space-y-2 p-4">
      <div className={`flex items-center gap-2 ${tones[tone]}`}>
        {icon}
        <span className="text-xs font-medium text-muted-foreground">{label}</span>
      </div>
      <div className={`font-semibold ${tones[tone]}`}>{value}</div>
      <p className="break-words text-xs text-muted-foreground">{detail}</p>
    </div>
  )
}

function DefinitionList({ items }: { items: [string, string | null | undefined, boolean?][] }) {
  const { t } = useI18n()

  return (
    <dl className="space-y-3 text-sm">
      {items.map(([label, value, mono]) => (
        <div className="grid grid-cols-[minmax(9rem,auto)_1fr] gap-3" key={label}>
          <dt className="text-muted-foreground">{label}</dt>
          <dd className={mono ? "break-all font-mono text-xs" : "break-words"}>
            {value || t("common.notRecorded")}
          </dd>
        </div>
      ))}
    </dl>
  )
}

function advisoryCategoryLabel(
  category: string,
  t: ReturnType<typeof useI18n>["t"],
) {
  switch (category) {
    case "security": return t("backupCatalog.detail.advisories.security")
    case "operational-safety": return t("backupCatalog.detail.advisories.operationalSafety")
    case "configuration": return t("backupCatalog.detail.advisories.configuration")
    case "provenance": return t("backupCatalog.detail.advisories.provenance")
    case "metadata": return t("backupCatalog.detail.advisories.metadata")
    case "format": return t("backupCatalog.detail.advisories.format")
    default: return t("backupCatalog.detail.advisories.review")
  }
}

function originLabel(
  originKind: string,
  t: ReturnType<typeof useI18n>["t"],
) {
  if (originKind === "imported-zip") return t("backupCatalog.origin.importedZip")
  if (originKind === "local-captured") return t("backupCatalog.origin.localBackup")
  return originKind
}
