import { afterEach, describe, expect, it } from "vitest"
import { screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { renderWithProviders } from "@/test/render-with-providers"

import type { WorkflowStep } from "../api/install.types"
import { InstallCompletedOperationChecklist } from "./install-completed-operation-checklist"

afterEach(() => {
  window.localStorage.removeItem(LANGUAGE_STORAGE_KEY)
})

describe("InstallCompletedOperationChecklist", () => {
  it("renders only authoritative succeeded operations in sequence", () => {
    renderWithProviders(
      <InstallCompletedOperationChecklist
        steps={[
          workflowStep(2, "Second completed operation", "Succeeded"),
          workflowStep(4, "Failed operation", "Failed"),
          workflowStep(1, "First completed operation", "Succeeded"),
          workflowStep(3, "Pending handoff", "WaitingForUser"),
        ]}
      />,
    )

    const checklist = screen.getByRole("region", { name: "Completed operations" })
    const rows = within(checklist).getAllByTestId(/completed-operation-/)

    expect(rows).toHaveLength(2)
    expect(rows[0]).toHaveTextContent("First completed operation")
    expect(rows[1]).toHaveTextContent("Second completed operation")
    expect(within(checklist).queryByText("Pending handoff")).not.toBeInTheDocument()
    expect(within(checklist).queryByText("Failed operation")).not.toBeInTheDocument()
    expect(
      within(checklist).getByText("2 operations completed successfully."),
    ).toBeInTheDocument()
  })

  it("expands retained successful phase history without projecting incomplete phases", async () => {
    const user = userEvent.setup()
    const step = workflowStep(8, "Issue and import platform certificate", "Succeeded")
    step.progress = {
      schemaVersion: 1,
      installationId: "11111111-1111-1111-1111-111111111111",
      stepId: "88888888-8888-8888-8888-888888888888",
      stepSequence: 8,
      stepName: step.title,
      attemptNumber: 1,
      stepStatus: "Succeeded",
      phaseCode: "certificate.store",
      phaseStatus: "Succeeded",
      safeSummary: "Certificate stored.",
      stepStartedAtUtc: "2026-08-15T00:00:00Z",
      lastActivityAtUtc: "2026-08-15T00:01:00Z",
      phases: [
        {
          code: "certificate.prepare",
          status: "Succeeded",
          safeSummary: "Prepared certificate request.",
          startedAtUtc: "2026-08-15T00:00:00Z",
          completedAtUtc: "2026-08-15T00:00:10Z",
        },
        {
          code: "certificate.store",
          status: "Succeeded",
          safeSummary: "Stored certificate.",
          startedAtUtc: "2026-08-15T00:00:10Z",
          completedAtUtc: "2026-08-15T00:00:20Z",
        },
        {
          code: "certificate.npm-import",
          status: "Pending",
          safeSummary: "Stale supplemental phase that must not be projected.",
          startedAtUtc: null,
          completedAtUtc: null,
        },
      ],
    }

    renderWithProviders(<InstallCompletedOperationChecklist steps={[step]} />)

    const operation = screen.getByTestId("completed-operation-8")
    expect(operation.tagName).toBe("DETAILS")
    expect(operation).not.toHaveAttribute("open")

    await user.click(
      within(operation).getByText("Issue and import platform certificate"),
    )

    expect(operation).toHaveAttribute("open")
    expect(
      within(operation).getByText("Prepare TLS certificate request"),
    ).toBeInTheDocument()
    expect(within(operation).getByText("Store TLS certificate securely")).toBeInTheDocument()
    expect(
      within(operation).queryByText("Import TLS certificate into Nginx Proxy Manager"),
    ).not.toBeInTheDocument()
  })

  it("keeps a completed high-level operation when no retained phase history exists", () => {
    renderWithProviders(
      <InstallCompletedOperationChecklist
        steps={[workflowStep(4, "Create persistent volumes", "Succeeded")]}
      />,
    )

    const operation = screen.getByTestId("completed-operation-4")
    expect(operation.tagName).toBe("DIV")
    expect(operation).toHaveTextContent("Create persistent volumes")
  })

  it("localizes the completed summary in German", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    renderWithProviders(
      <InstallCompletedOperationChecklist
        steps={[workflowStep(1, "Validate install plan", "Succeeded")]}
      />,
    )

    expect(
      screen.getByRole("region", { name: "Abgeschlossene Vorgänge" }),
    ).toBeInTheDocument()
    expect(screen.getByText("1 Vorgang erfolgreich abgeschlossen.")).toBeInTheDocument()
  })
})

function workflowStep(
  order: number,
  title: string,
  status: WorkflowStep["status"],
): WorkflowStep {
  return {
    order,
    name: title,
    title,
    kind: "server-step",
    notes: null,
    category: "Installation",
    tags: [],
    requiresHumanAction: status === "WaitingForUser",
    humanActionPrompt: null,
    isCheckpoint: false,
    status,
    message: null,
    errorMessage: null,
    attemptCount: 1,
    startedAtUtc: "2026-08-15T00:00:00Z",
    completedAtUtc: status === "Succeeded" ? "2026-08-15T00:01:00Z" : null,
    progress: null,
  }
}
