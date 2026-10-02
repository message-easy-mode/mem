import type { ReactNode } from "react"
import {
  CheckCircle2,
  Clipboard,
  Database,
  Download,
  ExternalLink,
  FileArchive,
  FileCheck2,
  FileText,
  Globe2,
  HardDrive,
  Info,
  LockKeyhole,
  Network,
  Server,
  ShieldCheck,
  TriangleAlert,
} from "lucide-react"
import { Link } from "react-router-dom"

import { useI18n, type I18nContextValue } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { cn } from "@/lib/utils"
import type {
  RestoreWorkspaceOperationSummary,
  RestoreWorkspaceResponse,
  RestoreWorkspaceTargetClaim,
  RestoreWorkspaceVerificationCheck,
} from "@/features/operator/backups/api/types/restore-workspace.types"

type ConfigurationTone = "success" | "warning" | "error" | "information" | "protected"

const configurationIdentifierKeys: Readonly<Record<string, TranslationKey>> = {
  "backup-catalog": "restoreWorkspace.configuration.identifier.backupCatalog",
  available: "restoreWorkspace.configuration.identifier.available",
  "not-selected": "restoreWorkspace.configuration.identifier.notSelected",
  "not-available": "restoreWorkspace.configuration.identifier.notAvailable",
  ready: "restoreWorkspace.configuration.identifier.ready",
  completed: "restoreWorkspace.configuration.identifier.completed",
  failed: "restoreWorkspace.configuration.identifier.failed",
  active: "restoreWorkspace.configuration.identifier.active",
  released: "restoreWorkspace.configuration.identifier.released",
  blocked: "restoreWorkspace.configuration.identifier.blocked",
  pending: "restoreWorkspace.configuration.identifier.pending",
  configured: "restoreWorkspace.configuration.identifier.configured",
  "local-captured": "restoreWorkspace.configuration.identifier.localCaptured",
  "imported-zip": "restoreWorkspace.configuration.identifier.importedZip",
  warning: "restoreWorkspace.configuration.identifier.warning",
  hidden: "restoreWorkspace.configuration.identifier.hidden",
  protected: "restoreWorkspace.configuration.identifier.protected",
}

export function RestoreConfigurationTab({
  restoreSessionId,
  workspace,
  onViewEvidence,
  onViewLogs,
}: {
  restoreSessionId: string
  workspace: RestoreWorkspaceResponse
  onViewEvidence: () => void
  onViewLogs: () => void
}) {
  const { intlLocale, t } = useI18n()
  const operation = resolveRestoreOperation(workspace)
  const verificationChecks = workspace.verification.checks ?? []
  const evidenceRecordCount = workspace.evidence.categories.reduce(
    (total, category) => total + category.itemCount,
    0,
  )
  const claims = workspace.target.claims ?? []
  const targetStackHref = workspace.target.stackSlug
    ? `/stacks/${encodeURIComponent(workspace.target.stackSlug)}`
    : null
  const identifier = (value: string | null | undefined) => presentIdentifier(value, t)
  const utc = (value: string | null | undefined) => formatUtc(value, intlLocale, t)

  const configurationSnapshot = buildSafeConfigurationSnapshot({
    restoreSessionId,
    workspace,
    operation,
    evidenceRecordCount,
  })

  const copySnapshot = async () => {
    await copyText(JSON.stringify(configurationSnapshot, null, 2))
  }

  const downloadSnapshot = () => {
    downloadJsonFile(
      `mem-restore-${restoreSessionId}-configuration-summary.json`,
      configurationSnapshot,
    )
  }

  return (
    <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_300px]">
      <section className="min-w-0 space-y-4">
        <Card>
          <CardHeader className="border-b">
            <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
              <div>
                <CardTitle className="flex items-center gap-2">
                  {t("restoreWorkspace.configuration.title")}
                  <Badge variant="outline" className="border-blue-500/35 bg-blue-500/10 text-blue-700 dark:text-blue-300">
                    <LockKeyhole className="mr-1 h-3 w-3" />
                    {t("restoreWorkspace.configuration.readOnly")}
                  </Badge>
                </CardTitle>
                <p className="mt-1 text-sm text-muted-foreground">
                  {t("restoreWorkspace.configuration.description")}
                </p>
              </div>
              <Button variant="outline" size="sm" onClick={downloadSnapshot}>
                <Download className="mr-2 h-4 w-4" />
                {t("restoreWorkspace.configuration.downloadSnapshot")}
              </Button>
            </div>
          </CardHeader>

          <CardContent className="grid gap-4 py-4 lg:grid-cols-2">
            <ConfigurationCard
              icon={<Globe2 className="h-5 w-5" />}
              iconClassName="bg-sky-500/15 text-sky-700 dark:text-sky-300"
              title={t("restoreWorkspace.configuration.identityEndpoints")}
            >
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.sourceMatrixIdentity")}
                value={workspace.source.matrixHost ?? t("restoreWorkspace.configuration.notRecordedInWorkspace")}
                state={t("restoreWorkspace.configuration.captured")}
              />
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.sourceElementAddress")}
                value={workspace.source.elementHost ?? t("restoreWorkspace.configuration.notRecordedInWorkspace")}
                state={t("restoreWorkspace.configuration.captured")}
              />
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.targetMatrixEndpoint")}
                value={workspace.target.matrixHost ?? t("restoreWorkspace.configuration.notSelected")}
                state={workspace.target.matrixHost
                  ? t("restoreWorkspace.configuration.appliedTarget")
                  : t("restoreWorkspace.configuration.notSelected")}
              />
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.targetElementEndpoint")}
                value={workspace.target.elementHost ?? t("restoreWorkspace.configuration.notSelected")}
                state={workspace.target.elementHost
                  ? t("restoreWorkspace.configuration.appliedTarget")
                  : t("restoreWorkspace.configuration.notSelected")}
              />
            </ConfigurationCard>

            <ConfigurationCard
              icon={<Server className="h-5 w-5" />}
              iconClassName="bg-violet-500/15 text-violet-700 dark:text-violet-300"
              title={t("restoreWorkspace.configuration.restoreOperation")}
            >
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.operation")}
                value={operation?.operation ?? t("restoreWorkspace.configuration.noRuntimeOperation")}
                mono={Boolean(operation?.operation)}
              />
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.operationStatus")}
                value={identifier(operation?.status ?? workspace.attempt.status)}
                tone={statusTone(operation?.status ?? workspace.attempt.status)}
              />
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.currentFinalStep")}
                value={operation?.currentStep ?? workspace.attempt.currentStage ?? t("restoreWorkspace.configuration.notRecorded")}
                mono={Boolean(operation?.currentStep ?? workspace.attempt.currentStage)}
              />
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.runtimeOperationId")}
                value={operation?.operationId ?? workspace.attempt.currentOperationId ?? t("restoreWorkspace.configuration.notRecorded")}
                mono
                copyable={Boolean(operation?.operationId ?? workspace.attempt.currentOperationId)}
              />
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.completed")}
                value={utc(operation?.completedAtUtc ?? workspace.attempt.terminalAtUtc)}
              />
            </ConfigurationCard>

            <ConfigurationCard
              icon={<Database className="h-5 w-5" />}
              iconClassName="bg-emerald-500/15 text-emerald-700 dark:text-emerald-300"
              title={t("restoreWorkspace.configuration.dataRestoreMaterial")}
            >
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.backupSource")}
                value={identifier(workspace.source.kind)}
              />
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.backupReference")}
                value={workspace.source.backupId ?? t("restoreWorkspace.configuration.notRecorded")}
                mono
                copyable={Boolean(workspace.source.backupId)}
              />
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.sourceStack")}
                value={workspace.source.stackSlug ?? t("restoreWorkspace.configuration.notRecorded")}
              />
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.backupValidation")}
                value={identifier(workspace.source.validationStatus)}
                tone={statusTone(workspace.source.validationStatus)}
              />
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.databaseMediaKeysSigningData")}
                value={t("restoreWorkspace.configuration.notEnumerated")}
                state={t("restoreWorkspace.configuration.notExposed")}
              />
            </ConfigurationCard>

            <ConfigurationCard
              icon={<Network className="h-5 w-5" />}
              iconClassName="bg-amber-500/15 text-amber-700 dark:text-amber-300"
              title={t("restoreWorkspace.configuration.targetClaimsRouting")}
            >
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.targetStack")}
                value={workspace.target.stackSlug ?? t("restoreWorkspace.configuration.notSelected")}
                mono={Boolean(workspace.target.stackSlug)}
              />
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.targetAvailability")}
                value={identifier(workspace.target.availability)}
                tone={statusTone(workspace.target.availability)}
              />
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.claimedResources")}
                value={claims.length === 0
                  ? t("restoreWorkspace.configuration.noTargetClaims")
                  : t("restoreWorkspace.configuration.claimsRecorded", { count: claims.length })}
              />
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.routeConfiguration")}
                value={workspace.verification.hasRun
                  ? workspace.verification.summary || t("restoreWorkspace.configuration.verificationHasRun")
                  : t("restoreWorkspace.configuration.notVerifiedYet")}
                tone={workspace.verification.allPassed === true ? "success" : "information"}
              />
            </ConfigurationCard>

            <ConfigurationCard
              icon={<FileCheck2 className="h-5 w-5" />}
              iconClassName="bg-cyan-500/15 text-cyan-700 dark:text-cyan-300"
              title={t("restoreWorkspace.configuration.validationVerification")}
            >
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.validationSummary")}
                value={workspace.source.validationSummary || t("restoreWorkspace.configuration.noValidationSummary")}
              />
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.backupCatalogItem")}
                value={workspace.source.sourceDeleted
                  ? t("restoreWorkspace.configuration.deletedBackup")
                  : workspace.source.catalogEntryId ?? t("restoreWorkspace.configuration.notRecorded")}
                mono={!workspace.source.sourceDeleted}
                copyable={Boolean(workspace.source.catalogEntryId) && !workspace.source.sourceDeleted}
              />
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.sourceOrigin")}
                value={identifier(workspace.source.sourceOriginKind)}
              />
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.publicVerification")}
                value={workspace.verification.hasRun
                  ? identifier(workspace.verification.status)
                  : t("restoreWorkspace.configuration.notRun")}
                tone={workspace.verification.allPassed === true ? "success" : statusTone(workspace.verification.status)}
              />
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.checksRepresented")}
                value={t("restoreWorkspace.configuration.checksRepresentedCount", { count: verificationChecks.length })}
              />
            </ConfigurationCard>

            <ConfigurationCard
              icon={<LockKeyhole className="h-5 w-5" />}
              iconClassName="bg-rose-500/15 text-rose-700 dark:text-rose-300"
              title={t("restoreWorkspace.configuration.securitySensitiveValues")}
            >
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.passwordsTokensPrivateKeys")}
                value={t("restoreWorkspace.configuration.notExposed")}
                tone="protected"
              />
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.rawConfigurationFiles")}
                value={t("restoreWorkspace.configuration.notExposedByCurrentApi")}
                state={t("restoreWorkspace.configuration.safeProjectionOnly")}
              />
              <ConfigurationValue
                label={t("restoreWorkspace.configuration.exportBehaviour")}
                value={t("restoreWorkspace.configuration.exportSafeSummary")}
                state={t("restoreWorkspace.configuration.redacted")}
              />
            </ConfigurationCard>
          </CardContent>
        </Card>

        <Card>
          <CardHeader className="border-b">
            <CardTitle className="flex items-center gap-2">
              <ShieldCheck className="h-4 w-4 text-primary" />
              {t("restoreWorkspace.configuration.verificationChecks")}
            </CardTitle>
            <p className="text-sm text-muted-foreground">
              {t("restoreWorkspace.configuration.verificationChecksDescription")}
            </p>
          </CardHeader>
          <CardContent className="py-4">
            {verificationChecks.length > 0 ? (
              <ul className="grid gap-2 md:grid-cols-2">
                {verificationChecks.map((check) => (
                  <VerificationCheckRow key={check.code} check={check} />
                ))}
              </ul>
            ) : (
              <p className="text-sm text-muted-foreground">
                {t("restoreWorkspace.configuration.noVerificationCheckDetails")}
              </p>
            )}
          </CardContent>
        </Card>

        {claims.length > 0 ? (
          <Card>
            <CardHeader className="border-b">
              <CardTitle className="flex items-center gap-2">
                <HardDrive className="h-4 w-4 text-primary" />
                {t("restoreWorkspace.configuration.targetResourceClaims")}
              </CardTitle>
              <p className="text-sm text-muted-foreground">
                {t("restoreWorkspace.configuration.targetResourceClaimsDescription")}
              </p>
            </CardHeader>
            <CardContent className="space-y-2 py-4">
              {claims.map((claim) => <TargetClaimRow key={`${claim.resourceType}-${claim.resourceValue}`} claim={claim} />)}
            </CardContent>
          </Card>
        ) : null}

        <Card className="border-primary/20 bg-primary/5">
          <CardContent className="flex flex-col gap-4 py-4 md:flex-row md:items-center md:justify-between">
            <div className="flex min-w-0 items-start gap-3">
              <div className="rounded-lg bg-primary/15 p-2 text-primary"><FileArchive className="h-4 w-4" /></div>
              <div>
                <div className="font-medium">{t("restoreWorkspace.configuration.partOfRestoreRecord")}</div>
                <p className="mt-1 max-w-3xl text-sm text-muted-foreground">
                  {t("restoreWorkspace.configuration.partOfRestoreRecordDescription")}
                </p>
              </div>
            </div>
            <div className="flex flex-wrap gap-2">
              <Button variant="outline" size="sm" onClick={onViewEvidence}>
                <FileText className="mr-2 h-4 w-4" />
                {t("restoreWorkspace.configuration.goToEvidence")}
              </Button>
              <Button variant="outline" size="sm" onClick={onViewLogs}>
                <FileText className="mr-2 h-4 w-4" />
                {t("restoreWorkspace.configuration.openRestoreLogs")}
              </Button>
            </div>
          </CardContent>
        </Card>
      </section>

      <aside className="space-y-4 xl:sticky xl:top-6 xl:self-start">
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Info className="h-4 w-4 text-blue-500" />
              {t("restoreWorkspace.configuration.about")}
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-4 text-sm">
            <p className="text-muted-foreground">
              {t("restoreWorkspace.configuration.aboutDescription")}
            </p>
            <dl className="space-y-3">
              <SideDetail label={t("restoreWorkspace.configuration.restoreSession")} value={restoreSessionId} copyable />
              <SideDetail
                label={t("restoreWorkspace.configuration.sourceBackup")}
                value={workspace.source.backupId ?? t("restoreWorkspace.configuration.notRecorded")}
                copyable={Boolean(workspace.source.backupId)}
              />
              <SideDetail
                label={t("restoreWorkspace.configuration.targetStack")}
                value={workspace.target.stackSlug ?? t("restoreWorkspace.configuration.notSelected")}
                copyable={Boolean(workspace.target.stackSlug)}
              />
              <SideDetail label={t("restoreWorkspace.configuration.evidenceRecords")} value={String(evidenceRecordCount)} />
            </dl>
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle>{t("restoreWorkspace.configuration.quickActions")}</CardTitle></CardHeader>
          <CardContent className="space-y-2">
            <Button variant="outline" size="sm" className="w-full justify-start" onClick={downloadSnapshot}>
              <Download className="mr-2 h-4 w-4" />
              {t("restoreWorkspace.configuration.downloadSnapshot")}
            </Button>
            <Button variant="outline" size="sm" className="w-full justify-start" onClick={() => void copySnapshot()}>
              <Clipboard className="mr-2 h-4 w-4" />
              {t("restoreWorkspace.configuration.copySnapshot")}
            </Button>
            <Button variant="outline" size="sm" className="w-full justify-start" onClick={onViewEvidence}>
              <FileText className="mr-2 h-4 w-4" />
              {t("restoreWorkspace.configuration.viewEvidence")}
            </Button>
            <Button variant="outline" size="sm" className="w-full justify-start" onClick={onViewLogs}>
              <FileText className="mr-2 h-4 w-4" />
              {t("restoreWorkspace.configuration.viewRestoreLogs")}
            </Button>
          </CardContent>
        </Card>

        <Card>
          <CardHeader><CardTitle>{t("restoreWorkspace.configuration.needChange")}</CardTitle></CardHeader>
          <CardContent className="space-y-3 text-sm">
            <p className="text-muted-foreground">
              {t("restoreWorkspace.configuration.needChangeDescription")}
            </p>
            {targetStackHref ? (
              <Button variant="outline" size="sm" className="w-full" asChild>
                <Link to={targetStackHref}>
                  {t("restoreWorkspace.configuration.openRestoredChatServer")}
                  <ExternalLink className="ml-2 h-4 w-4" />
                </Link>
              </Button>
            ) : null}
          </CardContent>
        </Card>
      </aside>
    </div>
  )
}

function ConfigurationCard({
  icon,
  iconClassName,
  title,
  children,
}: {
  icon: ReactNode
  iconClassName: string
  title: string
  children: ReactNode
}) {
  return (
    <div className="rounded-xl border bg-card p-4 shadow-sm">
      <div className="mb-4 flex items-center gap-3">
        <div className={cn("rounded-lg p-2", iconClassName)}>{icon}</div>
        <h2 className="font-medium">{title}</h2>
      </div>
      <dl className="space-y-3">{children}</dl>
    </div>
  )
}

function ConfigurationValue({
  label,
  value,
  state,
  tone,
  mono = false,
  copyable = false,
}: {
  label: string
  value: string
  state?: string
  tone?: ConfigurationTone
  mono?: boolean
  copyable?: boolean
}) {
  const { t } = useI18n()

  return (
    <div className="grid gap-1 sm:grid-cols-[minmax(0,0.9fr)_minmax(0,1.1fr)] sm:gap-3">
      <dt className="text-sm text-muted-foreground">{label}</dt>
      <dd className="min-w-0">
        <div className="flex min-w-0 items-start gap-1.5">
          <span className={cn("min-w-0 break-words text-sm", mono && "font-mono text-xs")}>{value}</span>
          {copyable ? (
            <Button
              variant="ghost"
              size="icon-xs"
              className="shrink-0"
              onClick={() => void copyText(value)}
              aria-label={t("restoreWorkspace.configuration.copyValue", { label })}
            >
              <Clipboard className="h-3 w-3" />
            </Button>
          ) : null}
        </div>
        {state ? <div className="mt-1 text-xs text-muted-foreground">{state}</div> : null}
        {tone ? <ToneBadge tone={tone} /> : null}
      </dd>
    </div>
  )
}

function ToneBadge({ tone }: { tone: ConfigurationTone }) {
  const { t } = useI18n()
  const labels: Readonly<Record<ConfigurationTone, TranslationKey>> = {
    success: "restoreWorkspace.configuration.tone.passed",
    warning: "restoreWorkspace.configuration.tone.attention",
    error: "restoreWorkspace.configuration.tone.failed",
    information: "restoreWorkspace.configuration.tone.recorded",
    protected: "restoreWorkspace.configuration.tone.protected",
  }

  const classes: Readonly<Record<ConfigurationTone, string>> = {
    success: "border-primary/35 bg-primary/10 text-primary",
    warning: "border-amber-500/35 bg-amber-500/10 text-amber-700 dark:text-amber-300",
    error: "border-destructive/35 bg-destructive/10 text-destructive",
    information: "border-blue-500/35 bg-blue-500/10 text-blue-700 dark:text-blue-300",
    protected: "border-violet-500/35 bg-violet-500/10 text-violet-700 dark:text-violet-300",
  }

  return <Badge variant="outline" className={cn("mt-1.5", classes[tone])}>{t(labels[tone])}</Badge>
}

function VerificationCheckRow({ check }: { check: RestoreWorkspaceVerificationCheck }) {
  const { t } = useI18n()
  const tone = statusTone(check.status)

  return (
    <li className="flex min-w-0 items-start gap-3 rounded-lg border bg-muted/20 p-3">
      {tone === "error" ? <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0 text-destructive" /> :
        tone === "warning" ? <TriangleAlert className="mt-0.5 h-4 w-4 shrink-0 text-amber-500" /> :
          <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-primary" />}
      <div className="min-w-0">
        <div className="text-sm font-medium">{check.title}</div>
        <div className="mt-1 flex flex-wrap items-center gap-2">
          <span className="font-mono text-xs text-muted-foreground">{check.code}</span>
          <span className="text-xs text-muted-foreground">{presentIdentifier(check.status, t)}</span>
        </div>
      </div>
    </li>
  )
}

function TargetClaimRow({ claim }: { claim: RestoreWorkspaceTargetClaim }) {
  const { intlLocale, t } = useI18n()

  return (
    <div className="grid gap-3 rounded-lg border bg-muted/20 p-3 md:grid-cols-[minmax(0,1fr)_auto] md:items-center">
      <div className="min-w-0">
        <div className="flex flex-wrap items-center gap-2">
          <span className="font-medium">{presentIdentifier(claim.resourceType, t)}</span>
          <ToneBadge tone={statusTone(claim.status)} />
        </div>
        <div className="mt-1 break-all font-mono text-xs text-muted-foreground">{claim.resourceValue}</div>
      </div>
      <div className="text-xs text-muted-foreground md:text-right">
        <div>{t("restoreWorkspace.configuration.claimedAt", { date: formatUtc(claim.claimedAtUtc, intlLocale, t) })}</div>
        {claim.releasedAtUtc ? (
          <div className="mt-1">{t("restoreWorkspace.configuration.releasedAt", { date: formatUtc(claim.releasedAtUtc, intlLocale, t) })}</div>
        ) : null}
        {claim.releaseReason ? <div className="mt-1">{presentIdentifier(claim.releaseReason, t)}</div> : null}
      </div>
    </div>
  )
}

function SideDetail({
  label,
  value,
  copyable = false,
}: {
  label: string
  value: string
  copyable?: boolean
}) {
  const { t } = useI18n()

  return (
    <div className="min-w-0">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="mt-1 flex min-w-0 items-start gap-1.5">
        <span className="min-w-0 break-all font-mono text-xs">{value}</span>
        {copyable ? (
          <Button
            variant="ghost"
            size="icon-xs"
            className="shrink-0"
            onClick={() => void copyText(value)}
            aria-label={t("restoreWorkspace.configuration.copyValue", { label })}
          >
            <Clipboard className="h-3 w-3" />
          </Button>
        ) : null}
      </dd>
    </div>
  )
}

function resolveRestoreOperation(workspace: RestoreWorkspaceResponse): RestoreWorkspaceOperationSummary | null {
  const currentOperationId = workspace.attempt.currentOperationId
  const operations = workspace.standardStages
    .map((stage) => stage.operationSummary)
    .filter((operation): operation is RestoreWorkspaceOperationSummary => Boolean(operation))

  return operations.find((operation) => operation.operationId === currentOperationId)
    ?? operations.at(-1)
    ?? null
}

function buildSafeConfigurationSnapshot({
  restoreSessionId,
  workspace,
  operation,
  evidenceRecordCount,
}: {
  restoreSessionId: string
  workspace: RestoreWorkspaceResponse
  operation: RestoreWorkspaceOperationSummary | null
  evidenceRecordCount: number
}) {
  return {
    schemaVersion: 1,
    generatedAtUtc: new Date().toISOString(),
    scope: "Safe read-only restore configuration summary",
    restoreSessionId,
    source: {
      kind: workspace.source.kind,
      stackSlug: workspace.source.stackSlug,
      backupId: workspace.source.backupId,
      matrixHost: workspace.source.matrixHost,
      elementHost: workspace.source.elementHost,
      validationStatus: workspace.source.validationStatus,
      catalogEntryId: workspace.source.catalogEntryId,
      sourceDisplayName: workspace.source.sourceDisplayName,
      sourceOriginKind: workspace.source.sourceOriginKind,
      sourceDeleted: workspace.source.sourceDeleted,
    },
    target: {
      stackSlug: workspace.target.stackSlug,
      matrixHost: workspace.target.matrixHost,
      elementHost: workspace.target.elementHost,
      availability: workspace.target.availability,
      claims: workspace.target.claims.map((claim) => ({
        resourceType: claim.resourceType,
        resourceValue: claim.resourceValue,
        status: claim.status,
        claimedAtUtc: claim.claimedAtUtc,
        releasedAtUtc: claim.releasedAtUtc,
        releaseReason: claim.releaseReason,
      })),
    },
    restoreOperation: operation ? {
      operationId: operation.operationId,
      operation: operation.operation,
      status: operation.status,
      currentStep: operation.currentStep,
      requestedAtUtc: operation.requestedAtUtc,
      startedAtUtc: operation.startedAtUtc,
      completedAtUtc: operation.completedAtUtc,
    } : null,
    verification: {
      status: workspace.verification.status,
      hasRun: workspace.verification.hasRun,
      allPassed: workspace.verification.allPassed,
      checkedAtUtc: workspace.verification.checkedAtUtc,
      checks: workspace.verification.checks.map((check) => ({
        code: check.code,
        title: check.title,
        status: check.status,
      })),
    },
    auditSummary: {
      evidenceCategories: workspace.evidence.categories.length,
      evidenceRecords: evidenceRecordCount,
      logEvents: workspace.logs.totalEvents,
      warningCount: workspace.logs.warningCount,
      errorCount: workspace.logs.errorCount,
    },
    exclusions: [
      "passwords",
      "tokens",
      "private keys",
      "raw configuration files",
      "raw connection strings",
      "unbounded host output",
    ],
  }
}

function statusTone(value: string | null | undefined): ConfigurationTone {
  const normalized = (value ?? "").trim().toLowerCase()

  if (normalized.includes("fail") || normalized.includes("error") || normalized.includes("blocked")) return "error"
  if (normalized.includes("warn") || normalized.includes("attention") || normalized.includes("pending")) return "warning"
  if (normalized.includes("pass") || normalized.includes("success") || normalized.includes("complete") || normalized.includes("released") || normalized.includes("active") || normalized.includes("configured")) return "success"
  if (normalized.includes("hidden") || normalized.includes("protected")) return "protected"
  return "information"
}

function presentIdentifier(
  value: string | null | undefined,
  t: I18nContextValue["t"],
) {
  if (!value) return t("restoreWorkspace.configuration.notRecorded")

  const translationKey = configurationIdentifierKeys[value.trim().toLowerCase()]
  return translationKey ? t(translationKey) : value
}

function formatUtc(
  value: string | null | undefined,
  intlLocale: string,
  t: I18nContextValue["t"],
) {
  if (!value) return t("restoreWorkspace.configuration.notRecorded")

  const date = new Date(value)
  if (Number.isNaN(date.valueOf())) return t("restoreWorkspace.configuration.notRecorded")

  return `${new Intl.DateTimeFormat(intlLocale, {
    timeZone: "UTC",
    year: "numeric",
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  }).format(date)} UTC`
}

async function copyText(value: string) {
  try {
    await navigator.clipboard.writeText(value)
  } catch {
    // Clipboard access is intentionally best effort; the data stays visible.
  }
}

function downloadJsonFile(name: string, value: unknown) {
  const content = JSON.stringify(value, null, 2)
  const blob = new Blob([content], { type: "application/json;charset=utf-8" })
  const url = URL.createObjectURL(blob)
  const link = document.createElement("a")

  link.href = url
  link.download = name
  link.click()
  URL.revokeObjectURL(url)
}
