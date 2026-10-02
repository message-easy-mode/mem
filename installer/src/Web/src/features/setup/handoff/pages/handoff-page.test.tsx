import { http, HttpResponse } from "msw"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it, vi } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"

import { HandoffPage } from "./handoff-page"

const installationId = "11111111-1111-1111-1111-111111111111"

afterEach(() => {
  window.localStorage.removeItem(LANGUAGE_STORAGE_KEY)
})

function renderPage() {
  return renderWithProviders(
    <MemoryRouter initialEntries={[`/setup/handoff/${installationId}`]}>
      <Routes>
        <Route path="/setup/handoff/:installationId" element={<HandoffPage />} />
        <Route path="/dashboard" element={<div>Dashboard destination</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe("HandoffPage legacy application retirement", () => {
  it("hands off to the private control plane without MEM API or MEM Web routes", async () => {
    server.use(
      http.get(`/api/setup/install-runs/${installationId}/handoff`, () =>
        HttpResponse.json({
          installationId,
          status: "ready",
          message: "Managed MEM platform setup is complete.",
          operatorDashboardPath: "/dashboard",
          baseDomain: "example.test",
          certificateCommonName: "*.example.test",
          certificateId: "example-test",
          isStagingCertificate: false,
          certificateExpiresAtUtc: "2026-10-01T00:00:00Z",
          npmCertificateId: 12,
          postgresContainerName: "mem-postgres",
          npmContainerName: "mem-npm",
          coturnContainerName: "mem-coturn",
          handoffRequired: false,
          handoffCompleted: true,
          warnings: [],
        }),
      ),
    )

    renderPage()

    expect(await screen.findByRole("heading", { name: "Setup complete" })).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open operator dashboard" })).toHaveAttribute(
      "href",
      "/dashboard",
    )
    expect(screen.getByText("Private MEM Control Plane")).toBeInTheDocument()
    expect(
      screen.getByText(
        "The private MEM Control Plane is ready for normal operator use. Continue in the operator dashboard.",
      ),
    ).toBeInTheDocument()
    expect(screen.queryByText("Managed MEM platform setup is complete.")).not.toBeInTheDocument()
    expect(screen.getByText("mem-postgres installed")).toBeInTheDocument()
    expect(screen.getByText("mem-npm installed")).toBeInTheDocument()
    expect(screen.getByText("mem-coturn installed")).toBeInTheDocument()
    expect(screen.getByText("Coturn (TURN)")).toBeInTheDocument()
    expect(screen.queryByText("Open MEM Web")).not.toBeInTheDocument()
    expect(screen.queryByText("Control plane API")).not.toBeInTheDocument()
    expect(screen.queryByText(/admin\.example\.test/)).not.toBeInTheDocument()
    expect(screen.queryByText(/api\.example\.test/)).not.toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "Create first Platform Owner" })).not.toBeInTheDocument()
    expect(screen.getByText("Certificate environment")).toBeInTheDocument()
    expect(screen.getByText("Let's Encrypt production")).toBeInTheDocument()
  })

  it("localizes the completed handoff description instead of rendering the server English message", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    server.use(
      http.get(`/api/setup/install-runs/${installationId}/handoff`, () =>
        HttpResponse.json({
          installationId,
          status: "ready",
          message: "Managed MEM platform setup is complete.",
          operatorDashboardPath: "/dashboard",
          baseDomain: "example.test",
          certificateCommonName: "*.example.test",
          certificateId: "example-test",
          isStagingCertificate: false,
          certificateExpiresAtUtc: "2026-10-01T00:00:00Z",
          npmCertificateId: 12,
          postgresContainerName: "mem-postgres",
          npmContainerName: "mem-npm",
          coturnContainerName: "mem-coturn",
          handoffRequired: false,
          handoffCompleted: true,
          warnings: [],
        }),
      ),
    )

    renderPage()

    expect(await screen.findByRole("heading", { name: "Einrichtung abgeschlossen" })).toBeInTheDocument()
    expect(
      screen.getByText(
        "Die private MEM Control Plane ist für den normalen Operatorbetrieb bereit. Fahren Sie im Operator-Dashboard fort.",
      ),
    ).toBeInTheDocument()
    expect(screen.queryByText("Managed MEM platform setup is complete.")).not.toBeInTheDocument()
  })

  it("durably completes a pending handoff before opening the operator dashboard", async () => {
    const user = userEvent.setup()
    let completionCalled = false

    server.use(
      http.get(`/api/setup/install-runs/${installationId}/handoff`, () =>
        HttpResponse.json({
          installationId,
          status: "ready",
          message: "Installation and verification are complete. Finish setup.",
          operatorDashboardPath: "/dashboard",
          baseDomain: "example.test",
          certificateCommonName: "*.example.test",
          certificateId: "example-test",
          isStagingCertificate: false,
          certificateExpiresAtUtc: "2026-10-01T00:00:00Z",
          npmCertificateId: 12,
          postgresContainerName: "mem-postgres",
          npmContainerName: "mem-npm",
          coturnContainerName: "mem-coturn",
          handoffRequired: true,
          handoffCompleted: false,
          warnings: [],
        }),
      ),
      http.post(
        `/api/setup/install-runs/${installationId}/handoff/complete`,
        () => {
          completionCalled = true
          return HttpResponse.json({
            installationId,
            completed: true,
            status: "completed",
            message: "Setup handoff completed.",
          })
        },
      ),
    )

    renderPage()

    expect(await screen.findByRole("heading", { name: "Finish setup" })).toBeInTheDocument()
    expect(screen.getByText("Ready to finish")).toBeInTheDocument()
    expect(screen.getByText("MEM is ready")).toBeInTheDocument()

    const finish = screen.getByRole("button", {
      name: "Finish setup and open dashboard",
    })

    await user.click(finish)

    expect(completionCalled).toBe(true)
    expect(await screen.findByText("Dashboard destination")).toBeInTheDocument()
  })
  it("downloads a bounded installation support report from the Finish page", async () => {
    const user = userEvent.setup()
    let reportRequested = false
    const click = vi
      .spyOn(HTMLAnchorElement.prototype, "click")
      .mockImplementation(() => undefined)
    Object.defineProperty(URL, "createObjectURL", {
      configurable: true,
      value: vi.fn(() => "blob:mem-install-report"),
    })
    Object.defineProperty(URL, "revokeObjectURL", {
      configurable: true,
      value: vi.fn(),
    })

    server.use(
      http.get(`/api/setup/install-runs/${installationId}/handoff`, () =>
        HttpResponse.json({
          installationId,
          status: "ready",
          message: "Managed MEM platform setup is complete.",
          operatorDashboardPath: "/dashboard",
          baseDomain: "example.test",
          certificateCommonName: "*.example.test",
          certificateId: "example-test",
          isStagingCertificate: false,
          certificateExpiresAtUtc: "2026-10-01T00:00:00Z",
          npmCertificateId: 12,
          postgresContainerName: "mem-postgres",
          npmContainerName: "mem-npm",
          coturnContainerName: "mem-coturn",
          handoffRequired: false,
          handoffCompleted: true,
          warnings: [],
        }),
      ),
      http.post(
        `/api/setup/installations/${installationId}/support-report`,
        async ({ request }) => {
          reportRequested = true
          expect(await request.json()).toEqual({
            includeDockerEvidence: true,
            format: "json",
          })
          return new HttpResponse('{"schemaVersion":1}', {
            status: 200,
            headers: {
              "Content-Type": "application/json; charset=utf-8",
              "Content-Disposition":
                'attachment; filename="mem-install-report-test.json"',
            },
          })
        },
      ),
    )

    try {
      renderPage()

      const download = await screen.findByRole("button", {
        name: "Download support report",
      })
      await user.click(download)

      expect(reportRequested).toBe(true)
      expect(URL.createObjectURL).toHaveBeenCalledTimes(1)
      expect(click).toHaveBeenCalledTimes(1)
      expect(URL.revokeObjectURL).toHaveBeenCalledWith("blob:mem-install-report")
      expect(
        screen.getByText(/Credentials, passwords, private keys/i),
      ).toBeInTheDocument()
    } finally {
      click.mockRestore()
    }
  })

  it("shows an explicit not-configured certificate environment when no certificate exists", async () => {
    server.use(
      http.get(`/api/setup/install-runs/${installationId}/handoff`, () =>
        HttpResponse.json({
          installationId,
          status: "not_ready",
          message: "Managed MEM platform setup is not complete.",
          operatorDashboardPath: "/dashboard",
          baseDomain: "example.test",
          certificateCommonName: null,
          certificateId: null,
          isStagingCertificate: false,
          certificateExpiresAtUtc: null,
          npmCertificateId: null,
          postgresContainerName: "mem-postgres",
          npmContainerName: "mem-npm",
          coturnContainerName: "mem-coturn",
          handoffRequired: false,
          handoffCompleted: false,
          warnings: [],
        }),
      ),
    )

    renderPage()

    expect(await screen.findByText("Certificate environment")).toBeInTheDocument()
    expect(screen.getAllByText("Not configured").length).toBeGreaterThan(0)
    expect(screen.queryByText("Let's Encrypt production")).not.toBeInTheDocument()
  })

})
