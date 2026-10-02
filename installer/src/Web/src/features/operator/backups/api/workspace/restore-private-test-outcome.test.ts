import { describe, expect, it, vi } from "vitest"

import {
  captureRestorePrivateTestBaseline,
  classifyRestorePrivateTestOutcome,
  reconcileRestorePrivateTestOutcome,
} from "./restore-private-test-outcome"

import type { RestoreWorkspaceResponse } from "../types/restore-workspace.types"

function workspace(options?: {
  operationId?: string | null
  operationStatus?: string | null
  completedAtUtc?: string | null
}): RestoreWorkspaceResponse {
  const operationId = options?.operationId ?? null
  const operationStatus = options?.operationStatus ?? null
  const completedAtUtc = options?.completedAtUtc ?? null

  return {
    schemaVersion: 3,
    restoreSessionId: "restore-1",
    attempt: {
      id: "attempt-1",
      status: operationStatus === "running" ? "testing" : "ready",
      currentStage: operationStatus === "running" ? "private-test" : "ready",
      createdAtUtc: "2026-08-20T00:00:00Z",
      updatedAtUtc: "2026-08-20T00:00:01Z",
      terminalAtUtc: null,
      lastEventAtUtc: null,
      lastErrorCode: null,
      lastErrorSummary: null,
      warningCount: 0,
      errorCount: 0,
      currentOperationId: operationId,
    },
    source: {
      kind: "backup-catalog",
      stackSlug: "cleanroom-a",
      backupId: "backup-1",
      createdAtUtc: "2026-08-19T00:00:00Z",
      sizeBytes: 1024,
      matrixHost: "matrix-cleanroom-a.deltabox.dev",
      elementHost: "chat-cleanroom-a.deltabox.dev",
      validationStatus: "available",
      validationSummary: "Available",
      catalogEntryId: "catalog-1",
      sourceDisplayName: "cleanroom-a",
      sourceOriginKind: "local-captured",
      sourceDeleted: false,
    },
    target: {
      stackSlug: null,
      matrixHost: null,
      elementHost: null,
      availability: "not-selected",
      detail: "Not selected",
      claims: [],
    },
    overallStatus: {
      code: "ready",
      title: "Ready",
      description: "Ready",
      severity: "information",
      nextAction: null,
    },
    standardStages: [
      {
        code: "private-test",
        title: "Private test",
        description: "Private test",
        state:
          completedAtUtc
            ? "completed"
            : operationStatus === "running"
              ? "running"
              : operationStatus === "failed"
                ? "failed"
                : "optional",
        required: false,
        unlocked: true,
        completedAtUtc,
        primaryAction: null,
        secondaryActions: [],
        summary: "Private test",
        blockers: [],
        evidenceSummary: {
          itemCount: completedAtUtc ? 1 : 0,
          latestOccurredAtUtc: completedAtUtc,
          latestStatus: completedAtUtc ? "succeeded" : null,
        },
        operationSummary:
          operationId && operationStatus
            ? {
                operationId,
                operation: "restore.private-test",
                status: operationStatus,
                currentStep: operationStatus === "running" ? "private-staging" : null,
                requestedAtUtc: "2026-08-20T00:00:02Z",
                startedAtUtc: "2026-08-20T00:00:02Z",
                completedAtUtc,
              }
            : null,
        privateTestEvidence: completedAtUtc
          ? {
              sourceKind: "backup-catalog",
              catalogEntryId: "catalog-1",
              stagingId: "staging-1",
              matrixServerName: "matrix-cleanroom-a.deltabox.dev",
              status: "ready",
              privateOnly: true,
              dockerNetworkInternal: true,
              databaseImportSucceeded: true,
              synapseHealthPassed: true,
              requiresExplicitDestroy: true,
              completedAtUtc,
              stagingRuntimeStatus: "running",
              stagingRuntimeDestroyed: false,
              destroyAvailable: true,
              destroyedAtUtc: null,
            }
          : null,
      },
    ],
    advancedTools: [],
    verification: {
      status: "not-available",
      hasRun: false,
      allPassed: null,
      checkedAtUtc: null,
      checks: [],
      summary: "Not available",
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
  }
}

describe("private restore test outcome reconciliation", () => {
  it("does not treat unchanged historical operation state as a new outcome", () => {
    const previous = workspace({
      operationId: "operation-old",
      operationStatus: "failed",
    })
    const baseline = captureRestorePrivateTestBaseline(previous)

    expect(
      classifyRestorePrivateTestOutcome(previous, baseline),
    ).toBeNull()
  })

  it("detects a newly accepted running private-test operation", () => {
    const baseline = captureRestorePrivateTestBaseline(workspace())

    expect(
      classifyRestorePrivateTestOutcome(
        workspace({
          operationId: "operation-new",
          operationStatus: "running",
        }),
        baseline,
      )?.kind,
    ).toBe("running")
  })

  it("detects newer durable private-test completion evidence", () => {
    const previous = workspace({
      operationId: "operation-old",
      operationStatus: "failed",
    })
    const baseline = captureRestorePrivateTestBaseline(previous)

    expect(
      classifyRestorePrivateTestOutcome(
        workspace({
          operationId: "operation-new",
          operationStatus: "succeeded",
          completedAtUtc: "2026-08-20T00:00:30Z",
        }),
        baseline,
      )?.kind,
    ).toBe("completed")
  })

  it("detects a genuinely new terminal private-test failure", () => {
    const baseline = captureRestorePrivateTestBaseline(workspace())

    expect(
      classifyRestorePrivateTestOutcome(
        workspace({
          operationId: "operation-new",
          operationStatus: "failed",
        }),
        baseline,
      )?.kind,
    ).toBe("failed")
  })

  it("tolerates transient read failures and returns the authoritative running state", async () => {
    const baseline = captureRestorePrivateTestBaseline(workspace())
    const readWorkspace = vi
      .fn()
      .mockRejectedValueOnce(new TypeError("Failed to fetch"))
      .mockResolvedValueOnce(
        workspace({
          operationId: "operation-new",
          operationStatus: "running",
        }),
      )

    const result = await reconcileRestorePrivateTestOutcome(
      "restore-1",
      baseline,
      {
        attempts: 2,
        delayMs: 0,
        readWorkspace,
        wait: async () => undefined,
      },
    )

    expect(result?.kind).toBe("running")
    expect(readWorkspace).toHaveBeenCalledTimes(2)
  })
})
