import { useCallback, useState, type ReactElement, type ReactNode } from "react"
import { renderWithProviders } from "@/test/render-with-providers"
import type { MigrationSessionDetail } from "../api/migration-sessions"
import type { MigrationWorkspaceGuidedState, MigrationWorkspaceResponse } from "../api/migration-workspace"
import { MigrationGuidedStateProvider } from "../components/migration-guided-state-context"

export const guidedStages = ["create-and-upload-package", "review-old-server", "prepare-and-test",
  "create-new-server", "make-new-server-live", "finish-migration"] as const

export function guidedDetail(migrationId = "mig_guided"): MigrationSessionDetail {
  return {
    session: {
      migrationId, displayName: "Guided migration", sourceAdapter: "mem-v010", sourceDisplay: "Message Easy Mode 0.1.0",
      phase: "source-assessment", status: "package-validated", nextAction: "review-source",
      createdAtUtc: "2026-09-20T00:00:00Z", updatedAtUtc: "2026-09-20T00:01:00Z",
      blockerCount: 0, warningCount: 0, advisoryCount: 0, needsAttention: false, sourceCount: 1, stackCount: 1,
      historicalCompatibility: { usesLegacyNeutralImportContract: false, legacyContractVersion: null,
        legacyContractStatus: null, legacyManifestSha256: null, usesCatalogRestorePath: false,
        catalogEntryCount: 0, restoreSessionCount: 0 },
    },
    package: { transferMode: "encrypted", status: "package-validated", fileName: "capture.zip.age", sizeBytes: 1024,
      encryptedSha256: "a".repeat(64), decryptedSha256: "b".repeat(64),
      uploadedAtUtc: "2026-09-20T00:00:00Z", validatedAtUtc: "2026-09-20T00:01:00Z", expiresAtUtc: null,
      ageRecipient: null, recipientFingerprint: null, archiveMigrationId: "source-capture", archiveSourceProduct: "MatrixEasyMode",
      archiveSourceVersion: "0.1.0", archiveStackCount: 1 },
    packageRevisions: [], sources: [], findings: [], linkedObjects: [],
  }
}

type WorkspaceOptions = {
  migrationId?: string
  stage?: typeof guidedStages[number]
  state?: string
  action?: string | null
  detail?: MigrationSessionDetail
  guided?: Partial<MigrationWorkspaceGuidedState>
}

/** Explicit server fixture. No specialist API is consulted to construct this snapshot. */
export function guidedWorkspace({ migrationId = "mig_guided", stage = "prepare-and-test", state = "ready",
  action = "start-private-test", detail = guidedDetail(migrationId), guided = {} }: WorkspaceOptions = {}): MigrationWorkspaceResponse {
  const stageIndex = guidedStages.indexOf(stage)
  const nextAction = action ? { code: action, enabled: true, relatedStage: stage } : null
  const operationRevisions = Object.fromEntries(["upload-package", "conversion", "private-test", "create-new-server",
    "review-go-live", "make-server-live", "finish-migration", "baseline-backup"].map((key) => [key, `initial-${key}`]))
  const snapshot = {
    revision: "initial-workspace", currentStageCode: stage, stageState: state, currentOperation: null, nextAction,
    blocker: null, uncertaintyState: "known" as const, detail, productionAuthorized: stageIndex >= 3,
    productionAuthorityType: stageIndex >= 3 ? "operator-attested-snapshot" : null,
    conversionAttemptId: null, conversionStatus: null, hasVerifiedCandidate: stageIndex >= 2,
    stagingRunId: null, stagingStatus: null, stagingRetained: false, adoptionPlanId: null,
    privateRuntimeReady: stageIndex >= 4, cutoverPreviewId: null, cutoverPreviewStatus: null,
    publicRoutesCreated: stageIndex >= 5, runtimePromotionCompleted: stageIndex >= 5,
    accepted: false, acceptanceId: null, baselineBackupStatus: "not-created", baselineBackupId: null,
    baselineCatalogEntryId: null, operationRevisions, ...guided,
  }
  return {
    schemaVersion: 2, guided: snapshot,
    migration: { migrationId, displayName: detail.session.displayName, status: detail.session.status,
      createdAtUtc: detail.session.createdAtUtc, updatedAtUtc: detail.session.updatedAtUtc },
    source: { adapter: "mem-v010", displayName: "Message Easy Mode 0.1.0", product: "MatrixEasyMode", productVersion: "0.1.0",
      matrixServerName: "matrix.example.test", usersCount: 3, roomsCount: 2, eventsCount: 28,
      blockerCount: 0, warningCount: 0, advisoryCount: 0 },
    target: { planPrepared: stageIndex >= 3, status: "private-runtime-ready", stackSlug: "tester", displayName: "Tester",
      matrixHost: "matrix.example.test", elementHost: "element.example.test" },
    overallStatus: { code: "migration.workspace.ready-for-private-test", severity: "info", currentStageCode: stage, nextAction },
    stages: guidedStages.map((code, index) => ({ code, state: index === stageIndex ? state : index < stageIndex ? "completed" : "not-started",
      unlocked: index <= stageIndex, completedAtUtc: null, primaryAction: index === stageIndex ? nextAction : null,
      secondaryActions: [], summaryCode: "migration.workspace.ready-for-private-test", problems: [],
      evidenceSummary: { itemCount: 0, latestOccurredAtUtc: null, latestStatus: null }, operationSummary: null, failureOutcome: null })),
    currentOperation: null, activity: [], verification: { status: "not-run", hasRun: false, allPassed: null,
      checkCount: 0, failedCheckCount: 0, checkedAtUtc: null },
    retention: { status: "not-created", retainUntilUtc: null, automaticDeletionAllowed: false },
    evidence: { categories: [] }, advancedTools: [], warnings: [],
  }
}

/** Upgrade an existing workspace test fixture without deriving anything from browser caches. */
export function withGuidedSnapshot(workspace: Omit<MigrationWorkspaceResponse, "guided">,
  detail: MigrationSessionDetail, overrides: Partial<MigrationWorkspaceGuidedState> = {}): MigrationWorkspaceResponse {
  const current = workspace.stages.find((stage) => stage.code === workspace.overallStatus.currentStageCode)!
  const fixture = guidedWorkspace({ migrationId: workspace.migration.migrationId,
    stage: current.code as typeof guidedStages[number], state: current.state, action: current.primaryAction?.code ?? null, detail })
  return { ...workspace, schemaVersion: 2, guided: { ...fixture.guided, ...overrides } }
}

/** Refresh simulates one canonical workspace response, independent of technical-evidence handlers. */
export function renderGuided(ui: ReactElement, getWorkspace: () => MigrationWorkspaceResponse) {
  let trigger: () => Promise<unknown> = async () => undefined
  function Harness({ children }: { children: ReactNode }) {
    const [snapshot, setSnapshot] = useState(() => structuredClone(getWorkspace()))
    const refresh = useCallback(async () => {
      const next = structuredClone(getWorkspace())
      setSnapshot(next)
      return next
    }, [])
    trigger = refresh
    return <MigrationGuidedStateProvider workspace={snapshot} refresh={refresh}>{children}</MigrationGuidedStateProvider>
  }
  const result = renderWithProviders(<Harness>{ui}</Harness>)
  return { ...result, refresh: () => trigger(), rerender: (next: ReactElement) => result.rerender(<Harness>{next}</Harness>) }
}
