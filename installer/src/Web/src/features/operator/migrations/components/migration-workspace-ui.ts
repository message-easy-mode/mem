import type { TranslationKey } from "@/app/i18n/messages"
import type {
  MigrationWorkspaceActivityItem,
  MigrationWorkspaceEvidenceCategory,
  MigrationWorkspaceOperationSummary,
  MigrationWorkspaceStage,
} from "@/features/operator/migrations/api/migration-workspace"

export const migrationStageOrder = [
  "create-and-upload-package",
  "review-old-server",
  "prepare-and-test",
  "create-new-server",
  "make-new-server-live",
  "finish-migration",
] as const

const stageTitleKeys: Record<string, TranslationKey> = {
  "create-and-upload-package": "migrationWorkspace.guide.stage.package.title",
  "review-old-server": "migrationWorkspace.guide.stage.review.title",
  "prepare-and-test": "migrationWorkspace.guide.stage.prepare.title",
  "create-new-server": "migrationWorkspace.guide.stage.create.title",
  "make-new-server-live": "migrationWorkspace.guide.stage.live.title",
  "finish-migration": "migrationWorkspace.guide.stage.finish.title",
}

const stageDescriptionKeys: Record<string, TranslationKey> = {
  "create-and-upload-package": "migrationWorkspace.guide.stage.package.description",
  "review-old-server": "migrationWorkspace.guide.stage.review.description",
  "prepare-and-test": "migrationWorkspace.guide.stage.prepare.description",
  "create-new-server": "migrationWorkspace.guide.stage.create.description",
  "make-new-server-live": "migrationWorkspace.guide.stage.live.description",
  "finish-migration": "migrationWorkspace.guide.stage.finish.description",
}

const summaryKeys: Record<string, TranslationKey> = {
  "migration.workspace.staging-retiring": "migrationRetirement.inProgress",
  "migration.workspace.staging-retirement-needs-attention": "migrationRetirement.needsAttention",
  "migration.workspace.historical-compatibility": "migrationWorkspace.guide.summary.historical",
  "migration.workspace.completed": "migrationWorkspace.guide.summary.completed",
  "migration.workspace.accepted-baseline-backup-failed": "migrationWorkspace.guide.summary.baselineFailed",
  "migration.workspace.accepted-baseline-backup-pending": "migrationWorkspace.guide.summary.baselinePending",
  "migration.workspace.rollback-failed": "migrationWorkspace.guide.summary.rollbackFailed",
  "migration.workspace.rollback-in-progress": "migrationWorkspace.guide.summary.rollbackInProgress",
  "migration.workspace.ready-to-finish": "migrationWorkspace.guide.summary.readyToFinish",
  "migration.workspace.live-verification-running": "migrationWorkspace.guide.summary.liveVerificationRunning",
  "migration.workspace.live-verification-failed": "migrationWorkspace.guide.summary.liveVerificationFailed",
  "migration.workspace.public-awaiting-verification": "migrationWorkspace.guide.summary.publicAwaitingVerification",
  "migration.workspace.cutover-running": "migrationWorkspace.guide.summary.cutoverRunning",
  "migration.workspace.cutover-failed-compensated": "migrationWorkspace.guide.summary.cutoverCompensated",
  "migration.workspace.cutover-failed-recovery-required": "migrationWorkspace.guide.summary.cutoverRecovery",
  "migration.workspace.go-live-blocked": "migrationWorkspace.guided.goLiveBlocked",
  "migration.workspace.finish-blocked": "migrationWorkspace.finish.blockedDescription",
  "migration.workspace.go-live-ready": "migrationWorkspace.guide.summary.goLiveReady",
  "migration.workspace.review-go-live": "migrationWorkspace.guide.summary.reviewGoLive",
  "migration.workspace.private-creation-running": "migrationWorkspace.guide.summary.creationRunning",
  "migration.workspace.private-creation-failed": "migrationWorkspace.guide.summary.creationFailed",
  "migration.workspace.new-server-ready": "migrationWorkspace.guide.summary.newServerReady",
  "migration.workspace.new-server-details-blocked": "migrationWorkspace.guide.summary.newServerBlocked",
  "migration.workspace.new-server-plan-stale": "migrationWorkspace.guide.summary.newServerStale",
  "migration.workspace.prepare-new-server": "migrationWorkspace.guide.summary.prepareNewServer",
  "migration.workspace.confirm-tested-data": "migrationWorkspace.guide.summary.confirmTestedData",
  "migration.workspace.private-test-running": "migrationWorkspace.guide.summary.privateTestRunning",
  "migration.workspace.private-test-destroyed": "migrationWorkspace.guide.summary.privateTestDestroyed",
  "migration.workspace.private-test-failed-retained": "migrationWorkspace.guide.summary.privateTestFailedRetained",
  "migration.workspace.private-test-failed-cleaned": "migrationWorkspace.guide.summary.privateTestFailedCleaned",
  "migration.workspace.private-test-ready": "migrationWorkspace.guide.summary.privateTestReady",
  "migration.workspace.preparation-running": "migrationWorkspace.guide.summary.preparationRunning",
  "migration.workspace.preparation-failed": "migrationWorkspace.guide.summary.preparationFailed",
  "migration.workspace.source-blocked": "migrationWorkspace.guide.summary.sourceBlocked",
  "migration.workspace.source-ready": "migrationWorkspace.guide.summary.sourceReady",
  "migration.workspace.awaiting-package": "migrationWorkspace.guide.summary.awaitingPackage",
  "migration.workspace.package-request-expired": "migrationWorkspace.guide.summary.packageExpired",
  "migration.workspace.cancelled": "migrationWorkspace.guide.summary.cancelled",
  "migration.workspace.unknown-state": "migrationWorkspace.guide.summary.unknown",
}

const actionKeys: Record<string, TranslationKey> = {
  "upload-package": "migrationWorkspace.guide.action.uploadPackage",
  "review-source-issues": "migrationWorkspace.guide.action.reviewSource",
  "start-conversion": "migrationWorkspace.guide.action.prepareData",
  "retry-conversion": "migrationWorkspace.guide.action.retryPreparation",
  "start-private-test": "migrationWorkspace.guide.action.startPrivateTest",
  "retry-private-test": "migrationWorkspace.guide.action.retryPrivateTest",
  "review-staging-retirement": "migrationRetirement.review",
  "recreate-private-test": "migrationWorkspace.guide.action.recreatePrivateTest",
  // The guided creation dialog includes the tested-snapshot acknowledgement.
  "confirm-tested-data": "migrationWorkspace.guide.action.createNewServer",
  "prepare-new-server": "migrationWorkspace.guide.action.prepareNewServer",
  "edit-new-server-details": "migrationWorkspace.guide.action.editNewServer",
  "create-new-server": "migrationWorkspace.guide.action.createNewServer",
  "review-go-live": "migrationWorkspace.guide.action.reviewGoLive",
  "review-go-live-again": "migrationWorkspace.guide.action.reviewGoLiveAgain",
  "make-server-live": "migrationWorkspace.guide.action.makeLive",
  "run-live-checks": "migrationWorkspace.guide.action.runLiveChecks",
  "rerun-live-checks": "migrationWorkspace.guide.action.rerunLiveChecks",
  "finish-migration": "migrationWorkspace.guide.action.finish",
  "retry-baseline-backup": "migrationWorkspace.guide.action.retryBackup",
  "open-baseline-backup": "migrationWorkspace.guide.action.openBackup",
  "open-materialization-recovery": "migrationWorkspace.guide.action.openRecovery",
  "open-cutover-recovery": "migrationWorkspace.guide.action.openRecovery",
  "open-rollback-recovery": "migrationWorkspace.guide.action.openRecovery",
  "open-technical-details": "migrationWorkspace.guide.action.openTechnical",
  "create-new-migration": "migrationWorkspace.guide.action.createNewMigration",
}

// Presentation only: match the operation AND its recorded status. A technical
// currentStep can be retained after failure and must not imply a successful
// outcome. Detailed steps remain unchanged in the workspace/evidence contract.
const operationStatusKeys: Readonly<Record<string, Readonly<Record<string, TranslationKey>>>> = {
  "prepare-migration-data": {
    "pending": "migrationWorkspace.guide.operationStatus.preparationQueued",
    "running": "migrationWorkspace.guide.operationStatus.preparationRunning",
    "completed": "migrationWorkspace.guide.operationStatus.preparationCompleted",
    "completed-with-warnings": "migrationWorkspace.guide.operationStatus.preparationWarnings",
    "failed": "migrationWorkspace.guide.operationStatus.preparationFailed",
  },
  "private-test": {
    "retiring": "migrationRetirement.inProgress",
    "retirement-needs-attention": "migrationRetirement.needsAttention",
    "running": "migrationWorkspace.guide.operationStatus.privateTestRunning",
    "verified": "migrationWorkspace.guide.operationStatus.privateTestPassed",
    "failed": "migrationWorkspace.guide.operationStatus.privateTestFailed",
    "failed-cleaned": "migrationWorkspace.guide.operationStatus.privateTestCleaned",
    "destroyed": "migrationWorkspace.guide.operationStatus.privateTestRemoved",
  },
  "prepare-new-server": {
    "prepared": "migrationWorkspace.guide.operationStatus.serverDetailsReady",
    "blocked": "migrationWorkspace.guide.operationStatus.serverDetailsBlocked",
  },
  "create-new-server": {
    "materializing": "migrationWorkspace.guide.operationStatus.serverCreating",
    "failed": "migrationWorkspace.guide.operationStatus.serverCreationFailed",
  },
  "make-server-live": {
    "private-runtime-ready": "migrationWorkspace.guide.operationStatus.serverPrivateReady",
    "cutover-preview-ready": "migrationWorkspace.guide.operationStatus.routeReviewReady",
    "executing": "migrationWorkspace.guide.operationStatus.routesChanging",
    "public-awaiting-verification": "migrationWorkspace.guide.operationStatus.routesChanged",
    "failed": "migrationWorkspace.guide.operationStatus.routeChangeFailed",
  },
  "live-verification": {
    "running": "migrationWorkspace.guide.operationStatus.verificationRunning",
    "passed": "migrationWorkspace.guide.operationStatus.verificationPassed",
    "failed": "migrationWorkspace.guide.operationStatus.verificationFailed",
  },
  "migration-rollback": {
    "preview-ready": "migrationWorkspace.guide.operationStatus.rollbackReviewReady",
    "preview-blocked": "migrationWorkspace.guide.operationStatus.rollbackReviewBlocked",
    "executing": "migrationWorkspace.guide.operationStatus.rollbackRunning",
    "target-rolled-back-awaiting-source": "migrationWorkspace.guide.operationStatus.rollbackAwaitingSource",
    "failed": "migrationWorkspace.guide.operationStatus.rollbackFailed",
    "coordinated-rollback-complete": "migrationWorkspace.guide.operationStatus.rollbackCompleted",
  },
  "baseline-backup": {
    "pending": "migrationWorkspace.guide.operationStatus.backupPending",
    "created": "migrationWorkspace.guide.operationStatus.backupCreated",
    "failed": "migrationWorkspace.guide.operationStatus.backupFailed",
  },
}

const activityKeys: Record<string, TranslationKey> = {
  "migration-created": "migrationWorkspace.activity.migrationCreated",
  "package-uploaded": "migrationWorkspace.activity.packageUploaded",
  "package-verified": "migrationWorkspace.activity.packageVerified",
  "preparation-started": "migrationWorkspace.activity.preparationStarted",
  "preparation-finished": "migrationWorkspace.activity.preparationFinished",
  "candidate-verified": "migrationWorkspace.activity.candidateVerified",
  "private-test-started": "migrationWorkspace.activity.privateTestStarted",
  "private-test-finished": "migrationWorkspace.activity.privateTestFinished",
  "private-test-destroyed": "migrationWorkspace.activity.privateTestDestroyed",
  "tested-snapshot-confirmed": "migrationWorkspace.activity.snapshotConfirmed",
  "new-server-details-prepared": "migrationWorkspace.activity.detailsPrepared",
  "new-server-creation-started": "migrationWorkspace.activity.creationStarted",
  "new-server-created-privately": "migrationWorkspace.activity.createdPrivately",
  "go-live-review-prepared": "migrationWorkspace.activity.goLivePrepared",
  "go-live-started": "migrationWorkspace.activity.goLiveStarted",
  "go-live-finished": "migrationWorkspace.activity.goLiveFinished",
  "live-verification-started": "migrationWorkspace.activity.verificationStarted",
  "live-verification-finished": "migrationWorkspace.activity.verificationFinished",
  "rollback-started": "migrationWorkspace.activity.rollbackStarted",
  "rollback-finished": "migrationWorkspace.activity.rollbackFinished",
  "migration-accepted": "migrationWorkspace.activity.accepted",
  "baseline-backup-started": "migrationWorkspace.activity.backupStarted",
  "baseline-backup-finished": "migrationWorkspace.activity.backupFinished",
}

const evidenceKeys: Record<string, TranslationKey> = {
  "package-and-source": "migrationWorkspace.evidence.packageSource",
  "preparation-and-candidate": "migrationWorkspace.evidence.preparation",
  "private-test": "migrationWorkspace.evidence.privateTest",
  "production-authority": "migrationWorkspace.evidence.authority",
  "new-server-creation": "migrationWorkspace.evidence.creation",
  "route-change-and-verification": "migrationWorkspace.evidence.routes",
  "acceptance-and-retention": "migrationWorkspace.evidence.acceptance",
  "baseline-backup": "migrationWorkspace.evidence.backup",
}

export function stageTitleKey(code: string): TranslationKey {
  return stageTitleKeys[code] ?? "migrationWorkspace.guide.stage.unknown.title"
}

export function stageDescriptionKey(code: string): TranslationKey {
  return stageDescriptionKeys[code] ?? "migrationWorkspace.guide.stage.unknown.description"
}

export function summaryKey(code: string): TranslationKey {
  if (summaryKeys[code]) return summaryKeys[code]
  if (code.endsWith(".completed")) return "migrationWorkspace.guide.summary.stageCompleted"
  if (code.endsWith(".not-started")) return "migrationWorkspace.guide.summary.stageLocked"
  return "migrationWorkspace.guide.summary.unknown"
}

export function actionKey(code: string): TranslationKey {
  return actionKeys[code] ?? "migrationWorkspace.guide.action.continue"
}

export function operationSummaryKey(summary: MigrationWorkspaceOperationSummary): TranslationKey {
  const statuses = Object.hasOwn(operationStatusKeys, summary.operation)
    ? operationStatusKeys[summary.operation]
    : undefined
  return statuses && Object.hasOwn(statuses, summary.status)
    ? statuses[summary.status]
    : "migrationWorkspace.guide.operationStatus.technical"
}

export function activityKey(item: MigrationWorkspaceActivityItem): TranslationKey {
  return activityKeys[item.code] ?? "migrationWorkspace.activity.technical"
}

export function evidenceKey(category: MigrationWorkspaceEvidenceCategory): TranslationKey {
  return evidenceKeys[category.code] ?? "migrationWorkspace.evidence.technical"
}

export function initialOpenMigrationStage(stages: MigrationWorkspaceStage[]) {
  const attention = stages.find((stage) =>
    ["failed", "running", "blocked", "action-required"].includes(stage.state),
  )
  if (attention) return attention.code

  const ready = stages.find((stage) => stage.state === "ready")
  if (ready) return ready.code

  const latestCompleted = [...stages]
    .reverse()
    .find((stage) => stage.state === "completed")

  return latestCompleted?.code ?? stages.find((stage) => stage.unlocked)?.code ?? stages[0]?.code ?? ""
}

export function stageStateLabelKey(state: string): TranslationKey {
  if (state === "completed") return "migrationWorkspace.guide.state.completed"
  if (state === "ready") return "migrationWorkspace.guide.state.ready"
  if (state === "running") return "migrationWorkspace.guide.state.running"
  if (state === "failed") return "migrationWorkspace.guide.state.failed"
  if (state === "blocked") return "migrationWorkspace.guide.state.blocked"
  if (state === "action-required") return "migrationWorkspace.guide.state.actionRequired"
  if (state === "closed") return "migrationWorkspace.guide.state.closed"
  return "migrationWorkspace.guide.state.locked"
}
