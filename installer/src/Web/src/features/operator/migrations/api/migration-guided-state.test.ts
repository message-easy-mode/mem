import { beforeEach, describe, expect, it } from "vitest"
import { guidedWorkspace } from "../test/migration-guided-fixtures"
import { hasGuidedOperationObservation, isDefinitiveGuidedRejection, isGuidedActionAllowed, isGuidedCancellationAllowed,
  pendingGuidedOperationKey, readPendingGuidedOperation, writePendingGuidedOperation, type PendingGuidedOperation } from "./migration-guided-state"

beforeEach(() => sessionStorage.clear())

describe("guided Migration operation receipts", () => {
  const pending: PendingGuidedOperation = { migrationId: "mig_guided", operation: "private-test", before: "initial-private-test" }
  it("persists only operation identity and the prior server token across refresh", () => {
    writePendingGuidedOperation(pending.migrationId, pending)
    expect(readPendingGuidedOperation(pending.migrationId)).toEqual(pending)
    expect(Object.keys(JSON.parse(sessionStorage.getItem(pendingGuidedOperationKey(pending.migrationId))!)).sort())
      .toEqual(["before", "migrationId", "operation"])
    writePendingGuidedOperation(pending.migrationId, null)
    expect(readPendingGuidedOperation(pending.migrationId)).toBeNull()
  })
  it("rejects malformed, unknown-operation and cross-session receipts", () => {
    for (const value of ["broken", "{}", JSON.stringify({ ...pending, operation: "delete-host" }),
      JSON.stringify({ ...pending, migrationId: "other" })]) {
      sessionStorage.setItem(pendingGuidedOperationKey(pending.migrationId), value)
      expect(readPendingGuidedOperation(pending.migrationId)).toBeNull()
    }
  })
  it("does not confuse an unrelated revision or old success with this command", () => {
    const workspace = guidedWorkspace()
    workspace.guided.revision = "new-global-revision"
    workspace.guided.operationRevisions["conversion"] = "new-conversion"
    expect(hasGuidedOperationObservation(pending, workspace)).toBe(false)
    workspace.guided.operationRevisions["private-test"] = "new-private-test"
    expect(hasGuidedOperationObservation(pending, workspace)).toBe(true)
    workspace.migration.migrationId = "another-session"
    expect(hasGuidedOperationObservation(pending, workspace)).toBe(false)
  })
  it.each([408, 409, 500, 503])("treats HTTP %s as uncertain rather than permission to repeat", (status) => {
    expect(isDefinitiveGuidedRejection({ status })).toBe(false)
  })
  it.each([400, 401, 403, 422])("preserves a definitive HTTP %s rejection and normal step-up handling", (status) => {
    expect(isDefinitiveGuidedRejection({ status })).toBe(true)
  })
  it("uses only the enabled current-stage action and fails closed on unknown state", () => {
    const workspace = guidedWorkspace()
    workspace.guided.detail.session.phase = "conversion"
    workspace.guided.detail.session.nextAction = "review-conversion"
    expect(isGuidedActionAllowed(workspace, "start-private-test")).toBe(true)
    expect(isGuidedActionAllowed(workspace, "finish-migration")).toBe(false)
    workspace.guided.nextAction!.enabled = false
    expect(isGuidedActionAllowed(workspace, "start-private-test")).toBe(false)
    workspace.guided.nextAction!.enabled = true
    workspace.guided.uncertaintyState = "unknown"
    expect(isGuidedActionAllowed(workspace, "start-private-test")).toBe(false)
  })
})


describe("cancellation uses the guided capability and its own terminal receipt", () => {
  const pending: PendingGuidedOperation = { migrationId: "mig_guided", operation: "cancel-migration", before: "before-cancel" }
  function cancellable() {
    const workspace = guidedWorkspace()
    workspace.guided.cancellation = { stateVersion: 2, lifecycleStatus: "active", canCancel: true,
      confirmationKind: "empty-session", blockedCode: null, blockedReason: null }
    workspace.guided.operationRevisions["cancel-migration"] = pending.before
    return workspace
  }
  it("requires the explicit capability and confirmation contract, not a session phase", () => {
    const workspace = cancellable()
    expect(isGuidedCancellationAllowed(workspace)).toBe(true)
    workspace.guided.cancellation!.confirmationKind = "progressed"
    expect(isGuidedCancellationAllowed(workspace)).toBe(true)
    workspace.guided.cancellation!.canCancel = false
    expect(isGuidedCancellationAllowed(workspace)).toBe(false)
    workspace.guided.cancellation = undefined
    expect(isGuidedCancellationAllowed(workspace)).toBe(false)
  })
  it("does not accept an unrelated update or an unconfirmed terminal claim", () => {
    const workspace = cancellable()
    workspace.guided.revision = "changed"
    workspace.guided.operationRevisions["upload-package"] = "new-upload"
    expect(hasGuidedOperationObservation(pending, workspace)).toBe(false)
    workspace.guided.operationRevisions["cancel-migration"] = "after-cancel"
    expect(hasGuidedOperationObservation(pending, workspace)).toBe(false)
    workspace.guided.cancellation!.lifecycleStatus = "cancelled"
    expect(hasGuidedOperationObservation(pending, workspace)).toBe(true)
    workspace.migration.migrationId = "other"
    expect(hasGuidedOperationObservation(pending, workspace)).toBe(false)
  })
  it("persists cancellation observation across reload, without a confirmation body", () => {
    writePendingGuidedOperation(pending.migrationId, pending)
    expect(readPendingGuidedOperation(pending.migrationId)).toEqual(pending)
    expect(Object.keys(JSON.parse(sessionStorage.getItem(pendingGuidedOperationKey(pending.migrationId))!)).sort())
      .toEqual(["before", "migrationId", "operation"])
  })
  it.each(["migration_session_state_stale", "migration_session_conversion_exists", "migration_session_conversion_active", "migration_session_package_state_not_eligible"])(
    "treats a definite cancellation rejection %s as rejected, but not a generic operation conflict", (code) => {
      expect(isDefinitiveGuidedRejection({ status: 409, code }, "cancel-migration")).toBe(true)
      expect(isDefinitiveGuidedRejection({ status: 409, code }, "private-test")).toBe(false)
    })
  it.each(["migration_session_package_cleanup_failed", "migration_session_progressed_cleanup_failed"])(
    "surfaces documented pre-commit cleanup failure %s without authorizing automatic replay", (code) => {
      const error = { status: 503, code }
      expect(isDefinitiveGuidedRejection(error, "cancel-migration")).toBe(true)
      expect(isDefinitiveGuidedRejection(error, "private-test")).toBe(false)
    })
  it.each([408, 409, 500, 503])("keeps undocumented cancellation HTTP %s outcomes uncertain", (status) => {
    expect(isDefinitiveGuidedRejection({ status, code: "unknown" }, "cancel-migration")).toBe(false)
  })
})
