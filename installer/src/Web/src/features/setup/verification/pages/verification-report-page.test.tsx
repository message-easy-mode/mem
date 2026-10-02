import { http, HttpResponse } from "msw"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { screen, within } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import { VerificationReportPage } from "./verification-report-page"

const installationId = "11111111-1111-1111-1111-111111111111"

function renderPage() {
  return renderWithProviders(
    <MemoryRouter initialEntries={[`/setup/verify/${installationId}`]}>
      <Routes>
        <Route path="/setup/verify/:installationId" element={<VerificationReportPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

describe("VerificationReportPage journey language", () => {
  it("uses operator-facing finish language instead of handoff jargon", async () => {
    server.use(
      http.get(`/api/setup/install-runs/${installationId}/verification-report`, () => HttpResponse.json({
        status: "Succeeded",
        message: "Platform verification completed successfully.",
        checkedAtUtc: "2026-08-12T10:49:25Z",
        checks: [
          {
            key: "npm",
            title: "Nginx Proxy Manager",
            description: "NPM is running and the managed certificate is available.",
            status: "Succeeded",
            message: "NPM verification passed.",
            evidence: [],
          },
        ],
      })),
      http.get(`/api/setup/install-runs/${installationId}/steps`, () => HttpResponse.json([
        {
          id: "00000000-0000-0000-0000-000000000007",
          stepName: "Start NPM / ingress",
          sequence: 7,
          status: "Succeeded",
          message: "Nginx Proxy Manager is ready.",
          errorMessage: null,
          attemptCount: 1,
          startedAtUtc: "2026-08-12T10:49:00Z",
          completedAtUtc: "2026-08-12T10:49:20Z",
          progress: {
            schemaVersion: 1,
            installationId,
            stepId: "00000000-0000-0000-0000-000000000007",
            stepSequence: 7,
            stepName: "Start NPM / ingress",
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
          },
        },
        {
          id: "00000000-0000-0000-0000-000000000011",
          stepName: "Complete setup handoff",
          sequence: 11,
          status: "WaitingForUser",
          message: "Finish setup.",
          errorMessage: null,
          attemptCount: 1,
          startedAtUtc: "2026-08-12T10:49:25Z",
          completedAtUtc: null,
          progress: null,
        },
      ])),
    )

    renderPage()

    expect(await screen.findByRole("heading", { name: "Verification report" })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Back to setup activity" })).toBeInTheDocument()
    expect(await screen.findByRole("button", { name: "Continue to finish" })).toBeInTheDocument()
    expect(screen.queryByText("Continue to handoff")).not.toBeInTheDocument()
    expect(await screen.findByText("Installation operation history")).toBeInTheDocument()

    const completedOperations = screen.getByRole("region", { name: "Completed operations" })
    expect(within(completedOperations).getByText("Start NPM / ingress")).toBeInTheDocument()
    expect(within(completedOperations).queryByText("Complete setup handoff")).not.toBeInTheDocument()
    expect(within(completedOperations).getByText("1 operation completed successfully.")).toBeInTheDocument()
    expect(within(completedOperations).getByText("Inspect Nginx Proxy Manager runtime")).toBeInTheDocument()
    expect(within(completedOperations).getByText("Wait for Nginx Proxy Manager readiness")).toBeInTheDocument()

    const installationDetails = screen.getByText("Installation details").closest("details")
    expect(installationDetails).not.toBeNull()
    expect(installationDetails?.open).toBe(false)
  })

  it("offers the dashboard instead of Finish after the handoff is already completed", async () => {
    server.use(
      http.get(`/api/setup/install-runs/${installationId}/verification-report`, () => HttpResponse.json({
        status: "Succeeded",
        message: "Platform verification completed successfully.",
        checkedAtUtc: "2026-08-12T10:55:00Z",
        checks: [],
      })),
      http.get(`/api/setup/install-runs/${installationId}/steps`, () => HttpResponse.json([
        {
          id: "00000000-0000-0000-0000-000000000010",
          stepName: "Run verification checks",
          sequence: 10,
          status: "Succeeded",
          message: "Verification completed.",
          errorMessage: null,
          attemptCount: 1,
          startedAtUtc: "2026-08-12T10:49:20Z",
          completedAtUtc: "2026-08-12T10:49:25Z",
          progress: null,
        },
        {
          id: "00000000-0000-0000-0000-000000000011",
          stepName: "Complete setup handoff",
          sequence: 11,
          status: "Succeeded",
          message: "Setup handoff acknowledged.",
          errorMessage: null,
          attemptCount: 1,
          startedAtUtc: "2026-08-12T10:49:25Z",
          completedAtUtc: "2026-08-12T10:55:00Z",
          progress: null,
        },
      ])),
    )

    renderPage()

    expect(await screen.findByRole("heading", { name: "Verification report" })).toBeInTheDocument()
    expect(await screen.findByRole("button", { name: "Open dashboard" })).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Continue to finish" })).not.toBeInTheDocument()

    const completedOperations = screen.getByRole("region", { name: "Completed operations" })
    expect(within(completedOperations).getByText("Run verification checks")).toBeInTheDocument()
    expect(within(completedOperations).getByText("Complete setup handoff")).toBeInTheDocument()
    expect(within(completedOperations).getByText("2 operations completed successfully.")).toBeInTheDocument()
  })
})
