import { afterEach, describe, expect, it } from "vitest"
import { http, HttpResponse } from "msw"
import { MemoryRouter } from "react-router-dom"
import { screen, within } from "@testing-library/react"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { OperatorSessionProvider } from "@/features/auth/operator-session-provider"
import { RuntimeContextDetailsProvider } from "@/features/runtime-context/components/runtime-context-details"
import { createRuntimeContext } from "@/features/runtime-context/runtime-context.test-fixture"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import { AppHeader } from "./page-header"

afterEach(() => {
  window.localStorage.clear()
})

function renderHeader(pathname: string) {
  return renderWithProviders(
    <MemoryRouter initialEntries={[pathname]}>
      <AppHeader />
    </MemoryRouter>,
  )
}

const operatorSession = {
  authenticated: true as const,
  authenticationKind: "operator" as const,
  displayName: "admin",
  roles: ["platform_owner", "operator"],
  requiresFirstOwnerBootstrap: false,
  hasCompletedPlatformOwner: true,
}

describe("AppHeader MEM brand lockup", () => {
  it("shows the final Message Easy Mode lockup in operator mode", () => {
    renderHeader("/migrations")

    expect(screen.getByRole("img", { name: "MEM logo" })).toBeInTheDocument()
    expect(screen.getByText("Message Easy Mode")).toBeInTheDocument()
    expect(screen.queryByText("CONTROL PLANE")).not.toBeInTheDocument()
    expect(screen.queryByText("Matrix Easy Mode")).not.toBeInTheDocument()
  })

  it("makes the MEM brand lockup a root-home link", () => {
    renderHeader("/setup/review")

    expect(screen.getByRole("link", { name: "Go to MEM home" })).toHaveAttribute(
      "href",
      "/",
    )
    const homeLink = screen.getByRole("link", { name: "Go to MEM home" })
    const logo = within(homeLink).getByRole("img", { name: "MEM logo" })

    expect(logo).toBeInTheDocument()
    expect(logo).toHaveClass("h-9", "w-9")
    expect(homeLink).toHaveClass("cursor-pointer", "hover:bg-muted/60")
  })


  it("aligns the MEM brand mark to the navigation icon axis and centers the label tightly", () => {
    renderHeader("/diagnostics")

    const homeLink = screen.getByRole("link", { name: "Go to MEM home" })
    const logo = within(homeLink).getByRole("img", { name: "MEM logo" })
    const title = within(homeLink).getByText("Message Easy Mode")
    const header = homeLink.closest("header")

    expect(header).toHaveClass("h-16", "py-1.5", "pl-3", "sm:pl-3", "lg:pl-3", "pr-3", "sm:pr-6", "lg:pr-8")
    expect(homeLink).toHaveClass("-ml-2", "gap-1", "py-0.5")
    expect(logo).toHaveAttribute("src", "/mem-logo-mark.png")
    expect(logo).toHaveClass("h-9", "w-9")
    expect(logo.parentElement).toHaveClass(
      "h-11",
      "w-11",
      "translate-x-[4px]",
      "items-center",
      "justify-center",
    )
    expect(title.parentElement).toHaveClass("-ml-[2px]", "flex", "h-11", "items-center")
    expect(title).toHaveClass("translate-y-px", "leading-[1.2]")
  })

  it("keeps the same compact MEM lockup in installer mode", () => {
    renderHeader("/setup")

    expect(screen.getByText("Message Easy Mode")).toBeInTheDocument()
    expect(screen.queryByText("CONTROL PLANE")).not.toBeInTheDocument()
    expect(screen.queryByText("MEM Installer")).not.toBeInTheDocument()
  })

  it("uses the same brand lockup in German", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    renderHeader("/migrations")

    expect(screen.getByRole("img", { name: "MEM-Logo" })).toBeInTheDocument()
    expect(screen.getByText("Message Easy Mode")).toBeInTheDocument()
    expect(screen.queryByText("Steuerungsebene")).not.toBeInTheDocument()
  })

  it("places healthy non-production runtime identity with the global utility controls", async () => {
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
      http.get("/api/operator/diagnostics/attention", () =>
        HttpResponse.json({
          schemaVersion: 1,
          observedAtUtc: "2026-08-09T00:00:00Z",
          state: "ready",
          total: 0,
          highestSeverity: null,
          items: [],
          partial: false,
          warnings: [],
        })),
    )

    renderWithProviders(
      <OperatorSessionProvider session={operatorSession} signOut={async () => undefined}>
        <MemoryRouter initialEntries={["/diagnostics"]}>
          <RuntimeContextDetailsProvider>
            <AppHeader />
          </RuntimeContextDetailsProvider>
        </MemoryRouter>
      </OperatorSessionProvider>,
    )

    const utilities = screen.getByTestId("global-header-utilities")
    const indicator = await within(utilities).findByTestId("runtime-context-indicator")
    const language = within(utilities).getByRole("combobox", { name: "Language" })

    expect(indicator).toHaveTextContent("Development · Local source")
    expect(indicator.compareDocumentPosition(language) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
    expect(language).toHaveClass("cursor-pointer")
    for (const control of within(utilities).getAllByRole("button")) {
      expect(control).toHaveClass("cursor-pointer")
    }
  })

  it("places the global diagnostics attention bell before the operator account menu", async () => {
    server.use(
      http.get("/api/operator/diagnostics/attention", () =>
        HttpResponse.json({
          schemaVersion: 1,
          observedAtUtc: "2026-08-04T05:30:00Z",
          state: "ready",
          total: 0,
          highestSeverity: null,
          items: [],
          partial: false,
          warnings: [],
        })),
    )

    renderWithProviders(
      <OperatorSessionProvider session={operatorSession} signOut={async () => undefined}>
        <MemoryRouter initialEntries={["/diagnostics"]}>
          <RuntimeContextDetailsProvider>
            <AppHeader />
          </RuntimeContextDetailsProvider>
        </MemoryRouter>
      </OperatorSessionProvider>,
    )

    const attention = await screen.findByRole("button", {
      name: "No diagnostic incidents need attention",
    })
    const account = screen.getByRole("button", { name: "Open account menu" })

    expect(attention.compareDocumentPosition(account) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy()
  })
})
