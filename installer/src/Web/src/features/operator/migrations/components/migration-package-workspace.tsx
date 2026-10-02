import { formatMigrationDateTime } from "./migration-time"
import { useMigrationGuidedState } from "./migration-guided-state-context"
import { isMigrationOutcomeUncertain } from "../api/migration-guided-state"
import { useMemo, useState } from "react"
import {
  AlertTriangle,
  CheckCircle2,
  Clipboard,
  Download,
  Eye,
  FileArchive,
  LockKeyhole,
  UploadCloud,
} from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import type { MigrationSessionDetail } from "@/features/operator/migrations/api/migration-sessions"
import {
  downloadMigrationSourceRequest,
  isSecureMigrationStepUpRequired,
  uploadSecureMigrationPackage,
} from "@/features/operator/migrations/api/migration-intakes"
import { buildSourcePackageCommand } from "@/features/operator/migrations/source-package-command"

type PendingAction = "upload" | null

type MigrationPackageWorkspaceProps = {
  detail: MigrationSessionDetail
  onChanged: () => Promise<unknown>
}

export function MigrationPackageWorkspace({ detail, onChanged }: MigrationPackageWorkspaceProps) {
  const { intlLocale, t } = useI18n()
  const guide = useMigrationGuidedState()
  const migrationPackage = detail.package
  const [packageFile, setPackageFile] = useState<File | null>(null)
  const [showSourceCommand, setShowSourceCommand] = useState(false)
  const [copied, setCopied] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [isWorking, setIsWorking] = useState(false)
  const [requestDownloading, setRequestDownloading] = useState(false)
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const [pendingAction, setPendingAction] = useState<PendingAction>(null)

  const sourceCommand = useMemo(() => {
    if (!migrationPackage?.ageRecipient || !migrationPackage.recipientFingerprint) {
      return ""
    }

    return buildSourcePackageCommand({
      intakeId: detail.session.migrationId,
      ageRecipient: migrationPackage.ageRecipient,
      recipientFingerprint: migrationPackage.recipientFingerprint,
    })
  }, [
    detail.session.migrationId,
    migrationPackage?.ageRecipient,
    migrationPackage?.recipientFingerprint,
  ])

  if (!migrationPackage) {
    return (
      <Card>
        <CardHeader>
          <CardTitle>{t("migrationWorkspace.package.title")}</CardTitle>
          <CardDescription>{t("migrationWorkspace.package.unavailableDescription")}</CardDescription>
        </CardHeader>
        <CardContent>
          <p className="text-sm text-muted-foreground">{t("migrationWorkspace.detail.noPackage")}</p>
        </CardContent>
      </Card>
    )
  }

  const copy = async (label: string, value: string) => {
    await navigator.clipboard.writeText(value)
    setCopied(label)
    window.setTimeout(() => setCopied(null), 1500)
  }

  const downloadSourceRequest = async () => {
    try {
      setError(null)
      setRequestDownloading(true)
      const download = await downloadMigrationSourceRequest(
        detail.session.migrationId,
        "preview",
      )
      const objectUrl = URL.createObjectURL(download.blob)
      const anchor = document.createElement("a")
      anchor.href = objectUrl
      anchor.download = download.fileName
      anchor.click()
      URL.revokeObjectURL(objectUrl)
    } catch (caught) {
      if (isMigrationOutcomeUncertain(caught)) return
      setError(caught instanceof Error ? caught.message : String(caught))
    } finally {
      setRequestDownloading(false)
    }
  }

  const runUpload = async () => {
    if (!packageFile || !guide.allows("upload-package")) return
    const file = packageFile

    try {
      setError(null)
      setIsWorking(true)
      await guide.run("upload-package", () => uploadSecureMigrationPackage(detail.session.migrationId, file))
      setPackageFile(null)
      setPendingAction(null)
      await onChanged().catch(() => undefined)
    } catch (caught) {
      if (isMigrationOutcomeUncertain(caught)) return
      if (isSecureMigrationStepUpRequired(caught)) {
        setPendingAction("upload")
        setStepUpOpen(true)
        return
      }

      setError(caught instanceof Error ? caught.message : String(caught))
    } finally {
      setIsWorking(false)
    }
  }

  const resumePendingAction = () => {
    setStepUpOpen(false)
    if (pendingAction === "upload") {
      void runUpload()
      return
    }

  }

  const awaitingPackage = migrationPackage.status === "awaiting-package"
  const validated = migrationPackage.status === "package-validated"

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle className="flex items-center gap-2">
              <LockKeyhole className="h-5 w-5" aria-hidden="true" />
              {t("migrationWorkspace.package.title")}
            </CardTitle>
            <CardDescription className="mt-1">
              {awaitingPackage
                ? t("migrationWorkspace.package.awaitingDescription")
                : t("migrationWorkspace.package.evidenceDescription")}
            </CardDescription>
          </div>
          {validated ? <Badge variant="outline">{t("migrationWorkspace.package.readyBadge")}</Badge> : null}
        </div>
      </CardHeader>
      <CardContent className="space-y-5">
        {error ? (
          <Alert variant="destructive">
            <AlertTriangle className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.package.actionFailed")}</AlertTitle>
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        ) : null}

        {awaitingPackage ? (
          <>
            <div className="grid gap-4 xl:grid-cols-2">
              <div className="space-y-4 rounded-xl border p-4 sm:p-5">
                <div className="flex items-start gap-3">
                  <StepNumber value="1" />
                  <div>
                    <h3 className="font-semibold">{t("migrationWorkspace.package.requestTitle")}</h3>
                    <p className="mt-1 text-sm leading-6 text-muted-foreground">
                      {t("migrationWorkspace.package.requestDescription")}
                    </p>
                  </div>
                </div>

                <div className="space-y-3 pl-0 sm:pl-11">
                  <Button
                    type="button"
                    disabled={requestDownloading}
                    onClick={() => void downloadSourceRequest()}
                  >
                    <Download className="mr-2 h-4 w-4" aria-hidden="true" />
                    {requestDownloading
                      ? t("migrationWorkspace.package.downloadingRequest")
                      : t("migrationWorkspace.package.downloadRequest")}
                  </Button>
                  <p className="text-xs leading-5 text-muted-foreground">
                    {t("migrationWorkspace.package.requestSafety")}
                  </p>
                </div>

                <details className="rounded-lg border bg-muted/10">
                  <summary className="cursor-pointer px-4 py-3 text-sm font-medium">
                    {t("migrationWorkspace.package.advancedCommand")}
                  </summary>
                  <div className="space-y-3 border-t p-4">
                    <p className="text-sm leading-6 text-muted-foreground">
                      {t("migrationWorkspace.package.commandDescription")}
                    </p>
                    <div className="flex flex-wrap gap-2">
                      <Button
                        type="button"
                        variant="outline"
                        disabled={!sourceCommand}
                        onClick={() => void copy("source-command", sourceCommand)}
                      >
                        <Clipboard className="mr-2 h-4 w-4" aria-hidden="true" />
                        {copied === "source-command"
                          ? t("migrationIntake.sourceCommandCopied")
                          : t("migrationIntake.sourceCommandCopy")}
                      </Button>
                      <Button
                        type="button"
                        variant="outline"
                        disabled={!sourceCommand}
                        onClick={() => setShowSourceCommand((value) => !value)}
                      >
                        <Eye className="mr-2 h-4 w-4" aria-hidden="true" />
                        {showSourceCommand
                          ? t("migrationIntake.sourceCommandHide")
                          : t("migrationIntake.sourceCommandShow")}
                      </Button>
                    </div>
                    {showSourceCommand && sourceCommand ? (
                      <pre
                        className="max-h-96 overflow-auto rounded-md border bg-muted/40 p-4 text-xs"
                        aria-label={t("migrationWorkspace.package.generatedCommand")}
                      >
                        <code>{sourceCommand}</code>
                      </pre>
                    ) : null}
                    <p className="text-xs leading-5 text-muted-foreground">
                      {t("migrationWorkspace.package.commandSafety")}
                    </p>
                  </div>
                </details>
              </div>

              <div className="space-y-4 rounded-xl border p-4 sm:p-5">
                <div className="flex items-start gap-3">
                  <StepNumber value="2" />
                  <div>
                    <h3 className="font-semibold">{t("migrationWorkspace.package.uploadTitle")}</h3>
                    <p className="mt-1 text-sm leading-6 text-muted-foreground">
                      {t("migrationWorkspace.package.uploadDescription")}
                    </p>
                  </div>
                </div>

                <div className="space-y-3 sm:pl-11">
                  <label className="block text-sm font-medium" htmlFor="migration-package-file">
                    {t("migrationWorkspace.package.encryptedFile")}
                  </label>
                  <input
                    id="migration-package-file"
                    type="file"
                    accept=".age,.zip.age,application/octet-stream"
                    aria-label={t("migrationWorkspace.package.encryptedFile")}
                    onChange={(event) => {
                      setPackageFile(event.target.files?.[0] ?? null)
                      setError(null)
                    }}
                    className="w-full rounded-md border border-border bg-background px-3 py-2 text-sm"
                  />
                  <div className="flex min-h-5 items-center gap-2 text-sm text-muted-foreground" aria-live="polite">
                    <FileArchive className="h-4 w-4 shrink-0" aria-hidden="true" />
                    <span className="break-all">
                      {packageFile
                        ? `${packageFile.name} · ${formatFileSize(packageFile.size)}`
                        : t("migrationWorkspace.package.chooseFile")}
                    </span>
                  </div>
                  <Button
                    type="button"
                    disabled={!packageFile || isWorking || !guide.allows("upload-package")}
                    onClick={() => void runUpload()}
                  >
                    <UploadCloud className="mr-2 h-4 w-4" aria-hidden="true" />
                    {isWorking
                      ? t("migrationWorkspace.package.validating")
                      : t("migrationWorkspace.package.upload")}
                  </Button>
                  <p className="text-xs leading-5 text-muted-foreground">
                    {t("migrationWorkspace.package.currentRequestDescription")}
                  </p>
                </div>
              </div>
            </div>

            <details className="rounded-lg border bg-muted/10">
              <summary className="cursor-pointer px-4 py-3 text-sm font-medium">
                {t("migrationWorkspace.package.securityDetails")}
              </summary>
              <div className="space-y-4 border-t px-4 py-4">
                <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
                  <PackageFact
                    label={t("migrationWorkspace.detail.expires")}
                    value={formatOptionalDate(
                      migrationPackage.expiresAtUtc,
                      intlLocale,
                      t("migrationWorkspace.notAvailable"),
                    )}
                  />
                  <PackageFact
                    label={t("migrationWorkspace.detail.transferMode")}
                    value={migrationPackage.transferMode}
                  />
                  <PackageFact
                    label={t("migrationWorkspace.package.fingerprint")}
                    value={migrationPackage.recipientFingerprint ?? t("migrationWorkspace.notAvailable")}
                    mono
                  />
                </div>

                <div>
                  <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                    {t("migrationWorkspace.package.recipient")}
                  </div>
                  <div className="mt-2 flex flex-col gap-3 sm:flex-row sm:items-center">
                    <code className="min-w-0 flex-1 break-all rounded bg-muted px-3 py-2 text-xs">
                      {migrationPackage.ageRecipient ?? t("migrationWorkspace.notAvailable")}
                    </code>
                    <Button
                      type="button"
                      variant="outline"
                      disabled={!migrationPackage.ageRecipient}
                      onClick={() =>
                        migrationPackage.ageRecipient
                          ? void copy("recipient", migrationPackage.ageRecipient)
                          : undefined
                      }
                    >
                      <Clipboard className="mr-2 h-4 w-4" aria-hidden="true" />
                      {copied === "recipient"
                        ? t("migrationWorkspace.package.copied")
                        : t("migrationWorkspace.package.copyRecipient")}
                    </Button>
                  </div>
                  <p className="mt-3 text-xs leading-5 text-muted-foreground">
                    {t("migrationWorkspace.package.privateIdentitySafety")}
                  </p>
                </div>

              </div>
            </details>
          </>
        ) : null}

        {validated ? (
          <>
            <Alert>
              <CheckCircle2 className="h-4 w-4" />
              <AlertTitle>{t("migrationWorkspace.package.validatedTitle")}</AlertTitle>
              <AlertDescription>{t("migrationWorkspace.package.validatedDescription")}</AlertDescription>
            </Alert>

            <div className="grid gap-3 sm:grid-cols-3">
              <PackageFact
                label={t("migrationWorkspace.detail.fileName")}
                value={migrationPackage.fileName ?? t("migrationWorkspace.notAvailable")}
              />
              <PackageFact
                label={t("migrationWorkspace.detail.packageSize")}
                value={formatNullableFileSize(migrationPackage.sizeBytes, t("migrationWorkspace.notAvailable"))}
              />
              <PackageFact
                label={t("migrationWorkspace.detail.validated")}
                value={formatOptionalDate(
                  migrationPackage.validatedAtUtc,
                  intlLocale,
                  t("migrationWorkspace.notAvailable"),
                )}
              />
            </div>

            <details className="rounded-lg border bg-muted/10">
              <summary className="cursor-pointer px-4 py-3 text-sm font-medium">
                {t("migrationWorkspace.package.validationDetails")}
              </summary>
              <div className="space-y-4 border-t px-4 py-4">
                <div className="grid gap-3 sm:grid-cols-2">
                  <PackageFact
                    label={t("migrationWorkspace.package.encryptedSha")}
                    value={migrationPackage.encryptedSha256 ?? t("migrationWorkspace.notAvailable")}
                    mono
                  />
                  <PackageFact
                    label={t("migrationWorkspace.package.decryptedSha")}
                    value={migrationPackage.decryptedSha256 ?? t("migrationWorkspace.notAvailable")}
                    mono
                  />
                  <PackageFact
                    label={t("migrationWorkspace.detail.archiveMigrationId")}
                    value={migrationPackage.archiveMigrationId ?? t("migrationWorkspace.notAvailable")}
                    mono
                  />
                  <PackageFact
                    label={t("migrationWorkspace.detail.transferMode")}
                    value={migrationPackage.transferMode}
                  />
                </div>
                <p className="text-sm leading-6 text-muted-foreground">
                  {t("migrationWorkspace.package.retentionDescription")}
                </p>
              </div>
            </details>
          </>
        ) : null}

        {!awaitingPackage && !validated ? (
          <Alert>
            <AlertTriangle className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.package.closedTitle")}</AlertTitle>
            <AlertDescription>
              {t("migrationWorkspace.package.closedDescription", { status: migrationPackage.status })}
            </AlertDescription>
          </Alert>
        ) : null}
      </CardContent>

      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={setStepUpOpen}
        onVerified={resumePendingAction}
      />
    </Card>
  )
}

function StepNumber({ value }: { value: string }) {
  return (
    <span className="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-primary text-sm font-semibold text-primary-foreground">
      {value}
    </span>
  )
}

function PackageFact({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="rounded-lg border p-3">
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className={mono ? "mt-1 break-all font-mono text-xs" : "mt-1 break-all text-sm font-medium"}>{value}</div>
    </div>
  )
}

function formatOptionalDate(value: string | null, locale: string, fallback: string) {
  return formatMigrationDateTime(value, locale, fallback)
}

function formatNullableFileSize(bytes: number | null, fallback: string) {
  return bytes === null ? fallback : formatFileSize(bytes)
}

function formatFileSize(bytes: number) {
  if (bytes < 1024) return `${bytes} B`
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(2)} GB`
}
