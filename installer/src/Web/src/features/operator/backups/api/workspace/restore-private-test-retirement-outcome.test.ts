import { describe, expect, it } from "vitest"

import type { RestoreWorkspaceResponse } from "../types/restore-workspace.types"
import {
  classifyRestorePrivateTestRetirementOutcome,
  reconcileRestorePrivateTestRetirementOutcome,
} from "./restore-private-test-retirement-outcome"

const stagingId = "20260819-221224Z-b4838e09"

function workspaceWithRuntimeStatus(
  stagingRuntimeStatus: string,
  stagingRuntimeDestroyed: boolean | null,
): RestoreWorkspaceResponse {
  return {
    schemaVersion: 3,
    restoreSessionId: "restore-retirement",
    attempt: {
      id: "00000000-0000-0000-0000-000000000001",
      status: "ready",
      currentStage: "private-test",
      createdAtUtc: "2026-08-19T22:12:18Z",
      updatedAtUtc: "2026-08-19T22:40:00Z",
      terminalAtUtc: null,
      lastEventAtUtc: "2026-08-19T22:40:00Z",
      lastErrorCode: null,
      lastErrorSummary: null,
      warningCount: 0,
      errorCount: 0,
      currentOperationId: null,
    },
    source: {
      kind: "backup-catalog",
      stackSlug: "cleanroom-a",
      backupId: "20260819-035504Z",
      createdAtUtc: "2026-08-19T03:55:04Z",
      sizeBytes: 869068,
      matrixHost: "matrix-cleanroom-a.deltabox.dev",
      elementHost: "chat-cleanroom-a.deltabox.dev",
      validationStatus: "available",
      validationSummary: "Catalog payload is ready.",
      catalogEntryId: "backup-1",
      sourceDisplayName: "cleanroom-a",
      sourceOriginKind: "local-backup",
      sourceDeleted: false,
    },
    target: {
      stackSlug: null,
      matrixHost: null,
      elementHost: null,
      availability: "not-selected",
      detail: "No target is selected.",
      claims: [],
    },
    overallStatus: {
      code: "ready-to-continue",
      title: "Ready to continue",
      description: "The backup is ready.",
      severity: "information",
      nextAction: null,
    },
    standardStages: [
      {
        code: "private-test",
        title: "Private test",
        description: "",
        state: "completed",
        required: false,
        unlocked: true,
        completedAtUtc: "2026-08-19T22:12:47Z",
        primaryAction: null,
        secondaryActions: [],
        summary: "",
        blockers: [],
        evidenceSummary: {
          itemCount: 1,
          latestOccurredAtUtc: "2026-08-19T22:12:47Z",
          latestStatus: "successful",
        },
        operationSummary: null,
        privateTestEvidence: {
          sourceKind: "catalog",
          catalogEntryId: "backup-1",
          stagingId,
          matrixServerName: "matrix-cleanroom-a.deltabox.dev",
          status: "ready",
          privateOnly: true,
          dockerNetworkInternal: true,
          databaseImportSucceeded: true,
          synapseHealthPassed: true,
          requiresExplicitDestroy: true,
          completedAtUtc: "2026-08-19T22:12:47Z",
          stagingRuntimeStatus,
          stagingRuntimeDestroyed,
          destroyAvailable: stagingRuntimeDestroyed !== true,
          destroyedAtUtc:
            stagingRuntimeDestroyed === true
              ? "2026-08-19T22:40:00Z"
              : null,
        },
      },
    ],
    advancedTools: [],
    verification: {
      status: "not-run",
      hasRun: false,
      allPassed: null,
      checkedAtUtc: null,
      checks: [],
      summary: "Not run.",
    },
    evidence: {
      categories: [],
      latestFailure: null,
      latestSuccess: null,
    },
    logs: {
      totalEvents: 0,
      warningCount: 0,
      errorCount: 0,
      latestEvent: null,
      latestWarningOrError: null,
      supportReportAvailable: false,
      supportBundleAvailable: false,
      warnings: [],
    },
    warnings: [],
    cancellation: null,
  }
}

describe("Private Restore Test retirement outcome reconciliation", () => {
  it("classifies the matching runtime as destroyed", () => {
    const workspace = workspaceWithRuntimeStatus("destroyed", true)

    expect(
      classifyRestorePrivateTestRetirementOutcome(workspace, stagingId)?.kind,
    ).toBe("destroyed")
  })

  it("classifies a partial cleanup as needs attention", () => {
    const workspace = workspaceWithRuntimeStatus(
      "retirement-needs-attention",
      false,
    )

    expect(
      classifyRestorePrivateTestRetirementOutcome(workspace, stagingId)?.kind,
    ).toBe("needs-attention")
  })

  it("does not use a different staging runtime as proof", () => {
    const workspace = workspaceWithRuntimeStatus("destroyed", true)

    expect(
      classifyRestorePrivateTestRetirementOutcome(
        workspace,
        "different-staging-id",
      ),
    ).toBeNull()
  })

  it("polls through retained state until retirement is confirmed", async () => {
    const retained = workspaceWithRuntimeStatus("retained", false)
    const destroyed = workspaceWithRuntimeStatus("destroyed", true)
    let reads = 0

    const outcome = await reconcileRestorePrivateTestRetirementOutcome(
      "restore-retirement",
      stagingId,
      {
        attempts: 3,
        delayMs: 0,
        readWorkspace: async () => {
          reads += 1
          return reads < 2 ? retained : destroyed
        },
        wait: async () => {},
      },
    )

    expect(outcome?.kind).toBe("destroyed")
    expect(reads).toBe(2)
  })

  it("tolerates a transient read failure while reconciliation remains bounded", async () => {
    const destroyed = workspaceWithRuntimeStatus("destroyed", true)
    let reads = 0

    const outcome = await reconcileRestorePrivateTestRetirementOutcome(
      "restore-retirement",
      stagingId,
      {
        attempts: 3,
        delayMs: 0,
        readWorkspace: async () => {
          reads += 1
          if (reads === 1) {
            throw new Error("temporary network failure")
          }
          return destroyed
        },
        wait: async () => {},
      },
    )

    expect(outcome?.kind).toBe("destroyed")
    expect(reads).toBe(2)
  })
})
