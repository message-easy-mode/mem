import { HttpResponse, http } from "msw"
import { MemoryRouter } from "react-router-dom"
import { screen, within } from "@testing-library/react"
import { beforeEach, describe, expect, it } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import { OperatorDomainsPage } from "./operator-domains-page"

const domains = [
  {
    id: "domain-pending",
    baseDomain: "pending.example",
    displayName: "pending.example",
    purpose: "stack",
    isMainPlatformDomain: false,
    dnsProvider: "desec",
    dnsZone: "pending.example",
    status: "Pending",
    activeCertificateEntityId: null,
    activeCertificateId: null,
    activeCertificateCommonName: null,
    activeCertificateIsStaging: null,
    activeCertificateExpiresAtUtc: null,
    certificateCount: 0,
    createdAtUtc: "2026-09-11T00:00:00Z",
    updatedAtUtc: "2026-09-11T00:00:00Z",
  },
  {
    id: "domain-staging",
    baseDomain: "staging.example",
    displayName: "staging.example",
    purpose: "stack",
    isMainPlatformDomain: false,
    dnsProvider: "desec",
    dnsZone: "staging.example",
    status: "Active",
    activeCertificateEntityId: "entity-staging",
    activeCertificateId: "cert-staging",
    activeCertificateCommonName: "*.staging.example",
    activeCertificateIsStaging: true,
    activeCertificateExpiresAtUtc: "2026-11-17T04:15:03Z",
    certificateCount: 1,
    createdAtUtc: "2026-09-11T00:00:00Z",
    updatedAtUtc: "2026-09-11T00:00:00Z",
  },
  {
    id: "domain-production",
    baseDomain: "production.example",
    displayName: "production.example",
    purpose: "stack",
    isMainPlatformDomain: false,
    dnsProvider: "desec",
    dnsZone: "production.example",
    status: "Active",
    activeCertificateEntityId: "entity-production",
    activeCertificateId: "cert-production",
    activeCertificateCommonName: "*.production.example",
    activeCertificateIsStaging: false,
    activeCertificateExpiresAtUtc: "2026-11-17T04:15:03Z",
    certificateCount: 1,
    createdAtUtc: "2026-09-11T00:00:00Z",
    updatedAtUtc: "2026-09-11T00:00:00Z",
  },
]


const renewalInventory = domains.map((domain, index) => ({
  domainId: domain.id,
  baseDomain: domain.baseDomain,
  dnsProvider: "desec",
  dnsZone: domain.dnsZone,
  policyConfigured: index === 2,
  autoRenewEnabled: index === 2,
  acmeEmail: index === 2 ? "ops@production.example" : null,
  renewalWindowDays: 30,
  retryIntervalHours: 24,
  credentialConfigured: index === 2,
  credentialUpdatedAtUtc: index === 2 ? "2026-09-11T00:00:00Z" : null,
  hasActiveProductionCertificate: index === 2,
  activeCertificateId: index === 2 ? "cert-production" : null,
  activeCertificateExpiresAtUtc: index === 2 ? "2026-11-17T04:15:03Z" : null,
  readinessStatus: index === 2 ? "Ready" : "RenewalCredentialRequired",
  readinessMessage: index === 2 ? "Ready" : "Credential required",
  policyUpdatedAtUtc: index === 2 ? "2026-09-11T00:00:00Z" : null,
  operationalStatus: index === 2 ? "ready" : "unready",
  certificateExpired: false,
  daysRemaining: index === 2 ? 66 : null,
  nextEligibleRenewalAtUtc: index === 2 ? "2026-10-18T04:15:03Z" : null,
  nextAutomaticAttemptAtUtc: null,
  lastAttemptAtUtc: null,
  lastSuccessfulRenewalAtUtc: null,
  latestOperationId: null,
  latestOperationStatus: null,
  latestOperationStep: null,
  latestOperationAttemptCount: 0,
  latestRequestedBy: null,
  latestErrorCode: null,
  diagnosticsIncidentId: null,
  diagnosticsHref: null,
  manualRenewAvailable: index === 2,
}))

function renderPage() {
  return renderWithProviders(
    <MemoryRouter initialEntries={["/domains"]}>
      <OperatorDomainsPage />
    </MemoryRouter>,
  )
}

beforeEach(() => {
  window.localStorage.removeItem(LANGUAGE_STORAGE_KEY)
  server.use(
    http.get("/api/operator/domains/renewal", () => HttpResponse.json(renewalInventory)),
  )
})

describe("OperatorDomainsPage readiness registry", () => {
  it("keeps the registry read-oriented with one Manage action per Domain", async () => {
    server.use(
      http.get("/api/operator/domains", () => HttpResponse.json(domains)),
    )

    renderPage()

    const pendingDomainLink = await screen.findByRole("link", { name: "pending.example" })
    expect(pendingDomainLink).toHaveAttribute("href", "/domains/domain-pending")
    expect(screen.queryByText("Purpose")).not.toBeInTheDocument()
    expect(screen.queryByText("stack")).not.toBeInTheDocument()
    expect(screen.getByText("Certificate needed")).toBeInTheDocument()
    expect(screen.getByText("Production certificate needed")).toBeInTheDocument()
    expect(screen.getByText("Ready to set as main")).toBeInTheDocument()
    const registry = screen.getByTestId("domain-registry")
    expect(within(registry).getByRole("table")).toHaveClass("min-w-[960px]")
    expect(screen.getAllByText("Actions").length).toBeGreaterThan(0)
    expect(screen.getAllByText("Renewal").length).toBeGreaterThan(0)
    expect(screen.getByRole("link", { name: "Healthy" })).toHaveAttribute(
      "href",
      "/domains/domain-production/renewal",
    )

    const pendingRow = screen.getByTestId("domain-row-domain-pending")
    const stagingRow = screen.getByTestId("domain-row-domain-staging")
    const productionRow = screen.getByTestId("domain-row-domain-production")
    const pendingActions = screen.getByTestId("domain-actions-domain-pending")

    expect(pendingActions).toHaveClass("sticky", "right-0", "min-w-40")
    expect(within(pendingRow).getByRole("link", { name: "Manage" })).toBeInTheDocument()
    expect(within(stagingRow).getByRole("link", { name: "Manage" })).toBeInTheDocument()
    expect(within(productionRow).getByRole("link", { name: "Manage" })).toBeInTheDocument()
    expect(within(registry).queryByRole("button", { name: "Set main" })).not.toBeInTheDocument()
    expect(within(registry).queryByRole("button", { name: "Delete" })).not.toBeInTheDocument()
  })

  it("renders the domain registry in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    server.use(
      http.get("/api/operator/domains", () => HttpResponse.json([domains[0]])),
    )

    renderPage()

    expect(await screen.findByText("Domain-Register")).toBeInTheDocument()
    expect(await screen.findByText("Zertifikat erforderlich")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Domain hinzufügen" })).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Zertifikate" })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Aktualisieren" })).toBeInTheDocument()
  })
})
