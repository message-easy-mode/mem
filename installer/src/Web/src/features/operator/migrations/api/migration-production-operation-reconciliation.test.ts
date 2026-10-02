import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"
import {
  createMigrationPrivateServer,
  getMigrationProductionAdoptionState,
  makeMigrationServerLive,
  MigrationProductionAdoptionProblemError,
  type MigrationProductionAdoptionPlan,
  type MigrationProductionAdoptionState,
} from "./migration-production-adoption"
import { createPrivateServerWithReconciliation, makeServerLiveWithReconciliation } from "./migration-production-operation-reconciliation"

vi.mock("./migration-production-adoption", async (original) => {
  const actual = await original<typeof import("./migration-production-adoption")>()
  return {
    ...actual,
    getMigrationProductionAdoptionState: vi.fn(),
    createMigrationPrivateServer: vi.fn(),
    makeMigrationServerLive: vi.fn(),
  }
})

const get = vi.mocked(getMigrationProductionAdoptionState)
const create = vi.mocked(createMigrationPrivateServer)
const goLive = vi.mocked(makeMigrationServerLive)
const migrationId = "mig_handoff"
const createRequest = {
  targetStackSlug: "tester", elementPublicHost: "chat.example.test",
  confirmVerifiedSnapshotIsAuthoritative: true,
  confirmLaterSourceWritesAreNotIncluded: true,
  confirmCreatePrivateServer: true,
}
const liveRequest = {
  confirmMovePublicTraffic: true, confirmStopUsingOldServer: true, confirmRunLiveVerification: true,
}
const transport = new TypeError("Failed to fetch")

// These unit fixtures model only the identity/evidence fields read by the
// reconciler. Rendering of the full plan is covered by the component tests.
function state({
  materializationId = null, executionId = null, verificationId = null,
  adoptionPlanId = "plan_handoff", targetStackSlug = "tester", id = migrationId, status = "prepared",
}: {
  materializationId?: string | null; executionId?: string | null; verificationId?: string | null
  adoptionPlanId?: string; targetStackSlug?: string; id?: string; status?: string
} = {}): MigrationProductionAdoptionState {
  return {
    source: "control-plane", migrationId: id, status, planPrepared: true, detail: status,
    plan: {
      adoptionPlanId, migrationId: id, targetStackSlug, elementPublicHost: "chat.example.test",
      materialization: { materializationId, status },
      cutover: { execution: { executionId, status } },
      productionVerification: { verificationId, status },
    } as MigrationProductionAdoptionPlan,
  }
}

beforeEach(() => { vi.resetAllMocks(); vi.useFakeTimers() })
afterEach(() => { vi.useRealTimers() })

async function expectUnconfirmed(operation: Promise<unknown>) {
  const rejected = expect(operation).rejects.toBe(transport)
  await vi.runAllTimersAsync()
  await rejected
}

describe("private creation and go-live response reconciliation", () => {
  it("returns the normal POST response without extra reconciliation reads", async () => {
    const ready = state({ materializationId: "mpm_new", status: "private-runtime-ready" })
    get.mockResolvedValue(state())
    create.mockResolvedValue(ready)
    expect(await createPrivateServerWithReconciliation(migrationId, createRequest)).toBe(ready)
    expect(get).toHaveBeenCalledTimes(1)
    expect(create).toHaveBeenCalledTimes(1)
    expect(create).toHaveBeenCalledWith(migrationId, createRequest)
  })

  it.each(["materializing", "private-runtime-ready", "materialization-failed"])(
    "adopts new matching %s evidence without pretending it succeeded or replaying the POST", async (status) => {
      const observed = state({ materializationId: "mpm_new", status })
      get.mockResolvedValueOnce(state()).mockResolvedValue(observed)
      create.mockRejectedValue(transport)
      expect(await createPrivateServerWithReconciliation(migrationId, createRequest)).toBe(observed)
      expect(create).toHaveBeenCalledTimes(1)
      expect(get).toHaveBeenCalledTimes(2)
    },
  )

  it.each([
    ["historical materialization", state({ materializationId: "mpm_old" }), state({ materializationId: "mpm_old" })],
    ["a prepared plan without accepted materialization", state(), state()],
    ["another target", state(), state({ materializationId: "mpm_new", targetStackSlug: "someone-else" })],
    ["another migration", state(), state({ materializationId: "mpm_new", id: "mig_other" })],
  ] as const)("does not reconcile %s", async (_name, before, after) => {
    get.mockResolvedValueOnce(before).mockResolvedValue(after)
    create.mockRejectedValue(transport)
    await expectUnconfirmed(createPrivateServerWithReconciliation(migrationId, createRequest))
    expect(create).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledTimes(4)
  })

  it.each([403, 409])("preserves an authoritative HTTP %s including step-up", async (status) => {
    const problem = new MigrationProductionAdoptionProblemError("Rejected", status, status === 403 ? "step_up_required" : "collision")
    get.mockResolvedValue(state())
    create.mockRejectedValue(problem)
    await expect(createPrivateServerWithReconciliation(migrationId, createRequest)).rejects.toBe(problem)
    expect(get).toHaveBeenCalledTimes(1)
    expect(create).toHaveBeenCalledTimes(1)
  })

  it("sends no creation POST when the initial observation is unavailable", async () => {
    get.mockRejectedValue(transport)
    await expect(createPrivateServerWithReconciliation(migrationId, createRequest)).rejects.toBe(transport)
    expect(create).not.toHaveBeenCalled()
  })

  it("does not infer a chosen target from default/null request values after response loss", async () => {
    get.mockResolvedValueOnce(state()).mockResolvedValue(state({ materializationId: "mpm_new" }))
    create.mockRejectedValue(transport)
    await expectUnconfirmed(createPrivateServerWithReconciliation(migrationId, { ...createRequest, targetStackSlug: null }))
  })

  it.each(["cutover-executing", "cutover-failed", "production-verification-passed"])(
    "adopts new same-plan %s evidence after go-live response loss", async (status) => {
      const observed = state({ executionId: "exec_new", status })
      get.mockResolvedValueOnce(state()).mockResolvedValue(observed)
      goLive.mockRejectedValue(transport)
      expect(await makeServerLiveWithReconciliation(migrationId, liveRequest)).toBe(observed)
      expect(goLive).toHaveBeenCalledTimes(1)
      expect(goLive).toHaveBeenCalledWith(migrationId, liveRequest)
    },
  )

  it("recognizes a new verification attempt on an existing public execution", async () => {
    const observed = state({ executionId: "exec_existing", verificationId: "verify_new", status: "production-verification-running" })
    get.mockResolvedValueOnce(state({ executionId: "exec_existing", verificationId: "verify_old" })).mockResolvedValue(observed)
    goLive.mockRejectedValue(transport)
    expect(await makeServerLiveWithReconciliation(migrationId, liveRequest)).toBe(observed)
    expect(goLive).toHaveBeenCalledTimes(1)
  })

  it.each([
    ["historical execution", state({ executionId: "old" }), state({ executionId: "old" })],
    ["replacement plan", state(), state({ executionId: "new", adoptionPlanId: "another-plan" })],
    ["another migration", state(), state({ executionId: "new", id: "another-migration" })],
  ] as const)("does not accept %s as evidence of this go-live", async (_name, before, after) => {
    get.mockResolvedValueOnce(before).mockResolvedValue(after)
    goLive.mockRejectedValue(transport)
    await expectUnconfirmed(makeServerLiveWithReconciliation(migrationId, liveRequest))
    expect(goLive).toHaveBeenCalledTimes(1)
    expect(get).toHaveBeenCalledTimes(4)
  })

  it("retains an authoritative go-live rejection instead of reconciling it away", async () => {
    const problem = new MigrationProductionAdoptionProblemError("Routes blocked", 409, "blocked")
    get.mockResolvedValue(state())
    goLive.mockRejectedValue(problem)
    await expect(makeServerLiveWithReconciliation(migrationId, liveRequest)).rejects.toBe(problem)
    expect(get).toHaveBeenCalledTimes(1)
  })
})
