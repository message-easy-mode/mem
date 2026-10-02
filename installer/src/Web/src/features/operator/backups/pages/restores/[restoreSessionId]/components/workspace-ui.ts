import type { I18nContextValue } from "@/app/i18n/i18n-context";
import type { RestoreWorkspaceStandardStage } from "@/features/operator/backups/api/types/restore-workspace.types";

export type VisibleStep = {
  key: string;
  title: string;
  description: string;
  state: string;
  summary: string;
  blockers: string[];
  sourceStages: RestoreWorkspaceStandardStage[];
};

export function buildVisibleSteps(
  stages: RestoreWorkspaceStandardStage[],
  t: I18nContextValue["t"],
): VisibleStep[] {
  const get = (code: string) => stages.find((stage) => stage.code === code);
  const toVisible = (
    stage: RestoreWorkspaceStandardStage | undefined,
    fallback: VisibleStep,
  ): VisibleStep =>
    stage
      ? {
          key: stage.code,
          title: fallback.title,
          // The stable stage code is the localisation seam. Stage descriptions
          // from older API versions remain English prose, so retain those as the
          // raw summary/evidence trail rather than letting them change the UI
          // language chosen by the operator.
          description: fallback.description,
          state: stage.state,
          summary: stage.summary,
          blockers: stage.blockers,
          sourceStages: [stage],
        }
      : fallback;

  const choose = get("choose-restored-server-details");
  const create = get("create-restored-chat-server");
  const createOwnsState =
    create && ["running", "failed", "completed"].includes(create.state);

  return [
    toVisible(get("backup-ready"), {
      key: "backup-ready",
      title: t("restoreWorkspace.stage.backupReady.title"),
      description: t("restoreWorkspace.stage.backupReady.description"),
      state: "not-started",
      summary: "",
      blockers: [],
      sourceStages: [],
    }),
    toVisible(get("private-test"), {
      key: "private-test",
      title: t("restoreWorkspace.stage.privateTest.title"),
      description: t("restoreWorkspace.stage.privateTest.description"),
      state: "not-started",
      summary: "",
      blockers: [],
      sourceStages: [],
    }),
    {
      key: "choose-and-create-restored-server",
      title: t("restoreWorkspace.stage.chooseCreate.title"),
      description: t("restoreWorkspace.stage.chooseCreate.description"),
      state: createOwnsState ? create!.state : (choose?.state ?? "not-started"),
      summary:
        create?.summary ||
        choose?.summary ||
        t("restoreWorkspace.stage.chooseCreate.summary"),
      blockers: Array.from(
        new Set([...(choose?.blockers ?? []), ...(create?.blockers ?? [])]),
      ),
      sourceStages: [choose, create].filter(
        (stage): stage is RestoreWorkspaceStandardStage => Boolean(stage),
      ),
    },
    toVisible(get("check-restored-server"), {
      key: "check-restored-server",
      title: t("restoreWorkspace.stage.check.title"),
      description: t("restoreWorkspace.stage.check.description"),
      state: "not-started",
      summary: "",
      blockers: [],
      sourceStages: [],
    }),
    toVisible(get("complete-and-hand-over"), {
      key: "complete-and-hand-over",
      title: t("restoreWorkspace.stage.complete.title"),
      description: t("restoreWorkspace.stage.complete.description"),
      state: "not-started",
      summary: "",
      blockers: [],
      sourceStages: [],
    }),
  ];
}

export function initialOpenStep(steps: VisibleStep[]) {
  // A brand-new restore begins at Step 1 so the operator sees the backup
  // validation and the safety boundary first. Once the workflow has advanced,
  // reopening or remounting the workspace must return the operator to the
  // current actionable stage instead of resetting them to the beginning.
  const needsAttention = steps.find((step) =>
    ["failed", "running"].includes(step.state),
  );

  if (needsAttention) {
    return needsAttention.key;
  }

  if (isFreshRestoreJourney(steps)) {
    return steps[0]?.key ?? "backup-ready";
  }

  const nextActionable = steps.find((step) => step.state === "ready");
  if (nextActionable) {
    return nextActionable.key;
  }

  // A completed workspace has no remaining ready stage. Re-open the latest
  // completed milestone, normally Restore complete, so the durable handover
  // record is the first thing the operator sees.
  const latestCompleted = [...steps]
    .reverse()
    .find((step) => step.state === "completed");

  return latestCompleted?.key ?? steps[0]?.key ?? "backup-ready";
}

function isFreshRestoreJourney(steps: VisibleStep[]) {
  const [backupReady, privateTest, chooseAndCreate, verification, handover] =
    steps;

  return Boolean(
    backupReady?.state === "completed" &&
    privateTest &&
    chooseAndCreate &&
    verification &&
    handover &&
    !hasRecordedProgress(privateTest) &&
    !hasRecordedProgress(chooseAndCreate) &&
    !hasRecordedProgress(verification) &&
    !hasRecordedProgress(handover) &&
    chooseAndCreate.state === "ready",
  );
}

function hasRecordedProgress(step: VisibleStep) {
  return step.sourceStages.some((stage) =>
    Boolean(
      stage.completedAtUtc ||
      stage.operationSummary?.startedAtUtc ||
      stage.operationSummary?.completedAtUtc,
    ),
  );
}

export function badgeVariant(state: string) {
  if (state === "failed") return "destructive" as const;
  if (state === "completed") return "default" as const;
  return "outline" as const;
}

export function stepTone(state: string) {
  if (state === "failed") return "bg-destructive/15 text-destructive";
  if (state === "completed") return "bg-primary/15 text-primary";
  if (["running", "ready", "optional"].includes(state))
    return "bg-blue-500/15 text-blue-600 dark:text-blue-400";
  return "bg-muted text-muted-foreground";
}
