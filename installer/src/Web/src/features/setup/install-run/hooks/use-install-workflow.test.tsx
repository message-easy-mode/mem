import { http, HttpResponse } from "msw"
import { useState } from "react"
import { act, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import { useInstallWorkflow } from "./use-install-workflow"

const installationId = "8edbb383-32aa-4aa7-8f55-e6cabf52efff"

function WorkflowProbe() {
  const install = useInstallWorkflow(installationId)
  const [clicks, setClicks] = useState(0)

  return (
    <div>
      <div data-testid="workflow-status">
        {install.workflow?.installationStatus ?? "loading"}
      </div>
      <button
        type="button"
        onClick={() => {
          setClicks((value) => value + 1)
          install.startOrContinue()
        }}
      >
        Continue installation
      </button>
      <div data-testid="click-count">{clicks}</div>
    </div>
  )
}

describe("useInstallWorkflow server-owned execution", () => {
  it("does not start a Draft installation merely because activity UI is opened", async () => {
    let runRequests = 0

    server.use(
      http.get(`/api/setup/install-plans/${installationId}`, () =>
        HttpResponse.json(installation("Draft")),
      ),
      http.get(`/api/setup/install-runs/${installationId}/steps`, () =>
        HttpResponse.json([]),
      ),
      http.post(`/api/setup/install-runs/${installationId}/run`, () => {
        runRequests += 1
        return HttpResponse.json({
          installationId,
          accepted: true,
          status: "Running",
          message: "queued",
        })
      }),
    )

    renderWithProviders(<WorkflowProbe />)

    await waitFor(() =>
      expect(screen.getByTestId("workflow-status")).toHaveTextContent("Draft"),
    )
    await settleEffects()
    expect(runRequests).toBe(0)
  })

  it("does not resume a Running installation from the browser after refresh", async () => {
    let runRequests = 0

    server.use(
      http.get(`/api/setup/install-plans/${installationId}`, () =>
        HttpResponse.json(installation("Running")),
      ),
      http.get(`/api/setup/install-runs/${installationId}/steps`, () =>
        HttpResponse.json([
          {
            id: "53da8b3a-90a9-4d3a-b007-5a17f0f5953a",
            stepName: "Start Postgres",
            sequence: 5,
            status: "Running",
            message: "Running...",
            errorMessage: null,
            attemptCount: 1,
            startedAtUtc: "2026-08-11T03:00:00Z",
            completedAtUtc: null,
          },
        ]),
      ),
      http.post(`/api/setup/install-runs/${installationId}/run`, () => {
        runRequests += 1
        return HttpResponse.json({
          installationId,
          accepted: true,
          status: "Running",
          message: "already owned",
        })
      }),
    )

    renderWithProviders(<WorkflowProbe />)

    await waitFor(() =>
      expect(screen.getByTestId("workflow-status")).toHaveTextContent("Running"),
    )
    await settleEffects()
    expect(runRequests).toBe(0)
  })

  it("refreshes durable installation state when another browser tab gains focus", async () => {
    let currentStatus = "Failed"
    let installationReads = 0

    server.use(
      http.get(`/api/setup/install-plans/${installationId}`, () => {
        installationReads += 1
        return HttpResponse.json(installation(currentStatus))
      }),
      http.get(`/api/setup/install-runs/${installationId}/steps`, () =>
        HttpResponse.json([]),
      ),
    )

    renderWithProviders(<WorkflowProbe />)

    await waitFor(() =>
      expect(screen.getByTestId("workflow-status")).toHaveTextContent("Failed"),
    )

    currentStatus = "Running"

    await act(async () => {
      window.dispatchEvent(new Event("focus"))
    })

    await waitFor(() =>
      expect(screen.getByTestId("workflow-status")).toHaveTextContent("Running"),
    )
    expect(installationReads).toBeGreaterThanOrEqual(2)
  })

  it("still sends an explicit operator resume request when requested", async () => {
    const user = userEvent.setup()
    let runRequests = 0

    server.use(
      http.get(`/api/setup/install-plans/${installationId}`, () =>
        HttpResponse.json(installation("Failed")),
      ),
      http.get(`/api/setup/install-runs/${installationId}/steps`, () =>
        HttpResponse.json([]),
      ),
      http.post(`/api/setup/install-runs/${installationId}/run`, () => {
        runRequests += 1
        return HttpResponse.json({
          installationId,
          accepted: true,
          status: "Running",
          message: "queued",
        })
      }),
    )

    renderWithProviders(<WorkflowProbe />)

    await waitFor(() =>
      expect(screen.getByTestId("workflow-status")).toHaveTextContent("Failed"),
    )

    await user.click(screen.getByRole("button", { name: "Continue installation" }))

    await waitFor(() => expect(runRequests).toBe(1))
    expect(screen.getByTestId("click-count")).toHaveTextContent("1")
  })
})

function installation(status: string) {
  return {
    id: installationId,
    status,
    configJson: null,
    frozenConfigJson: null,
    lastError: null,
    createdAtUtc: "2026-08-11T02:58:00Z",
    updatedAtUtc: "2026-08-11T03:00:00Z",
    startedAtUtc: status === "Draft" ? null : "2026-08-11T02:59:00Z",
    completedAtUtc: null,
  }
}

async function settleEffects() {
  await new Promise((resolve) => window.setTimeout(resolve, 100))
}
