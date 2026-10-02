import { useState } from "react";

import { useI18n, type I18nContextValue } from "@/app/i18n/i18n-context";
import { useQueryClient } from "@tanstack/react-query";
import { Link, Navigate, useParams, useSearchParams } from "react-router-dom";
import {
  AlertCircle,
  ArrowLeft,
  ArrowRight,
  CheckCircle2,
  CircleAlert,
  Clock3,
  FileArchive,
  FileCheck2,
  FileText,
  HelpCircle,
  LoaderCircle,
  RefreshCw,
  Server,
  ShieldCheck,
} from "lucide-react";

import { PageBreadcrumbs } from "@/components/layout/page-breadcrumbs";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import { cn } from "@/lib/utils";
import { restoreWorkspacePreflightKeys } from "@/features/operator/backups/hooks/use-backups";
import {
  useGenerateRestoreSupportReport,
  useRestoreWorkspace,
} from "@/features/operator/backups/hooks/use-restore-workspace";
import type { RestoreWorkspaceResponse } from "@/features/operator/backups/api/types/restore-workspace.types";
import type { HostAgentProblem } from "@/features/operator/backups/api/transport/host-agent";
import { StandardRestoreAccordion } from "./components/standard-restore-accordion";
import { RestoreLogsTab } from "./components/restore-logs-tab";
import { RestoreEvidenceTab } from "./components/restore-evidence-tab";
import { RestoreActivityTab } from "./components/restore-activity-tab";
import { RestoreConfigurationTab } from "./components/restore-configuration-tab";
import { RestoreWorkspaceCancelAction } from "./components/restore-workspace-cancel-action";
import {
  formatBytes,
  formatDate,
} from "@/features/operator/backups/shared/components/backup-formatting";
import {
  getRestoreWorkspaceProblem,
  getRestoreWorkspaceProblemMessage,
  RestoreWorkspaceTechnicalDetails,
} from "./components/restore-workspace-problems";

const workspaceTabValues = [
  "standard",
  "activity",
  "logs",
  "evidence",
  "configuration",
] as const;

type WorkspaceTab = (typeof workspaceTabValues)[number];

function getWorkspaceTabs(t: I18nContextValue["t"]) {
  return [
    { value: "standard" as const, label: t("restoreWorkspace.tabs.standard") },
    { value: "activity" as const, label: t("restoreWorkspace.tabs.activity") },
    { value: "logs" as const, label: t("restoreWorkspace.tabs.logs") },
    { value: "evidence" as const, label: t("restoreWorkspace.tabs.evidence") },
    {
      value: "configuration" as const,
      label: t("restoreWorkspace.tabs.configuration"),
    },
  ];
}

export function BackupRestoreWorkspaceRouteBoundary() {
  const { t } = useI18n();
  const { restoreSessionId } = useParams();
  const [searchParams, setSearchParams] = useSearchParams();
  const workspaceTabs = getWorkspaceTabs(t);
  const selectedTab = getWorkspaceTab(searchParams.get("tab"));
  const queryClient = useQueryClient();
  const workspaceQuery = useRestoreWorkspace(restoreSessionId);
  const generateSupportReport = useGenerateRestoreSupportReport();
  const [isRefreshing, setIsRefreshing] = useState(false);

  if (!restoreSessionId) {
    return <Navigate to="/restores" replace />;
  }

  const setTab = (tab: WorkspaceTab) => {
    const next = new URLSearchParams(searchParams);
    next.set("tab", tab);
    setSearchParams(next);
  };

  const refreshWorkspace = async () => {
    setIsRefreshing(true);

    try {
      // The workspace projection and Stage 3 preflight are separate read models.
      // Refreshing the page must re-check both; otherwise changed route ownership
      // can remain stale until the Step 3 accordion remounts.
      await workspaceQuery.refetch();
      await queryClient.invalidateQueries({
        queryKey: restoreWorkspacePreflightKeys.all,
      });
    } finally {
      setIsRefreshing(false);
    }
  };

  const generateReport = async () => {
    try {
      const result = await generateSupportReport.mutateAsync(restoreSessionId);
      downloadJsonFile(
        `mem-restore-${restoreSessionId}-support-report.json`,
        result.report ?? null,
      );
      await refreshWorkspace();
    } catch {
      // Mutation state renders the operator-facing error where the action was invoked.
    }
  };

  if (workspaceQuery.isLoading) return <RestoreWorkspaceLoadingState />;
  if (workspaceQuery.isError || !workspaceQuery.data) {
    const problem = getRestoreWorkspaceProblem(workspaceQuery.error);

    return (
      <RestoreWorkspaceErrorState
        restoreSessionId={restoreSessionId}
        problem={problem}
      />
    );
  }

  const workspace = workspaceQuery.data;
  const progressionClosed = ["cancelled", "abandoned", "superseded"].includes(
    workspace.attempt.status.trim().toLowerCase(),
  );
  const isFullWidthTab = [
    "logs",
    "evidence",
    "configuration",
    "activity",
  ].includes(selectedTab);

  return (
    <div className="space-y-6">
      <div className="flex flex-col gap-4 md:flex-row md:items-start md:justify-between">
        <div>
          <PageBreadcrumbs
            items={[
              {
                label: t("restoreWorkspace.breadcrumb.backups"),
                to: "/backups",
              },
              {
                label: t("restoreWorkspace.breadcrumb.restores"),
                to: "/restores",
              },
              { label: t("restoreWorkspace.breadcrumb.current") },
            ]}
          />
          <h1 className="text-2xl font-semibold tracking-tight">
            {t("restoreWorkspace.title")}
          </h1>
          <p className="mt-1 max-w-3xl text-sm leading-6 text-muted-foreground">
            {t("restoreWorkspace.description")}
            <span className="block text-foreground/90">
              {t("restoreWorkspace.safetyDescription")}
            </span>
          </p>
        </div>

        <div className="flex flex-wrap gap-2">
          <Button variant="outline" size="sm" asChild>
            <Link to="/restores">
              <ArrowLeft className="mr-2 h-4 w-4" />
              {t("restoreWorkspace.backToRestores")}
            </Link>
          </Button>
          <RestoreWorkspaceCancelAction
            workspace={workspace}
            onCancelled={refreshWorkspace}
          />
          <Button
            variant="outline"
            size="sm"
            onClick={() => void refreshWorkspace()}
            disabled={workspaceQuery.isFetching || isRefreshing}
          >
            {workspaceQuery.isFetching || isRefreshing ? (
              <LoaderCircle className="mr-2 h-4 w-4 animate-spin" />
            ) : (
              <RefreshCw className="mr-2 h-4 w-4" />
            )}
            {t("restoreWorkspace.refresh")}
          </Button>
        </div>
      </div>

      <div
        className={cn(
          "grid gap-5",
          isFullWidthTab ? "grid-cols-1" : "xl:grid-cols-[minmax(0,1fr)_17rem]",
        )}
      >
        <div className="space-y-5">
          <RestoreWorkspaceHeader
            workspace={workspace}
            progressionClosed={progressionClosed}
            onRestoreSteps={() => setTab("standard")}
          />

          <div className="border-b border-border/80">
            <div
              className="flex gap-6 overflow-x-auto"
              role="tablist"
              aria-label={t("restoreWorkspace.tablistAria")}
            >
              {workspaceTabs.map((tab) => (
                <button
                  key={tab.value}
                  type="button"
                  role="tab"
                  aria-selected={selectedTab === tab.value}
                  onClick={() => setTab(tab.value)}
                  className={cn(
                    "border-b-2 px-1 py-3 text-sm font-medium whitespace-nowrap transition-colors",
                    selectedTab === tab.value
                      ? "border-primary text-primary"
                      : "border-transparent text-muted-foreground hover:text-foreground",
                  )}
                >
                  {tab.label}
                </button>
              ))}
            </div>
          </div>

          {selectedTab === "standard" ? (
            <StandardRestoreAccordion
              key={`${workspace.attempt.currentStage}:${workspace.attempt.status}`}
              workspace={workspace}
              onViewEvidence={() => setTab("evidence")}
              onViewLogs={() => setTab("logs")}
              onWorkspaceChanged={refreshWorkspace}
            />
          ) : selectedTab === "evidence" ? (
            <RestoreEvidenceTab
              restoreSessionId={restoreSessionId}
              workspace={workspace}
              onViewLogs={() => setTab("logs")}
            />
          ) : selectedTab === "logs" ? (
            <RestoreLogsTab
              restoreSessionId={restoreSessionId}
              workspace={workspace}
            />
          ) : selectedTab === "configuration" ? (
            <RestoreConfigurationTab
              restoreSessionId={restoreSessionId}
              workspace={workspace}
              onViewEvidence={() => setTab("evidence")}
              onViewLogs={() => setTab("logs")}
            />
          ) : selectedTab === "activity" ? (
            <RestoreActivityTab
              restoreSessionId={restoreSessionId}
              workspace={workspace}
              onViewLogs={() => setTab("logs")}
            />
          ) : null}
        </div>

        {!isFullWidthTab ? (
          <RestoreWorkspaceRail
            workspace={workspace}
            onRefresh={refreshWorkspace}
            isRefreshing={workspaceQuery.isFetching || isRefreshing}
            onViewLogs={() => setTab("logs")}
            onViewEvidence={() => setTab("evidence")}
            onGenerateSupportReport={() => void generateReport()}
            isGeneratingSupportReport={generateSupportReport.isPending}
          />
        ) : null}
      </div>
    </div>
  );
}

function RestoreWorkspaceHeader({
  workspace,
  progressionClosed,
  onRestoreSteps,
}: {
  workspace: RestoreWorkspaceResponse;
  progressionClosed: boolean;
  onRestoreSteps: () => void;
}) {
  const { language, t } = useI18n();
  const nextAction = workspace.overallStatus.nextAction;
  const targetSelected = Boolean(workspace.target.stackSlug);
  const targetState = presentWorkspaceState(workspace.target.availability, t);
  const isCompleted = workspace.attempt.status.toLowerCase() === "completed";
  const stackHref = workspace.target.stackSlug
    ? `/stacks/${encodeURIComponent(workspace.target.stackSlug)}`
    : null;
  const elementUrl = workspace.target.elementHost
    ? `https://${workspace.target.elementHost}`
    : null;

  return (
    <div className="grid gap-4 lg:grid-cols-3">
      <Card className="border-border/80 bg-gradient-to-br from-card via-card to-primary/[0.035] py-0 shadow-sm">
        <CardContent className="p-5">
          <div className="flex items-center gap-2 text-sm font-medium">
            <FileArchive className="h-4 w-4 text-muted-foreground" />
            <span>{t("restoreWorkspace.sourceBackup")}</span>
            <Badge
              className="bg-primary/12 text-primary hover:bg-primary/12"
              variant="secondary"
            >
              {sourceKindLabel(workspace.source.kind, t)}
            </Badge>
          </div>
          <div className="mt-4">
            <div className="text-base font-semibold">
              {workspace.source.stackSlug ?? t("restoreWorkspace.localBackup")}
            </div>
            <div className="mt-1 font-mono text-sm text-muted-foreground">
              {workspace.source.backupId ??
                t("restoreWorkspace.backupReferenceUnavailable")}
            </div>
          </div>
          <dl className="mt-5 grid grid-cols-2 gap-x-5 gap-y-3 text-xs">
            <DetailBlock
              label={t("restoreWorkspace.created")}
              value={formatWorkspaceDate(
                workspace.source.createdAtUtc,
                language,
                t,
              )}
            />
            <DetailBlock
              label={t("restoreWorkspace.size")}
              value={formatWorkspaceBytes(
                workspace.source.sizeBytes,
                language,
                t,
              )}
            />
            <DetailBlock
              label={t("restoreWorkspace.validation")}
              value={presentWorkspaceState(
                workspace.source.validationStatus,
                t,
              )}
              tone="success"
            />
            <DetailBlock
              label={t("restoreWorkspace.files")}
              value={t("restoreWorkspace.manifestRetained")}
            />
          </dl>
        </CardContent>
      </Card>

      <Card className="border-border/80 bg-gradient-to-br from-card via-card to-blue-500/[0.035] py-0 shadow-sm">
        <CardContent className="p-5">
          <div className="flex items-center gap-2 text-sm font-medium">
            <Server className="h-4 w-4 text-muted-foreground" />
            <span>{t("restoreWorkspace.target")}</span>
            {targetSelected ? (
              <Badge
                className={availabilityBadgeClass(
                  workspace.target.availability,
                )}
                variant="secondary"
              >
                {targetState}
              </Badge>
            ) : null}
          </div>
          {targetSelected ? (
            <div className="mt-4 space-y-2 text-sm">
              <div className="font-semibold">{workspace.target.stackSlug}</div>
              {workspace.target.matrixHost ? (
                <EndpointLine
                  label={t("restoreWorkspace.matrix")}
                  value={workspace.target.matrixHost}
                />
              ) : null}
              {workspace.target.elementHost ? (
                <EndpointLine
                  label={t("restoreWorkspace.element")}
                  value={workspace.target.elementHost}
                />
              ) : null}
              <p className="pt-1 text-xs leading-5 text-muted-foreground">
                {workspace.target.detail ||
                  t("restoreWorkspace.targetDetailsFallback")}
              </p>
            </div>
          ) : (
            <div className="mt-4">
              <div className="text-base font-semibold">
                {t("restoreWorkspace.notSelectedYet")}
              </div>
              <p className="mt-2 text-sm leading-6 text-muted-foreground">
                {t("restoreWorkspace.targetSelectDescription")}
              </p>
              {!progressionClosed ? (
                <Button
                  variant="outline"
                  size="sm"
                  className="mt-4"
                  onClick={onRestoreSteps}
                >
                  {t("restoreWorkspace.chooseDetails")}{" "}
                  <ArrowRight className="ml-2 h-4 w-4" />
                </Button>
              ) : null}
            </div>
          )}
        </CardContent>
      </Card>

      <Card
        className={cn(
          "border-border/80 py-0 shadow-sm",
          overallStatusCardClass(workspace.overallStatus.severity),
        )}
      >
        <CardContent className="p-5">
          <div className="flex items-center gap-2 text-sm font-medium">
            <ShieldCheck
              className={cn(
                "h-4 w-4",
                overallStatusIconClass(workspace.overallStatus.severity),
              )}
            />
            <span>{t("restoreWorkspace.overallStatus")}</span>
          </div>
          <div className="mt-4 flex items-start gap-3">
            <StatusIcon severity={workspace.overallStatus.severity} />
            <div className="min-w-0">
              <div className="text-base font-semibold">
                {workspace.overallStatus.title}
              </div>
              <p className="mt-1 text-sm leading-6 text-muted-foreground">
                {workspace.overallStatus.description}
              </p>
            </div>
          </div>
          {isCompleted && stackHref ? (
            <div className="mt-4 border-t border-border/70 pt-3">
              <div className="text-xs text-muted-foreground">
                {t("restoreWorkspace.nextAction")}
              </div>
              <Button
                variant="link"
                size="sm"
                className="mt-1 h-auto px-0 text-blue-500 hover:text-blue-400"
                asChild
              >
                <Link to={stackHref}>
                  {t("restoreWorkspace.openRestoredStack")}{" "}
                  <ArrowRight className="ml-2 h-4 w-4" />
                </Link>
              </Button>
            </div>
          ) : isCompleted && elementUrl ? (
            <div className="mt-4 border-t border-border/70 pt-3">
              <div className="text-xs text-muted-foreground">
                {t("restoreWorkspace.nextAction")}
              </div>
              <Button
                variant="link"
                size="sm"
                className="mt-1 h-auto px-0 text-blue-500 hover:text-blue-400"
                asChild
              >
                <a href={elementUrl} target="_blank" rel="noreferrer">
                  {t("restoreWorkspace.openElement")}{" "}
                  <ArrowRight className="ml-2 h-4 w-4" />
                </a>
              </Button>
            </div>
          ) : nextAction ? (
            <div className="mt-4 border-t border-border/70 pt-3">
              <div className="text-xs text-muted-foreground">
                {t("restoreWorkspace.nextAction")}
              </div>
              <button
                type="button"
                onClick={onRestoreSteps}
                className="mt-1 inline-flex items-center gap-2 text-sm font-medium text-blue-500 transition-colors hover:text-blue-400"
              >
                {nextAction.title} <ArrowRight className="h-4 w-4" />
              </button>
            </div>
          ) : null}
        </CardContent>
      </Card>
    </div>
  );
}

function RestoreWorkspaceRail({
  workspace,
  onRefresh,
  isRefreshing,
  onViewLogs,
  onViewEvidence,
  onGenerateSupportReport,
  isGeneratingSupportReport,
}: {
  workspace: RestoreWorkspaceResponse;
  onRefresh: () => Promise<unknown>;
  isRefreshing: boolean;
  onViewLogs: () => void;
  onViewEvidence: () => void;
  onGenerateSupportReport: () => void;
  isGeneratingSupportReport: boolean;
}) {
  const { language, t } = useI18n();
  const isCompleted = workspace.attempt.status.toLowerCase() === "completed";

  return (
    <aside className="space-y-4">
      <Card className="border-border/80 py-0 shadow-sm">
        <CardContent className="p-5">
          <div className="flex items-center gap-2 text-sm font-medium">
            <Clock3 className="h-4 w-4 text-muted-foreground" />
            {t("restoreWorkspace.attemptSummary")}
          </div>
          <dl className="mt-4 space-y-3 text-sm">
            <RailDetail
              label={t("restoreWorkspace.status")}
              value={presentWorkspaceState(workspace.attempt.status, t)}
              tone={isCompleted ? "success" : "info"}
            />
            <RailDetail
              label={t("restoreWorkspace.started")}
              value={formatWorkspaceDate(
                workspace.attempt.createdAtUtc,
                language,
                t,
              )}
            />
            {workspace.attempt.terminalAtUtc ? (
              <RailDetail
                label={t("restoreWorkspace.completed")}
                value={formatWorkspaceDate(
                  workspace.attempt.terminalAtUtc,
                  language,
                  t,
                )}
              />
            ) : (
              <RailDetail
                label={t("restoreWorkspace.lastUpdated")}
                value={formatWorkspaceDate(
                  workspace.attempt.updatedAtUtc,
                  language,
                  t,
                )}
              />
            )}
            <RailDetail
              label={t("restoreWorkspace.warnings")}
              value={String(workspace.attempt.warningCount)}
              tone={workspace.attempt.warningCount > 0 ? "warning" : undefined}
            />
            <RailDetail
              label={t("restoreWorkspace.errors")}
              value={String(workspace.attempt.errorCount)}
              tone={workspace.attempt.errorCount > 0 ? "error" : undefined}
            />
          </dl>
        </CardContent>
      </Card>

      <Card className="border-amber-500/20 bg-gradient-to-br from-card to-amber-500/[0.035] py-0 shadow-sm">
        <CardContent className="p-5">
          <div className="flex items-center gap-2 text-sm font-medium">
            <ShieldCheck className="h-4 w-4 text-amber-500" />
            {t("restoreWorkspace.safety")}
          </div>
          <p className="mt-3 text-sm leading-6 text-muted-foreground">
            {t("restoreWorkspace.safetyRailDescription")}
          </p>
        </CardContent>
      </Card>

      <Card className="border-border/80 py-0 shadow-sm">
        <CardContent className="p-5">
          <div className="flex items-center gap-2 text-sm font-medium">
            <HelpCircle className="h-4 w-4 text-muted-foreground" />
            {t("restoreWorkspace.needHelp")}
          </div>
          <div className="mt-3 space-y-1">
            <QuickLink
              icon={FileText}
              label={t("restoreWorkspace.viewLogs")}
              onClick={onViewLogs}
            />
            <QuickLink
              icon={FileCheck2}
              label={t("restoreWorkspace.viewEvidence")}
              onClick={onViewEvidence}
            />
            <QuickLink
              icon={FileArchive}
              label={
                workspace.logs.supportReportAvailable
                  ? t("restoreWorkspace.regenerateReport")
                  : t("restoreWorkspace.generateReport")
              }
              onClick={onGenerateSupportReport}
              disabled={isGeneratingSupportReport}
              loading={isGeneratingSupportReport}
            />
          </div>
          <p className="mt-3 text-xs leading-5 text-muted-foreground">
            {t("restoreWorkspace.supportReportDescription")}
          </p>
        </CardContent>
      </Card>

      <Card className="border-border/80 py-0 shadow-sm">
        <CardContent className="p-5">
          <div className="text-sm font-medium">
            {t("restoreWorkspace.quickActions")}
          </div>
          <div className="mt-3 space-y-2">
            <Button
              variant="outline"
              size="sm"
              className="w-full justify-start"
              onClick={() => void onRefresh()}
              disabled={isRefreshing}
            >
              {isRefreshing ? (
                <LoaderCircle className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <RefreshCw className="mr-2 h-4 w-4" />
              )}
              {t("restoreWorkspace.refreshWorkspace")}
            </Button>
            <Button
              variant="outline"
              size="sm"
              className="w-full justify-start"
              asChild
            >
              <Link to="/restores">
                <ArrowLeft className="mr-2 h-4 w-4" />
                {t("restoreWorkspace.backToRestores")}
              </Link>
            </Button>
          </div>
        </CardContent>
      </Card>
    </aside>
  );
}

function QuickLink({
  icon: Icon,
  label,
  onClick,
  disabled,
  loading,
}: {
  icon: typeof FileText;
  label: string;
  onClick: () => void;
  disabled?: boolean;
  loading?: boolean;
}) {
  return (
    <Button
      type="button"
      variant="link"
      className="h-auto w-full justify-start px-0 text-left"
      onClick={onClick}
      disabled={disabled}
    >
      {loading ? (
        <LoaderCircle className="mr-2 h-4 w-4 animate-spin" />
      ) : (
        <Icon className="mr-2 h-4 w-4" />
      )}
      {label}
    </Button>
  );
}

function EndpointLine({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex min-w-0 items-start gap-2 text-muted-foreground">
      <span className="shrink-0">{label}:</span>
      <span className="min-w-0 break-all text-foreground/90">{value}</span>
    </div>
  );
}

function StatusIcon({ severity }: { severity: string }) {
  const normalized = severity.trim().toLowerCase();

  if (["error", "failed", "danger"].includes(normalized)) {
    return (
      <AlertCircle
        className="mt-0.5 h-5 w-5 shrink-0 text-destructive"
        aria-hidden="true"
      />
    );
  }

  if (["warning", "warn", "caution"].includes(normalized)) {
    return (
      <CircleAlert
        className="mt-0.5 h-5 w-5 shrink-0 text-amber-500"
        aria-hidden="true"
      />
    );
  }

  if (["success", "completed", "ok"].includes(normalized)) {
    return (
      <CheckCircle2
        className="mt-0.5 h-5 w-5 shrink-0 text-primary"
        aria-hidden="true"
      />
    );
  }

  return (
    <Clock3
      className="mt-0.5 h-5 w-5 shrink-0 text-blue-500"
      aria-hidden="true"
    />
  );
}

function RestoreWorkspaceLoadingState() {
  const { t } = useI18n();

  return (
    <div className="space-y-6" aria-label={t("restoreWorkspace.loading")}>
      <div className="space-y-2">
        <div className="h-4 w-40 animate-pulse rounded bg-muted" />
        <div className="h-8 w-64 animate-pulse rounded bg-muted" />
        <div className="h-4 w-full max-w-2xl animate-pulse rounded bg-muted" />
      </div>
      <div className="grid gap-4 md:grid-cols-3">
        <div className="h-44 animate-pulse rounded-xl bg-muted" />
        <div className="h-44 animate-pulse rounded-xl bg-muted" />
        <div className="h-44 animate-pulse rounded-xl bg-muted" />
      </div>
      <div className="h-72 animate-pulse rounded-xl bg-muted" />
    </div>
  );
}

function RestoreWorkspaceErrorState({
  restoreSessionId,
  problem,
}: {
  restoreSessionId: string;
  problem?: HostAgentProblem;
}) {
  const { t } = useI18n();
  const localisedProblem = getRestoreWorkspaceProblemMessage(problem);

  return (
    <div className="space-y-6">
      <PageBreadcrumbs
        items={[
          { label: t("restoreWorkspace.breadcrumb.backups"), to: "/backups" },
          { label: t("restoreWorkspace.breadcrumb.restores"), to: "/restores" },
          { label: t("restoreWorkspace.breadcrumb.current") },
        ]}
      />
      <Alert variant="destructive">
        <AlertCircle className="h-4 w-4" />
        <AlertTitle>{t("restoreWorkspace.error.title")}</AlertTitle>
        <AlertDescription>
          <p>
            {localisedProblem
              ? t(localisedProblem.key, localisedProblem.values)
              : t("restoreWorkspace.error.description")}
          </p>
          <span className="mt-2 block font-mono text-xs">
            {t("restoreWorkspace.error.reference", { restoreSessionId })}
          </span>
          <RestoreWorkspaceTechnicalDetails detail={problem?.detail} />
        </AlertDescription>
      </Alert>
      <Button variant="outline" asChild>
        <Link to="/restores">
          <ArrowLeft className="mr-2 h-4 w-4" />
          {t("restoreWorkspace.backToRestores")}
        </Link>
      </Button>
    </div>
  );
}

function downloadJsonFile(name: string, value: unknown) {
  const blob = new Blob([JSON.stringify(value, null, 2)], {
    type: "application/json;charset=utf-8",
  });
  const objectUrl = URL.createObjectURL(blob);
  const anchor = document.createElement("a");

  anchor.href = objectUrl;
  anchor.download = name;
  anchor.style.display = "none";
  document.body.appendChild(anchor);
  anchor.click();
  anchor.remove();

  window.setTimeout(() => URL.revokeObjectURL(objectUrl), 1000);
}

function getWorkspaceTab(value: string | null): WorkspaceTab {
  return workspaceTabValues.includes(value as WorkspaceTab)
    ? (value as WorkspaceTab)
    : "standard";
}

function DetailBlock({
  label,
  value,
  tone,
}: {
  label: string;
  value: string;
  tone?: "success";
}) {
  return (
    <div>
      <dt className="text-muted-foreground">{label}</dt>
      <dd
        className={cn("mt-1 font-medium", tone === "success" && "text-primary")}
      >
        {value}
      </dd>
    </div>
  );
}

function RailDetail({
  label,
  value,
  tone,
}: {
  label: string;
  value: string;
  tone?: "success" | "warning" | "error" | "info";
}) {
  return (
    <div className="flex items-baseline justify-between gap-3">
      <dt className="text-muted-foreground">{label}</dt>
      <dd
        className={cn(
          "text-right font-medium",
          tone === "success" && "text-primary",
          tone === "warning" && "text-amber-500",
          tone === "error" && "text-destructive",
          tone === "info" && "text-blue-500",
        )}
      >
        {value}
      </dd>
    </div>
  );
}

function formatWorkspaceDate(
  value: string | null | undefined,
  language: "en" | "de",
  t: I18nContextValue["t"],
) {
  return value
    ? formatDate(value, language)
    : t("restoreWorkspace.notRecorded");
}

function formatWorkspaceBytes(
  value: number | null | undefined,
  language: "en" | "de",
  t: I18nContextValue["t"],
) {
  return value === null || value === undefined
    ? t("restoreWorkspace.notRecorded")
    : formatBytes(value, language);
}

function sourceKindLabel(
  value: string | null | undefined,
  t: I18nContextValue["t"],
) {
  if (value === "backup-catalog")
    return t("restoreWorkspace.sourceKind.catalog");
  if (value === "local-backup") return t("restoreWorkspace.sourceKind.local");
  return presentWorkspaceState(value, t);
}

function presentWorkspaceState(
  value: string | null | undefined,
  t: I18nContextValue["t"],
) {
  switch (value?.trim().toLowerCase()) {
    case "ready":
      return t("restoreWorkspace.status.ready");
    case "running":
    case "in-progress":
      return t("restoreWorkspace.status.running");
    case "completed":
      return t("restoreWorkspace.status.completed");
    case "failed":
    case "needs-attention":
      return t("restoreWorkspace.status.failed");
    case "cancelled":
      return t("restoreWorkspace.status.cancelled");
    case "not-selected":
      return t("restoreWorkspace.status.notSelected");
    case "available":
      return t("restoreWorkspace.status.available");
    case "not-started":
      return t("restoreWorkspace.status.locked");
    case "optional":
      return t("restoreWorkspace.status.optional");
    case "destroyed":
    case "retired":
      return t("restoreWorkspace.status.retired");
    default:
      return value
        ? value.replaceAll("-", " ")
        : t("restoreWorkspace.notRecorded");
  }
}

function availabilityBadgeClass(availability: string) {
  const normalized = availability.toLowerCase();
  if (
    ["ready", "running", "active", "claimed", "reserved"].some((value) =>
      normalized.includes(value),
    )
  ) {
    return "bg-primary/12 text-primary hover:bg-primary/12";
  }
  if (normalized.includes("released"))
    return "bg-muted text-muted-foreground hover:bg-muted";
  return "bg-blue-500/12 text-blue-500 hover:bg-blue-500/12";
}

function overallStatusCardClass(severity: string) {
  const normalized = severity.toLowerCase();
  if (["error", "failed", "danger"].includes(normalized))
    return "bg-gradient-to-br from-card via-card to-destructive/[0.05]";
  if (["warning", "warn", "caution"].includes(normalized))
    return "bg-gradient-to-br from-card via-card to-amber-500/[0.05]";
  if (["success", "completed", "ok"].includes(normalized))
    return "bg-gradient-to-br from-card via-card to-primary/[0.045]";
  return "bg-gradient-to-br from-card via-card to-blue-500/[0.045]";
}

function overallStatusIconClass(severity: string) {
  const normalized = severity.toLowerCase();
  if (["error", "failed", "danger"].includes(normalized))
    return "text-destructive";
  if (["warning", "warn", "caution"].includes(normalized))
    return "text-amber-500";
  if (["success", "completed", "ok"].includes(normalized))
    return "text-primary";
  return "text-blue-500";
}
