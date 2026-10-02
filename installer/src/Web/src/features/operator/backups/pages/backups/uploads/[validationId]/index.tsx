import {
  Archive,
  Boxes,
  FileArchive,
  RefreshCw,
  ShieldAlert,
  Trash2,
} from "lucide-react"
import { Link, useNavigate, useParams } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { PageBreadcrumbs } from "@/components/layout/page-breadcrumbs"
import { useValidatedImportArtifact } from "@/features/operator/backups/hooks/use-backups"
import { useMaterialiseImportedZip } from "@/features/operator/backups/hooks/use-backup-catalog"
import { DeleteCatalogImportArchiveAction } from "@/features/operator/backups/shared/components/artifacts/delete-catalog-import-archive-action"
import { Info, WarningList } from "@/features/operator/backups/shared/components/backup-common"
import {
  formatBytes,
  formatDate,
} from "@/features/operator/backups/shared/components/backup-formatting"

export function UploadedZipSourceDetailPage() {
  const { t, language } = useI18n()
  const { validationId } = useParams()
  const navigate = useNavigate()
  const sourceQuery = useValidatedImportArtifact(validationId)
  const materialiseMutation = useMaterialiseImportedZip()
  const breadcrumbs = [
    { label: t("backupCatalog.breadcrumb.backupRestore"), to: "/backups" },
    { label: t("backupCatalog.breadcrumb.catalog"), to: "/backups" },
    { label: t("backupImport.uploadDetail.breadcrumbUploadedZip") },
  ]

  if (!validationId) {
    return (
      <div className="space-y-4">
        <PageBreadcrumbs items={breadcrumbs} />
        <Alert variant="destructive">
          <AlertTitle>{t("backupImport.uploadDetail.missingReferenceTitle")}</AlertTitle>
          <AlertDescription>{t("backupImport.uploadDetail.missingReferenceDescription")}</AlertDescription>
        </Alert>
      </div>
    )
  }

  if (sourceQuery.isLoading) {
    return (
      <div className="space-y-4">
        <PageBreadcrumbs items={breadcrumbs} />
        <div className="rounded-lg border border-border bg-background/40 p-4 text-sm text-muted-foreground">
          {t("backupImport.uploadDetail.loading")}
        </div>
      </div>
    )
  }

  if (sourceQuery.error || !sourceQuery.data) {
    return (
      <div className="space-y-4">
        <PageBreadcrumbs items={breadcrumbs} />
        <Alert variant="destructive">
          <AlertTitle>{t("backupImport.uploadDetail.loadError")}</AlertTitle>
          <AlertDescription>
            {sourceQuery.error?.message ?? t("backupImport.uploadDetail.notFound")}
          </AlertDescription>
        </Alert>
        <Button variant="outline" asChild>
          <Link to="/backups">{t("backupCatalog.detail.backToCatalog")}</Link>
        </Button>
      </div>
    )
  }

  const source = sourceQuery.data
  const uploadedFileName = source.uploadedFileName?.trim() || t("backupImport.uploadDetail.badge")
  const sourceRemoved = source.archiveState === "removed"
  const displayWarnings = deduplicateWarnings(source.warnings)
  const canMaterialise = !sourceRemoved && source.validation.status === "valid"

  async function materialiseIntoCatalog() {
    if (!validationId || !canMaterialise) {
      return
    }

    try {
      const result = await materialiseMutation.mutateAsync(validationId)
      await sourceQuery.refetch()
      navigate(`/backups/catalog/${encodeURIComponent(result.catalogEntryId)}`)
    } catch {
      // The mutation error is rendered in the operator-facing alert below.
    }
  }

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div>
          <PageBreadcrumbs items={breadcrumbs} />

          <div className="flex flex-wrap items-center gap-2">
            <h1 className="text-2xl font-semibold tracking-tight">
              {uploadedFileName}
            </h1>
            <Badge variant={sourceRemoved ? "destructive" : "outline"}>
              <FileArchive className="mr-1.5 h-3.5 w-3.5" />
              {t("backupImport.uploadDetail.badge")}
            </Badge>
            <Badge variant={source.validation.status === "valid" ? "secondary" : "destructive"}>
              {source.validation.status}
            </Badge>
          </div>

          <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
            {t("backupImport.uploadDetail.description")}
          </p>
        </div>

        <div className="flex flex-wrap gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => void sourceQuery.refetch()}
            disabled={sourceQuery.isFetching}
          >
            <RefreshCw className="mr-2 h-4 w-4" />
            {t("common.refresh")}
          </Button>

          {canMaterialise ? (
            <Button
              size="sm"
              onClick={() => void materialiseIntoCatalog()}
              disabled={materialiseMutation.isPending}
            >
              <Boxes className="mr-2 h-4 w-4" />
              {materialiseMutation.isPending
                ? t("backupImport.uploadDetail.materialising")
                : t("backupImport.uploadDetail.materialise")}
            </Button>
          ) : null}

          {!sourceRemoved && source.retention.canDelete ? (
            <DeleteCatalogImportArchiveAction
              validationId={source.validationId}
              fileName={uploadedFileName}
              archiveBytes={source.archiveBytes}
              onDeleted={() => { void sourceQuery.refetch() }}
            />
          ) : null}
        </div>
      </div>

      {materialiseMutation.isError ? (
        <Alert variant="destructive">
          <AlertTitle>{t("backupImport.uploadDetail.materialiseError")}</AlertTitle>
          <AlertDescription>{materialiseMutation.error.message}</AlertDescription>
        </Alert>
      ) : null}

      {!sourceRemoved && source.validation.status !== "valid" ? (
        <Alert>
          <ShieldAlert className="h-4 w-4" />
          <AlertTitle>{t("backupImport.uploadDetail.requiresValidTitle")}</AlertTitle>
          <AlertDescription>{t("backupImport.uploadDetail.requiresValidDescription")}</AlertDescription>
        </Alert>
      ) : null}

      {sourceRemoved ? (
        <Alert variant="destructive">
          <Trash2 className="h-4 w-4" />
          <AlertTitle>{t("backupImport.uploadDetail.removedTitle")}</AlertTitle>
          <AlertDescription>{t("backupImport.uploadDetail.removedDescription")}</AlertDescription>
        </Alert>
      ) : null}

      {!sourceRemoved && !source.retention.canDelete ? (
        <Alert>
          <ShieldAlert className="h-4 w-4" />
          <AlertTitle>{t("backupImport.uploadDetail.deletionBlockedTitle")}</AlertTitle>
          <AlertDescription>
            {source.retention.deleteBlockReason ?? t("backupImport.uploadDetail.notEligible")}
          </AlertDescription>
        </Alert>
      ) : null}

      <div className="grid gap-4 xl:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Archive className="h-5 w-5" />
              {t("backupImport.uploadDetail.sourceValidationTitle")}
            </CardTitle>
          </CardHeader>
          <CardContent className="grid gap-4 text-sm sm:grid-cols-2">
            <Info label={t("backupImport.validation.uploadedFile")} value={uploadedFileName} />
            <Info label={t("backupImport.uploadDetail.uploaded")} value={formatDate(source.recordedAtUtc, language)} />
            <Info
              label={t("backupImport.uploadDetail.retainedSize")}
              value={source.archiveBytes === null ? t("common.notRecorded") : formatBytes(source.archiveBytes, language)}
            />
            <Info label={t("backupImport.uploadDetail.archiveState")} value={formatState(source.archiveState)} />
            <Info label={t("backupImport.uploadDetail.validation")} value={source.validation.summary} />
            <Info
              label={t("backupImport.uploadDetail.checksumResults")}
              value={t("backupImport.uploadDetail.checksumCounts", {
                passed: source.validation.passedChecks,
                failed: source.validation.failedChecks,
              })}
            />
            <Info label={t("backupImport.validation.zipEntries")} value={source.validation.zipEntryCount.toString()} />
            <Info label={t("backupImport.uploadDetail.uncompressedSize")} value={formatBytes(source.validation.totalUncompressedBytes, language)} />
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <FileArchive className="h-5 w-5" />
              {t("backupImport.uploadDetail.manifestIdentityTitle")}
            </CardTitle>
          </CardHeader>
          <CardContent className="grid gap-4 text-sm sm:grid-cols-2">
            <Info label={t("backupImport.uploadDetail.sourceStack")} value={source.manifest?.sourceStackSlug ?? t("common.notRecorded")} />
            <Info label={t("backupImport.uploadDetail.stackDisplayName")} value={source.manifest?.sourceStackDisplayName ?? t("common.notRecorded")} />
            <Info label={t("backupImport.uploadDetail.matrixServer")} value={source.manifest?.matrixServerName ?? t("common.notRecorded")} />
            <Info label={t("backupImport.uploadDetail.memVersion")} value={source.manifest?.memVersion ?? t("common.notRecorded")} />
            <Info
              label={t("backupImport.uploadDetail.manifestVersion")}
              value={source.manifest?.manifestVersion === null || source.manifest?.manifestVersion === undefined
                ? t("common.notRecorded")
                : source.manifest.manifestVersion.toString()}
            />
            <Info label={t("backupImport.uploadDetail.includedFiles")} value={source.manifest?.includedFileCount.toString() ?? t("common.notRecorded")} />
          </CardContent>
        </Card>
      </div>

      <div className="grid gap-4 xl:grid-cols-2">
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <ShieldAlert className="h-5 w-5" />
              {t("backupImport.uploadDetail.retentionTitle")}
            </CardTitle>
          </CardHeader>
          <CardContent className="grid gap-4 text-sm sm:grid-cols-2">
            <Info label={t("backupImport.uploadDetail.archiveState")} value={formatState(source.archiveState)} />
            <Info label={t("backupImport.uploadDetail.deleteEligibility")} value={source.retention.canDelete ? t("backupImport.uploadDetail.eligible") : t("backupImport.uploadDetail.notEligibleValue")} />
            <Info label={t("backupImport.uploadDetail.removed")} value={source.retention.removedAtUtc ? formatDate(source.retention.removedAtUtc, language) : t("backupImport.uploadDetail.notRemoved")} />
            <Info label={t("backupImport.uploadDetail.removalActor")} value={source.retention.removedBy ?? t("backupImport.uploadDetail.notApplicable")} />
          </CardContent>
        </Card>
      </div>

      {source.validation.errors.length > 0 ? (
        <Alert variant="destructive">
          <AlertTitle>{t("backupImport.uploadDetail.validationErrors")}</AlertTitle>
          <AlertDescription>
            <ul className="mt-2 list-disc space-y-1 pl-5">
              {source.validation.errors.map((error) => (
                <li key={error}>{error}</li>
              ))}
            </ul>
          </AlertDescription>
        </Alert>
      ) : null}

      {displayWarnings.length > 0 ? <WarningList warnings={displayWarnings} /> : null}

      <Card>
        <CardHeader>
          <CardTitle>{t("backupImport.uploadDetail.sourceReferenceTitle")}</CardTitle>
          <p className="text-sm text-muted-foreground">
            {t("backupImport.uploadDetail.sourceReferenceDescription")}
          </p>
        </CardHeader>
        <CardContent>
          <code className="rounded bg-muted px-2 py-1 text-sm">{source.validationId}</code>
          {source.detail ? (
            <p className="mt-3 text-sm text-muted-foreground">{source.detail}</p>
          ) : null}
        </CardContent>
      </Card>
    </div>
  )
}

function formatState(value: string) {
  return value.replaceAll("-", " ")
}

function deduplicateWarnings(warnings: string[]) {
  const seen = new Set<string>()

  return warnings.filter((warning) => {
    const key = getWarningCategory(warning)

    if (seen.has(key)) {
      return false
    }

    seen.add(key)
    return true
  })
}

function getWarningCategory(warning: string) {
  const normalized = warning
    .toLocaleLowerCase()
    .replace(/^manifest warning:\s*/, "")
    .trim()

  if (/(signing identity|signing key)/.test(normalized)) {
    return "matrix-signing-material"
  }

  if (/two public homeservers/.test(normalized)) {
    return "duplicate-matrix-server-name"
  }

  if (/(route|turn) snapshots/.test(normalized)) {
    return "route-turn-snapshots"
  }

  return normalized.replace(/[^a-z0-9]+/g, " ").trim()
}
