import { screen } from "@testing-library/react"
import { http, HttpResponse } from "msw"
import { MemoryRouter } from "react-router-dom"
import { afterEach, describe, expect, it } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { OperatorSessionProvider } from "@/features/auth/operator-session-provider"
import "@/test/msw-lifecycle"
import { renderWithProviders } from "@/test/render-with-providers"
import { server } from "@/test/msw-server"
import { DiagnosticsPortainerPage } from "./diagnostics-portainer-page"

const ownerSession = {
  authenticated: true as const,
  authenticationKind: "operator" as const,
  displayName: "admin",
  roles: ["platform_owner"],
  requiresFirstOwnerBootstrap: false,
  hasCompletedPlatformOwner: true,
}

const operatorSession = {
  ...ownerSession,
  displayName: "operator",
  roles: ["operator"],
}

const readyOverview = {
  schemaVersion: 1,
  observedAtUtc: "2026-08-04T08:00:00Z",
  available: true,
  managed: true,
  runtimeState: "ready",
  ownershipState: "managed",
  version: "2.39.5",
  approvedVersion: "2.39.5",
  environmentConfigured: true,
  exactResourceLinksSupported: true,
  unexpectedContainerId: "0123456789abcdef0123456789abcdef",
  links: {
    home: "https://portainer.example.test/",
    environment: "https://portainer.example.test/#!/endpoints/1/docker/dashboard",
    containers: "https://portainer.example.test/#!/endpoints/1/docker/containers",
  },
  capabilities: {
    canOpenHome: true,
    canOpenEnvironment: true,
    canOpenContainers: true,
    canOpenExactResource: true,
  },
  warnings: [],
}

function renderPage(
  session = ownerSession,
  parent: "diagnostics" | "services" = "diagnostics",
) {
  return renderWithProviders(
    <MemoryRouter
      initialEntries={[parent === "services" ? "/services/portainer" : "/diagnostics/portainer"]}
    >
      <OperatorSessionProvider session={session} signOut={async () => undefined}>
        <DiagnosticsPortainerPage parent={parent} />
      </OperatorSessionProvider>
    </MemoryRouter>,
  )
}

afterEach(() => {
  window.localStorage.clear()
})

describe("DiagnosticsPortainerPage", () => {
  it("projects safe server-authored Portainer links and the product boundary", async () => {
    server.use(
      http.get("/api/operator/diagnostics/portainer", () => HttpResponse.json(readyOverview)),
    )

    renderPage()

    expect(await screen.findByRole("heading", { name: "Portainer overview" })).toBeInTheDocument()
    const breadcrumbs = screen.getByRole("navigation", { name: "Breadcrumb" })
    expect(breadcrumbs).toHaveTextContent("Diagnostics")
    expect(breadcrumbs).toHaveTextContent("Advanced container diagnostics")
    expect(screen.getByRole("link", { name: "Diagnostics" })).toHaveAttribute("href", "/diagnostics")
    expect(screen.getByRole("link", { name: "Open Portainer" })).toHaveAttribute(
      "href",
      "https://portainer.example.test/",
    )
    expect(screen.getByRole("link", { name: "Open local environment" })).toHaveAttribute(
      "href",
      "https://portainer.example.test/#!/endpoints/1/docker/dashboard",
    )
    expect(screen.getByRole("link", { name: "Open containers" })).toHaveAttribute(
      "href",
      "https://portainer.example.test/#!/endpoints/1/docker/containers",
    )
    expect(screen.getByText("MEM explains; Portainer inspects")).toBeInTheDocument()
    expect(screen.queryByText("0123456789abcdef0123456789abcdef")).not.toBeInTheDocument()
  })

  it("can render the same safe Portainer workspace under the Services navigation root", async () => {
    server.use(
      http.get("/api/operator/diagnostics/portainer", () => HttpResponse.json(readyOverview)),
    )

    renderPage(ownerSession, "services")

    expect(await screen.findByRole("heading", { name: "Portainer", level: 1 })).toBeInTheDocument()
    expect(await screen.findByRole("heading", { name: "Portainer overview" })).toBeInTheDocument()
    const breadcrumbs = screen.getByRole("navigation", { name: "Breadcrumb" })
    expect(breadcrumbs).toHaveTextContent("Services")
    expect(screen.getByRole("link", { name: "Services" })).toHaveAttribute("href", "/services")
    expect(screen.getByRole("link", { name: "Open Portainer" })).toHaveAttribute(
      "href",
      "https://portainer.example.test/",
    )
  })

  it("guides first-time Portainer setup and timeout recovery when exact handoff is not configured", async () => {
    server.use(
      http.get("/api/operator/diagnostics/portainer", () =>
        HttpResponse.json({
          ...readyOverview,
          environmentConfigured: false,
          exactResourceLinksSupported: false,
          links: {
            home: "https://localhost:9443",
            environment: null,
            containers: null,
          },
          capabilities: {
            canOpenHome: true,
            canOpenEnvironment: false,
            canOpenContainers: false,
            canOpenExactResource: false,
          },
          warnings: [
            "portainer_ui_not_configured",
            "portainer_environment_not_configured",
          ],
        }),
      ),
    )

    renderPage()

    expect(await screen.findByText("Portainer setup and handoff")).toBeInTheDocument()
    expect(
      screen.getByText(/fresh Portainer 2\.39\.5 installation requires a one-time setup token/i),
    ).toBeInTheDocument()
    expect(
      screen.getByText("docker logs portainer 2>&1 | grep 'setup_token=' | tail -n 1"),
    ).toBeInTheDocument()
    expect(screen.getByText("docker restart portainer")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open Portainer setup" })).toHaveAttribute(
      "href",
      "https://localhost:9443/",
    )
    expect(
      screen.getByText(/MEM does not read or store the Portainer setup token or administrator password/i),
    ).toBeInTheDocument()
  })

  it("rejects unsafe browser-facing URLs even when a malformed response claims capability", async () => {
    server.use(
      http.get("/api/operator/diagnostics/portainer", () => HttpResponse.json({
        ...readyOverview,
        links: {
          home: "javascript:alert(1)",
          environment: "data:text/html,bad",
          containers: "file:///tmp/containers",
        },
      })),
    )

    renderPage()

    expect(await screen.findByRole("heading", { name: "Portainer overview" })).toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "Open Portainer" })).not.toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "Open containers" })).not.toBeInTheDocument()
  })

  it("explains containers-list fallback when exact resource links are unavailable", async () => {
    server.use(
      http.get("/api/operator/diagnostics/portainer", () => HttpResponse.json({
        ...readyOverview,
        exactResourceLinksSupported: false,
        capabilities: {
          ...readyOverview.capabilities,
          canOpenExactResource: false,
        },
      })),
    )

    renderPage()

    expect(await screen.findByText(/uses the configured containers list instead of guessing a route/i)).toBeInTheDocument()
  })

  it("does not request owner-only Portainer details for an Operator", async () => {
    let requests = 0
    server.use(
      http.get("/api/operator/diagnostics/portainer", () => {
        requests += 1
        return HttpResponse.json(readyOverview)
      }),
    )

    renderPage(operatorSession)

    expect(await screen.findByText("Portainer handoff is restricted")).toBeInTheDocument()
    expect(requests).toBe(0)
  })

  it("renders the Portainer workspace in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    server.use(
      http.get("/api/operator/diagnostics/portainer", () => HttpResponse.json(readyOverview)),
    )

    renderPage()

    expect(await screen.findByRole("heading", { name: "Portainer-Übersicht" })).toBeInTheDocument()
    expect(screen.getByText("MEM erklärt; Portainer prüft")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Container öffnen" })).toBeInTheDocument()
  })
})
