import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { MemoryRouter } from "react-router-dom"
import { screen } from "@testing-library/react"
import { beforeEach, describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { OperatorSessionProvider } from "@/features/auth/operator-session-provider"
import { createRuntimeContext } from "@/features/runtime-context/runtime-context.test-fixture"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import { AppShell } from "./app-shell"

const operatorSession = {
  authenticated: true as const,
  authenticationKind: "operator" as const,
  displayName: "admin",
  roles: ["platform_owner", "operator"],
  requiresFirstOwnerBootstrap: false,
  hasCompletedPlatformOwner: true,
}

function renderShell(pathname: string) {
  renderWithProviders(
    <OperatorSessionProvider session={operatorSession} signOut={async () => undefined}>
      <MemoryRouter initialEntries={[pathname]}>
        <AppShell>
          <div>Canvas probe</div>
        </AppShell>
      </MemoryRouter>
    </OperatorSessionProvider>,
  )
}

describe("AppShell", () => {
  beforeEach(() => {
    server.use(
      http.get("/api/operator/runtime-context", () => HttpResponse.json(
        createRuntimeContext(),
      )),
      http.get("/api/operator/diagnostics/attention", () =>
        HttpResponse.json({
          schemaVersion: 1,
          observedAtUtc: "2026-09-12T00:00:00Z",
          state: "ready",
          total: 0,
          highestSeverity: null,
          items: [],
          partial: false,
          warnings: [],
        })),
    )
  })

  it("moves a healthy development runtime into the compact global header indicator", async () => {
    const user = userEvent.setup()
    server.use(
      http.get("/api/operator/runtime-context", () => HttpResponse.json(
        createRuntimeContext({
          runtimeMode: "local-development",
          environmentName: "Development",
          runningInContainer: false,
          stateRootKind: "repository-local",
          stateRootProfile: "default",
          uiDeliveryMode: "vite",
          configuredContainerName: null,
          showDevelopmentBanner: true,
        }),
      )),
    )

    renderShell("/dashboard")

    const indicator = await screen.findByTestId("runtime-context-indicator")
    expect(indicator).toHaveTextContent("Development · Local source")
    expect(screen.queryByTestId("runtime-context-warning-banner")).not.toBeInTheDocument()

    await user.click(indicator)
    expect(await screen.findByRole("dialog", { name: "Control Plane runtime" })).toBeInTheDocument()
  })


  it("keeps production chrome quiet while exposing system information from the navigation footer", async () => {
    const user = userEvent.setup()

    renderShell("/dashboard")

    expect(screen.queryByTestId("runtime-context-indicator")).not.toBeInTheDocument()
    expect(await screen.findByText("Version 0.2.0-test")).toBeInTheDocument()
    expect(screen.getByTestId("runtime-system-information")).toHaveTextContent("baby-fish")
    expect(screen.getByText("Build abc123")).toBeInTheDocument()

    const systemInformation = screen.getByRole("button", { name: "System information" })
    expect(systemInformation).toHaveClass("cursor-pointer", "border-border", "bg-card/60")

    await user.click(systemInformation)
    expect(await screen.findByRole("dialog", { name: "Control Plane runtime" })).toBeInTheDocument()
  })


  it("uses a 64px shell header with distinct light surfaces and unchanged dark fallbacks", () => {
    renderShell("/dashboard")

    const homeLink = screen.getByRole("link", { name: "Go to MEM home" })
    const header = homeLink.closest("header")
    expect(header).toHaveClass("h-16")
    expect(header?.parentElement).toHaveClass("bg-card/95", "dark:bg-background/85")

    const sideNav = screen.getByRole("complementary", { name: "Menu", hidden: true })
    expect(sideNav).toHaveClass("top-16", "bg-sidebar/95", "dark:bg-background/60")

    const shellRow = screen.getByText("Canvas probe").closest("main")?.parentElement
    expect(shellRow).toHaveClass("min-h-[calc(100vh-4rem)]")
  })

  it("places a visible compact main menu trigger in the header on smaller screens", () => {
    renderShell("/docs")

    const menuButton = screen.getByRole("button", { name: "Open menu" })

    expect(menuButton).toHaveClass("lg:hidden")
    expect(menuButton).toHaveClass("cursor-pointer")
    expect(menuButton).toHaveClass("border-border")
    expect(menuButton).toHaveClass("bg-muted/70")
  })

  it("uses the dense operator canvas for restore-session inventory routes", () => {
    renderShell("/restores")

    expect(screen.getByText("Canvas probe").parentElement).toHaveClass("max-w-[1760px]")
  })

  it("uses the dense operator canvas for the stack workspace overview", () => {
    renderShell("/stacks/demo-stack")

    expect(screen.getByText("Canvas probe").parentElement).toHaveClass("max-w-[1760px]")
  })

  it("keeps normal operator surfaces on the standard canvas", () => {
    renderShell("/storage")

    expect(screen.getByText("Canvas probe").parentElement).toHaveClass("max-w-7xl")
  })

  it("keeps the pending Setup finish handoff visible across Diagnostics routes", async () => {
    const installationId = "44444444-4444-4444-4444-444444444444"
    server.use(
      http.get("/api/setup/start/status", () =>
        HttpResponse.json({
          installationState: "installed",
          recommendedAction: "complete-handoff",
          startupTarget: "resume-installation",
          docker: { reachable: true, message: null },
          requiredServices: [],
          supportToolsServices: [],
          warnings: [],
          activeInstallationId: installationId,
          activeInstallationStage: "handoff",
        }),
      ),
    )

    renderShell("/diagnostics/logs?tab=health")

    expect(await screen.findByText("Setup still needs finishing")).toBeInTheDocument()
    expect(screen.getByText(/opening Diagnostics does not complete setup/i)).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Return to finish setup" })).toHaveAttribute(
      "href",
      `/setup/handoff/${installationId}`,
    )
  })

  it("removes the Setup finish return once the server says handoff is complete", async () => {
    server.use(
      http.get("/api/setup/start/status", () =>
        HttpResponse.json({
          installationState: "installed",
          recommendedAction: "open-dashboard",
          startupTarget: "dashboard",
          docker: { reachable: true, message: null },
          requiredServices: [],
          supportToolsServices: [],
          warnings: [],
          activeInstallationId: null,
          activeInstallationStage: null,
        }),
      ),
    )

    renderShell("/diagnostics")

    expect(await screen.findByText("Canvas probe")).toBeInTheDocument()
    expect(screen.queryByTestId("diagnostics-setup-finish-banner")).not.toBeInTheDocument()
  })

})
