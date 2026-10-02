import { formatMigrationDateTime } from "./migration-time"
import { useMemo, useState } from "react"
import {
  AlertTriangle,
  CheckCircle2,
  Clipboard,
  Download,
  Eye,
  KeyRound,
  ShieldCheck,
  UploadCloud,
} from "lucide-react"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from "@/components/ui/card"
import { Input } from "@/components/ui/input"
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog"
import {
  createFinalPackageRecipient,
  downloadMigrationSourceRequest,
  isSecureMigrationStepUpRequired,
  uploadFinalMigrationPackage,
} from "@/features/operator/migrations/api/migration-intakes"
import type {
  MigrationSessionDetail,
  MigrationSessionPackageRevision,
} from "@/features/operator/migrations/api/migration-sessions"
import { buildSourcePackageCommand } from "@/features/operator/migrations/source-package-command"

type MigrationFinalPackageHandoffProps = {
  detail: MigrationSessionDetail
  onChanged: () => Promise<unknown>
}

type PendingAction = "create" | "upload" | null

export function MigrationFinalPackageHandoff({
  detail,
  onChanged,
}: MigrationFinalPackageHandoffProps) {
  const { intlLocale, t } = useI18n()
  const [showCommand, setShowCommand] = useState(false)
  const [copied, setCopied] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [isWorking, setIsWorking] = useState(false)
  const [requestDownloading, setRequestDownloading] = useState(false)
  const [stepUpOpen, setStepUpOpen] = useState(false)
  const [pendingAction, setPendingAction] = useState<PendingAction>(null)
  const [packageFile, setPackageFile] = useState<File | null>(null)

  const preview = latestActiveRevision(detail.packageRevisions ?? [], "preview")
  const finalRevision = latestActiveRevision(detail.packageRevisions ?? [], "final")
  const handoffAvailable =
    !detail.session.historicalCompatibility.usesCatalogRestorePath &&
    preview?.status === "package-validated" &&
    (detail.session.phase === "cutover" || detail.session.status === "staging-verified")

  const finalCommand = useMemo(() => {
    if (
      !finalRevision?.ageRecipient ||
      !finalRevision.recipientFingerprint ||
      finalRevision.status !== "awaiting-package"
    ) {
      return ""
    }

    return buildSourcePackageCommand({
      intakeId: detail.session.migrationId,
      packageRevisionId: finalRevision.packageRevisionId,
      ageRecipient: finalRevision.ageRecipient,
      recipientFingerprint: finalRevision.recipientFingerprint,
      requireFinalFrozen: true,
    })
  }, [detail.session.migrationId, finalRevision])

  if (!handoffAvailable && !finalRevision) {
    return null
  }

  const copy = async (label: string, value: string) => {
    await navigator.clipboard.writeText(value)
    setCopied(label)
    window.setTimeout(() => setCopied(null), 1500)
  }

  const createRecipient = async () => {
    try {
      setError(null)
      setIsWorking(true)
      await createFinalPackageRecipient(detail.session.migrationId)
      setPendingAction(null)
      await onChanged()
    } catch (caught) {
      if (isSecureMigrationStepUpRequired(caught)) {
        setPendingAction("create")
        setStepUpOpen(true)
        return
      }
      setError(caught instanceof Error ? caught.message : String(caught))
    } finally {
      setIsWorking(false)
    }
  }

  const downloadSourceRequest = async () => {
    try {
      setError(null)
      setRequestDownloading(true)
      const download = await downloadMigrationSourceRequest(
        detail.session.migrationId,
        "final",
      )
      const objectUrl = URL.createObjectURL(download.blob)
      const anchor = document.createElement("a")
      anchor.href = objectUrl
      anchor.download = download.fileName
      anchor.click()
      URL.revokeObjectURL(objectUrl)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : String(caught))
    } finally {
      setRequestDownloading(false)
    }
  }

  const uploadPackage = async () => {
    if (!packageFile) return
    try {
      setError(null)
      setIsWorking(true)
      await uploadFinalMigrationPackage(detail.session.migrationId, packageFile)
      setPendingAction(null)
      setPackageFile(null)
      await onChanged()
    } catch (caught) {
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

  const recipientExpired = finalRevision?.status === "expired"
  const finalValidated = finalRevision?.status === "package-validated"

  const completeStepUp = () => {
    if (pendingAction === "upload") {
      void uploadPackage()
      return
    }
    void createRecipient()
  }

  return (
    <Card>
      <CardHeader>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <CardTitle className="flex items-center gap-2">
              <KeyRound className="h-5 w-5" aria-hidden="true" />
              {t("migrationWorkspace.finalPackage.title")}
            </CardTitle>
            <CardDescription className="mt-1">
              {t("migrationWorkspace.finalPackage.description")}
            </CardDescription>
          </div>
          <Badge variant="outline">{t("migrationWorkspace.finalPackage.finalBadge")}</Badge>
        </div>
      </CardHeader>
      <CardContent className="space-y-5">
        {error ? (
          <Alert variant="destructive">
            <AlertTriangle className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.finalPackage.actionFailed")}</AlertTitle>
            <AlertDescription>{error}</AlertDescription>
          </Alert>
        ) : null}

        {!finalRevision || recipientExpired ? (
          <div className="space-y-4 rounded-lg border p-4">
            <div>
              <h3 className="font-medium">
                {recipientExpired
                  ? t("migrationWorkspace.finalPackage.replaceTitle")
                  : t("migrationWorkspace.finalPackage.createTitle")}
              </h3>
              <p className="mt-1 text-sm text-muted-foreground">
                {t("migrationWorkspace.finalPackage.createDescription")}
              </p>
            </div>
            <Alert>
              <ShieldCheck className="h-4 w-4" />
              <AlertTitle>{t("migrationWorkspace.finalPackage.stepUpTitle")}</AlertTitle>
              <AlertDescription>
                {t("migrationWorkspace.finalPackage.stepUpDescription")}
              </AlertDescription>
            </Alert>
            <Button
              type="button"
              disabled={isWorking || !handoffAvailable}
              onClick={() => void createRecipient()}
            >
              <KeyRound className="mr-2 h-4 w-4" aria-hidden="true" />
              {isWorking
                ? t("migrationWorkspace.finalPackage.creating")
                : recipientExpired
                  ? t("migrationWorkspace.finalPackage.replace")
                  : t("migrationWorkspace.finalPackage.create")}
            </Button>
          </div>
        ) : null}

        {finalRevision?.status === "awaiting-package" ? (
          <>
            <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
              <Fact
                label={t("migrationWorkspace.finalPackage.revision")}
                value={finalRevision.packageRevisionId}
                mono
              />
              <Fact
                label={t("migrationWorkspace.detail.packageStatus")}
                value={finalRevision.status}
              />
              <Fact
                label={t("migrationWorkspace.package.fingerprint")}
                value={finalRevision.recipientFingerprint ?? t("migrationWorkspace.notAvailable")}
                mono
              />
              <Fact
                label={t("migrationWorkspace.detail.expires")}
                value={formatDate(
                  finalRevision.expiresAtUtc,
                  intlLocale,
                  t("migrationWorkspace.notAvailable"),
                )}
              />
            </div>

            <div className="rounded-lg border p-4">
              <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
                {t("migrationWorkspace.finalPackage.recipient")}
              </div>
              <div className="mt-2 flex flex-col gap-3 sm:flex-row sm:items-center">
                <code className="min-w-0 flex-1 break-all rounded bg-muted px-3 py-2 text-xs">
                  {finalRevision.ageRecipient}
                </code>
                <Button
                  type="button"
                  variant="outline"
                  disabled={!finalRevision.ageRecipient}
                  onClick={() =>
                    finalRevision.ageRecipient
                      ? void copy("final-recipient", finalRevision.ageRecipient)
                      : undefined
                  }
                >
                  <Clipboard className="mr-2 h-4 w-4" aria-hidden="true" />
                  {copied === "final-recipient"
                    ? t("migrationWorkspace.package.copied")
                    : t("migrationWorkspace.package.copyRecipient")}
                </Button>
              </div>
              <p className="mt-3 text-xs text-muted-foreground">
                {t("migrationWorkspace.finalPackage.privateIdentitySafety")}
              </p>
            </div>

            <div className="space-y-4 rounded-lg border p-4">
              <div>
                <h3 className="font-medium">{t("migrationWorkspace.finalPackage.requestTitle")}</h3>
                <p className="mt-1 text-sm text-muted-foreground">
                  {t("migrationWorkspace.finalPackage.requestDescription")}
                </p>
              </div>
              <Button
                type="button"
                disabled={requestDownloading}
                onClick={() => void downloadSourceRequest()}
              >
                <Download className="mr-2 h-4 w-4" aria-hidden="true" />
                {requestDownloading
                  ? t("migrationWorkspace.package.downloadingRequest")
                  : t("migrationWorkspace.finalPackage.downloadRequest")}
              </Button>
              <details className="rounded-lg border bg-muted/10">
                <summary className="cursor-pointer px-4 py-3 text-sm font-medium">
                  {t("migrationWorkspace.package.advancedCommand")}
                </summary>
                <div className="space-y-4 border-t p-4">
                  <div>
                    <h3 className="font-medium">{t("migrationWorkspace.finalPackage.commandTitle")}</h3>
                <p className="mt-1 text-sm text-muted-foreground">
                  {t("migrationWorkspace.finalPackage.commandDescription")}
                </p>
              </div>
              <div className="flex flex-wrap gap-2">
                <Button
                  type="button"
                  disabled={!finalCommand}
                  onClick={() => void copy("final-command", finalCommand)}
                >
                  <Clipboard className="mr-2 h-4 w-4" aria-hidden="true" />
                  {copied === "final-command"
                    ? t("migrationIntake.sourceCommandCopied")
                    : t("migrationIntake.sourceCommandCopy")}
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  disabled={!finalCommand}
                  onClick={() => setShowCommand((value) => !value)}
                >
                  <Eye className="mr-2 h-4 w-4" aria-hidden="true" />
                  {showCommand
                    ? t("migrationIntake.sourceCommandHide")
                    : t("migrationIntake.sourceCommandShow")}
                </Button>
              </div>
              {showCommand && finalCommand ? (
                <pre
                  className="max-h-96 overflow-auto rounded-md border bg-muted/40 p-4 text-xs"
                  aria-label={t("migrationWorkspace.finalPackage.generatedCommand")}
                >
                  <code>{finalCommand}</code>
                </pre>
              ) : null}
                </div>
              </details>
              <Alert>
                <ShieldCheck className="h-4 w-4" />
                <AlertTitle>{t("migrationWorkspace.finalPackage.finalOnlyTitle")}</AlertTitle>
                <AlertDescription>
                  {t("migrationWorkspace.finalPackage.finalOnlyDescription")}
                </AlertDescription>
              </Alert>
            </div>

            <div className="space-y-4 rounded-lg border p-4">
              <div>
                <h3 className="font-medium">{t("migrationWorkspace.finalPackage.uploadTitle")}</h3>
                <p className="mt-1 text-sm text-muted-foreground">
                  {t("migrationWorkspace.finalPackage.uploadDescription")}
                </p>
              </div>
              <Input
                type="file"
                accept=".age,.zip.age,.memmigration.zip.age"
                aria-label={t("migrationWorkspace.finalPackage.encryptedFile")}
                onChange={(event) => setPackageFile(event.target.files?.[0] ?? null)}
              />
              <Alert>
                <ShieldCheck className="h-4 w-4" />
                <AlertTitle>{t("migrationWorkspace.finalPackage.validationTitle")}</AlertTitle>
                <AlertDescription>
                  {t("migrationWorkspace.finalPackage.validationDescription")}
                </AlertDescription>
              </Alert>
              <Button
                type="button"
                disabled={!packageFile || isWorking}
                onClick={() => void uploadPackage()}
              >
                <UploadCloud className="mr-2 h-4 w-4" aria-hidden="true" />
                {isWorking
                  ? t("migrationWorkspace.finalPackage.validating")
                  : t("migrationWorkspace.finalPackage.upload")}
              </Button>
            </div>
          </>
        ) : null}

        {finalValidated && finalRevision ? (
          <div className="space-y-4">
            <Alert>
              <CheckCircle2 className="h-4 w-4" />
              <AlertTitle>{t("migrationWorkspace.finalPackage.validatedTitle")}</AlertTitle>
              <AlertDescription>
                {t("migrationWorkspace.finalPackage.validatedDescription")}
              </AlertDescription>
            </Alert>
            <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
              <Fact
                label={t("migrationWorkspace.finalPackage.revision")}
                value={finalRevision.packageRevisionId}
                mono
              />
              <Fact
                label={t("migrationWorkspace.finalPackage.captureKind")}
                value={finalRevision.captureKind ?? t("migrationWorkspace.notAvailable")}
              />
              <Fact
                label={t("migrationWorkspace.finalPackage.sourceFrozen")}
                value={finalRevision.sourceFrozen
                  ? t("common.yes")
                  : t("common.no")}
              />
              <Fact
                label={t("migrationWorkspace.finalPackage.rehearsalOnly")}
                value={finalRevision.rehearsalOnly
                  ? t("common.yes")
                  : t("common.no")}
              />
            </div>
            <div className="grid gap-3 xl:grid-cols-2">
              <Fact
                label={t("migrationWorkspace.package.encryptedSha")}
                value={finalRevision.encryptedSha256 ?? t("migrationWorkspace.notAvailable")}
                mono
              />
              <Fact
                label={t("migrationWorkspace.package.decryptedSha")}
                value={finalRevision.decryptedSha256 ?? t("migrationWorkspace.notAvailable")}
                mono
              />
            </div>
            <p className="text-sm text-muted-foreground">
              {finalRevision.validationSummary}
            </p>
          </div>
        ) : null}
      </CardContent>

      <OperatorStepUpDialog
        open={stepUpOpen}
        onOpenChange={setStepUpOpen}
        onVerified={completeStepUp}
      />
    </Card>
  )
}

function latestActiveRevision(
  revisions: MigrationSessionPackageRevision[],
  purpose: string,
) {
  return revisions
    .filter((revision) => revision.purpose === purpose && revision.active)
    .sort((left, right) => right.revisionNumber - left.revisionNumber)[0]
}

function Fact({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return (
    <div className="rounded-lg border p-3">
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">{label}</div>
      <div className={mono ? "mt-1 break-all font-mono text-xs" : "mt-1 break-all text-sm font-medium"}>
        {value}
      </div>
    </div>
  )
}

function formatDate(value: string | null, locale: string, fallback: string) {
  return formatMigrationDateTime(value, locale, fallback)
}
