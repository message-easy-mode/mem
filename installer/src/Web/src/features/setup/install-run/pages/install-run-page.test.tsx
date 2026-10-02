import { http, HttpResponse } from "msw"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import { InstallRunPage } from "./install-run-page"

const installationId = "11111111-1111-1111-1111-111111111111"

function renderPage() {
  return renderWithProviders(
    <MemoryRouter initialEntries={[`/setup/install/${installationId}`]}>
      <Routes>
        <Route path="/setup/install/:installationId" element={<InstallRunPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe("InstallRunPage journey semantics", () => {
  it("treats successful platform work as ready to finish rather than offering a new setup or stale Review path", async () => {
    const user = userEvent.setup()

    server.use(
      http.get(`/api/setup/install-plans/${installationId}`, () => HttpResponse.json({
        id: installationId,
        status: "Succeeded",
        configJson: "{}",
        frozenConfigJson: "{}",
        lastError: null,
        createdAtUtc: "2026-08-11T09:17:25Z",
        updatedAtUtc: "2026-08-12T10:49:25Z",
        startedAtUtc: "2026-08-11T09:17:25Z",
        completedAtUtc: "2026-08-12T10:49:25Z",
      })),
      http.get(`/api/setup/install-runs/${installationId}/steps`, () => HttpResponse.json([
        ...Array.from({ length: 10 }, (_, index) => ({
          id: `00000000-0000-0000-0000-${String(index + 1).padStart(12, "0")}`,
          stepName: `Step ${index + 1}`,
          sequence: index + 1,
          status: "Succeeded",
          message: "Completed",
          errorMessage: null,
          attemptCount: 1,
          startedAtUtc: "2026-08-12T10:49:00Z",
          completedAtUtc: "2026-08-12T10:49:20Z",
          progress: index === 6
            ? {
                schemaVersion: 1,
                installationId,
                stepId: `00000000-0000-0000-0000-${String(index + 1).padStart(12, "0")}`,
                stepSequence: index + 1,
                stepName: `Step ${index + 1}`,
                attemptNumber: 1,
                stepStatus: "Succeeded",
                phaseCode: "npm.readiness",
                phaseStatus: "Succeeded",
                safeSummary: "Nginx Proxy Manager is ready.",
                stepStartedAtUtc: "2026-08-12T10:49:00Z",
                lastActivityAtUtc: "2026-08-12T10:49:20Z",
                phases: [
                  {
                    code: "npm.inspect",
                    status: "Succeeded",
                    safeSummary: "Inspected Nginx Proxy Manager.",
                    startedAtUtc: "2026-08-12T10:49:00Z",
                    completedAtUtc: "2026-08-12T10:49:05Z",
                  },
                  {
                    code: "npm.readiness",
                    status: "Succeeded",
                    safeSummary: "Nginx Proxy Manager is ready.",
                    startedAtUtc: "2026-08-12T10:49:05Z",
                    completedAtUtc: "2026-08-12T10:49:20Z",
                  },
                ],
              }
            : null,
        })),
        {
          id: "00000000-0000-0000-0000-000000000011",
          stepName: "Complete setup handoff",
          sequence: 11,
          status: "WaitingForUser",
          message: "Verification passed. Finish setup to acknowledge the handoff into the operator Control Plane.",
          errorMessage: null,
          attemptCount: 1,
          startedAtUtc: "2026-08-12T10:49:25Z",
          completedAtUtc: null,
        },
      ])),
    )

    renderPage()

    expect(await screen.findByText("Platform installation complete")).toBeInTheDocument()
    expect(
      screen.queryByRole("navigation", { name: "Setup progress" }),
    ).not.toBeInTheDocument()
    expect(screen.getByRole("status")).toHaveTextContent("Completed")
    expect(screen.getByText("Ready to finish setup")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Continue to finish" })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "View verification report" })).toBeInTheDocument()
    expect(screen.queryByText("Finish")).not.toBeInTheDocument()
    expect(screen.queryByText("Open MEM")).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Start over" })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Review changes" })).not.toBeInTheDocument()
    expect(screen.queryByText("Live activity")).not.toBeInTheDocument()
    expect(screen.queryByText("Needs attention")).not.toBeInTheDocument()

    const completedOperations = screen.getByRole("region", { name: "Completed operations" })
    expect(within(completedOperations).getAllByTestId(/completed-operation-/)).toHaveLength(10)
    expect(within(completedOperations).queryByText("Complete setup handoff")).not.toBeInTheDocument()
    expect(within(completedOperations).getByText("10 operations completed successfully.")).toBeInTheDocument()

    const installationDetails = screen.getByText("Installation details").closest("details")
    expect(installationDetails).not.toBeNull()
    expect(installationDetails?.open).toBe(false)

    await user.click(
      screen.getByRole("button", { name: "View installation details" }),
    )

    expect(installationDetails?.open).toBe(true)
    expect(within(installationDetails!).getByText("Inspect Nginx Proxy Manager runtime")).toBeInTheDocument()
    expect(within(installationDetails!).getByText("Wait for Nginx Proxy Manager readiness")).toBeInTheDocument()
  })

  it("treats an acknowledged handoff as a completed record and returns to the dashboard", async () => {
    server.use(
      http.get(`/api/setup/install-plans/${installationId}`, () => HttpResponse.json({
        id: installationId,
        status: "Succeeded",
        configJson: "{}",
        frozenConfigJson: "{}",
        lastError: null,
        createdAtUtc: "2026-08-11T09:17:25Z",
        updatedAtUtc: "2026-08-12T10:55:00Z",
        startedAtUtc: "2026-08-11T09:17:25Z",
        completedAtUtc: "2026-08-12T10:49:25Z",
      })),
      http.get(`/api/setup/install-runs/${installationId}/steps`, () => HttpResponse.json(
        Array.from({ length: 11 }, (_, index) => ({
          id: `00000000-0000-0000-0000-${String(index + 1).padStart(12, "0")}`,
          stepName: index === 10 ? "Complete setup handoff" : `Step ${index + 1}`,
          sequence: index + 1,
          status: "Succeeded",
          message: index === 10 ? "Setup handoff acknowledged." : "Completed",
          errorMessage: null,
          attemptCount: 1,
          startedAtUtc: "2026-08-12T10:49:00Z",
          completedAtUtc: "2026-08-12T10:55:00Z",
          progress: null,
        })),
      )),
    )

    renderPage()

    expect(await screen.findByText("Setup completed")).toBeInTheDocument()
    expect(screen.getByText("Automated platform changes, verification, and the operator handoff have completed successfully.")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Open dashboard" })).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Continue to finish" })).not.toBeInTheDocument()
    expect(screen.getByRole("button", { name: "View installation details" })).toBeInTheDocument()

    const completedOperations = screen.getByRole("region", { name: "Completed operations" })
    expect(within(completedOperations).getAllByTestId(/completed-operation-/)).toHaveLength(11)
    expect(within(completedOperations).getByText("Complete setup handoff")).toBeInTheDocument()
    expect(within(completedOperations).getByText("11 operations completed successfully.")).toBeInTheDocument()
  })

  it("offers a bounded support report from a failed installation", async () => {
    const user = userEvent.setup()
    let reportRequested = false
    const click = vi
      .spyOn(HTMLAnchorElement.prototype, "click")
      .mockImplementation(() => undefined)
    Object.defineProperty(URL, "createObjectURL", {
      configurable: true,
      value: vi.fn(() => "blob:mem-install-report"),
    })
    Object.defineProperty(URL, "revokeObjectURL", {
      configurable: true,
      value: vi.fn(),
    })

    server.use(
      http.get(`/api/setup/install-plans/${installationId}`, () =>
        HttpResponse.json({
          id: installationId,
          status: "Failed",
          configJson: "{}",
          frozenConfigJson: "{}",
          lastError: "Certificate import failed safely.",
          createdAtUtc: "2026-08-11T09:17:25Z",
          updatedAtUtc: "2026-08-12T10:49:25Z",
          startedAtUtc: "2026-08-11T09:17:25Z",
          completedAtUtc: "2026-08-12T10:49:25Z",
        }),
      ),
      http.get(`/api/setup/install-runs/${installationId}/steps`, () =>
        HttpResponse.json([
          {
            id: "00000000-0000-0000-0000-000000000001",
            stepName: "Validate install plan",
            sequence: 1,
            status: "Succeeded",
            message: "Completed",
            errorMessage: null,
            attemptCount: 1,
            startedAtUtc: "2026-08-12T10:49:00Z",
            completedAtUtc: "2026-08-12T10:49:05Z",
          },
          {
            id: "00000000-0000-0000-0000-000000000008",
            stepName: "Issue and import platform certificate",
            sequence: 8,
            status: "Failed",
            message: "Wildcard certificate is retained.",
            errorMessage: "NPM import failed safely.",
            attemptCount: 2,
            startedAtUtc: "2026-08-12T10:49:05Z",
            completedAtUtc: "2026-08-12T10:49:10Z",
          },
        ]),
      ),
      http.post(
        `/api/setup/installations/${installationId}/support-report`,
        async ({ request }) => {
          reportRequested = true
          expect(await request.json()).toEqual({
            includeDockerEvidence: true,
            format: "json",
          })
          return new HttpResponse('{"schemaVersion":1}', {
            status: 200,
            headers: {
              "Content-Type": "application/json; charset=utf-8",
              "Content-Disposition":
                'attachment; filename="mem-install-report-failure.json"',
            },
          })
        },
      ),
    )

    try {
      renderPage()

      expect(
        await screen.findByText("Platform setup needs attention"),
      ).toBeInTheDocument()
      const download = screen.getByRole("button", {
        name: "Download support report",
      })
      await user.click(download)

      expect(reportRequested).toBe(true)
      expect(URL.createObjectURL).toHaveBeenCalledTimes(1)
      expect(click).toHaveBeenCalledTimes(1)
      expect(URL.revokeObjectURL).toHaveBeenCalledWith("blob:mem-install-report")
      expect(screen.getByRole("link", { name: "Troubleshoot setup" })).toHaveAttribute(
        "href",
        `/setup/troubleshooting?installationId=${installationId}`,
      )
    } finally {
      click.mockRestore()
    }
  })

})
