import { act, fireEvent, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"
import { guidedWorkspace, renderGuided } from "../test/migration-guided-fixtures"
import { isMigrationOutcomeUncertain, readPendingGuidedOperation, writePendingGuidedOperation } from "../api/migration-guided-state"
import { useMigrationGuidedState } from "./migration-guided-state-context"

beforeEach(() => sessionStorage.clear())

function Command({ command }: { command: () => Promise<unknown> }) {
  const guide = useMigrationGuidedState()
  return <><span data-testid="stage">{guide.workspace.guided.stageState}</span>
    <button disabled={!guide.allows("start-private-test")} onClick={() => {
      void guide.run("private-test", command).catch((error) => { if (!isMigrationOutcomeUncertain(error)) throw error })
    }}>Start private test</button></>
}

describe("one guided read-only reconciliation path", () => {
  it.each(["running", "completed", "failed"])("adopts server %s after a lost response, without repeating the mutation", async (state) => {
    let workspace = guidedWorkspace()
    const command = vi.fn(async () => {
      workspace = guidedWorkspace({ state, action: null, guided: {
        operationRevisions: { ...workspace.guided.operationRevisions, "private-test": "accepted-attempt-2" },
      } })
      throw new TypeError("Failed to fetch")
    })
    renderGuided(<Command command={command} />, () => workspace)
    await userEvent.setup().click(screen.getByRole("button", { name: "Start private test" }))
    await waitFor(() => expect(screen.getByTestId("stage")).toHaveTextContent(state))
    await waitFor(() => expect(readPendingGuidedOperation("mig_guided")).toBeNull())
    expect(command).toHaveBeenCalledTimes(1)
    expect(screen.queryByText("Failed to fetch")).not.toBeInTheDocument()
  })
  it("keeps a genuinely unknown result blocked across unmount/remount and permits only reads", async () => {
    let workspace = guidedWorkspace()
    const command = vi.fn(async () => { throw new TypeError("Failed to fetch") })
    const first = renderGuided(<Command command={command} />, () => workspace)
    await userEvent.setup().click(screen.getByRole("button", { name: "Start private test" }))
    await waitFor(() => expect(readPendingGuidedOperation("mig_guided")).not.toBeNull())
    first.unmount()
    const second = renderGuided(<Command command={command} />, () => workspace)
    expect(screen.getByRole("button", { name: "Start private test" })).toBeDisabled()
    await userEvent.setup().click(screen.getByRole("button", { name: "Check the server state again" }))
    expect(command).toHaveBeenCalledTimes(1)
    expect(readPendingGuidedOperation("mig_guided")).not.toBeNull()
    workspace = guidedWorkspace({ state: "failed", action: "retry-private-test", guided: {
      operationRevisions: { ...workspace.guided.operationRevisions, "private-test": "observed-failure" },
    } })
    await act(async () => { await second.refresh() })
    await waitFor(() => expect(readPendingGuidedOperation("mig_guided")).toBeNull())
    expect(command).toHaveBeenCalledTimes(1)
  })
  it("clears a retained receipt on reload when the same operation is already durably observed", async () => {
    const workspace = guidedWorkspace({ state: "running", action: null })
    writePendingGuidedOperation("mig_guided", { migrationId: "mig_guided", operation: "private-test", before: "older" })
    const command = vi.fn(async () => undefined)
    renderGuided(<Command command={command} />, () => workspace)
    await waitFor(() => expect(readPendingGuidedOperation("mig_guided")).toBeNull())
    expect(command).not.toHaveBeenCalled()
  })
  it("prevents a second submission while the first response is pending", async () => {
    let release!: () => void
    const command = vi.fn(() => new Promise<void>((resolve) => { release = resolve }))
    const workspace = guidedWorkspace()
    renderGuided(<Command command={command} />, () => workspace)
    const user = userEvent.setup()
    await user.click(screen.getByRole("button", { name: "Start private test" }))
    await waitFor(() => expect(command).toHaveBeenCalledTimes(1))
    expect(screen.getByRole("button", { name: "Start private test" })).toBeDisabled()
    await user.click(screen.getByRole("button", { name: "Start private test" }))
    expect(command).toHaveBeenCalledTimes(1)
    await act(async () => { release() })
  })
})

function PreviewCommand({ command }: { command: () => Promise<unknown> }) {
  const guide = useMigrationGuidedState()
  return <button disabled={!guide.allows("review-go-live")} onClick={() => {
    void guide.run("review-go-live", command).catch((error) => { if (!isMigrationOutcomeUncertain(error)) throw error })
  }}>Review go-live</button>
}

describe("bounded observation and idempotent results", () => {
  it("settles a received idempotent preview response even when its content token does not change", async () => {
    const workspace = guidedWorkspace({ stage: "make-new-server-live", action: "review-go-live" })
    const command = vi.fn(async () => undefined)
    renderGuided(<PreviewCommand command={command} />, () => workspace)
    await userEvent.setup().click(screen.getByRole("button", { name: "Review go-live" }))
    await waitFor(() => expect(readPendingGuidedOperation("mig_guided")).toBeNull())
    expect(screen.getByRole("button", { name: "Review go-live" })).toBeEnabled()
    expect(command).toHaveBeenCalledTimes(1)
  })

  it("stops the bounded reconciliation burst after six reads without expiring an unknown receipt", async () => {
    vi.useFakeTimers()
    const workspace = guidedWorkspace()
    const command = vi.fn(async () => { throw new TypeError("Failed to fetch") })
    const read = vi.fn(() => workspace)
    const view = renderGuided(<Command command={command} />, read)
    try {
      await act(async () => { fireEvent.click(screen.getByRole("button", { name: "Start private test" })) })
      const readsAfterSubmission = read.mock.calls.length
      for (let index = 0; index < 6; index++) {
        await act(async () => { await vi.advanceTimersByTimeAsync(1500) })
      }
      expect(read.mock.calls.length - readsAfterSubmission).toBe(6)
      expect(screen.getByText("The operation result is not yet known")).toBeInTheDocument()
      await act(async () => { await vi.advanceTimersByTimeAsync(30_000) })
      expect(read.mock.calls.length - readsAfterSubmission).toBe(6)
      expect(command).toHaveBeenCalledTimes(1)
      expect(readPendingGuidedOperation("mig_guided")).not.toBeNull()
      expect(screen.getByRole("button", { name: "Start private test" })).toBeDisabled()
    } finally { view.unmount(); vi.useRealTimers() }
  })
})
