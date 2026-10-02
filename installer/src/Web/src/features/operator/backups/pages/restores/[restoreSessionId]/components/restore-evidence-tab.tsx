import { useState, type ReactNode } from "react"
import {
  CheckCircle2,
  ChevronDown,
  ChevronRight,
  Clipboard,
  Download,
  FileCheck2,
  FileText,
  FlaskConical,
  Flag,
  HeartPulse,
  Info,
  LoaderCircle,
  ShieldCheck,
  TriangleAlert,
} from "lucide-react"

import { useI18n, type I18nContextValue } from "@/app/i18n/i18n-context"
import type { TranslationKey } from "@/app/i18n/messages"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { cn } from "@/lib/utils"
import type {
  RestoreWorkspaceEvidenceCategory,
  RestoreWorkspaceEvidenceItem,
  RestoreWorkspaceResponse,
} from "@/features/operator/backups/api/types/restore-workspace.types"
import type { RestoreSupportReportGenerationResult } from "@/features/operator/backups/api/types/restore-logs.types"
import { useGenerateRestoreSupportReport } from "@/features/operator/backups/hooks/use-restore-workspace"

import {
  getRestoreWorkspaceTechnicalDetail,
  RestoreWorkspaceTechnicalDetails,
} from "./restore-workspace-problems"

const evidenceStatusKeys: Readonly<Record<string, TranslationKey>> = {
  information: "restoreWorkspace.evidence.status.information",
  info: "restoreWorkspace.evidence.status.information",
  warning: "restoreWorkspace.evidence.status.warning",
  warn: "restoreWorkspace.evidence.status.warning",
  failed: "restoreWorkspace.evidence.status.failed",
  error: "restoreWorkspace.evidence.status.failed",
  success: "restoreWorkspace.evidence.status.success",
  succeeded: "restoreWorkspace.evidence.status.success",
  passed: "restoreWorkspace.evidence.status.success",
  completed: "restoreWorkspace.evidence.status.success",
}

type RestoreEvidenceTabProps = {
  restoreSessionId: string
  workspace: RestoreWorkspaceResponse
  onViewLogs: () => void
}

export function RestoreEvidenceTab({
  restoreSessionId,
  workspace,
  onViewLogs,
}: RestoreEvidenceTabProps) {
  const { intlLocale, t } = useI18n()
  const categories = workspace.evidence.categories ?? []
  const totalRecords = categories.reduce(
    (count, category) => count + category.itemCount,
    0,
  )
  const latestRecord = workspace.evidence.latestFailure ?? workspace.evidence.latestSuccess
  const [expandedCategoryCode, setExpandedCategoryCode] = useState<string | null>(null)
  const [generatedReport, setGeneratedReport] =
    useState<RestoreSupportReportGenerationResult | null>(null)
  const supportReportMutation = useGenerateRestoreSupportReport()
  const supportReportTechnicalDetail = supportReportMutation.isError
    ? getRestoreWorkspaceTechnicalDetail(supportReportMutation.error)
    : undefined

  const toggleCategory = (categoryCode: string) => {
    setExpandedCategoryCode((current) =>
      current === categoryCode ? null : categoryCode,
    )
  }

  const generateSupportReport = async () => {
    try {
      const result = await supportReportMutation.mutateAsync(restoreSessionId)
      setGeneratedReport(result)
    } catch {
      // The mutation state renders the operator-facing error message.
    }
  }

  return (
    <div className="grid gap-5 xl:grid-cols-[minmax(0,1fr)_300px]">
      <section className="space-y-4">
        <Card size="sm" className="border-primary/20 bg-primary/5">
          <CardContent className="flex flex-col gap-3 py-3 sm:flex-row sm:items-center sm:justify-between">
            <div className="flex min-w-0 items-start gap-3">
              <div className="mt-0.5 rounded-lg bg-primary/15 p-2 text-primary">
                <FileCheck2 className="h-4 w-4" aria-hidden="true" />
              </div>
              <div className="min-w-0">
                <div className="font-medium">{t("restoreWorkspace.evidence.summary")}</div>
                <p className="mt-0.5 text-sm text-muted-foreground">
                  {t("restoreWorkspace.evidence.summary.recordsAvailable", { count: totalRecords })}
                </p>
              </div>
            </div>
            {latestRecord ? (
              <div className="text-sm sm:max-w-xs sm:text-right">
                <div className="text-muted-foreground">{t("restoreWorkspace.evidence.latestOutcome")}</div>
                <div className="mt-0.5 flex items-center gap-1.5 font-medium sm:justify-end">
                  <EvidenceStatusIcon status={latestRecord.status} className="h-4 w-4" />
                  <span className="truncate">{latestRecord.title}</span>
                </div>
              </div>
            ) : null}
          </CardContent>
        </Card>

        <div>
          <h2 className="text-lg font-semibold tracking-tight">{t("restoreWorkspace.evidence.records")}</h2>
          <p className="mt-1 text-sm text-muted-foreground">
            {t("restoreWorkspace.evidence.recordsDescription")}
          </p>
        </div>

        {categories.length > 0 ? (
          <ol className="relative space-y-2 before:absolute before:bottom-5 before:left-4 before:top-5 before:w-px before:bg-border">
            {categories.map((category, index) => {
              const expanded = expandedCategoryCode === category.code
              const latestItem = category.items.at(0) ?? null

              return (
                <li key={category.code} className="relative pl-10">
                  <div
                    className={cn(
                      "absolute left-0 top-5 z-10 flex h-8 w-8 items-center justify-center rounded-full border border-background",
                      evidenceStatusClasses(category.status).marker,
                    )}
                    aria-hidden="true"
                  >
                    <EvidenceStatusIcon status={category.status} className="h-4 w-4" />
                  </div>

                  <div className="overflow-hidden rounded-xl border bg-card shadow-sm">
                    <button
                      type="button"
                      className="flex w-full items-center gap-3 p-4 text-left transition-colors hover:bg-muted/50 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-inset"
                      onClick={() => toggleCategory(category.code)}
                      aria-expanded={expanded}
                    >
                      <CategoryGlyph categoryCode={category.code} />

                      <div className="min-w-0 flex-1">
                        <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
                          <span className="font-medium">
                            {index + 1}. {category.title}
                          </span>
                          <EvidenceStatusBadge status={category.status} />
                          <span className="text-sm text-muted-foreground">
                            {t("restoreWorkspace.evidence.category.recordCount", { count: category.itemCount })}
                          </span>
                        </div>
                        <p className="mt-1 line-clamp-2 text-sm text-muted-foreground">
                          {latestItem?.description ?? t("restoreWorkspace.evidence.category.noItemSummary")}
                        </p>
                      </div>

                      <div className="hidden shrink-0 text-right text-sm text-muted-foreground lg:block">
                        <div>{formatUtc(category.latestOccurredAtUtc, intlLocale, t)}</div>
                        <div className="mt-1 text-xs">{t("restoreWorkspace.evidence.utc")}</div>
                      </div>

                      <span className="flex shrink-0 items-center gap-1 text-sm font-medium text-primary">
                        {expanded
                          ? t("restoreWorkspace.evidence.hideCategory")
                          : t("restoreWorkspace.evidence.viewCategory")}
                        {expanded ? (
                          <ChevronDown className="h-4 w-4" aria-hidden="true" />
                        ) : (
                          <ChevronRight className="h-4 w-4" aria-hidden="true" />
                        )}
                      </span>
                    </button>

                    {expanded ? (
                      <div className="border-t bg-muted/20 px-4 py-3">
                        <EvidenceCategoryDetails
                          category={category}
                          onViewLogs={onViewLogs}
                        />
                      </div>
                    ) : null}
                  </div>
                </li>
              )
            })}
          </ol>
        ) : (
          <EmptyEvidenceState />
        )}

        <Alert>
          <Info className="h-4 w-4" />
          <AlertTitle>{t("restoreWorkspace.evidence.timestampsTitle")}</AlertTitle>
          <AlertDescription>
            {t("restoreWorkspace.evidence.timestampsDescription")}
          </AlertDescription>
        </Alert>

        <Card className="border-primary/20 bg-primary/5">
          <CardContent className="flex flex-col gap-4 py-4 md:flex-row md:items-center md:justify-between">
            <div>
              <div className="font-medium">{t("restoreWorkspace.evidence.support.title")}</div>
              <p className="mt-1 max-w-2xl text-sm text-muted-foreground">
                {t("restoreWorkspace.evidence.support.description")}
              </p>
            </div>
            <SupportReportActions
              generatedReport={generatedReport}
              isPending={supportReportMutation.isPending}
              onGenerate={() => void generateSupportReport()}
            />
          </CardContent>
        </Card>
      </section>

      <aside className="space-y-4 xl:sticky xl:top-6 xl:self-start">
        <Card>
          <CardHeader>
            <CardTitle className="flex items-center gap-2">
              <Info className="h-4 w-4 text-blue-500" />
              {t("restoreWorkspace.evidence.about.title")}
            </CardTitle>
          </CardHeader>
          <CardContent className="space-y-4 text-sm">
            <p className="text-muted-foreground">
              {t("restoreWorkspace.evidence.about.description")}
            </p>
            <dl className="space-y-3">
              <EvidenceDetail label={t("restoreWorkspace.evidence.detail.restoreSession")} value={restoreSessionId} copyable />
              <EvidenceDetail label={t("restoreWorkspace.evidence.detail.categories")} value={String(categories.length)} />
              <EvidenceDetail label={t("restoreWorkspace.evidence.detail.records")} value={String(totalRecords)} />
              <EvidenceDetail
                label={t("restoreWorkspace.evidence.detail.latestRecord")}
                value={latestRecord
                  ? `${formatUtc(latestRecord.occurredAtUtc, intlLocale, t)} ${t("restoreWorkspace.evidence.utc")}`
                  : t("restoreWorkspace.evidence.noEvidenceYet")}
              />
            </dl>
            <p className="border-t pt-3 text-xs text-muted-foreground">
              {t("restoreWorkspace.evidence.about.apiLimitation")}
            </p>
          </CardContent>
        </Card>

        <Card>
          <CardHeader>
            <CardTitle>{t("restoreWorkspace.evidence.support.reportTitle")}</CardTitle>
          </CardHeader>
          <CardContent className="space-y-3">
            <p className="text-sm text-muted-foreground">
              {t("restoreWorkspace.evidence.support.reportDescription")}
            </p>
            <SupportReportActions
              generatedReport={generatedReport}
              isPending={supportReportMutation.isPending}
              onGenerate={() => void generateSupportReport()}
              compact
            />
            {supportReportMutation.isError ? (
              <Alert variant="destructive">
                <TriangleAlert className="h-4 w-4" />
                <AlertTitle>{t("restoreWorkspace.evidence.support.errorTitle")}</AlertTitle>
                {supportReportTechnicalDetail ? (
                  <AlertDescription>
                    <RestoreWorkspaceTechnicalDetails detail={supportReportTechnicalDetail} />
                  </AlertDescription>
                ) : null}
              </Alert>
            ) : null}
          </CardContent>
        </Card>

        <Card size="sm">
          <CardContent className="py-3">
            <Button variant="outline" size="sm" className="w-full" onClick={onViewLogs}>
              <FileText className="mr-2 h-4 w-4" />
              {t("restoreWorkspace.evidence.openLogs")}
            </Button>
          </CardContent>
        </Card>
      </aside>
    </div>
  )
}

function EvidenceCategoryDetails({
  category,
  onViewLogs,
}: {
  category: RestoreWorkspaceEvidenceCategory
  onViewLogs: () => void
}) {
  const { t } = useI18n()

  if (category.items.length === 0) {
    return (
      <p className="text-sm text-muted-foreground">
        {t("restoreWorkspace.evidence.category.noItems")}
      </p>
    )
  }

  return (
    <div className="space-y-3">
      {category.items.map((item) => (
        <EvidenceRecord key={item.code} item={item} onViewLogs={onViewLogs} />
      ))}
    </div>
  )
}

function EvidenceRecord({
  item,
  onViewLogs,
}: {
  item: RestoreWorkspaceEvidenceItem
  onViewLogs: () => void
}) {
  const { intlLocale, t } = useI18n()

  return (
    <div className="rounded-lg border bg-background/70 p-3">
      <div className="flex flex-col gap-3 md:flex-row md:items-start md:justify-between">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <EvidenceStatusBadge status={item.status} />
            <span className="font-medium">{item.title}</span>
          </div>
          <p className="mt-2 text-sm text-muted-foreground">{item.description}</p>
        </div>
        <div className="shrink-0 text-sm text-muted-foreground">
          {formatUtc(item.occurredAtUtc, intlLocale, t)} {t("restoreWorkspace.evidence.utc")}
        </div>
      </div>

      <dl className="mt-3 grid gap-2 border-t pt-3 text-sm sm:grid-cols-2">
        <EvidenceDetail label={t("restoreWorkspace.evidence.detail.stage")} value={item.stage ?? "—"} />
        <EvidenceDetail label={t("restoreWorkspace.evidence.detail.eventCode")} value={item.eventCode ?? "—"} copyable={Boolean(item.eventCode)} />
        <EvidenceDetail label={t("restoreWorkspace.evidence.detail.operationId")} value={item.operationId ?? "—"} copyable={Boolean(item.operationId)} />
        <EvidenceDetail label={t("restoreWorkspace.evidence.detail.recordCode")} value={item.code} copyable />
      </dl>

      <div className="mt-3 flex justify-end">
        <Button variant="outline" size="sm" onClick={onViewLogs}>
          {t("restoreWorkspace.evidence.openLogs")}
          <ChevronRight className="ml-1 h-4 w-4" />
        </Button>
      </div>
    </div>
  )
}

function SupportReportActions({
  generatedReport,
  isPending,
  onGenerate,
  compact = false,
}: {
  generatedReport: RestoreSupportReportGenerationResult | null
  isPending: boolean
  onGenerate: () => void
  compact?: boolean
}) {
  const { t } = useI18n()
  const reportFileName = `mem-restore-${generatedReport?.restoreSessionId ?? "support-report"}-support-report.json`

  return (
    <div className={cn("flex flex-wrap gap-2", compact && "flex-col") }>
      <Button size="sm" onClick={onGenerate} disabled={isPending}>
        {isPending ? <LoaderCircle className="mr-2 h-4 w-4 animate-spin" /> : <FileCheck2 className="mr-2 h-4 w-4" />}
        {isPending
          ? t("restoreWorkspace.evidence.support.generating")
          : generatedReport
            ? t("restoreWorkspace.evidence.support.regenerate")
            : t("restoreWorkspace.evidence.support.generate")}
      </Button>
      {generatedReport ? (
        <Button
          variant="outline"
          size="sm"
          onClick={() => downloadJsonFile(reportFileName, generatedReport.report ?? null)}
        >
          <Download className="mr-2 h-4 w-4" />
          {t("restoreWorkspace.evidence.support.downloadJson")}
        </Button>
      ) : null}
    </div>
  )
}

function EvidenceDetail({
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
        {copyable && value !== "—" ? (
          <Button
            variant="ghost"
            size="icon-xs"
            className="shrink-0"
            onClick={() => void copyText(value)}
            aria-label={t("restoreWorkspace.evidence.copyValue", { label })}
          >
            <Clipboard className="h-3 w-3" />
          </Button>
        ) : null}
      </dd>
    </div>
  )
}

function EvidenceStatusBadge({ status }: { status: string | null | undefined }) {
  const { t } = useI18n()
  const classes = evidenceStatusClasses(status)

  return (
    <Badge variant="outline" className={classes.badge}>
      <EvidenceStatusIcon status={status} className="h-3 w-3" />
      {formatStatus(status, t)}
    </Badge>
  )
}

function EvidenceStatusIcon({
  status,
  className,
}: {
  status: string | null | undefined
  className?: string
}) {
  const normalized = normalizeStatus(status)

  if (normalized === "failed" || normalized === "error") {
    return <TriangleAlert className={cn("text-destructive", className)} aria-hidden="true" />
  }

  if (normalized === "warning" || normalized === "warn") {
    return <TriangleAlert className={cn("text-amber-500", className)} aria-hidden="true" />
  }

  if (normalized === "information" || normalized === "info") {
    return <Info className={cn("text-blue-500", className)} aria-hidden="true" />
  }

  return <CheckCircle2 className={cn("text-primary", className)} aria-hidden="true" />
}

function CategoryGlyph({ categoryCode }: { categoryCode: string }) {
  const normalized = categoryCode.toLowerCase()

  if (normalized.includes("validation")) {
    return <IconTile className="bg-sky-500/15 text-sky-600 dark:text-sky-300"><FileCheck2 className="h-5 w-5" /></IconTile>
  }

  if (normalized.includes("reservation") || normalized.includes("claim")) {
    return <IconTile className="bg-violet-500/15 text-violet-600 dark:text-violet-300"><ShieldCheck className="h-5 w-5" /></IconTile>
  }

  if (normalized.includes("private") || normalized.includes("test") || normalized.includes("lab")) {
    return <IconTile className="bg-fuchsia-500/15 text-fuchsia-600 dark:text-fuchsia-300"><FlaskConical className="h-5 w-5" /></IconTile>
  }

  if (normalized.includes("verification") || normalized.includes("doctor") || normalized.includes("health")) {
    return <IconTile className="bg-emerald-500/15 text-emerald-600 dark:text-emerald-300"><HeartPulse className="h-5 w-5" /></IconTile>
  }

  if (normalized.includes("completion") || normalized.includes("handover")) {
    return <IconTile className="bg-teal-500/15 text-teal-600 dark:text-teal-300"><Flag className="h-5 w-5" /></IconTile>
  }

  return <IconTile className="bg-blue-500/15 text-blue-600 dark:text-blue-300"><FileText className="h-5 w-5" /></IconTile>
}

function IconTile({
  className,
  children,
}: {
  className: string
  children: ReactNode
}) {
  return <div className={cn("hidden shrink-0 rounded-lg p-2.5 sm:block", className)}>{children}</div>
}

function EmptyEvidenceState() {
  const { t } = useI18n()

  return (
    <Card>
      <CardContent className="py-10 text-center">
        <FileText className="mx-auto h-9 w-9 text-muted-foreground" />
        <h2 className="mt-3 font-medium">{t("restoreWorkspace.evidence.empty.title")}</h2>
        <p className="mx-auto mt-1 max-w-lg text-sm text-muted-foreground">
          {t("restoreWorkspace.evidence.empty.description")}
        </p>
      </CardContent>
    </Card>
  )
}

function evidenceStatusClasses(status: string | null | undefined) {
  const normalized = normalizeStatus(status)

  if (normalized === "failed" || normalized === "error") {
    return {
      badge: "border-destructive/40 bg-destructive/10 text-destructive",
      marker: "bg-destructive/15 text-destructive",
    }
  }

  if (normalized === "warning" || normalized === "warn") {
    return {
      badge: "border-amber-500/40 bg-amber-500/10 text-amber-700 dark:text-amber-300",
      marker: "bg-amber-500/15 text-amber-600 dark:text-amber-300",
    }
  }

  if (normalized === "information" || normalized === "info") {
    return {
      badge: "border-blue-500/40 bg-blue-500/10 text-blue-700 dark:text-blue-300",
      marker: "bg-blue-500/15 text-blue-600 dark:text-blue-300",
    }
  }

  return {
    badge: "border-primary/40 bg-primary/10 text-primary",
    marker: "bg-primary/15 text-primary",
  }
}

function normalizeStatus(status: string | null | undefined) {
  return (status ?? "information").trim().toLowerCase()
}

function formatStatus(
  status: string | null | undefined,
  t: I18nContextValue["t"],
) {
  const normalized = normalizeStatus(status)
  const translationKey = evidenceStatusKeys[normalized]

  return translationKey ? t(translationKey) : status?.trim() || t("restoreWorkspace.evidence.status.information")
}

function formatUtc(
  value: string | null,
  intlLocale: string,
  t: I18nContextValue["t"],
) {
  if (!value) return t("restoreWorkspace.evidence.timeUnavailable")

  const date = new Date(value)
  if (Number.isNaN(date.valueOf())) return t("restoreWorkspace.evidence.timeUnavailable")

  return new Intl.DateTimeFormat(intlLocale, {
    timeZone: "UTC",
    year: "numeric",
    month: "short",
    day: "2-digit",
    hour: "2-digit",
    minute: "2-digit",
    hour12: false,
  })
    .format(date)
    .replace(",", "")
}

async function copyText(value: string) {
  try {
    await navigator.clipboard.writeText(value)
  } catch {
    // Clipboard access is deliberately best-effort. Evidence remains readable without it.
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
