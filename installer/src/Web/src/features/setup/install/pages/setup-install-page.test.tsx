import { http, HttpResponse } from "msw"
import { MemoryRouter, useLocation } from "react-router-dom"
import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import { SetupInstallPage } from "./setup-install-page"

const installationId = "11111111-1111-1111-1111-111111111111"

function reviewResponse(reviewAccepted: boolean) {
  return {
    installationId,
    installationStatus: reviewAccepted ? "Ready" : "Draft",
    canAccept: !reviewAccepted,
    reviewAccepted,
    message: reviewAccepted ? "Review accepted." : "Review the plan.",
    errorCode: null,
    planSha256: reviewAccepted ? "b".repeat(64) : null,
    reviewedAtUtc: reviewAccepted ? "2026-08-11T08:00:00Z" : null,
    preflight: {
      available: true,
      ready: true,
      runId: "preflight-01e",
      completedAtUtc: "2026-08-11T07:30:00Z",
      passed: 9,
      warnings: 0,
      failed: 0,
      skipped: 2,
      unavailable: 2,
      unknown: 0,
      blockingIssueCount: 0,
    },
    domain: {
      validated: true,
      baseDomain: "deltabox.dev",
      wildcardCertificate: "*.deltabox.dev",
      dnsProvider: "desec",
      acmeEmail: "admin@deltabox.dev",
      certificateEnvironment: "Let's Encrypt staging",
      providerAccessConfirmed: true,
      providerCredentialStored: true,
    },
    platform: {
      networkName: "mem-gateway",
      postgresContainerName: "mem-postgres",
      postgresVolumeName: "mem_postgres_data",
      npmContainerName: "mem-npm",
      npmHttpPort: 80,
      npmHttpsPort: 443,
      npmAdminPort: 81,
      coturnContainerName: "mem-coturn",
      coturnPublicHost: "turn.deltabox.dev",
      coturnTurnPort: 3478,
      coturnRelayPortRange: "49160-49200/udp",
      enabledSupportTools: ["Portainer"],
    },
    plannedActions: [],
    willNotChange: [],
    blockers: [],
  }
}

function planResponse(reviewAccepted: boolean) {
  return {
    id: installationId,
    status: reviewAccepted ? "Ready" : "Draft",
    configJson: "{}",
    frozenConfigJson: reviewAccepted ? "{\"review\":true}" : null,
    lastError: null,
  }
}

function LocationProbe() {
  const location = useLocation()
  return <span data-testid="location-probe">{location.pathname}</span>
}

function renderPage(initialEntry = "/setup/install") {
  return renderWithProviders(
    <MemoryRouter initialEntries={[initialEntry]}>
      <SetupInstallPage />
      <LocationProbe />
    </MemoryRouter>,
  )
}

describe("SetupInstallPage reviewed mutation boundary", () => {
  it("links an installation failure to its recorded incident", async () => {
    server.use(
      http.get("/api/setup/review/", () => HttpResponse.json(reviewResponse(true))),
      http.get(
        "/api/setup/install-plans/current",
        () => HttpResponse.json(
          {
            type: "https://mem.invalid/problems/install_plan_load_failed",
            title: "Installation state could not be loaded",
            status: 500,
            detail: "MEM could not read the current installation state.",
            code: "install_plan_load_failed",
            traceId: "trace-install-1",
            incidentId: "inc_install_1",
          },
          {
            status: 500,
            headers: { "Content-Type": "application/problem+json" },
          },
        ),
      ),
    )

    renderPage()

    expect(
      await screen.findByText("MEM could not read the current installation state."),
    ).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open diagnostics" })).toHaveAttribute(
      "href",
      "/diagnostics/logs?incident=inc_install_1",
    )
  })

  it("refuses to start platform mutation until Review is frozen", async () => {
    server.use(
      http.get("/api/setup/install-plans/current", () => HttpResponse.json(planResponse(false))),
      http.get("/api/setup/review/", () => HttpResponse.json(reviewResponse(false))),
    )

    renderPage()

    expect(await screen.findByText("Review acceptance is required")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Install platform" })).toBeDisabled()
    expect(screen.getByRole("link", { name: "Return to review" })).toHaveAttribute("href", "/setup/review")
  })

  it("starts only the frozen reviewed plan from the explicit Install platform action", async () => {
    const user = userEvent.setup()
    let runCalls = 0

    server.use(
      http.get("/api/setup/install-plans/current", () => HttpResponse.json(planResponse(true))),
      http.get("/api/setup/review/", () => HttpResponse.json(reviewResponse(true))),
      http.post(`/api/setup/install-runs/${installationId}/run`, () => {
        runCalls += 1
        return HttpResponse.json({
          installationId,
          accepted: true,
          status: "Running",
          message: "Installation workflow has started under the server-owned worker.",
        })
      }),
    )

    renderPage()

    expect(await screen.findByText("Ready to install the reviewed plan")).toBeInTheDocument()
    expect(screen.getByText("deltabox.dev")).toBeInTheDocument()
    expect(screen.getByText("Let's Encrypt staging")).toBeInTheDocument()
    expect(screen.getByText("mem-postgres")).toBeInTheDocument()
    expect(screen.getByText("mem-npm")).toBeInTheDocument()
    expect(screen.getByText("mem-coturn")).toBeInTheDocument()
    expect(screen.getByText("Portainer")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Install platform" }))

    await waitFor(() => expect(runCalls).toBe(1))
    await waitFor(() =>
      expect(screen.getByTestId("location-probe")).toHaveTextContent(`/setup/install/${installationId}`),
    )
  })
  it("keeps completed-Setup preview read-only instead of exposing installation mutation", async () => {
    server.use(
      http.get("/api/setup/install-plans/current", () => HttpResponse.json(planResponse(false))),
      http.get("/api/setup/review/", () => HttpResponse.json(reviewResponse(false))),
    )

    renderPage("/setup/install?setupPreview=1")

    expect(
      await screen.findByText("Installation actions are locked in this preview"),
    ).toBeInTheDocument()
    expect(
      screen.queryByRole("button", { name: "Install platform" }),
    ).not.toBeInTheDocument()
    expect(screen.getAllByRole("link", { name: "Back to review" })).toHaveLength(1)
  })

})
