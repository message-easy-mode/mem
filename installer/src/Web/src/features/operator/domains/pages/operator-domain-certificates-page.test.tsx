import { HttpResponse, http } from "msw"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import { OperatorDomainCertificatesPage } from "./operator-domain-certificates-page"

const domain = {
  id: "domain-deltabox",
  baseDomain: "deltabox.dev",
  displayName: "Deltabox",
  purpose: "platform-main",
  isMainPlatformDomain: true,
  dnsProvider: "desec",
  dnsZone: "deltabox.dev",
  status: "Active",
  activeCertificateEntityId: "registry-cert-production",
  activeCertificateId: "cert-production",
  activeCertificateCommonName: "*.deltabox.dev",
  activeCertificateIsStaging: false,
  activeCertificateExpiresAtUtc: "2026-11-17T04:45:03Z",
  certificateCount: 1,
  createdAtUtc: "2026-08-19T04:00:00Z",
  updatedAtUtc: "2026-08-20T00:00:00Z",
}

const certificate = {
  id: "registry-cert-production",
  domainId: "domain-deltabox",
  domainBaseDomain: "deltabox.dev",
  domainDisplayName: "Deltabox",
  domainIsMainPlatformDomain: true,
  domainActiveCertificateEntityId: "registry-cert-production",
  domainDnsProvider: "desec",
  domainDnsZone: "deltabox.dev",
  certificateId: "cert-production",
  commonName: "*.deltabox.dev",
  domain: "*.deltabox.dev",
  zone: "deltabox.dev",
  provider: "desec",
  isWildcard: true,
  isStaging: false,
  isMainPlatformCertificate: true,
  isActive: true,
  isInUse: true,
  purpose: "platform-main",
  status: "Succeeded",
  createdAtUtc: "2026-08-19T04:45:03Z",
  expiresAtUtc: "2026-11-17T04:45:03Z",
  thumbprint: "thumbprint",
  npmCertificateId: 1,
  importedToNpm: true,
  lastValidatedAtUtc: null,
  lastImportedToNpmAtUtc: null,
  lastError: null,
}

const npmReady = {
  containerExists: true,
  containerRunning: true,
  adminUiReachable: true,
  initialized: true,
  apiAuthenticated: true,
  certificateApiReachable: true,
  runtimeState: "ready",
  baseUrl: "http://npm:81/api",
  baseUrlSource: "docker-network",
  certificateCount: 1,
  recommendedAction: "ready",
  warnings: [],
}

beforeEach(() => {
  window.localStorage.removeItem(LANGUAGE_STORAGE_KEY)
  server.use(
    http.get("/api/operator/domains", () => HttpResponse.json([domain])),
    http.get("/api/operator/domains/certificates", () => HttpResponse.json([certificate])),
    http.get("/api/operator/domains/certificates/issuance/active", () =>
      HttpResponse.json({ operations: [] }),
    ),
    http.get("/api/operator/domains/ingress/npm/status", () => HttpResponse.json(npmReady)),
  )
})

describe("OperatorDomainCertificatesPage Domain-owned inventory", () => {
  it("renders a fleet inventory whose certificate links resolve through the owning Domain", async () => {
    renderWithProviders(
      <MemoryRouter initialEntries={["/domains/certificates"]}>
        <OperatorDomainCertificatesPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText("Certificate inventory")).toBeInTheDocument()
    const certificateLink = await screen.findByRole("link", { name: /\*\.deltabox\.dev/ })
    expect(certificateLink).toHaveTextContent("Deltabox")
    expect(certificateLink).toHaveTextContent("deltabox.dev")
    expect(screen.getByText("Active for Domain")).toBeInTheDocument()
    expect(certificateLink).toHaveAttribute(
      "href",
      "/domains/domain-deltabox/certificates/cert-production",
    )
    expect(screen.queryByText("Selected for tools only")).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: /Use for/ })).not.toBeInTheDocument()
  })

  it("surfaces active durable issuance in the fleet inventory and links back to progress", async () => {
    server.use(
      http.get("/api/operator/domains/certificates/issuance/active", () =>
        HttpResponse.json({
          operations: [
            {
              operationId: "issue-matrixeasyhost",
              domainId: "domain-matrixeasyhost",
              baseDomain: "matrixeasyhost.com",
              status: "running",
              phaseCode: "certificate.dns-authoritative",
              phaseSummary: "Waiting for authoritative DNS readiness.",
              useStaging: false,
              requestedAtUtc: "2026-09-14T10:40:00Z",
              startedAtUtc: "2026-09-14T10:40:01Z",
            },
          ],
        }),
      ),
    )

    renderWithProviders(
      <MemoryRouter initialEntries={["/domains/certificates"]}>
        <OperatorDomainCertificatesPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText("Certificate issuance in progress · 1")).toBeInTheDocument()
    const placeholder = screen.getByTestId(
      "certificate-issuance-placeholder-domain-matrixeasyhost",
    )
    expect(placeholder).toHaveTextContent("*.matrixeasyhost.com")
    expect(placeholder).toHaveTextContent("Wait for authoritative DNS readiness")
    expect(within(placeholder).getByRole("link", { name: "View progress" })).toHaveAttribute(
      "href",
      "/domains/domain-matrixeasyhost/certificates/new",
    )
  })

  it("redirects a legacy query-owned bookmark to the authoritative Domain issue route", async () => {
    renderWithProviders(
      <MemoryRouter
        initialEntries={["/domains/certificates?domain=deltabox.dev&zone=deltabox.dev"]}
      >
        <Routes>
          <Route path="/domains/certificates" element={<OperatorDomainCertificatesPage />} />
          <Route
            path="/domains/:domainId/certificates/new"
            element={<div>Domain-owned issue route</div>}
          />
        </Routes>
      </MemoryRouter>,
    )

    expect(await screen.findByText("Domain-owned issue route")).toBeInTheDocument()
  })

  it("keeps low-level NPM work explicitly under advanced ingress diagnostics", async () => {
    const user = userEvent.setup()

    server.use(
      http.post(
        "/api/operator/domains/certificates/cert-production/validate",
        () => HttpResponse.json({
          succeeded: true,
          status: "Succeeded",
          message: "Certificate is valid.",
          errorCode: null,
          errorDetail: null,
          evidence: [],
        }),
      ),
    )

    renderWithProviders(
      <MemoryRouter>
        <OperatorDomainCertificatesPage />
      </MemoryRouter>,
    )

    await user.click(await screen.findByText("Advanced ingress diagnostics"))
    expect(screen.getByText("Certificate for diagnostics")).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Validate cert/key" }))

    await waitFor(() => {
      expect(screen.getByText("Certificate is valid.")).toBeInTheDocument()
    })
  })

  it("renders the inventory contract in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    renderWithProviders(
      <MemoryRouter>
        <OperatorDomainCertificatesPage />
      </MemoryRouter>,
    )

    expect(await screen.findByText("Zertifikatsinventar")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Zertifikat ausstellen" })).toBeInTheDocument()
  })
})
