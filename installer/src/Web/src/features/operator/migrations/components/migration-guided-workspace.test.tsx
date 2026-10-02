import { cleanup, screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { MemoryRouter } from "react-router-dom"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { LANGUAGE_STORAGE_KEY, translate } from "@/app/i18n/i18n-core"
import type { UiLanguage } from "@/app/i18n/messages"
import type { MigrationWorkspaceOperationSummary, MigrationWorkspaceResponse } from "../api/migration-workspace"
import { guidedWorkspace } from "../test/migration-guided-fixtures"
import { renderWithProviders } from "@/test/render-with-providers"
import { MigrationGuidedWorkspace } from "./migration-guided-workspace"

function operationSummary(operation: string, status: string, currentStep: string | null): MigrationWorkspaceOperationSummary {
  return {
    operationId: "operation-copy-test",
    operation,
    status,
    currentStep,
    requestedAtUtc: "2026-09-21T00:00:00Z",
    startedAtUtc: null,
    completedAtUtc: null,
  }
}

function currentStage(workspace: MigrationWorkspaceResponse) {
  return workspace.stages.find((stage) => stage.code === workspace.overallStatus.currentStageCode)!
}

function stageHeader(code: string) {
  return screen.getAllByRole("button").find((button) => button.getAttribute("aria-controls") === `migration-stage-${code}`)!
}

function renderWorkspace(workspace: MigrationWorkspaceResponse) {
  const renderStage = vi.fn(() => <div data-testid="existing-stage-body">Existing stage controls</div>)
  const view = renderWithProviders(
    <MemoryRouter>
      <MigrationGuidedWorkspace
        workspace={workspace}
        renderStage={renderStage}
        evidenceDetails={<pre data-testid="technical-evidence">{JSON.stringify(currentStage(workspace).operationSummary)}</pre>}
        advancedContent={<div>Existing recovery controls</div>}
      />
    </MemoryRouter>,
  )
  return { ...view, renderStage }
}

afterEach(() => {
  cleanup()
  window.localStorage.removeItem(LANGUAGE_STORAGE_KEY)
  document.documentElement.lang = ""
})

describe.each(["en", "de"] as const)("guided Migration copy (%s)", (language: UiLanguage) => {
  beforeEach(() => window.localStorage.setItem(LANGUAGE_STORAGE_KEY, language))
  const t = (key: Parameters<typeof translate>[1]) => translate(language, key)

  it("shows one matching Create new server next-action label without replacing stage controls", () => {
    const workspace = guidedWorkspace({ stage: "create-new-server", action: "confirm-tested-data" })
    const before = structuredClone(workspace)
    const { renderStage } = renderWorkspace(workspace)
    const nextAction = `${t("migrationWorkspace.guide.nextAction")}: ${t("migrationWorkspace.privateServer.create")}`

    expect(stageHeader("create-new-server")).toHaveTextContent(nextAction)
    const journey = screen.getByText(t("migrationWorkspace.header.oldServer")).closest('[data-slot="card"]')
    if (!(journey instanceof HTMLElement)) {
      throw new Error("Expected the old-server label to be inside the journey summary card")
    }
    expect(within(journey).queryByText(nextAction)).not.toBeInTheDocument()
    expect(screen.getAllByText(nextAction)).toHaveLength(1)
    expect(screen.queryByText(t("migrationWorkspace.guide.stageSummary"))).not.toBeInTheDocument()
    expect(screen.queryByText(/Use tested snapshot|Getesteten Snapshot verwenden/)).not.toBeInTheDocument()
    expect(screen.getByTestId("existing-stage-body")).toBeInTheDocument()
    expect(renderStage).toHaveBeenCalledWith(currentStage(workspace))
    expect(workspace).toEqual(before)
    expect(workspace.guided.nextAction?.code).toBe("confirm-tested-data")
  })

  it.each([
    { stage: "prepare-and-test", state: "ready", operation: "prepare-migration-data", status: "completed", step: "candidate-created",
      english: "Migration data prepared", german: "Migrationsdaten vorbereitet", badge: "migrationWorkspace.guide.state.ready" },
    { stage: "create-new-server", state: "running", operation: "create-new-server", status: "materializing", step: "materializing",
      english: "Creating the new server", german: "Neuer Server wird erstellt", badge: "migrationWorkspace.guide.state.running" },
    { stage: "make-new-server-live", state: "ready", operation: "make-server-live", status: "cutover-preview-ready", step: "cutover-preview-ready",
      english: "Public route review ready", german: "Prüfung der öffentlichen Routen bereit", badge: "migrationWorkspace.guide.state.ready" },
    { stage: "finish-migration", state: "completed", operation: "baseline-backup", status: "created", step: "created",
      english: "First native backup created", german: "Erste native Sicherung erstellt", badge: "migrationWorkspace.guide.state.completed" },
  ] as const)("shows readable activity for $operation / $status and preserves the stage state", ({ stage, state, operation, status, step, english, german, badge }) => {
    const workspace = guidedWorkspace({ stage, state, action: null })
    currentStage(workspace).operationSummary = operationSummary(operation, status, step)
    const before = structuredClone(workspace)
    renderWorkspace(workspace)

    const label = screen.getByText(language === "en" ? english : german)
    const activityRow = label.parentElement!
    expect(within(activityRow).getByText(t("migrationWorkspace.guide.operation"))).toBeInTheDocument()
    expect(screen.queryByText(step, { exact: true })).not.toBeInTheDocument()
    expect(within(stageHeader(stage)).getByText(t(badge))).toBeInTheDocument()
    expect(Boolean(activityRow.querySelector("svg.animate-spin"))).toBe(state === "running")
    expect(workspace).toEqual(before)
  })

  it("keeps unknown activity neutral and retains the supplied technical evidence", async () => {
    const workspace = guidedWorkspace({ stage: "finish-migration", state: "action-required", action: "open-technical-details" })
    currentStage(workspace).operationSummary = operationSummary("future-operation", "created", "created")
    const before = structuredClone(workspace)
    renderWorkspace(workspace)

    expect(screen.getByText(t("migrationWorkspace.guide.operationStatus.technical"))).toBeInTheDocument()
    expect(screen.queryByText(t("migrationWorkspace.guide.operationStatus.backupCreated"))).not.toBeInTheDocument()
    expect(within(stageHeader("finish-migration")).getByText(t("migrationWorkspace.guide.state.actionRequired"))).toBeInTheDocument()
    await userEvent.setup().click(screen.getByRole("tab", { name: t("migrationWorkspace.tabs.evidence") }))
    expect(screen.getByTestId("technical-evidence")).toHaveTextContent(JSON.stringify(currentStage(workspace).operationSummary))
    expect(workspace).toEqual(before)
  })

  it("shows backup failure rather than a retained successful technical step", () => {
    const workspace = guidedWorkspace({ stage: "finish-migration", state: "failed", action: "retry-baseline-backup" })
    currentStage(workspace).operationSummary = operationSummary("baseline-backup", "failed", "created")
    renderWorkspace(workspace)

    expect(screen.getByText(t("migrationWorkspace.guide.operationStatus.backupFailed"))).toBeInTheDocument()
    expect(screen.queryByText(t("migrationWorkspace.guide.operationStatus.backupCreated"))).not.toBeInTheDocument()
    expect(within(stageHeader("finish-migration")).getByText(t("migrationWorkspace.guide.state.failed"))).toBeInTheDocument()
  })

  it("does not invent an activity when the server supplies no operation summary", () => {
    renderWorkspace(guidedWorkspace())
    expect(screen.queryByText(t("migrationWorkspace.guide.operation"))).not.toBeInTheDocument()
    expect(screen.queryByText(t("migrationWorkspace.guide.operationStatus.technical"))).not.toBeInTheDocument()
  })

  it.each(["ready", "running", "completed"])("does not add another routine status panel for %s", (state) => {
    const workspace = guidedWorkspace({ state, action: null })
    renderWorkspace(workspace)
    expect(screen.queryByText(t("migrationWorkspace.guide.stageSummary"))).not.toBeInTheDocument()
    expect(screen.getByTestId("existing-stage-body")).toBeInTheDocument()
  })

  it.each(["ready", "running", "completed"] as const)("keeps the %s guide marker aligned and consistently sized", (state) => {
    const workspace = guidedWorkspace({ stage: "prepare-and-test", state, action: null })
    renderWorkspace(workspace)

    const marker = stageHeader("prepare-and-test").firstElementChild
    expect(marker).toHaveClass("mt-0.5", "h-9", "w-9", "items-center", "justify-center", "leading-none")

    const icon = marker?.querySelector("svg")
    if (state === "running" || state === "completed") {
      expect(icon).toHaveClass("h-5", "w-5", "shrink-0")
    } else {
      expect(icon).toBeNull()
      expect(marker).toHaveTextContent("3")
    }
  })

  it.each(["blocked", "action-required", "failed", "closed", "unknown"])("keeps important %s state visible", (state) => {
    const workspace = guidedWorkspace({ state, action: null })
    currentStage(workspace).problems.push({ code: "operator-review", severity: "warning", detail: "Read the retained evidence before continuing." })
    renderWorkspace(workspace)
    expect(screen.getByRole("alert")).toHaveTextContent("Read the retained evidence before continuing.")
    expect(screen.getByTestId("existing-stage-body")).toBeInTheDocument()
  })

  it("preserves mutation, compensation and observed-state failure evidence", () => {
    const workspace = guidedWorkspace({ state: "failed", action: "open-cutover-recovery" })
    currentStage(workspace).failureOutcome = {
      failureCode: "cutover_failed", failedComponent: "route-publication", mutationState: "attempted",
      compensationState: "unconfirmed", observedCurrentState: "requires-inspection", safeToRetry: false,
      safeStateSummaryCode: "migration.workspace.cutover-failed-recovery-required", nextAction: null,
    }
    const before = structuredClone(workspace)
    renderWorkspace(workspace)
    const alert = screen.getByRole("alert")
    expect(alert).toHaveTextContent("attempted")
    expect(alert).toHaveTextContent("unconfirmed")
    expect(alert).toHaveTextContent("requires-inspection")
    expect(workspace).toEqual(before)
  })

})
