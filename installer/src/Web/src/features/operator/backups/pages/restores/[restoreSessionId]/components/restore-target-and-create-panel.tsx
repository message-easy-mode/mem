import { useEffect, useMemo, useState } from "react";
import { useI18n, type I18nContextValue } from "@/app/i18n/i18n-context";
import {
  AlertCircle,
  ArrowRight,
  CheckCircle2,
  CircleAlert,
  Clock3,
  Info,
  LoaderCircle,
  LockKeyhole,
  Server,
  ShieldCheck,
} from "lucide-react";

import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { OperatorStepUpDialog } from "@/features/auth/operator-step-up-dialog";
import {
  isHostAgentProblemError,
  isStepUpRequiredHostAgentProblem,
} from "@/features/operator/backups/api/transport/host-agent";
import { useProductionRecreatePreflight } from "@/features/operator/backups/hooks/use-backups";
import { useRunRestoreWorkspaceStandardRecreate } from "@/features/operator/backups/hooks/use-restore-workspace";
import type { RuntimeStackBackupProductionRecreatePreflightResponse } from "@/features/operator/backups/api/types/backups.types";
import type {
  RestoreWorkspaceResponse,
  RestoreWorkspaceStandardRecreateActionRequest,
} from "@/features/operator/backups/api/types/restore-workspace.types";
import type { VisibleStep } from "./workspace-ui";
import {
  getRestoreWorkspaceTechnicalDetail,
  RestoreWorkspaceTechnicalDetails,
} from "./restore-workspace-problems";

type Props = {
  workspace: RestoreWorkspaceResponse;
  step: VisibleStep;
  onWorkspaceChanged: () => Promise<unknown>;
  onContinueToVerification: () => void;
};

function defaultStackSlug(workspace: RestoreWorkspaceResponse) {
  return (
    workspace.target.stackSlug ||
    `${workspace.source.stackSlug || "restored"}-restored`
  );
}

function defaultElementHost(workspace: RestoreWorkspaceResponse) {
  return workspace.target.elementHost || workspace.source.elementHost || "";
}

function useDebouncedValue<T>(value: T, delayMs: number) {
  const [debouncedValue, setDebouncedValue] = useState(value);

  useEffect(() => {
    const timer = window.setTimeout(() => setDebouncedValue(value), delayMs);
    return () => window.clearTimeout(timer);
  }, [delayMs, value]);

  return debouncedValue;
}

function matchesHost(
  candidate: string,
  sourceHost: string | null | undefined,
) {
  return (
    Boolean(sourceHost) &&
    candidate.trim().toLowerCase() === sourceHost?.trim().toLowerCase()
  );
}

function CreationChecksPanel({
  preflight,
  checking,
  targetDetailsPresent,
  matrixIdentityAvailable,
  t,
}: {
  preflight: RuntimeStackBackupProductionRecreatePreflightResponse | undefined;
  checking: boolean;
  targetDetailsPresent: boolean;
  matrixIdentityAvailable: boolean;
  t: I18nContextValue["t"];
}) {
  const isReady = preflight?.status === "ready" && preflight.canCreate;
  const checks = preflight?.checks ?? [];
  const groupedChecks = groupCreationChecks(checks, t);

  return (
    <aside className="min-w-0 rounded-xl border border-blue-500/30 bg-gradient-to-br from-blue-500/[0.075] via-card to-card p-5 shadow-sm">
      <div className="flex items-start gap-3">
        {checking ? (
          <LoaderCircle className="mt-0.5 h-5 w-5 shrink-0 animate-spin text-primary" />
        ) : isReady ? (
          <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0 text-primary" />
        ) : (
          <ShieldCheck className="mt-0.5 h-5 w-5 shrink-0 text-primary" />
        )}

        <div className="min-w-0">
          <h3 className="font-semibold">{t("restoreWorkspace.create.preflight")}</h3>
          <p className="mt-1 text-sm text-muted-foreground">
            {checking
              ? t("restoreWorkspace.create.checking")
              : isReady
                ? t("restoreWorkspace.create.ready")
                : !matrixIdentityAvailable
                  ? t("restoreWorkspace.create.identityMissing")
                  : targetDetailsPresent
                    ? t("restoreWorkspace.create.resolveBlocked")
                    : t("restoreWorkspace.create.enterDetails")}
          </p>
        </div>
      </div>

      {groupedChecks.length ? (
        <ul className="mt-4 space-y-3 text-sm">
          {groupedChecks.map((check) => (
            <li key={check.code} className="flex gap-2">
              {check.passed ? (
                <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
              ) : (
                <AlertCircle className="mt-0.5 h-4 w-4 shrink-0 text-destructive" />
              )}
              <div className="min-w-0">
                <p
                  className={
                    check.passed ? "text-foreground" : "text-destructive"
                  }
                >
                  {check.title}
                </p>
                <p className="mt-0.5 text-xs text-muted-foreground">
                  {check.message}
                </p>
              </div>
            </li>
          ))}
        </ul>
      ) : targetDetailsPresent && !checking ? (
        <p className="mt-4 flex gap-2 text-sm text-destructive">
          <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" />
          {t("restoreWorkspace.create.checkFailed")}
        </p>
      ) : null}

      {preflight?.turn ? (
        <div className="mt-4 rounded-lg border border-cyan-500/25 bg-cyan-500/[0.055] p-3">
          <div className="flex items-start gap-2">
            <Info className="mt-0.5 h-4 w-4 shrink-0 text-cyan-500" />
            <div className="min-w-0">
              <p className="text-sm font-medium">
                {t("restoreWorkspace.create.turn.title")}
              </p>
              <p className="mt-1 text-xs leading-5 text-muted-foreground">
                {turnPlanDescription(preflight.turn.mode, t)}
              </p>
              {preflight.turn.publicHost ? (
                <p className="mt-2 break-all text-xs text-muted-foreground">
                  {t("restoreWorkspace.create.turn.host")}:{" "}
                  <span className="font-mono text-foreground">
                    {preflight.turn.publicHost}
                  </span>
                </p>
              ) : null}
            </div>
          </div>
        </div>
      ) : null}

      {preflight?.warnings.length ? (
        <div className="mt-4 space-y-2 border-t border-border/70 pt-4">
          {preflight.warnings.map((warning) => (
            <p
              key={warning}
              className="flex gap-2 text-xs text-muted-foreground"
            >
              <CircleAlert className="mt-0.5 h-4 w-4 shrink-0 text-amber-500" />
              <span>{warning}</span>
            </p>
          ))}
        </div>
      ) : null}

      <div className="mt-4 border-t border-border/70 pt-4">
        <p className="flex gap-2 text-sm">
          <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
          <span>{t("restoreWorkspace.create.safety")}</span>
        </p>
        <p className="mt-2 text-xs text-muted-foreground">
          {t("restoreWorkspace.create.safetyDetail")}
        </p>
      </div>
    </aside>
  );
}

function turnPlanDescription(
  mode: string,
  t: I18nContextValue["t"],
) {
  switch (mode) {
    case "restore-disconnected":
      return t("restoreWorkspace.create.turn.disconnected")
    case "rebind-platform":
      return t("restoreWorkspace.create.turn.rebind")
    case "preserve-backup":
      return t("restoreWorkspace.create.turn.preserve")
    default:
      return t("restoreWorkspace.create.turn.unknown")
  }
}

function groupCreationChecks(
  checks: RuntimeStackBackupProductionRecreatePreflightResponse["checks"],
  t: I18nContextValue["t"],
) {
  const groups = [
    {
      code: "backup-ready",
      title: t("restoreWorkspace.create.check.backupReady"),
      codes: ["restore-source-valid"],
    },
    {
      code: "matrix-server-identity",
      title: t("restoreWorkspace.create.check.matrixIdentity"),
      codes: [
        "matrix-server-identity-preserved",
        "matrix-server-identity-available",
      ],
    },
    {
      code: "restore-ready",
      title: t("restoreWorkspace.create.check.restoreReady"),
      codes: [
        "restore-attempt-ready",
        "restore-operation-idle",
        "restore-target-selection-compatible",
      ],
    },
    {
      code: "target-stack-available",
      title: t("restoreWorkspace.create.check.stackAvailable"),
      codes: [
        "target-stack-runtime-available",
        "target-stack-claim-available",
        "target-stack-manifest-available",
      ],
    },
    {
      code: "matrix-recovery-address-ready",
      title: t("restoreWorkspace.create.check.matrixAddress"),
      codes: ["matrix-host-runtime-available", "matrix-host-claim-available"],
    },
    {
      code: "element-host-availability",
      title: t("restoreWorkspace.create.check.elementHost"),
      codes: ["element-host-runtime-available", "element-host-claim-available"],
    },
    {
      code: "platform-domain-ready",
      title: t("restoreWorkspace.create.check.platformDomain"),
      codes: ["platform-domain-resolved"],
    },
  ];

  const grouped = groups
    .map((group) => {
      const matches = checks.filter((check) =>
        group.codes.includes(check.code),
      );
      if (!matches.length) {
        return null;
      }

      const failed = matches.find((check) => check.state !== "passed");
      return {
        code: group.code,
        title: group.title,
        passed: !failed,
        message: failed?.message ?? matches[matches.length - 1].message,
      };
    })
    .filter((group): group is NonNullable<typeof group> => group !== null);

  const groupedCodes = new Set(groups.flatMap((group) => group.codes));
  const ungrouped = checks
    .filter((check) => !groupedCodes.has(check.code))
    .map((check) => ({
      code: check.code,
      title: check.title,
      passed: check.state === "passed",
      message: check.message,
    }));

  return [...grouped, ...ungrouped];
}


const RESTORE_PROGRESS_STEPS = [
  "targets-reserved",
  "prepare-runtime",
  "restore-source-material",
  "restore-database",
  "configure-matrix",
  "start-matrix",
  "start-element",
  "publish-routes",
  "verify-readiness",
  "register-runtime",
] as const;

function RestoreOperationProgressPanel({
  workspace,
  t,
}: {
  workspace: RestoreWorkspaceResponse;
  t: I18nContextValue["t"];
}) {
  const operation = workspace.standardStages
    .find((stage) => stage.code === "create-restored-chat-server")
    ?.operationSummary ?? null;
  const [now, setNow] = useState(() => Date.now());

  useEffect(() => {
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, []);

  const currentStep = operation?.currentStep?.trim().toLowerCase() ?? "";
  const terminal = ["succeeded", "completed", "passed"].includes(
    operation?.status.toLowerCase() ?? "",
  );
  const activeIndex = RESTORE_PROGRESS_STEPS.findIndex(
    (stepCode) => stepCode === currentStep,
  );
  const startedAt = operation?.startedAtUtc ?? operation?.requestedAtUtc ?? null;
  const lastActivityAt =
    operation?.lastActivityAtUtc ??
    operation?.startedAtUtc ??
    operation?.requestedAtUtc ??
    null;
  const stale = lastActivityAt
    ? now - new Date(lastActivityAt).getTime() >= 30_000
    : false;

  return (
    <section
      className="rounded-xl border border-blue-500/30 bg-blue-500/[0.07] p-5 shadow-sm"
      data-testid="restore-standard-recreate-progress"
    >
      <div className="flex items-start justify-between gap-4">
        <div>
          <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
            {t("restoreWorkspace.create.progress.currentOperation")}
          </div>
          <h3 className="mt-1 text-lg font-semibold">
            {restoreProgressStepLabel(currentStep, t)}
          </h3>
        </div>
        <LoaderCircle className="mt-1 h-5 w-5 shrink-0 animate-spin text-blue-500" />
      </div>

      <div className="mt-4 grid gap-3 sm:grid-cols-3">
        <ProgressMetric
          label={t("restoreWorkspace.create.progress.elapsed")}
          value={formatProgressElapsed(startedAt, now, t)}
        />
        <ProgressMetric
          label={t("restoreWorkspace.create.progress.lastActivity")}
          value={formatProgressRelative(lastActivityAt, now, t)}
        />
        <ProgressMetric
          label={t("restoreWorkspace.create.progress.attempt")}
          value={(operation?.attemptNumber ?? 1).toString()}
        />
      </div>

      <div className="mt-4 flex items-start gap-2 rounded-lg border border-blue-500/20 bg-background/35 p-3 text-sm">
        <Clock3 className="mt-0.5 h-4 w-4 shrink-0 text-blue-500" />
        <div>
          <div className="font-medium">
            {t(
              stale
                ? "restoreWorkspace.create.progress.noRecentActivityTitle"
                : "restoreWorkspace.create.progress.workingTitle",
            )}
          </div>
          <div className="mt-1 text-muted-foreground">
            {t(
              stale
                ? "restoreWorkspace.create.progress.noRecentActivityDescription"
                : "restoreWorkspace.create.progress.workingDescription",
            )}
          </div>
        </div>
      </div>

      <div className="mt-4 rounded-lg border border-border/70 bg-background/30 p-3">
        <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
          {t("restoreWorkspace.create.progress.operationProgress")}
        </div>
        <ul className="mt-3 space-y-2">
          {RESTORE_PROGRESS_STEPS.map((stepCode, index) => {
            const completed =
              terminal ||
              (activeIndex >= 0 && index < activeIndex);
            const active = !terminal && index === activeIndex;

            return (
              <li key={stepCode} className="flex items-start gap-2 text-sm">
                {completed ? (
                  <CheckCircle2 className="mt-0.5 h-4 w-4 shrink-0 text-primary" />
                ) : active ? (
                  <LoaderCircle className="mt-0.5 h-4 w-4 shrink-0 animate-spin text-blue-500" />
                ) : (
                  <Clock3 className="mt-0.5 h-4 w-4 shrink-0 text-muted-foreground" />
                )}
                <span
                  className={
                    active
                      ? "font-medium text-foreground"
                      : completed
                        ? "text-foreground"
                        : "text-muted-foreground"
                  }
                >
                  {restoreProgressStepLabel(stepCode, t)}
                </span>
              </li>
            );
          })}
        </ul>
      </div>
    </section>
  );
}

function ProgressMetric({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-lg border border-border/70 bg-background/30 p-3">
      <div className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {label}
      </div>
      <div className="mt-1 text-sm font-medium">{value}</div>
    </div>
  );
}

function restoreProgressStepLabel(
  stepCode: string,
  t: I18nContextValue["t"],
) {
  switch (stepCode) {
    case "targets-reserved":
      return t("restoreWorkspace.create.progress.targetsReserved");
    case "prepare-runtime":
      return t("restoreWorkspace.create.progress.prepareRuntime");
    case "restore-source-material":
      return t("restoreWorkspace.create.progress.restoreSource");
    case "restore-database":
      return t("restoreWorkspace.create.progress.restoreDatabase");
    case "configure-matrix":
      return t("restoreWorkspace.create.progress.configureMatrix");
    case "start-matrix":
      return t("restoreWorkspace.create.progress.startMatrix");
    case "start-element":
      return t("restoreWorkspace.create.progress.startElement");
    case "publish-routes":
      return t("restoreWorkspace.create.progress.publishRoutes");
    case "verify-readiness":
      return t("restoreWorkspace.create.progress.verifyReadiness");
    case "register-runtime":
      return t("restoreWorkspace.create.progress.registerRuntime");
    default:
      return t("restoreWorkspace.create.progress.starting");
  }
}

function formatProgressElapsed(
  value: string | null,
  now: number,
  t: I18nContextValue["t"],
) {
  if (!value) return t("restoreWorkspace.create.progress.notRecorded");
  const timestamp = new Date(value).getTime();
  if (!Number.isFinite(timestamp)) {
    return t("restoreWorkspace.create.progress.notRecorded");
  }

  const seconds = Math.max(0, Math.floor((now - timestamp) / 1000));
  const minutes = Math.floor(seconds / 60);
  const remainder = seconds % 60;
  return minutes > 0
    ? t("restoreWorkspace.create.progress.minutesSeconds", {
        minutes,
        seconds: remainder,
      })
    : t("restoreWorkspace.create.progress.seconds", { count: seconds });
}

function formatProgressRelative(
  value: string | null,
  now: number,
  t: I18nContextValue["t"],
) {
  if (!value) return t("restoreWorkspace.create.progress.notRecorded");
  const timestamp = new Date(value).getTime();
  if (!Number.isFinite(timestamp)) {
    return t("restoreWorkspace.create.progress.notRecorded");
  }

  const seconds = Math.max(0, Math.floor((now - timestamp) / 1000));
  if (seconds < 5) return t("restoreWorkspace.create.progress.justNow");
  if (seconds < 60) {
    return t("restoreWorkspace.create.progress.secondsAgo", { count: seconds });
  }

  return t("restoreWorkspace.create.progress.minutesAgo", {
    count: Math.floor(seconds / 60),
  });
}

export function RestoreTargetAndCreatePanel({
  workspace,
  step,
  onWorkspaceChanged,
  onContinueToVerification,
}: Props) {
  const { t } = useI18n();
  const recreate = useRunRestoreWorkspaceStandardRecreate();
  const recreateReturnedServerResponse = isHostAgentProblemError(recreate.error);
  const recreateTechnicalDetail = recreate.isError
    ? getRestoreWorkspaceTechnicalDetail(recreate.error)
    : undefined;

  const [targetStackSlug, setTargetStackSlug] = useState(
    defaultStackSlug(workspace),
  );
  const [elementHost, setElementHost] = useState(defaultElementHost(workspace));
  const [operator, setOperator] = useState("");
  const [isStepUpOpen, setIsStepUpOpen] = useState(false);
  const [pendingStepUpRequest, setPendingStepUpRequest] =
    useState<RestoreWorkspaceStandardRecreateActionRequest | null>(null);

  const matrixServerIdentity = workspace.source.matrixHost?.trim() ?? "";
  const debouncedTargetStackSlug = useDebouncedValue(targetStackSlug, 450);
  const debouncedElementHost = useDebouncedValue(elementHost, 450);

  const preflightRequest = useMemo(() => {
    const targetStack = debouncedTargetStackSlug.trim();
    const element = debouncedElementHost.trim();

    if (!workspace.restoreSessionId || !targetStack || !element) {
      return null;
    }

    return {
      restoreSessionId: workspace.restoreSessionId,
      targetStackSlug: targetStack,
      elementHost: element,
    };
  }, [
    debouncedElementHost,
    debouncedTargetStackSlug,
    workspace.restoreSessionId,
  ]);

  const preflight = useProductionRecreatePreflight(preflightRequest);

  const targetDetailsPresent = Boolean(
    matrixServerIdentity && targetStackSlug.trim() && elementHost.trim(),
  );
  const waitingForLatestPreflight =
    targetStackSlug !== debouncedTargetStackSlug ||
    elementHost !== debouncedElementHost ||
    preflight.isLoading ||
    preflight.isFetching;

  const elementUsesSourceHost = matchesHost(
    elementHost,
    workspace.source.elementHost,
  );
  const stepUpPending = pendingStepUpRequest !== null || isStepUpOpen;

  const canCreate = useMemo(
    () =>
      Boolean(
        workspace.restoreSessionId &&
        matrixServerIdentity &&
        targetDetailsPresent &&
        step.blockers.length === 0 &&
        preflight.data?.canCreate &&
        !waitingForLatestPreflight &&
        !preflight.isError &&
        !recreate.isPending &&
        !stepUpPending &&
        !["running", "completed", "failed"].includes(step.state),
      ),
    [
      matrixServerIdentity,
      preflight.data?.canCreate,
      preflight.isError,
      recreate.isPending,
      step.blockers.length,
      stepUpPending,
      step.state,
      targetDetailsPresent,
      waitingForLatestPreflight,
      workspace.restoreSessionId,
    ],
  );

  const runStandardRecreate = (
    request: RestoreWorkspaceStandardRecreateActionRequest,
  ) => {
    recreate.mutate(request, {
      onSuccess: async () => {
        setPendingStepUpRequest(null);
        setIsStepUpOpen(false);
        await onWorkspaceChanged();
      },
      onError: (error) => {
        if (!isStepUpRequiredHostAgentProblem(error)) {
          // A transport failure does not prove that the server rejected the
          // mutation. Standard Recreate becomes server-owned after acceptance,
          // so immediately refresh the authoritative workspace before inviting
          // the operator to retry anything. HTTP problem responses also benefit
          // from this refresh because the server may have advanced the attempt.
          void onWorkspaceChanged();
          return;
        }

        // Preserve only the action the operator has already explicitly
        // acknowledged. The password-plus-current-TOTP verifier must not
        // broaden authority to a different target or a future click.
        recreate.reset();
        setPendingStepUpRequest(request);
        setIsStepUpOpen(true);
      },
    });
  };

  const execute = () => {
    if (!canCreate) {
      return;
    }

    runStandardRecreate({
      restoreSessionId: workspace.restoreSessionId,
      targetStackSlug: targetStackSlug.trim(),
      elementHost: elementHost.trim(),
      operator: operator.trim() || null,
      executeProductionRecreate: true,

      // The explicit create action is the operator acknowledgement. The backend
      // repeats source-aware ownership checks and claims targets atomically before
      // it mutates Docker, Postgres, or public routes.
      acknowledgeCreatesRealStack: true,
      acknowledgeMutatesProductionPostgres: true,
      acknowledgeMutatesNpmRoutes: true,
      acknowledgeNoAutomaticRollback: true,
    });
  };

  const changeStepUpOpen = (open: boolean) => {
    setIsStepUpOpen(open);

    if (!open) {
      setPendingStepUpRequest(null);
    }
  };

  const resumeStandardRecreateAfterStepUp = () => {
    const request = pendingStepUpRequest;
    setPendingStepUpRequest(null);

    if (request) {
      runStandardRecreate(request);
    }
  };

  if (step.state === "running") {
    return (
      <div className="space-y-4">
        <RestoreOperationProgressPanel workspace={workspace} t={t} />
        <Alert>
          <LoaderCircle className="h-4 w-4 animate-spin" />
          <AlertTitle>{t("restoreWorkspace.create.runningTitle")}</AlertTitle>
          <AlertDescription>
            {t("restoreWorkspace.create.runningDescription")}
          </AlertDescription>
        </Alert>
      </div>
    );
  }

  if (step.state === "completed") {
    return (
      <section className="rounded-xl border border-primary/25 bg-gradient-to-br from-primary/[0.06] via-card to-card p-5 shadow-sm">
        <div className="flex items-start gap-3">
          <CheckCircle2 className="mt-0.5 h-6 w-6 shrink-0 text-primary" />
          <div className="min-w-0">
            <h3 className="font-semibold">{t("restoreWorkspace.create.completedTitle")}</h3>
            <p className="mt-1 text-sm leading-6 text-muted-foreground">
              {t("restoreWorkspace.create.completedDescription")}
            </p>
          </div>
        </div>
        <div className="mt-5 border-t border-border/70 pt-4">
          <Button onClick={onContinueToVerification}>
            {t("restoreWorkspace.create.continueVerify")} <ArrowRight className="ml-2 h-4 w-4" />
          </Button>
        </div>
      </section>
    );
  }

  if (step.state === "failed") {
    return (
      <Alert variant="destructive">
        <AlertCircle className="h-4 w-4" />
        <AlertTitle>{t("restoreWorkspace.create.failed")}</AlertTitle>
        <AlertDescription>
          {step.summary ||
            t("restoreWorkspace.create.failedDescription")}
        </AlertDescription>
      </Alert>
    );
  }

  return (
    <div className="min-w-0 space-y-4">
      {step.blockers.length > 0 ? (
        <Alert variant="destructive">
          <AlertCircle className="h-4 w-4" />
          <AlertTitle>{t("restoreWorkspace.create.blocked")}</AlertTitle>
          <AlertDescription>
            <ul className="mt-2 list-disc space-y-1 pl-4">
              {step.blockers.map((blocker) => (
                <li key={blocker}>{blocker}</li>
              ))}
            </ul>
          </AlertDescription>
        </Alert>
      ) : null}

      {!matrixServerIdentity ? (
        <Alert variant="destructive">
          <AlertCircle className="h-4 w-4" />
          <AlertTitle>{t("restoreWorkspace.create.identityUnavailable")}</AlertTitle>
          <AlertDescription>
            {t("restoreWorkspace.create.identityUnavailableDescription")}
          </AlertDescription>
        </Alert>
      ) : null}

      {recreate.isError ? (
        <Alert variant={recreateReturnedServerResponse ? "destructive" : undefined}>
          <AlertCircle className="h-4 w-4" />
          <AlertTitle>
            {t(
              recreateReturnedServerResponse
                ? "restoreWorkspace.create.startError"
                : "restoreWorkspace.create.transportUncertainTitle",
            )}
          </AlertTitle>
          <AlertDescription>
            <p>
              {t(
                recreateReturnedServerResponse
                  ? "restoreWorkspace.create.serverErrorDescription"
                  : "restoreWorkspace.create.transportUncertainDescription",
              )}
            </p>
            <RestoreWorkspaceTechnicalDetails detail={recreateTechnicalDetail} />
          </AlertDescription>
        </Alert>
      ) : null}

      <div className="grid min-w-0 gap-5 min-[1800px]:grid-cols-[minmax(0,1fr)_21rem]">
        <div className="min-w-0 rounded-xl border border-border/80 bg-card p-5 shadow-sm">
          <div>
            <h3 className="text-base font-semibold">{t("restoreWorkspace.create.details")}</h3>
            <p className="mt-1 text-sm leading-6 text-muted-foreground">
              {t("restoreWorkspace.create.detailsDescription")}
            </p>
          </div>

          <div className="mt-5 grid min-w-0 gap-5 min-[1500px]:grid-cols-[minmax(0,0.9fr)_minmax(0,1.1fr)]">
            <section className="min-w-0 rounded-xl border border-blue-500/30 bg-gradient-to-br from-blue-500/[0.09] via-card to-card p-5">
              <div className="flex items-center gap-2">
                <LockKeyhole className="h-4 w-4 text-blue-500" />
                <h4 className="font-semibold">{t("restoreWorkspace.create.identity")} <span className="text-muted-foreground">({t("restoreWorkspace.create.immutable")})</span></h4>
              </div>
              <p className="mt-2 text-sm leading-6 text-muted-foreground">
                {t("restoreWorkspace.create.identityDescription")}
              </p>

              <div className="mt-5 rounded-lg border border-blue-500/35 bg-blue-500/[0.08] p-4">
                <div className="flex min-w-0 items-center gap-3">
                  <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-blue-500/15 text-blue-500">
                    <Server className="h-5 w-5" />
                  </div>
                  <p className="min-w-0 break-all font-semibold">
                    {matrixServerIdentity || t("restoreWorkspace.create.identityFallback")}
                  </p>
                </div>
                <p className="mt-2 pl-12 text-xs text-blue-600 dark:text-blue-300">{t("restoreWorkspace.create.preserved")}</p>
              </div>

              <div className="mt-5 rounded-lg border border-blue-500/20 bg-blue-500/[0.045] p-4 text-sm leading-6 text-muted-foreground">
                <div className="flex gap-2">
                  <Info className="mt-0.5 h-4 w-4 shrink-0 text-blue-500" />
                  <p>
                    {t("restoreWorkspace.create.identitySafety")}
                  </p>
                </div>
              </div>
            </section>

            <section className="min-w-0 rounded-xl border border-border/80 bg-card p-5">
              <div>
                <h4 className="font-semibold">{t("restoreWorkspace.create.newTarget")}</h4>
                <p className="mt-2 text-sm leading-6 text-muted-foreground">
                  {t("restoreWorkspace.create.newTargetDescription")}
                </p>
              </div>

              <div className="mt-5 space-y-4">
                <div className="min-w-0 space-y-2">
                  <Label htmlFor="target-stack-slug">{t("restoreWorkspace.create.targetStackSlug")}</Label>
                  <Input
                    id="target-stack-slug"
                    value={targetStackSlug}
                    onChange={(event) => setTargetStackSlug(event.target.value)}
                    disabled={recreate.isPending || stepUpPending}
                    className="w-full min-w-0"
                  />
                  <p className="text-xs text-muted-foreground">
                    {t("restoreWorkspace.create.targetStackHelp")}
                  </p>
                </div>

                <div className="min-w-0 space-y-2">
                  <Label htmlFor="element-host">{t("restoreWorkspace.create.elementHost")}</Label>
                  <Input
                    id="element-host"
                    value={elementHost}
                    onChange={(event) => setElementHost(event.target.value)}
                    disabled={recreate.isPending || stepUpPending}
                    className="w-full min-w-0"
                  />
                  <p className="text-xs text-muted-foreground">
                    {elementUsesSourceHost
                      ? t("restoreWorkspace.create.sourceElementHelp")
                      : t("restoreWorkspace.create.elementHelp")}
                  </p>
                </div>

                <div className="min-w-0 space-y-2">
                  <Label htmlFor="operator-name">
                    {t("restoreWorkspace.create.operatorName")}{" "}
                    <span className="text-muted-foreground">({t("restoreWorkspace.create.optional")})</span>
                  </Label>
                  <Input
                    id="operator-name"
                    value={operator}
                    onChange={(event) => setOperator(event.target.value)}
                    disabled={recreate.isPending || stepUpPending}
                    className="w-full min-w-0"
                  />
                  <p className="text-xs text-muted-foreground">
                    {t("restoreWorkspace.create.operatorHelp")}
                  </p>
                </div>
              </div>

              <div className="mt-5 rounded-lg border border-blue-500/30 bg-blue-500/[0.055] p-3 text-sm text-muted-foreground">
                <div className="flex gap-2">
                  <Info className="mt-0.5 h-4 w-4 shrink-0 text-blue-500" />
                  {t("restoreWorkspace.create.elementConfigured")}
                </div>
              </div>
            </section>
          </div>

          <div className="mt-5 flex flex-wrap items-center gap-3 border-t border-border/70 pt-5">
            <Button
              type="button"
              className="bg-blue-600 text-white hover:bg-blue-500"
              onClick={() => void execute()}
              disabled={!canCreate}
            >
              {recreate.isPending ? (
                <LoaderCircle className="mr-2 h-4 w-4 animate-spin" />
              ) : (
                <ShieldCheck className="mr-2 h-4 w-4" />
              )}
              {t("restoreWorkspace.create.submit")}
            </Button>

            <p className="max-w-md text-xs leading-5 text-muted-foreground">
              {t("restoreWorkspace.create.submitDescription")}
            </p>
          </div>

        </div>

        <CreationChecksPanel
          preflight={preflight.data}
          checking={waitingForLatestPreflight}
          targetDetailsPresent={targetDetailsPresent}
          matrixIdentityAvailable={Boolean(matrixServerIdentity)}
          t={t}
        />
      </div>
      <OperatorStepUpDialog
        open={isStepUpOpen}
        onOpenChange={changeStepUpOpen}
        onVerified={resumeStandardRecreateAfterStepUp}
      />
    </div>
  );
}
