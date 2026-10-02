import { HttpResponse, http } from "msw"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import { OperatorDomainDetailPage } from "./operator-domain-detail-page"

const detailWithStaleActivePointer = {
  id: "domain-deltabox",
  baseDomain: "deltabox.dev",
  displayName: "deltabox.dev",
  purpose: "platform-main",
  isMainPlatformDomain: true,
  dnsProvider: "desec",
  dnsZone: "deltabox.dev",
  status: "Active",
  notes: null,
  // Deliberately stale: the registry pointer still references staging while
  // the production certificate is the authoritative main platform cert.
  activeCertificateEntityId: "registry-cert-staging",
  createdAtUtc: "2026-08-19T04:00:00Z",
  updatedAtUtc: "2026-08-20T00:00:00Z",
  certificates: [
    {
      id: "registry-cert-staging",
      domainId: "domain-deltabox",
      certificateId: "cert-staging",
      commonName: "*.deltabox.dev",
      provider: "desec",
      isWildcard: true,
      isStaging: true,
      isMainPlatformCertificate: false,
      isActive: true,
      status: "Succeeded",
      createdAtUtc: "2026-08-19T04:15:03Z",
      expiresAtUtc: "2026-11-17T04:15:03Z",
      thumbprint: "staging-thumbprint",
      npmCertificateId: 1,
      importedToNpm: true,
      lastValidatedAtUtc: null,
      lastImportedToNpmAtUtc: null,
      lastError: null,
    },
    {
      id: "registry-cert-production",
      domainId: "domain-deltabox",
      certificateId: "cert-production",
      commonName: "*.deltabox.dev",
      provider: "desec",
      isWildcard: true,
      isStaging: false,
      isMainPlatformCertificate: true,
      isActive: true,
      status: "Succeeded",
      createdAtUtc: "2026-08-19T04:45:03Z",
      expiresAtUtc: "2026-11-17T04:45:03Z",
      thumbprint: "production-thumbprint",
      npmCertificateId: 1,
      importedToNpm: true,
      lastValidatedAtUtc: null,
      lastImportedToNpmAtUtc: null,
      lastError: null,
    },
  ],
}

const pendingDomain = {
  id: "domain-matrixeasyhost",
  baseDomain: "matrixeasyhost.com",
  displayName: "matrixeasyhost.com",
  purpose: "stack",
  isMainPlatformDomain: false,
  dnsProvider: "desec",
  dnsZone: "matrixeasyhost.com",
  status: "Pending",
  notes: null,
  activeCertificateEntityId: null,
  createdAtUtc: "2026-09-11T00:00:00Z",
  updatedAtUtc: "2026-09-11T00:00:00Z",
  certificates: [],
}

const readyDomain = {
  ...pendingDomain,
  status: "Active",
  activeCertificateEntityId: "registry-cert-production",
  certificates: [
    {
      id: "registry-cert-production",
      domainId: "domain-matrixeasyhost",
      certificateId: "cert-production-matrixeasyhost",
      commonName: "*.matrixeasyhost.com",
      provider: "desec",
      isWildcard: true,
      isStaging: false,
      isMainPlatformCertificate: false,
      isActive: true,
      status: "Succeeded",
      createdAtUtc: "2026-09-11T00:00:00Z",
      expiresAtUtc: "2026-12-10T00:00:00Z",
      thumbprint: "production-thumbprint",
      npmCertificateId: 2,
      importedToNpm: true,
      lastValidatedAtUtc: null,
      lastImportedToNpmAtUtc: null,
      lastError: null,
    },
  ],
}


function renewalFor(domainId: string) {
  const production = domainId === "domain-deltabox"
  return {
    domainId,
    baseDomain: production ? "deltabox.dev" : "matrixeasyhost.com",
    dnsProvider: "desec",
    dnsZone: production ? "deltabox.dev" : "matrixeasyhost.com",
    policyConfigured: production,
    autoRenewEnabled: production,
    acmeEmail: production ? "ops@deltabox.dev" : null,
    renewalWindowDays: 30,
    retryIntervalHours: 24,
    credentialConfigured: production,
    credentialUpdatedAtUtc: production ? "2026-09-11T00:00:00Z" : null,
    hasActiveProductionCertificate: production,
    activeCertificateId: production ? "cert-production" : null,
    activeCertificateExpiresAtUtc: production ? "2026-11-17T04:45:03Z" : null,
    readinessStatus: production ? "Ready" : "RenewalCredentialRequired",
    readinessMessage: production ? "Ready for unattended renewal." : "Renewal credential required.",
    policyUpdatedAtUtc: production ? "2026-09-11T00:00:00Z" : null,
    operationalStatus: production ? "ready" : "unready",
    certificateExpired: false,
    daysRemaining: production ? 66 : null,
    nextEligibleRenewalAtUtc: production ? "2026-10-18T04:45:03Z" : null,
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
    manualRenewAvailable: production,
  }
}

function renderPage(domainId: string) {
  return renderWithProviders(
    <MemoryRouter initialEntries={[`/domains/${domainId}`]}>
      <Routes>
        <Route path="/domains/:domainId" element={<OperatorDomainDetailPage />} />
        <Route path="/domains" element={<div>domain registry destination</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

beforeEach(() => {
  window.localStorage.removeItem(LANGUAGE_STORAGE_KEY)
  server.use(
    http.get("/api/operator/domains/:domainId/renewal", ({ params }) =>
      HttpResponse.json(renewalFor(String(params.domainId))),
    ),
    http.get("/api/operator/domains/:domainId/certificates/issuance/latest", () =>
      HttpResponse.json({ operation: null }),
    ),
  )
})

describe("OperatorDomainDetailPage operator readiness", () => {
  it("presents the authoritative main platform certificate as selected", async () => {
    server.use(
      http.get("/api/operator/domains/domain-deltabox", () =>
        HttpResponse.json(detailWithStaleActivePointer),
      ),
    )

    renderPage("domain-deltabox")

    expect(await screen.findByText("Main-domain readiness")).toBeInTheDocument()
    expect(screen.getAllByText("*.deltabox.dev").length).toBeGreaterThan(0)
    expect(screen.getByText("Main platform cert")).toBeInTheDocument()
    expect(screen.getByText("In use for this domain")).toBeInTheDocument()
    expect(screen.getByText("Available")).toBeInTheDocument()
    expect(screen.getAllByText("Staging").length).toBeGreaterThan(0)
    expect(screen.getAllByText("Production").length).toBeGreaterThan(0)
    expect(screen.queryByText("Next step")).not.toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Set main" })).toBeDisabled()
    expect(screen.getByRole("button", { name: "Delete domain" })).toBeDisabled()
    expect(screen.getByText(/main platform domain cannot be deleted/i)).toBeInTheDocument()
    expect(await screen.findByText("Automatic renewal")).toBeInTheDocument()
    expect(screen.getByText("Renewal window begins")).toBeInTheDocument()
    expect(screen.queryByText("Renewal window begins {timestamp}")).not.toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open renewal" })).toHaveAttribute(
      "href",
      "/domains/domain-deltabox/renewal",
    )
  })

  it("replaces legacy registry metadata with an actionable no-certificate state", async () => {
    server.use(
      http.get("/api/operator/domains/domain-matrixeasyhost", () =>
        HttpResponse.json(pendingDomain),
      ),
    )

    renderPage("domain-matrixeasyhost")

    expect(await screen.findByText("Main-domain readiness")).toBeInTheDocument()
    expect(screen.getByText("Not issued yet")).toBeInTheDocument()
    expect(screen.queryByText("Next step")).not.toBeInTheDocument()
    expect(screen.getByText("deSEC")).toBeInTheDocument()
    expect(screen.getByText("DNS zone: matrixeasyhost.com")).toBeInTheDocument()
    expect(screen.getByText("No certificate yet")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Set main" })).toBeDisabled()
    expect(screen.getByRole("button", { name: "Delete domain" })).toBeEnabled()
    expect(screen.queryByText("Back to domains")).not.toBeInTheDocument()
    expect(
      screen.getByRole("link", { name: "Issue certificate" }),
    ).toHaveAttribute(
      "href",
      "/domains/domain-matrixeasyhost/certificates/new",
    )
    expect(screen.queryByText("Purpose")).not.toBeInTheDocument()
    expect(screen.queryByText("Display name")).not.toBeInTheDocument()
    expect(screen.queryByText("stack")).not.toBeInTheDocument()
    expect(screen.queryByText("Pending")).not.toBeInTheDocument()
  })


  it("shows compact certificate issuance activity instead of an empty certificate state", async () => {
    server.use(
      http.get("/api/operator/domains/domain-matrixeasyhost", () =>
        HttpResponse.json(pendingDomain),
      ),
      http.get(
        "/api/operator/domains/domain-matrixeasyhost/certificates/issuance/latest",
        () =>
          HttpResponse.json({
            operation: {
              operationId: "issue-running",
              domainId: "domain-matrixeasyhost",
              requestId: "request-running",
              status: "running",
              phaseCode: "certificate.dns-authoritative",
              phaseSummary: "Waiting for authoritative DNS readiness.",
              useStaging: true,
              requestedAtUtc: "2026-09-14T11:00:00Z",
              startedAtUtc: "2026-09-14T11:00:01Z",
              completedAtUtc: null,
              attemptCount: 1,
              progress: [],
              result: null,
              certificateId: null,
              diagnosticsIncidentId: null,
              isTerminal: false,
            },
          }),
      ),
    )

    renderPage("domain-matrixeasyhost")

    const placeholder = await screen.findByTestId(
      "certificate-issuance-placeholder-domain-matrixeasyhost",
    )
    expect(placeholder).toHaveTextContent("*.matrixeasyhost.com")
    expect(placeholder).toHaveTextContent("Staging")
    expect(placeholder).toHaveTextContent("Wait for authoritative DNS readiness")
    expect(within(placeholder).getByRole("link", { name: "View progress" })).toHaveAttribute(
      "href",
      "/domains/domain-matrixeasyhost/certificates/new",
    )
    expect(screen.queryByText("No certificate yet")).not.toBeInTheDocument()
  })

  it("keeps Set main on detail but blocks Domain deletion until certificates are removed", async () => {
    const user = userEvent.setup()
    let setMainCalls = 0

    server.use(
      http.get("/api/operator/domains/domain-matrixeasyhost", () =>
        HttpResponse.json(readyDomain),
      ),
      http.post("/api/operator/domains/domain-matrixeasyhost/set-main", () => {
        setMainCalls += 1
        return HttpResponse.json({ ...readyDomain, isMainPlatformDomain: true })
      }),
    )

    renderPage("domain-matrixeasyhost")

    const setMain = await screen.findByRole("button", { name: "Set main" })
    expect(setMain).toBeEnabled()
    expect(screen.getByRole("button", { name: "Delete domain" })).toBeDisabled()
    expect(
      screen.getByText(/delete this Domain's certificates first/i),
    ).toBeInTheDocument()
    expect(screen.queryByText("Back to domains")).not.toBeInTheDocument()

    await user.click(setMain)

    const dialog = await screen.findByRole("alertdialog")
    expect(
      within(dialog).getByRole("heading", {
        name: "Set matrixeasyhost.com as the main platform domain?",
      }),
    ).toBeInTheDocument()
    expect(
      within(dialog).getByText(/active production certificate.*main platform certificate/i),
    ).toBeInTheDocument()

    await user.click(within(dialog).getByRole("button", { name: "Set main" }))
    await waitFor(() => expect(setMainCalls).toBe(1))
  })

  it("deletes an eligible Domain only from its detail workspace", async () => {
    const user = userEvent.setup()
    let deleteCalls = 0
    let forceValue: string | null = null

    server.use(
      http.get("/api/operator/domains/domain-matrixeasyhost", () =>
        HttpResponse.json(pendingDomain),
      ),
      http.delete("/api/operator/domains/domain-matrixeasyhost", ({ request }) => {
        deleteCalls += 1
        forceValue = new URL(request.url).searchParams.get("force")
        return HttpResponse.json({ deleted: true, domainId: "domain-matrixeasyhost" })
      }),
    )

    renderPage("domain-matrixeasyhost")

    await user.click(await screen.findByRole("button", { name: "Delete domain" }))

    const dialog = await screen.findByRole("alertdialog")
    expect(
      within(dialog).getByRole("heading", { name: "Delete matrixeasyhost.com?" }),
    ).toBeInTheDocument()
    expect(
      within(dialog).getByText(/This cannot be undone/i),
    ).toBeInTheDocument()
    expect(deleteCalls).toBe(0)

    await user.click(within(dialog).getByRole("button", { name: "Delete domain" }))

    await waitFor(() => expect(deleteCalls).toBe(1))
    expect(forceValue).toBe("false")
    expect(await screen.findByText("domain registry destination")).toBeInTheDocument()
  })

  it("renders the readiness surface in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")

    server.use(
      http.get("/api/operator/domains/domain-matrixeasyhost", () =>
        HttpResponse.json(pendingDomain),
      ),
    )

    renderPage("domain-matrixeasyhost")

    expect(
      await screen.findByText("Bereitschaft als Hauptdomain"),
    ).toBeInTheDocument()
    expect(screen.getByText("Noch nicht ausgestellt")).toBeInTheDocument()
    expect(screen.getByText("Noch kein Zertifikat")).toBeInTheDocument()
    expect(
      screen.getByText(
        "Vor einer unbeaufsichtigten Verlängerung ist einmalig die Einrichtung der deSEC-Zugangsdaten erforderlich.",
      ),
    ).toBeInTheDocument()
    expect(screen.queryByText("Renewal credential required.")).not.toBeInTheDocument()
    expect(screen.queryByText("Nächster Schritt")).not.toBeInTheDocument()
    expect(
      screen.getByRole("button", { name: "Als Hauptdomain setzen" }),
    ).toBeDisabled()
    expect(screen.getByRole("button", { name: "Domain löschen" })).toBeEnabled()
    expect(
      screen.getByRole("link", { name: "Zertifikat ausstellen" }),
    ).toHaveAttribute(
      "href",
      "/domains/domain-matrixeasyhost/certificates/new",
    )
  })
})
