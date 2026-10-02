import { describe, expect, it } from "vitest"

import {
  HostAgentProblemError,
  type HostAgentProblem,
} from "@/features/operator/backups/api/transport/host-agent"

import {
  getRestoreWorkspaceProblem,
  getRestoreWorkspaceProblemMessage,
  getRestoreWorkspaceTechnicalDetail,
} from "./restore-workspace-problems"

const restoreSessionId = "restore-problem-map-1"

function createProblem(code: string): HostAgentProblem {
  return {
    error: code.replaceAll(".", "_"),
    detail: `Raw detail for ${code}.`,
    message: {
      code,
      arguments: { restoreSessionId },
    },
  }
}

describe("Restore Workspace structured problems", () => {
  it.each([
    ["restore.attempt.not-found", "restoreWorkspace.api.attemptNotFound"],
    [
      "restore.workspace-request.invalid",
      "restoreWorkspace.api.workspaceRequestInvalid",
    ],
    [
      "restore.private-test.not-available",
      "restoreWorkspace.api.privateTestNotAvailable",
    ],
    [
      "restore.cancel.acknowledgement-required",
      "restoreWorkspace.api.cancelAcknowledgementRequired",
    ],
    ["restore.cancel.not-available", "restoreWorkspace.api.cancelNotAvailable"],
    [
      "restore.handover.acknowledgement-required",
      "restoreWorkspace.api.handoverAcknowledgementRequired",
    ],
    [
      "restore.handover.not-available",
      "restoreWorkspace.api.handoverNotAvailable",
    ],
  ] as const)("maps %s through an explicit local key", (code, key) => {
    expect(getRestoreWorkspaceProblemMessage(createProblem(code))).toEqual({
      key,
      values: { restoreSessionId },
    })
  })

  it("rejects unknown descriptors and incomplete required arguments", () => {
    expect(
      getRestoreWorkspaceProblemMessage(createProblem("restore.future-problem")),
    ).toBeUndefined()
    expect(
      getRestoreWorkspaceProblemMessage({
        message: { code: "restore.cancel.not-available" },
      }),
    ).toBeUndefined()
  })

  it("preserves raw detail while retaining the established Error fallback", () => {
    const problem = createProblem("restore.cancel.not-available")
    const error = new HostAgentProblemError("Existing transport string", problem)

    expect(getRestoreWorkspaceProblem(error)).toEqual(problem)
    expect(getRestoreWorkspaceTechnicalDetail(error)).toBe(problem.detail)
    expect(getRestoreWorkspaceTechnicalDetail(new Error("Network failed"))).toBe(
      "Network failed",
    )
  })
})
