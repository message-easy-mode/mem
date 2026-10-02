import { http, HttpResponse } from "msw"
import { MemoryRouter } from "react-router-dom"
import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it, vi } from "vitest"

import "@/test/msw-lifecycle"
import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import { SetupTroubleshootingPage } from "./setup-troubleshooting-page"

afterEach(() => {
  window.localStorage.removeItem(LANGUAGE_STORAGE_KEY)
})

describe("SetupTroubleshootingPage", () => {
  it("shows a runtime-correct copyable host support command for the active installation", async () => {
    const user = userEvent.setup()
    const writeText = vi.fn().mockResolvedValue(undefined)
    Object.defineProperty(navigator, "clipboard", {
      configurable: true,
      value: { writeText },
    })

    server.use(
      http.get("/health/runtime", () =>
        HttpResponse.json({
          schemaVersion: 1,
          productDisplayName: "MEM Control Plane",
          runtimeMode: "containerized-development",
          controlPlaneInstanceId: "control-plane-1",
          apiProcessInstanceId: "process-1",
          uiDeliveryMode: "embedded-spa",
          version: "0.2.0",
          commit: "abc123",
          validationState: "valid",
          showDevelopmentBanner: true,
        }),
      ),
      http.get("/api/setup/start/status", () =>
        HttpResponse.json({
          installationState: "partially-installed",
          recommendedAction: "resume-install",
          startupTarget: "resume-installation",
          setupMode: "repair",
          detectedInstallation: null,
          docker: { reachable: true, message: null },
          requiredServices: [],
          supportToolsServices: [],
          warnings: [],
          activeInstallationId: "95f7eebb-0e83-4bab-9916-b503c853caca",
          activeInstallationStage: "failure-review",
        }),
      ),
    )

    renderWithProviders(
      <MemoryRouter initialEntries={["/setup/troubleshooting"]}>
        <SetupTroubleshootingPage />
      </MemoryRouter>,
    )

    expect(
      await screen.findByText("containerized-development"),
    ).toBeInTheDocument()
    expect(
      screen.getByText("95f7eebb-0e83-4bab-9916-b503c853caca"),
    ).toBeInTheDocument()
    expect(
      screen.getByText(/docker exec mem-control-plane-dev/),
    ).toBeInTheDocument()

    const copyButtons = screen.getAllByRole("button", { name: "Copy command" })
    await user.click(copyButtons[0])

    expect(writeText).toHaveBeenCalledTimes(1)
    expect(writeText.mock.calls[0]?.[0]).toContain(
      '--installation-id "95f7eebb-0e83-4bab-9916-b503c853caca"',
    )
    expect(await screen.findByRole("button", { name: "Copied" })).toBeInTheDocument()
  })

  it("does not invent a Docker host command for local development", async () => {
    server.use(
      http.get("/health/runtime", () =>
        HttpResponse.json({
          runtimeMode: "local-development",
        }),
      ),
      http.get("/api/setup/start/status", () =>
        HttpResponse.json({
          installationState: "partially-installed",
          recommendedAction: "resume-install",
          startupTarget: "resume-installation",
          setupMode: "repair",
          detectedInstallation: null,
          docker: { reachable: true, message: null },
          requiredServices: [],
          supportToolsServices: [],
          warnings: [],
          activeInstallationId: "installation-local",
          activeInstallationStage: "failure-review",
        }),
      ),
    )

    renderWithProviders(
      <MemoryRouter initialEntries={["/setup/troubleshooting"]}>
        <SetupTroubleshootingPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText("local-development")).toBeInTheDocument()
    expect(
      screen.getAllByText(/A host command is not available for this runtime/i),
    ).toHaveLength(2)
    expect(screen.queryByText(/docker exec mem-control-plane/)).not.toBeInTheDocument()
  })

  it("routes German Setup troubleshooting to the German installation documentation", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    server.use(
      http.get("/health/runtime", () =>
        HttpResponse.json({ runtimeMode: "containerized-development" }),
      ),
      http.get("/api/setup/start/status", () =>
        HttpResponse.json({
          installationState: "partially-installed",
          recommendedAction: "resume-install",
          startupTarget: "resume-installation",
          setupMode: "repair",
          detectedInstallation: null,
          docker: { reachable: true, message: null },
          requiredServices: [],
          supportToolsServices: [],
          warnings: [],
          activeInstallationId: "installation-de",
          activeInstallationStage: "failure-review",
        }),
      ),
    )

    renderWithProviders(
      <MemoryRouter initialEntries={["/setup/troubleshooting"]}>
        <SetupTroubleshootingPage />
      </MemoryRouter>,
    )

    expect(
      await screen.findByRole("heading", { name: "Fehlerbehebung bei der Einrichtung" }),
    ).toBeInTheDocument()
    expect(
      screen.getByRole("link", { name: "Dokumentation zur Fehlerbehebung öffnen" }),
    ).toHaveAttribute("href", "/docs/installation/troubleshooting")
  })

})
