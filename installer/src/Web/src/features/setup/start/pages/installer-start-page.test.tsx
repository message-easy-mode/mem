import { http, HttpResponse } from "msw"
import { MemoryRouter, useLocation } from "react-router-dom"
import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import { InstallerStartPage } from "./installer-start-page"

const endpoint = "/api/setup/start/status"
const beginEndpoint = "/api/setup/start/begin"

function LocationProbe() {
  const location = useLocation()
  return <span data-testid="location-probe">{location.pathname}</span>
}

function renderPage() {
  return renderWithProviders(
    <MemoryRouter>
      <InstallerStartPage />
      <LocationProbe />
    </MemoryRouter>,
  )
}

function requiredServices() {
  return [
    {
      key: "postgres",
      displayName: "Postgres",
      description: "Managed PostgreSQL service.",
      required: true,
      installed: true,
      running: true,
      healthy: true,
      state: "running",
      containerName: "mem-postgres",
      image: "postgres:16",
      urls: [],
      warnings: [],
    },
    {
      key: "npm",
      displayName: "Nginx Proxy Manager",
      description: "Ingress provider.",
      required: true,
      installed: true,
      running: true,
      healthy: true,
      state: "running",
      containerName: "mem-npm",
      image: "jc21/nginx-proxy-manager:2.14.0",
      urls: [],
      warnings: [],
    },
    {
      key: "coturn",
      displayName: "Coturn (TURN)",
      description: "Shared voice/video relay service.",
      required: true,
      installed: true,
      running: true,
      healthy: true,
      state: "ready",
      containerName: "mem-coturn",
      image: "coturn/coturn@sha256:approved",
      urls: [],
      warnings: [],
    },
  ]
}

describe("InstallerStartPage first-time setup authority", () => {
  it("creates durable setup authority before navigating to preflight", async () => {
    const user = userEvent.setup()
    let beginCalls = 0

    server.use(
      http.get(endpoint, () =>
        HttpResponse.json({
          setupMode: "fresh-install",
          installationState: "not-installed",
          recommendedAction: "run-preflight",
          startupTarget: "setup-start",
          docker: { reachable: true, message: "Docker is reachable." },
          detectedInstallation: null,
          requiredServices: requiredServices(),
          supportToolsServices: [],
          warnings: [],
        }),
      ),
      http.post(beginEndpoint, () => {
        beginCalls += 1
        return HttpResponse.json({
          id: "11111111-1111-1111-1111-111111111111",
          status: "Draft",
        })
      }),
    )

    renderPage()

    expect(await screen.findByRole("heading", { name: "MEM Control Plane is ready" })).toBeInTheDocument()
    expect(screen.getByText(/private Control Plane is already installed/i)).toBeInTheDocument()

    await user.click(
      await screen.findByRole("button", { name: "Continue to server checks" }),
    )

    await waitFor(() => expect(beginCalls).toBe(1))
    expect(screen.getByTestId("location-probe")).toHaveTextContent("/preflight")
  })

  it("reuses the durable planning lifecycle when setup is reopened", async () => {
    const user = userEvent.setup()
    let beginCalls = 0

    server.use(
      http.get(endpoint, () =>
        HttpResponse.json({
          setupMode: "fresh-install",
          installationState: "partially-installed",
          recommendedAction: "continue-setup",
          startupTarget: "setup-start",
          activeInstallationId: "22222222-2222-2222-2222-222222222222",
          docker: { reachable: true, message: "Docker is reachable." },
          detectedInstallation: null,
          requiredServices: requiredServices(),
          supportToolsServices: [],
          warnings: [
            {
              code: "setup-planning-in-progress",
              title: "Platform setup is in progress",
              message: "Continue the existing setup.",
              blocking: false,
            },
          ],
        }),
      ),
      http.post(beginEndpoint, () => {
        beginCalls += 1
        return HttpResponse.json({
          id: "22222222-2222-2222-2222-222222222222",
          status: "Draft",
        })
      }),
    )

    renderPage()

    expect(
      await screen.findByText("Platform setup is in progress"),
    ).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Continue setup" }))

    await waitFor(() => expect(beginCalls).toBe(1))
    expect(screen.getByTestId("location-probe")).toHaveTextContent("/preflight")
  })
})

describe("InstallerStartPage legacy application retirement", () => {
  it("blocks fresh setup and directs a v0.1.0 source to mem-migrate", async () => {
    server.use(
      http.get(endpoint, () =>
        HttpResponse.json({
          setupMode: "migration-required",
          installationState: "partially-installed",
          recommendedAction: "use-mem-migrate",
          startupTarget: "setup-start",
          docker: { reachable: true, message: "Docker is reachable." },
          detectedInstallation: {
            detected: true,
            productName: "Message Easy Mode v0.1.0 migration source",
            version: "0.1.0",
            apiReachable: false,
            upgradeAvailable: false,
            targetVersion: "0.2.0",
          },
          requiredServices: requiredServices(),
          supportToolsServices: [],
          warnings: [
            {
              code: "legacy-v010-applications-detected",
              title: "Legacy MEM installation detected",
              message: "Use the separate mem-migrate tool.",
              blocking: true,
            },
          ],
        }),
      ),
    )

    renderPage()

    expect(
      await screen.findByRole("heading", {
        name: "Legacy MEM installation detected",
      }),
    ).toBeInTheDocument()
    expect(screen.getByText(/mem-migrate/)).toBeInTheDocument()
    expect(screen.getByText("Fresh installation is blocked")).toBeInTheDocument()
    expect(screen.getByText(/separate server/)).toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "Start upgrade check" })).not.toBeInTheDocument()
    expect(screen.queryByRole("link", { name: /Open MEM/ })).not.toBeInTheDocument()
  })


  it("offers the authoritative resume action instead of a fresh repair journey", async () => {
    const installationId = "33333333-3333-3333-3333-333333333333"
    server.use(
      http.get(endpoint, () =>
        HttpResponse.json({
          setupMode: "repair",
          installationState: "repair-required",
          recommendedAction: "resume-install",
          startupTarget: "resume-installation",
          activeInstallationId: installationId,
          activeInstallationStage: "failure-review",
          docker: { reachable: true, message: "Docker is reachable." },
          detectedInstallation: {
            detected: true,
            productName: "MEM Control Plane",
            version: "0.2.0",
            apiReachable: false,
            upgradeAvailable: false,
            targetVersion: "0.2.0",
          },
          requiredServices: requiredServices(),
          supportToolsServices: [],
          warnings: [
            {
              code: "installation-resumable",
              title: "An incomplete installation can be resumed",
              message: "Review the authoritative installation activity.",
              blocking: false,
            },
          ],
        }),
      ),
    )

    renderPage()

    expect(
      await screen.findByRole("heading", { name: "Installation needs review" }),
    ).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Review failure" })).toHaveAttribute(
      "href",
      `/setup/install/${installationId}`,
    )
    expect(screen.queryByRole("link", { name: "Start repair check" })).not.toBeInTheDocument()
  })

  it("opens the durable setup handoff when installation is complete but finish is pending", async () => {
    const installationId = "44444444-4444-4444-4444-444444444444"

    server.use(
      http.get(endpoint, () =>
        HttpResponse.json({
          setupMode: "already-installed",
          installationState: "installed",
          recommendedAction: "complete-handoff",
          startupTarget: "resume-installation",
          activeInstallationId: installationId,
          activeInstallationStage: "handoff",
          docker: { reachable: true, message: "Docker is reachable." },
          detectedInstallation: {
            detected: true,
            productName: "MEM Control Plane",
            version: "0.2.0",
            apiReachable: false,
            upgradeAvailable: false,
            targetVersion: "0.2.0",
          },
          requiredServices: requiredServices(),
          supportToolsServices: [],
          warnings: [
            {
              code: "installation-handoff-required",
              title: "Setup is ready to finish",
              message: "Finish setup.",
              blocking: false,
            },
          ],
        }),
      ),
    )

    renderPage()

    expect(
      await screen.findByRole("heading", { name: "Setup is ready to finish" }),
    ).toBeInTheDocument()
    expect(
      screen.getByRole("link", { name: "Finish setup" }),
    ).toHaveAttribute("href", `/setup/handoff/${installationId}`)
  })

  it("opens authoritative final-verification evidence instead of generic failure review", async () => {
    const installationId = "22222222-2222-2222-2222-222222222222"

    server.use(
      http.get(endpoint, () =>
        HttpResponse.json({
          setupMode: "repair",
          installationState: "repair-required",
          recommendedAction: "review-verification",
          startupTarget: "resume-installation",
          activeInstallationId: installationId,
          activeInstallationStage: "verification",
          docker: { reachable: true, message: "Docker is reachable." },
          detectedInstallation: {
            detected: true,
            productName: "MEM Control Plane",
            version: "0.2.0",
            apiReachable: false,
            upgradeAvailable: false,
            targetVersion: "0.2.0",
          },
          requiredServices: requiredServices(),
          supportToolsServices: [],
          warnings: [
            {
              code: "installation-verification-required",
              title: "Installation verification needs review",
              message: "Final verification failed.",
              blocking: false,
            },
          ],
        }),
      ),
    )

    renderPage()

    expect(
      await screen.findByRole("heading", {
        name: "Installation verification needs review",
      }),
    ).toBeInTheDocument()
    expect(
      screen.getByRole("link", { name: "Open verification" }),
    ).toHaveAttribute("href", `/setup/verify/${installationId}`)
  })

  it("shows established-state recovery guidance without offering first-time repair", async () => {
    server.use(
      http.get(endpoint, () =>
        HttpResponse.json({
          setupMode: "repair",
          installationState: "partially-installed",
          recommendedAction: "review-diagnostics",
          startupTarget: "dashboard",
          docker: { reachable: true, message: "Docker is reachable." },
          detectedInstallation: {
            detected: true,
            productName: "MEM Control Plane",
            version: "0.2.0",
            apiReachable: false,
            upgradeAvailable: false,
            targetVersion: "0.2.0",
          },
          requiredServices: requiredServices(),
          supportToolsServices: [],
          warnings: [
            {
              code: "installation-history-missing",
              title: "Historical installation record is unavailable",
              message: "Normal startup will continue to the dashboard.",
              blocking: false,
            },
          ],
        }),
      ),
    )

    renderPage()

    expect(
      await screen.findByRole("heading", { name: "MEM Control Plane is already in use" }),
    ).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open dashboard" })).toHaveAttribute(
      "href",
      "/dashboard",
    )
    expect(screen.getByRole("link", { name: "Run diagnostics" })).toHaveAttribute(
      "href",
      "/diagnostics",
    )
    expect(screen.queryByRole("link", { name: "Start repair check" })).not.toBeInTheDocument()
  })

  it("continues in the operator dashboard without offering MEM Web", async () => {
    server.use(
      http.get(endpoint, () =>
        HttpResponse.json({
          setupMode: "already-installed",
          installationState: "installed",
          recommendedAction: "open-dashboard",
          startupTarget: "dashboard",
          docker: { reachable: true, message: "Docker is reachable." },
          detectedInstallation: {
            detected: true,
            productName: "MEM Control Plane",
            version: "0.2.0",
            apiReachable: false,
            upgradeAvailable: false,
            targetVersion: "0.2.0",
          },
          requiredServices: requiredServices(),
          supportToolsServices: [],
          warnings: [],
        }),
      ),
    )

    renderPage()

    expect(
      await screen.findByRole("heading", {
        name: "MEM Control Plane is already installed",
      }),
    ).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open dashboard" })).toHaveAttribute(
      "href",
      "/dashboard",
    )
    expect(screen.queryByText("Open MEM control panel")).not.toBeInTheDocument()
    expect(screen.queryByText("MEM Web")).not.toBeInTheDocument()
  })
})
