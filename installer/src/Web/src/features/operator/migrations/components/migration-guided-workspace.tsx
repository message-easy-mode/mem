import "./migration-target.css"
import { formatMigrationDateTime, migrationDisplayTimeZone, migrationUtcIso } from "./migration-time"
import { useEffect, useMemo, useState, type ReactNode } from "react"
import {
  AlertTriangle,
  ArrowRight,
  CheckCircle2,
  Clock3,
  CircleDot,
  Loader2,
  ShieldAlert,
} from "lucide-react"
import { useSearchParams } from "react-router-dom"

import { useI18n } from "@/app/i18n/i18n-context"
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import type {
  MigrationWorkspaceResponse,
  MigrationWorkspaceStage,
} from "@/features/operator/migrations/api/migration-workspace"
import {
  actionKey,
  activityKey,
  evidenceKey,
  initialOpenMigrationStage,
  operationSummaryKey,
  stageDescriptionKey,
  stageStateLabelKey,
  stageTitleKey,
  summaryKey,
} from "@/features/operator/migrations/components/migration-workspace-ui"
import { cn } from "@/lib/utils"

const tabValues = ["guide", "activity", "evidence", "advanced"] as const
type WorkspaceTab = (typeof tabValues)[number]

const tabLabelKeys = {
  guide: "migrationWorkspace.tabs.guide",
  activity: "migrationWorkspace.tabs.activity",
  evidence: "migrationWorkspace.tabs.evidence",
  advanced: "migrationWorkspace.tabs.advanced",
} as const

export function MigrationGuidedWorkspace({
  workspace,
  renderStage,
  evidenceDetails,
  advancedContent,
}: {
  workspace: MigrationWorkspaceResponse
  renderStage: (stage: MigrationWorkspaceStage) => ReactNode
  evidenceDetails: ReactNode
  advancedContent: ReactNode
}) {
  const { intlLocale, t } = useI18n()
  const [searchParams, setSearchParams] = useSearchParams()
  const selectedTab = getTab(searchParams.get("tab"))
  const initialStage = useMemo(
    () => initialOpenMigrationStage(workspace.stages),
    [workspace.overallStatus.currentStageCode, workspace.migration.updatedAtUtc, workspace.stages],
  )
  const [openStage, setOpenStage] = useState(initialStage)

  useEffect(() => {
    setOpenStage(workspace.overallStatus.currentStageCode)
  }, [workspace.overallStatus.currentStageCode])

  const setTab = (tab: WorkspaceTab) => {
    const next = new URLSearchParams(searchParams)
    if (tab === "guide") next.delete("tab")
    else next.set("tab", tab)
    setSearchParams(next)
  }

  return (
    <div className="space-y-5">
      <MigrationJourneyHeader workspace={workspace} />

      <div className="border-b border-border/80">
        <div
          className="flex gap-6 overflow-x-auto"
          role="tablist"
          aria-label={t("migrationWorkspace.tabs.aria")}
        >
          {tabValues.map((tab) => (
            <button
              key={tab}
              type="button"
              role="tab"
              aria-selected={selectedTab === tab}
              onClick={() => setTab(tab)}
              className={cn(
                "border-b-2 px-1 py-3 text-sm font-medium whitespace-nowrap transition-colors",
                selectedTab === tab
                  ? "border-primary text-primary"
                  : "border-transparent text-muted-foreground hover:text-foreground",
              )}
            >
              {t(tabLabelKeys[tab])}
            </button>
          ))}
        </div>
      </div>

      {selectedTab === "guide" ? (
        <div className="space-y-3">
          {workspace.stages.map((stage, index) => {
            const isOpen = openStage === stage.code
            return (
              <section
                key={stage.code}
                className={cn(
                  "overflow-hidden rounded-xl border bg-card",
                  stage.state === "failed" && "border-destructive/45",
                  stage.state === "blocked" && "border-amber-500/45",
                )}
              >
                <button
                  type="button"
                  className="flex w-full items-start gap-4 p-4 text-left sm:p-5"
                  aria-expanded={isOpen}
                  aria-controls={`migration-stage-${stage.code}`}
                  disabled={!stage.unlocked}
                  onClick={() => setOpenStage(isOpen ? "" : stage.code)}
                >
                  <StageNumber index={index} state={stage.state} />
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2">
                      <h2 className="font-semibold">{t(stageTitleKey(stage.code))}</h2>
                      <Badge variant={badgeVariant(stage.state)}>
                        {t(stageStateLabelKey(stage.state))}
                      </Badge>
                    </div>
                    {isOpen && stage.state !== "completed" && !needsStageNotice(stage) ? (
                      <p className="mt-1 text-sm leading-6 text-muted-foreground">
                        {t(summaryKey(stage.summaryCode))}
                      </p>
                    ) : !isOpen && stage.state !== "completed" ? (
                      <p className="mt-1 text-sm text-muted-foreground">{t(stageDescriptionKey(stage.code))}</p>
                    ) : null}
                    {isOpen && stage.primaryAction && !(stage.code === "finish-migration" && stage.state === "completed") ? (
                      <p className="mt-2 text-sm font-medium text-foreground">
                        {t("migrationWorkspace.guide.nextAction")}:{" "}
                        {t(actionKey(stage.primaryAction.code))}
                      </p>
                    ) : null}
                  </div>
                  <ArrowRight
                    className={cn(
                      "mt-1 h-4 w-4 shrink-0 text-muted-foreground transition-transform",
                      isOpen && "rotate-90",
                    )}
                    aria-hidden="true"
                  />
                </button>

                {isOpen ? (
                  <div
                    id={`migration-stage-${stage.code}`}
                    className="space-y-4 border-t bg-muted/15 p-4 sm:p-5"
                  >
                    {needsStageNotice(stage) ? <StageSummary stage={stage} /> : null}
                    {stage.operationSummary ? (
                      <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
                        {stage.state === "running" ? (
                          <Loader2 className="h-4 w-4 animate-spin text-primary" aria-hidden="true" />
                        ) : (
                          <Clock3 className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
                        )}
                        <span className="font-medium">
                          {t("migrationWorkspace.guide.operation")}
                        </span>
                        <span className="text-muted-foreground">
                          {t(operationSummaryKey(stage.operationSummary))}
                        </span>
                      </div>
                    ) : null}
                    <div className="migration-stage-content">{renderStage(stage)}</div>
                  </div>
                ) : null}
              </section>
            )
          })}
        </div>
      ) : selectedTab === "activity" ? (
        <Card>
          <CardHeader>
            <CardTitle>{t("migrationWorkspace.activity.title")}</CardTitle>
            <p className="text-sm text-muted-foreground">
              {t("migrationWorkspace.activity.description")}
            </p>
          </CardHeader>
          <CardContent>
            {workspace.activity.length === 0 ? (
              <p className="text-sm text-muted-foreground">
                {t("migrationWorkspace.activity.empty")}
              </p>
            ) : (
              <ol className="space-y-4">
                {[...workspace.activity].reverse().map((item) => (
                  <li key={`${item.code}-${item.occurredAtUtc}`} className="flex gap-3">
                    <div className="mt-1 h-2.5 w-2.5 shrink-0 rounded-full bg-primary" />
                    <div>
                      <p className="text-sm font-medium">{t(activityKey(item))}</p>
                      <p className="mt-1 text-xs text-muted-foreground">
                        <time dateTime={migrationUtcIso(item.occurredAtUtc) ?? undefined} title={item.occurredAtUtc}>
                          {formatDate(item.occurredAtUtc, intlLocale)}
                        </time> · {item.status}
                      </p>
                    </div>
                  </li>
                ))}
              </ol>
            )}
          </CardContent>
        </Card>
      ) : selectedTab === "evidence" ? (
        <div className="space-y-5">
          <Card>
            <CardHeader>
              <CardTitle>{t("migrationWorkspace.evidence.title")}</CardTitle>
              <p className="text-sm text-muted-foreground">
                {t("migrationWorkspace.evidence.description")}
              </p>
            </CardHeader>
            <CardContent className="grid gap-3 md:grid-cols-2">
              {workspace.evidence.categories.map((category) => (
                <div key={category.code} className="rounded-lg border p-4">
                  <div className="flex items-center justify-between gap-3">
                    <span className="font-medium">{t(evidenceKey(category))}</span>
                    <Badge variant="outline">{category.status}</Badge>
                  </div>
                  <p className="mt-2 text-sm text-muted-foreground">
                    {t("migrationWorkspace.evidence.itemCount", { count: category.itemCount })}
                  </p>
                  {category.latestOccurredAtUtc ? (
                    <p className="mt-1 text-xs text-muted-foreground">
                      {migrationUtcIso(category.latestOccurredAtUtc) ?? "—"}
                    </p>
                  ) : null}
                </div>
              ))}
            </CardContent>
          </Card>
          {evidenceDetails}
        </div>
      ) : (
        <div className="space-y-5">
          <Alert>
            <ShieldAlert className="h-4 w-4" />
            <AlertTitle>{t("migrationWorkspace.advanced.title")}</AlertTitle>
            <AlertDescription>{t("migrationWorkspace.advanced.description")}</AlertDescription>
          </Alert>
          {advancedContent}
        </div>
      )}
    </div>
  )
}

function MigrationJourneyHeader({ workspace }: { workspace: MigrationWorkspaceResponse }) {
  const { t } = useI18n()

  return (
    <Card className="min-w-0 py-0">
      <CardContent className="space-y-3 p-4">
        <div className="grid min-w-0 gap-3 sm:grid-cols-[minmax(0,1fr)_auto_minmax(0,1fr)] sm:items-center">
          <div className="min-w-0">
            <div className="text-xs font-medium text-muted-foreground">{t("migrationWorkspace.header.oldServer")}</div>
            <div className="mt-1 break-words font-medium">{workspace.source.displayName}</div>
            <div className="mt-1 text-xs text-muted-foreground [overflow-wrap:anywhere]">
              {workspace.source.matrixServerName ?? workspace.source.productVersion ?? t("migrationWorkspace.notAvailable")}
            </div>
          </div>
          <ArrowRight className="hidden h-4 w-4 text-muted-foreground sm:block" aria-hidden="true" />
          <div className="min-w-0">
            <div className="text-xs font-medium text-muted-foreground">{t("migrationWorkspace.header.newServer")}</div>
            <div className="mt-1 break-words font-medium">
              {workspace.target.displayName ?? t("migrationWorkspace.header.notCreated")}
            </div>
            <div className="mt-1 text-xs text-muted-foreground [overflow-wrap:anywhere]">
              {workspace.target.matrixHost ?? t("migrationWorkspace.header.privateUntilLive")}
            </div>
          </div>
        </div>
        <p className="border-t pt-3 text-sm">
          <span className="text-muted-foreground">{t("migrationWorkspace.header.current")}: </span>
          <span className="font-medium">{t(stageTitleKey(workspace.overallStatus.currentStageCode))}</span>
        </p>
        <p className="text-xs text-muted-foreground">
          {t("migrationWorkspace.time.browserZone", { zone: migrationDisplayTimeZone() })}
        </p>
      </CardContent>
    </Card>
  )
}

function needsStageNotice(stage: MigrationWorkspaceStage): boolean {
  return Boolean(stage.failureOutcome) || stage.problems.length > 0 ||
    !["ready", "running", "completed", "not-started"].includes(stage.state)
}

function StageSummary({ stage }: { stage: MigrationWorkspaceStage }) {
  const { t } = useI18n()
  const isFailure = stage.state === "failed" || Boolean(stage.failureOutcome)
  const isWarning = ["blocked", "action-required"].includes(stage.state)

  return (
    <Alert variant={isFailure ? "destructive" : "default"}>
      {isFailure ? (
        <AlertTriangle className="h-4 w-4" />
      ) : stage.state === "completed" ? (
        <CheckCircle2 className="h-4 w-4" />
      ) : (
        <CircleDot className="h-4 w-4 shrink-0" />
      )}
      <AlertTitle>
        {isFailure
          ? t("migrationWorkspace.guide.failureTitle")
          : isWarning
            ? t("migrationWorkspace.guide.attentionTitle")
            : t("migrationWorkspace.guide.stageSummary")}
      </AlertTitle>
      <AlertDescription className="space-y-2">
        <p>{t(summaryKey(stage.summaryCode))}</p>
        {stage.failureOutcome ? (
          <>
            <p>
              {t("migrationWorkspace.guide.failureMutation")}:{" "}
              {stage.failureOutcome.mutationState}
            </p>
            <p>
              {t("migrationWorkspace.guide.failureCompensation")}:{" "}
              {stage.failureOutcome.compensationState}
            </p>
            <p>
              {t("migrationWorkspace.guide.failureCurrentState")}:{" "}
              {stage.failureOutcome.observedCurrentState}
            </p>
          </>
        ) : null}
        {stage.problems.map((problem) => (
          <p key={problem.code}>
            {problem.detail ?? problem.code}
          </p>
        ))}
      </AlertDescription>
    </Alert>
  )
}

function StageNumber({ index, state }: { index: number; state: string }) {
  if (state === "completed") {
    return (
      <span className="mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-primary/15 text-primary leading-none">
        <CheckCircle2 className="h-5 w-5 shrink-0" aria-hidden="true" />
      </span>
    )
  }

  return (
    <span
      className={cn(
        "mt-0.5 flex h-9 w-9 shrink-0 items-center justify-center rounded-full border text-sm font-semibold leading-none",
        ["ready", "running", "action-required"].includes(state)
          ? "border-primary text-primary"
          : state === "failed"
            ? "border-destructive text-destructive"
            : "border-border text-muted-foreground",
      )}
    >
      {state === "running" ? (
        <Loader2 className="h-5 w-5 shrink-0 animate-spin" aria-hidden="true" />
      ) : (
        <span className="leading-none">{index + 1}</span>
      )}
    </span>
  )
}

function badgeVariant(state: string) {
  if (state === "failed") return "destructive" as const
  if (state === "completed") return "default" as const
  return "outline" as const
}

function getTab(value: string | null): WorkspaceTab {
  return tabValues.includes(value as WorkspaceTab) ? value as WorkspaceTab : "guide"
}

function formatDate(value: string, locale: string) {
  return formatMigrationDateTime(value, locale)
}
