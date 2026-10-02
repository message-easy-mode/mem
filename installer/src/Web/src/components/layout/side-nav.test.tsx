import { afterEach, beforeEach, describe, expect, it } from "vitest"
import { MemoryRouter } from "react-router-dom"
import { http, HttpResponse } from "msw"
import { screen, within } from "@testing-library/react"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { renderWithProviders } from "@/test/render-with-providers"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"

import { SideNav } from "./side-nav"

afterEach(() => { window.localStorage.clear() })


const platformStatusEndpoint = "/api/setup/start/status"

function platformStatus(
  startupTarget: "dashboard" | "setup-start" | "resume-installation",
) {
  const complete = startupTarget === "dashboard"

  return {
    setupMode: complete ? "already-installed" : "fresh-install",
    installationState: complete ? "installed" : "not-installed",
    recommendedAction: complete ? "open-dashboard" : "run-preflight",
    startupTarget,
    docker: { reachable: true, message: "Docker is reachable." },
    detectedInstallation: null,
    requiredServices: [],
    supportToolsServices: [],
    warnings: [],
  }
}

beforeEach(() => {
  server.use(
    http.get(platformStatusEndpoint, () =>
      HttpResponse.json(platformStatus("setup-start")),
    ),
  )
})

describe("SideNav", () => {
  it("expands the Domains workspace and marks the active certificate child route", () => {
    renderWithProviders(
      <MemoryRouter initialEntries={["/domains/domain-example/certificates/cert-example"]}>
        <SideNav />
      </MemoryRouter>,
    )

    expect(screen.getByRole("button", { name: "Collapse Domains navigation" })).toHaveAttribute(
      "aria-expanded",
      "true",
    )
    expect(screen.getByRole("link", { name: "Domain registry" })).toHaveAttribute(
      "href",
      "/domains",
    )
    expect(screen.getByRole("link", { name: "Certificates" })).toHaveAttribute(
      "aria-current",
      "page",
    )
    expect(screen.getByRole("link", { name: "Renewal" })).toHaveAttribute(
      "href",
      "/domains/renewal",
    )
    expect(screen.queryByText("Certificate authorities")).not.toBeInTheDocument()
    expect(screen.queryByText("Future")).not.toBeInTheDocument()
  })

  it("uses the Message Easy Mode mobile drawer with an obvious close affordance", () => {
    renderWithProviders(
      <MemoryRouter initialEntries={["/domains"]}>
        <SideNav mobileOpen onMobileOpenChange={() => {}} />
      </MemoryRouter>,
    )

    expect(screen.getByText("Message Easy Mode")).toBeInTheDocument()
    const close = screen.getByRole("button", { name: "Close menu" })
    expect(close).toHaveAttribute("title", "Close menu")
    expect(close).toHaveClass("cursor-pointer", "hover:bg-muted")
    expect(screen.getAllByRole("button", { name: "Collapse Domains navigation" })).toHaveLength(2)
    expect(screen.getAllByRole("link", { name: "Domain registry" })).toHaveLength(2)
    expect(screen.getAllByRole("link", { name: "Certificates" })).toHaveLength(2)
    expect(screen.getAllByRole("link", { name: "Renewal" })).toHaveLength(2)
    expect(screen.queryByText("Certificate authorities")).not.toBeInTheDocument()
  })

  it("renders the Domains workspace navigation in German", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    renderWithProviders(
      <MemoryRouter initialEntries={["/domains/renewal"]}>
        <SideNav />
      </MemoryRouter>,
    )

    expect(screen.getByRole("link", { name: "Domain-Verzeichnis" })).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Zertifikate" })).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Verlängerung" })).toHaveAttribute(
      "aria-current",
      "page",
    )
    expect(screen.queryByText("Zertifizierungsstellen")).not.toBeInTheDocument()
    expect(screen.queryByText("Später")).not.toBeInTheDocument()
  })

  it("renders Backup / Restore and top-level Migrations navigation in German", () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    renderWithProviders(<MemoryRouter initialEntries={["/backups"]}><SideNav /></MemoryRouter>)
    expect(screen.getByRole("link", { name: "Sicherung / Wiederherstellung" })).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Sicherungen" })).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "ZIP importieren" })).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Migrationen" })).toHaveAttribute("href", "/migrations")
  })


  it("shows Documentation once in operator navigation without a duplicate section heading", () => {
    renderWithProviders(<MemoryRouter initialEntries={["/dashboard"]}><SideNav /></MemoryRouter>)

    const documentationLink = screen.getByRole("link", { name: "Documentation" })
    expect(documentationLink).toHaveAttribute("href", "/docs")
    expect(documentationLink).toHaveClass("cursor-pointer")
    expect(screen.getAllByText("Documentation")).toHaveLength(1)
    expect(documentationLink.parentElement).toHaveClass("border-t")
  })

  it("keeps Documentation available while the setup navigation is active", () => {
    renderWithProviders(<MemoryRouter initialEntries={["/setup/start"]}><SideNav /></MemoryRouter>)

    const documentationLink = screen.getByRole("link", { name: "Documentation" })
    expect(documentationLink).toHaveAttribute(
      "href",
      "/setup/docs?returnTo=%2Fsetup%2Fstart",
    )
    expect(screen.getAllByText("Documentation")).toHaveLength(1)
    expect(documentationLink.parentElement).toHaveClass("border-t")
    expect(screen.getByRole("link", { name: "Start" })).toHaveAttribute("href", "/setup/start")
    expect(screen.queryByRole("link", { name: "Diagnostics" })).not.toBeInTheDocument()
  })

  it("keeps Setup navigation active while reading Setup-scoped documentation", () => {
    renderWithProviders(
      <MemoryRouter
        initialEntries={[
          "/setup/docs/installation/domain-and-certificate?returnTo=%2Fsetup%2Fdomain",
        ]}
      >
        <SideNav />
      </MemoryRouter>,
    )

    expect(screen.getByRole("link", { name: "Documentation" })).toHaveAttribute(
      "href",
      "/setup/docs/installation/domain-and-certificate?returnTo=%2Fsetup%2Fdomain",
    )
    expect(screen.getByRole("link", { name: "Public domain" })).toHaveAttribute(
      "data-setup-complete",
      "false",
    )
    expect(screen.getByRole("link", { name: "Start" })).toHaveAttribute(
      "data-setup-complete",
      "true",
    )
    expect(screen.queryByRole("link", { name: "Diagnostics" })).not.toBeInTheDocument()
  })

  it("keeps the active installation id on each later Setup step without duplicate links", () => {
    const installationId = "80a6361e-b1fb-4be7-a2e7-daed0d8a62d8"
    renderWithProviders(
      <MemoryRouter initialEntries={[`/setup/install/${installationId}`]}>
        <SideNav />
      </MemoryRouter>,
    )

    expect(screen.getAllByRole("link", { name: "Install platform" })).toHaveLength(1)
    expect(screen.getByRole("link", { name: "Install platform" })).toHaveAttribute(
      "href",
      `/setup/install/${installationId}`,
    )
    expect(screen.getAllByRole("link", { name: "Verify" })).toHaveLength(1)
    expect(screen.getByRole("link", { name: "Verify" })).toHaveAttribute(
      "href",
      `/setup/verify/${installationId}`,
    )
    expect(screen.getAllByRole("link", { name: "Finish" })).toHaveLength(1)
    expect(screen.getByRole("link", { name: "Finish" })).toHaveAttribute(
      "href",
      `/setup/handoff/${installationId}`,
    )
  })

  it("marks every Setup stage complete when server truth says first-time setup is finished", async () => {
    const installationId = "418abce3-f85d-4afd-a746-a14a9a15f185"
    server.use(
      http.get(platformStatusEndpoint, () =>
        HttpResponse.json(platformStatus("dashboard")),
      ),
    )

    renderWithProviders(
      <MemoryRouter
        initialEntries={[`/setup/install/${installationId}?setupPreview=1`]}
      >
        <SideNav />
      </MemoryRouter>,
    )

    for (const label of [
      "Start",
      "Check server",
      "Public domain",
      "Review",
      "Install platform",
      "Verify",
      "Finish",
    ]) {
      expect(
        await screen.findByRole("link", { name: label }),
      ).toHaveAttribute("data-setup-complete", "true")
    }
  })

  it("does not expose the retired Activity destination in operator navigation", () => {
    renderWithProviders(<MemoryRouter initialEntries={["/servers"]}><SideNav /></MemoryRouter>)

    expect(screen.queryByRole("link", { name: "Activity" })).not.toBeInTheDocument()
    expect(screen.queryByText("Activity")).not.toBeInTheDocument()
  })

  it("places Migrations outside Backup / Restore and keeps the recovery group collapsed on /migrations", () => {
    renderWithProviders(<MemoryRouter initialEntries={["/migrations"]}><SideNav /></MemoryRouter>)
    const groupLink = screen.getByRole("link", { name: "Backup / Restore" })
    const group = groupLink.closest("div.space-y-1")
    expect(group).not.toBeNull()
    expect(within(group as HTMLElement).queryByRole("link", { name: "Migrations" })).not.toBeInTheDocument()
    const migrationsLink = screen.getByRole("link", { name: "Migrations" })
    expect(migrationsLink).toHaveAttribute("href", "/migrations")
    expect(screen.getAllByRole("link", { name: "Migrations" })).toHaveLength(1)
    expect(screen.getByRole("button", { name: "Expand Backup / Restore navigation" })).toHaveAttribute("aria-expanded", "false")
  })
})
