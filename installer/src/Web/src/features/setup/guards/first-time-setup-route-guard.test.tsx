import { http, HttpResponse } from "msw"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { render, screen } from "@testing-library/react"
import { QueryClient, QueryClientProvider } from "@tanstack/react-query"
import { afterEach, describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import { I18nProvider } from "@/app/i18n/i18n-provider"
import { ThemeProvider } from "@/app/theme"
import { platformStatusQueryKeys } from "@/features/setup/start/hooks/use-platform-status"

import { FirstTimeSetupRouteGuard } from "./first-time-setup-route-guard"

const endpoint = "/api/setup/start/status"
const runtimeEndpoint = "/health/runtime"
const previewSessionKey = "mem.setupPreview"

function status(startupTarget: "dashboard" | "setup-start" | "resume-installation") {
  const isDashboard = startupTarget === "dashboard"
  const isResume = startupTarget === "resume-installation"

  return {
    setupMode: isDashboard
      ? "already-installed"
      : isResume
        ? "repair"
        : "fresh-install",
    installationState: isDashboard
      ? "installed"
      : isResume
        ? "partially-installed"
        : "not-installed",
    recommendedAction: isDashboard
      ? "open-dashboard"
      : isResume
        ? "resume-install"
        : "run-setup-check",
    startupTarget,
    docker: { reachable: true, message: "Docker is reachable." },
    detectedInstallation: null,
    requiredServices: [],
    supportToolsServices: [],
    warnings: [],
  }
}

function runtime(runtimeMode: string) {
  return {
    schemaVersion: 1,
    productDisplayName: "MEM Control Plane",
    runtimeMode,
    controlPlaneInstanceId: "11111111-1111-1111-1111-111111111111",
    apiProcessInstanceId: "22222222-2222-2222-2222-222222222222",
    uiDeliveryMode: runtimeMode === "local-development" ? "vite" : "embedded-spa",
    version: "0.2.0",
    commit: null,
    validationState: "valid",
    showDevelopmentBanner: runtimeMode !== "containerized-production",
  }
}

function renderGuard(path = "/setup/start") {
  return renderWithProviders(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/setup" element={<FirstTimeSetupRouteGuard />}>
          <Route path="start" element={<div>Actionable setup page</div>} />
          <Route path="domain" element={<div>Actionable domain setup</div>} />
          <Route
            path="install/:installationId"
            element={<div>Resumable installation activity</div>}
          />
        </Route>
        <Route path="/dashboard" element={<div>Dashboard destination</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

function renderGuardWithCachedSetupStatus(path = "/setup/start") {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  })
  queryClient.setQueryData(platformStatusQueryKeys.current, status("setup-start"))

  return render(
    <I18nProvider>
      <ThemeProvider>
        <QueryClientProvider client={queryClient}>
          <MemoryRouter initialEntries={[path]}>
            <Routes>
              <Route path="/setup" element={<FirstTimeSetupRouteGuard />}>
                <Route path="start" element={<div>Actionable setup page</div>} />
              </Route>
              <Route path="/dashboard" element={<div>Dashboard destination</div>} />
            </Routes>
          </MemoryRouter>
        </QueryClientProvider>
      </ThemeProvider>
    </I18nProvider>,
  )
}

afterEach(() => {
  window.sessionStorage.clear()
})

describe("STARTUP-01C first-time setup route lockout", () => {
  it("redirects an installed system before rendering the first-time setup page", async () => {
    server.use(http.get(endpoint, () => HttpResponse.json(status("dashboard"))))

    renderGuard()

    expect(await screen.findByText("Dashboard destination")).toBeInTheDocument()
    expect(screen.queryByText("Actionable setup page")).not.toBeInTheDocument()
  })

  it("does not trust a cached pre-install setup target while the mounted guard refetches server truth", async () => {
    server.use(http.get(endpoint, () => HttpResponse.json(status("dashboard"))))

    renderGuardWithCachedSetupStatus()

    expect(screen.queryByText("Actionable setup page")).not.toBeInTheDocument()
    expect(await screen.findByText("Dashboard destination")).toBeInTheDocument()
  })

  it("redirects a direct nested setup route when installation is already complete", async () => {
    server.use(http.get(endpoint, () => HttpResponse.json(status("dashboard"))))

    renderGuard("/setup/domain")

    expect(await screen.findByText("Dashboard destination")).toBeInTheDocument()
    expect(screen.queryByText("Actionable domain setup")).not.toBeInTheDocument()
  })

  it("keeps first-time setup available while the server selects the setup journey", async () => {
    server.use(http.get(endpoint, () => HttpResponse.json(status("setup-start"))))

    renderGuard()

    expect(await screen.findByText("Actionable setup page")).toBeInTheDocument()
    expect(screen.queryByText("Dashboard destination")).not.toBeInTheDocument()
  })

  it("keeps the guarded setup tree available for the server-selected active installation", async () => {
    server.use(
      http.get(endpoint, () =>
        HttpResponse.json({
          ...status("resume-installation"),
          activeInstallationId: "11111111-1111-1111-1111-111111111111",
          activeInstallationStage: "activity",
        }),
      ),
    )

    renderGuard("/setup/install/11111111-1111-1111-1111-111111111111")

    expect(
      await screen.findByText("Resumable installation activity"),
    ).toBeInTheDocument()
    expect(screen.queryByText("Dashboard destination")).not.toBeInTheDocument()
  })

  it("fails closed instead of rendering setup when startup state is unavailable", async () => {
    server.use(http.get(endpoint, () => HttpResponse.json({ status: "unavailable" }, { status: 503 })))

    renderGuard()

    expect(
      await screen.findByText("MEM could not determine the startup route"),
    ).toBeInTheDocument()
    expect(screen.queryByText("Actionable setup page")).not.toBeInTheDocument()
  })
})

describe("STARTUP-01C-CORR-01 development setup preview", () => {
  it("allows setupPreview=1 to render locked setup in local development", async () => {
    server.use(
      http.get(endpoint, () => HttpResponse.json(status("dashboard"))),
      http.get(runtimeEndpoint, () => HttpResponse.json(runtime("local-development"))),
    )

    renderGuard("/setup/start?setupPreview=1")

    expect(await screen.findByText("Actionable setup page")).toBeInTheDocument()
    expect(screen.getByText("Setup preview — development only")).toBeInTheDocument()
    expect(screen.getByText(/Preview runtime: local-development/)).toBeInTheDocument()
    expect(screen.queryByText("Dashboard destination")).not.toBeInTheDocument()
  })

  it("preserves an accepted preview across setup navigation in the same browser tab", async () => {
    server.use(
      http.get(endpoint, () => HttpResponse.json(status("dashboard"))),
      http.get(runtimeEndpoint, () => HttpResponse.json(runtime("containerized-development"))),
    )

    const firstRender = renderGuard("/setup/start?setupPreview=1")
    expect(await screen.findByText("Actionable setup page")).toBeInTheDocument()
    expect(window.sessionStorage.getItem(previewSessionKey)).toBe("1")
    firstRender.unmount()

    renderGuard("/setup/domain")

    expect(await screen.findByText("Actionable domain setup")).toBeInTheDocument()
    expect(screen.queryByText("Dashboard destination")).not.toBeInTheDocument()
  })

  it("refuses setupPreview=1 in containerized production", async () => {
    server.use(
      http.get(endpoint, () => HttpResponse.json(status("dashboard"))),
      http.get(runtimeEndpoint, () => HttpResponse.json(runtime("containerized-production"))),
    )

    renderGuard("/setup/start?setupPreview=1")

    expect(await screen.findByText("Dashboard destination")).toBeInTheDocument()
    expect(screen.queryByText("Actionable setup page")).not.toBeInTheDocument()
    expect(window.sessionStorage.getItem(previewSessionKey)).toBeNull()
  })
})
