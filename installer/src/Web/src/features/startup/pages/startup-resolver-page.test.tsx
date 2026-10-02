import { http, HttpResponse } from "msw"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { screen, within } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import { StartupResolverPage } from "./startup-resolver-page"

const endpoint = "/api/setup/start/status"

function status(startupTarget: string, overrides: Record<string, unknown> = {}) {
  return {
    setupMode: "already-installed",
    installationState: "installed",
    recommendedAction: "open-dashboard",
    startupTarget,
    docker: { reachable: true, message: "Docker is reachable." },
    detectedInstallation: null,
    requiredServices: [],
    supportToolsServices: [],
    warnings: [],
    ...overrides,
  }
}

function renderResolver() {
  return renderWithProviders(
    <MemoryRouter initialEntries={["/"]}>
      <Routes>
        <Route path="/" element={<StartupResolverPage />} />
        <Route path="/dashboard" element={<div>Dashboard destination</div>} />
        <Route path="/setup/start" element={<div>Setup destination</div>} />
        <Route
          path="/setup/install/:installationId"
          element={<div>Install activity destination</div>}
        />
        <Route
          path="/setup/verify/:installationId"
          element={<div>Verification destination</div>}
        />
        <Route
          path="/setup/handoff/:installationId"
          element={<div>Handoff destination</div>}
        />
      </Routes>
    </MemoryRouter>,
  )
}

describe("STARTUP-01A canonical startup resolver", () => {
  it("opens the dashboard for a completed healthy installation", async () => {
    server.use(http.get(endpoint, () => HttpResponse.json(status("dashboard"))))

    renderResolver()

    expect(await screen.findByText("Dashboard destination")).toBeInTheDocument()
    expect(screen.queryByText("Setup destination")).not.toBeInTheDocument()
  })

  it("keeps a completed but degraded installation in the operator dashboard", async () => {
    server.use(
      http.get(endpoint, () =>
        HttpResponse.json(
          status("dashboard", {
            setupMode: "repair",
            installationState: "repair-required",
            recommendedAction: "run-repair-check",
          }),
        ),
      ),
    )

    renderResolver()

    expect(await screen.findByText("Dashboard destination")).toBeInTheDocument()
    expect(screen.queryByText("Setup destination")).not.toBeInTheDocument()
  })

  it("opens first-time setup when the API selects the setup journey", async () => {
    server.use(
      http.get(endpoint, () =>
        HttpResponse.json(
          status("setup-start", {
            setupMode: "fresh-install",
            installationState: "not-installed",
            recommendedAction: "run-setup-check",
          }),
        ),
      ),
    )

    renderResolver()

    expect(await screen.findByText("Setup destination")).toBeInTheDocument()
    expect(screen.queryByText("Dashboard destination")).not.toBeInTheDocument()
  })

  it("resumes a running or failed installation at its authoritative activity", async () => {
    server.use(
      http.get(endpoint, () =>
        HttpResponse.json(
          status("resume-installation", {
            setupMode: "repair",
            installationState: "partially-installed",
            recommendedAction: "resume-install",
            activeInstallationId: "11111111-1111-1111-1111-111111111111",
            activeInstallationStage: "failure-review",
          }),
        ),
      ),
    )

    renderResolver()

    expect(
      await screen.findByText("Install activity destination"),
    ).toBeInTheDocument()
    expect(screen.queryByText("Setup destination")).not.toBeInTheDocument()
  })

  it("resumes a failed final verification at the verification report", async () => {
    server.use(
      http.get(endpoint, () =>
        HttpResponse.json(
          status("resume-installation", {
            setupMode: "repair",
            installationState: "repair-required",
            recommendedAction: "review-verification",
            activeInstallationId: "22222222-2222-2222-2222-222222222222",
            activeInstallationStage: "verification",
          }),
        ),
      ),
    )

    renderResolver()

    expect(
      await screen.findByText("Verification destination"),
    ).toBeInTheDocument()
    expect(
      screen.queryByText("Install activity destination"),
    ).not.toBeInTheDocument()
    expect(screen.queryByText("Setup destination")).not.toBeInTheDocument()
  })

  it("resumes a completed installation at its pending handoff", async () => {
    server.use(
      http.get(endpoint, () =>
        HttpResponse.json(
          status("resume-installation", {
            setupMode: "already-installed",
            installationState: "installed",
            recommendedAction: "complete-handoff",
            activeInstallationId: "44444444-4444-4444-4444-444444444444",
            activeInstallationStage: "handoff",
          }),
        ),
      ),
    )

    renderResolver()

    expect(await screen.findByText("Handoff destination")).toBeInTheDocument()
    expect(screen.queryByText("Dashboard destination")).not.toBeInTheDocument()
  })

  it("fails closed when a resume target lacks authoritative installation identity", async () => {
    server.use(
      http.get(endpoint, () =>
        HttpResponse.json(
          status("resume-installation", {
            setupMode: "repair",
            installationState: "partially-installed",
            recommendedAction: "resume-install",
            activeInstallationId: null,
            activeInstallationStage: "activity",
          }),
        ),
      ),
    )

    renderResolver()

    expect(
      await screen.findByText("MEM could not determine the startup route"),
    ).toBeInTheDocument()
    expect(screen.queryByText("Install activity destination")).not.toBeInTheDocument()
  })

  it("fails closed when the API does not provide a recognised startup target", async () => {
    server.use(http.get(endpoint, () => HttpResponse.json(status("unexpected"))))

    renderResolver()

    const alert = await screen.findByRole("alert")
    expect(
      within(alert).getByText("MEM could not determine the startup route"),
    ).toBeInTheDocument()
    expect(screen.queryByText("Dashboard destination")).not.toBeInTheDocument()
    expect(screen.queryByText("Setup destination")).not.toBeInTheDocument()
  })
})
