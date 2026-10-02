import { describe, expect, it, vi } from "vitest"

import { startMigrationOperationWithReconciliation } from "./migration-start-reconciliation"

type Operation = { id: string; migrationId: string; status: string; retryOf: string | null }
const old: Operation = { id: "old", migrationId: "mig_test", status: "failed", retryOf: null }
const accepted: Operation = { id: "new", migrationId: "mig_test", status: "running", retryOf: "old" }

function options(list: () => Promise<Operation[]>, start: () => Promise<Operation>) {
  return {
    list,
    start,
    identity: (operation: Operation) => operation.id,
    matches: (operation: Operation) => operation.migrationId === "mig_test" && operation.retryOf === "old",
    isHttpProblem: (error: unknown) => error instanceof HttpProblem,
  }
}
class HttpProblem extends Error {}

describe("Migration start reconciliation", () => {
  it("observes before posting and sends a successful mutation once", async () => {
    const calls: string[] = []
    const list = vi.fn(async () => { calls.push("GET"); return [old] })
    const start = vi.fn(async () => { calls.push("POST"); return accepted })
    expect(await startMigrationOperationWithReconciliation(options(list, start))).toEqual(accepted)
    expect(calls).toEqual(["GET", "POST"])
  })

  it("adopts a newly accepted run after a lost response, not the historical failed run", async () => {
    const list = vi.fn<() => Promise<Operation[]>>()
      .mockResolvedValueOnce([old]).mockResolvedValue([accepted, old])
    const start = vi.fn(async () => { throw new TypeError("Failed to fetch") })
    expect(await startMigrationOperationWithReconciliation(options(list, start))).toEqual(accepted)
    expect(start).toHaveBeenCalledTimes(1)
    expect(list).toHaveBeenCalledTimes(2)
  })

  it("returns a newly failed operation as durable evidence rather than a transport error", async () => {
    const failed = { ...accepted, status: "failed-cleaned" }
    const list = vi.fn<() => Promise<Operation[]>>()
      .mockResolvedValueOnce([old]).mockResolvedValue([failed, old])
    expect(await startMigrationOperationWithReconciliation(options(list, async () => {
      throw new TypeError("Failed to fetch")
    }))).toEqual(failed)
  })

  it("does not swallow an authoritative HTTP problem or step-up rejection", async () => {
    const problem = new HttpProblem("step_up_required")
    const list = vi.fn(async () => [old])
    const start = vi.fn(async () => { throw problem })
    await expect(startMigrationOperationWithReconciliation(options(list, start))).rejects.toBe(problem)
    expect(list).toHaveBeenCalledTimes(1)
    expect(start).toHaveBeenCalledTimes(1)
  })

  it("does not send a mutation when the pre-start observation fails", async () => {
    const problem = new TypeError("Disconnected before POST")
    const start = vi.fn(async () => accepted)
    await expect(startMigrationOperationWithReconciliation(options(async () => {
      throw problem
    }, start))).rejects.toBe(problem)
    expect(start).not.toHaveBeenCalled()
  })

  it("allows bounded read retries while a newly accepted row becomes visible", async () => {
    const list = vi.fn<() => Promise<Operation[]>>()
      .mockResolvedValueOnce([old])
      .mockResolvedValueOnce([old])
      .mockResolvedValueOnce([old])
      .mockResolvedValueOnce([accepted, old])
    const start = vi.fn(async () => { throw new TypeError("Response lost") })
    expect(await startMigrationOperationWithReconciliation(options(list, start))).toEqual(accepted)
    expect(list).toHaveBeenCalledTimes(4)
    expect(start).toHaveBeenCalledTimes(1)
  })

  it("recovers when the first reconciliation GET also loses its connection", async () => {
    const list = vi.fn<() => Promise<Operation[]>>()
      .mockResolvedValueOnce([old])
      .mockRejectedValueOnce(new TypeError("Network changed"))
      .mockResolvedValueOnce([accepted, old])
    const start = vi.fn(async () => { throw new TypeError("Response lost") })
    expect(await startMigrationOperationWithReconciliation(options(list, start))).toEqual(accepted)
    expect(list).toHaveBeenCalledTimes(3)
    expect(start).toHaveBeenCalledTimes(1)
  })

  const ambiguousCases: [string, Operation[]][] = [
    ["only historical evidence", [old]],
    ["another migration", [{ ...accepted, migrationId: "mig_other" }, old]],
    ["a different retry", [{ ...accepted, retryOf: "some-other-run" }, old]],
    ["multiple possible new runs", [accepted, { ...accepted, id: "another" }, old]],
  ]
  it.each(ambiguousCases)("does not claim success from %s or automatically replay the POST", async (_label, rows) => {
    const problem = new TypeError("Failed to fetch")
    const list = vi.fn<() => Promise<Operation[]>>()
      .mockResolvedValueOnce([old]).mockResolvedValue(rows)
    const start = vi.fn(async () => { throw problem })
    await expect(startMigrationOperationWithReconciliation(options(list, start))).rejects.toBe(problem)
    expect(list).toHaveBeenCalledTimes(4)
    expect(start).toHaveBeenCalledTimes(1)
  })
})
