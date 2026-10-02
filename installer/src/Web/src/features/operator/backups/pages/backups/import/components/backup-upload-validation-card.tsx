import { useMemo, useState } from "react"
import { Link } from "react-router-dom"
import {
  CheckCircle2,
  ClipboardList,
  FileArchive,
  ShieldAlert,
  UploadCloud,
  XCircle,
} from "lucide-react"

import { useI18n, type I18nContextValue } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import type {
  RuntimeStackBackupUploadValidationCheck,
  RuntimeStackBackupUploadValidationResponse,
} from "@/features/operator/backups/api/types/backups.types"
import { useValidateBackupExportUpload } from "@/features/operator/backups/hooks/use-backups"
import { Info, PathInfo, WarningList } from "@/features/operator/backups/shared/components/backup-common"
import {
  formatBytes,
  formatDate,
} from "@/features/operator/backups/shared/components/backup-formatting"

/**
 * Upload validation is an ingestion/provenance step only. A successful ZIP is
 * materialised into the Backup Catalog; it never creates or resumes a restore
 * workspace directly.
 */
export function BackupUploadValidationCard() {
  const { t, language } = useI18n()
  const [file, setFile] = useState<File | null>(null)
  const validation = useValidateBackupExportUpload()

  const result = validation.data
  const canValidate = file !== null && !validation.isPending

  const validate = () => {
    if (file) {
      validation.mutate(file)
    }
  }

  const selectedFileSummary = useMemo(() => {
    if (!file) {
      return t("backupImport.validation.chooseFile")
    }

    return `${file.name} · ${formatBytes(file.size, language)}`
  }, [file, language, t])

  return (
    <Card>
      <CardHeader>
        <CardTitle className="flex items-center gap-2">
          <UploadCloud className="h-5 w-5" />
          {t("backupImport.validation.title")}
        </CardTitle>
        <p className="text-sm text-muted-foreground">
          {t("backupImport.validation.description")}
        </p>
      </CardHeader>

      <CardContent className="space-y-4">
        <Alert>
          <ShieldAlert className="h-4 w-4" />
          <AlertTitle>{t("backupImport.validation.firstTitle")}</AlertTitle>
          <AlertDescription>{t("backupImport.validation.firstDescription")}</AlertDescription>
        </Alert>

        <div className="rounded-lg border border-border bg-background/40 p-4">
          <div className="flex flex-col gap-3 lg:flex-row lg:items-center lg:justify-between">
            <div>
              <div className="flex items-center gap-2 text-sm font-medium">
                <FileArchive className="h-4 w-4" />
                {t("backupImport.validation.fileLabel")}
              </div>
              <div className="mt-1 text-sm text-muted-foreground">
                {selectedFileSummary}
              </div>
            </div>

            <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
              <input
                type="file"
                accept=".zip,application/zip"
                aria-label={t("backupImport.validation.fileLabel")}
                onChange={(event) => {
                  setFile(event.target.files?.[0] ?? null)
                  validation.reset()
                }}
                className="max-w-sm rounded-md border border-border bg-background px-3 py-2 text-sm"
              />

              <Button onClick={validate} disabled={!canValidate}>
                <UploadCloud className="mr-2 h-4 w-4" />
                {validation.isPending
                  ? t("backupImport.validation.validating")
                  : t("backupImport.validation.validate")}
              </Button>
            </div>
          </div>
        </div>

        {validation.error ? (
          <Alert variant="destructive">
            <AlertTitle>{t("backupImport.validation.error")}</AlertTitle>
            <AlertDescription>{validation.error.message}</AlertDescription>
          </Alert>
        ) : null}

        {result ? <ValidationResultPanel result={result} /> : null}
      </CardContent>
    </Card>
  )
}

function ValidationResultPanel({
  result,
}: {
  result: RuntimeStackBackupUploadValidationResponse
}) {
  const { t, language } = useI18n()
  const valid = result.status === "valid"
  const catalogEntryPath = result.catalogEntryId
    ? `/backups/catalog/${encodeURIComponent(result.catalogEntryId)}`
    : null
  const uploadedZipPath = `/backups/uploads/${encodeURIComponent(result.validationId)}`

  return (
    <div className="space-y-4 rounded-xl border border-border bg-background/40 p-4">
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <Badge variant={valid ? "secondary" : "destructive"}>
              {valid
                ? t("backupImport.validation.checkPassed")
                : t("backupImport.validation.checkFailed")}
            </Badge>
            <span className="font-mono text-sm">{result.validationId}</span>
          </div>

          <p className="mt-1 text-sm text-muted-foreground">
            {result.detail ?? t("backupImport.validation.completed")}
          </p>
        </div>

        <div className="flex items-center gap-2 text-sm">
          {valid ? (
            <>
              <CheckCircle2 className="h-4 w-4 text-emerald-400" />
              {t("backupImport.validation.outcomePassed")}
            </>
          ) : (
            <>
              <XCircle className="h-4 w-4 text-destructive" />
              {t("backupImport.validation.outcomeFailed")}
            </>
          )}
        </div>
      </div>

      <div className="grid gap-3 text-sm md:grid-cols-2 xl:grid-cols-4">
        <Info label={t("backupImport.validation.uploadedFile")} value={result.uploadedFileName} />
        <Info label={t("backupImport.validation.zipSize")} value={formatBytes(result.zipBytes, language)} />
        <Info label={t("backupImport.validation.zipEntries")} value={result.zipEntryCount.toString()} />
        <Info label={t("backupImport.validation.uncompressed")} value={formatBytes(result.totalUncompressedBytes, language)} />
        <Info label={t("backupImport.validation.manifest")} value={result.manifestPresent ? t("backupImport.validation.present") : t("backupImport.validation.missing")} />
        <Info label={t("backupImport.validation.checksums")} value={result.checksumsPresent ? t("backupImport.validation.present") : t("backupImport.validation.missing")} />
        <Info label={t("backupImport.validation.passedFiles")} value={result.integrity.passedFiles.toString()} />
        <Info label={t("backupImport.validation.failedFiles")} value={result.integrity.failedFiles.toString()} />
      </div>

      <PathInfo label={t("backupImport.validation.storedZipPath")} value={result.storedZipPath || t("backupImport.validation.notStored")} />

      {result.manifest ? (
        <div className="rounded-lg border border-border bg-card/50 p-4">
          <div className="mb-3 text-sm font-medium">{t("backupImport.validation.manifestSummary")}</div>
          <div className="grid gap-3 text-sm md:grid-cols-2 xl:grid-cols-4">
            <Info label={t("backupImport.validation.version")} value={result.manifest.manifestVersion.toString()} />
            <Info label={t("backupImport.validation.kind")} value={result.manifest.exportKind ?? t("common.unknown")} />
            <Info label={t("backupImport.validation.created")} value={formatDate(result.manifest.createdAtUtc, language)} />
            <Info label={t("backupImport.validation.memVersion")} value={result.manifest.memVersion ?? t("common.unknown")} />
            <Info label={t("backupImport.validation.stackSlug")} value={result.manifest.stack.slug ?? t("common.unknown")} />
            <Info label={t("backupImport.validation.matrixServer")} value={result.manifest.stack.matrixServerName ?? t("common.unknown")} />
            <Info label={t("backupImport.validation.matrixUrl")} value={result.manifest.stack.matrixPublicUrl ?? t("common.unknown")} />
            <Info label={t("backupImport.validation.elementUrl")} value={result.manifest.stack.elementPublicUrl ?? t("common.unknown")} />
            <Info label={t("backupImport.validation.database")} value={result.manifest.database.present ? t("backupImport.validation.present") : t("backupImport.validation.missing")} />
            <Info label={t("backupImport.validation.matrix")} value={result.manifest.matrix.present ? t("backupImport.validation.present") : t("backupImport.validation.missing")} />
            <Info label={t("backupImport.validation.mediaFiles")} value={result.manifest.matrix.mediaFiles.toString()} />
            <Info label={t("backupImport.validation.mediaBytes")} value={formatBytes(result.manifest.matrix.mediaBytes, language)} />
            <Info label={t("backupImport.validation.element")} value={result.manifest.element.present ? t("backupImport.validation.present") : t("backupImport.validation.missing")} />
            <Info label={t("backupImport.validation.requiresDns")} value={result.manifest.routes.requiresDns ? t("backupImport.validation.yes") : t("backupImport.validation.no")} />
            <Info label={t("backupImport.validation.coturn")} value={result.manifest.coturn.configured ? t("backupImport.validation.configured") : t("backupImport.validation.notConfigured")} />
            <Info
              label={t("backupImport.validation.turnRestoreIntent")}
              value={turnRestoreIntent(result.manifest.coturn.management, result.manifest.coturn.configured, t)}
            />
            <Info label={t("backupImport.validation.oldServerStopped")} value={result.manifest.restorePolicy.requiresOldServerStoppedForSameServerName ? t("backupImport.validation.required") : t("backupImport.validation.notRequired")} />
          </div>
        </div>
      ) : null}

      <div className="rounded-lg border border-border bg-card/50 p-4">
        <div className="mb-3 text-sm font-medium">{t("backupImport.validation.validationChecks")}</div>
        <div className="space-y-2">
          {result.checks.map((check) => (
            <ValidationCheckRow key={check.code} check={check} />
          ))}
        </div>
      </div>

      {valid ? (
        <div className="space-y-3 rounded-lg border border-emerald-500/20 bg-emerald-500/10 p-3">
          <div className="text-sm">
            <div className="font-medium text-emerald-100">{t("backupImport.validation.readyTitle")}</div>
            <p className="mt-1 text-muted-foreground">{t("backupImport.validation.readyDescription")}</p>
          </div>

          <div className="flex flex-wrap gap-2">
            {catalogEntryPath ? (
              <Button asChild>
                <Link to={catalogEntryPath}>
                  <ClipboardList className="mr-2 h-4 w-4" />
                  {t("backupImport.validation.openCatalog")}
                </Link>
              </Button>
            ) : (
              <Alert>
                <AlertTitle>{t("backupImport.validation.notReadyTitle")}</AlertTitle>
                <AlertDescription>{t("backupImport.validation.notReadyDescription")}</AlertDescription>
              </Alert>
            )}

            <Button variant="outline" asChild>
              <Link to={uploadedZipPath}>
                <FileArchive className="mr-2 h-4 w-4" />
                {t("backupImport.validation.manageUpload")}
              </Link>
            </Button>
          </div>
        </div>
      ) : null}

      {result.warnings.length > 0 ? <WarningList warnings={result.warnings} /> : null}

      {result.errors.length > 0 ? (
        <Alert variant="destructive">
          <AlertTitle>{t("backupImport.validation.errors")}</AlertTitle>
          <AlertDescription>
            <ul className="mt-2 list-disc space-y-1 pl-5">
              {result.errors.map((error) => (
                <li key={error}>{error}</li>
              ))}
            </ul>
          </AlertDescription>
        </Alert>
      ) : null}
    </div>
  )
}


function turnRestoreIntent(
  management: string | undefined,
  configured: boolean,
  t: I18nContextValue["t"],
) {
  if (!configured) {
    return t("backupImport.validation.turnIntentDisconnected")
  }

  switch (management) {
    case "mem-managed":
      return t("backupImport.validation.turnIntentPlatform")
    case "external-observed":
      return t("backupImport.validation.turnIntentExternal")
    default:
      return t("backupImport.validation.turnIntentLegacy")
  }
}

function ValidationCheckRow({
  check,
}: {
  check: RuntimeStackBackupUploadValidationCheck
}) {
  const { t } = useI18n()

  return (
    <div className="rounded-lg border border-border bg-background/40 p-3">
      <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <div className="flex flex-wrap items-center gap-2">
            <span className="font-mono text-xs text-muted-foreground">{check.code}</span>
            {!check.passed ? (
              <Badge variant="destructive">{check.severity}</Badge>
            ) : null}
          </div>
          <p className="mt-1 text-sm">{check.message}</p>
        </div>
        <Badge variant={check.passed ? "secondary" : "destructive"}>
          {check.passed
            ? t("backupImport.validation.checkPassed")
            : t("backupImport.validation.checkFailed")}
        </Badge>
      </div>
      {check.detail ? (
        <p className="mt-2 break-words text-xs text-muted-foreground">
          {check.detail}
        </p>
      ) : null}
    </div>
  )
}
