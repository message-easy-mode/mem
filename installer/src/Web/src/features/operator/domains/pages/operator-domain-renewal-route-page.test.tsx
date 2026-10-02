import { HttpResponse, http } from "msw"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { OperatorSessionProvider } from "@/features/auth/operator-session-provider"
import "@/test/msw-lifecycle"
import { renderWithProviders } from "@/test/render-with-providers"
import { server } from "@/test/msw-server"

import { OperatorDomainRenewalRoutePage } from "./operator-domain-renewal-route-page"

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

const renewalInventory = [
  {
    domainId: "domain-ready",
    baseDomain: "deltabox.dev",
    dnsProvider: "desec",
    dnsZone: "deltabox.dev",
    policyConfigured: true,
    autoRenewEnabled: true,
    acmeEmail: "ops@deltabox.dev",
    renewalWindowDays: 30,
    retryIntervalHours: 24,
    credentialConfigured: true,
    credentialUpdatedAtUtc: "2026-09-12T01:00:00Z",
    hasActiveProductionCertificate: true,
    activeCertificateId: "cert-production",
    activeCertificateExpiresAtUtc: "2026-11-27T00:00:00Z",
    readinessStatus: "Ready",
    readinessMessage: "Automatic renewal is configured.",
    policyUpdatedAtUtc: "2026-09-12T01:00:00Z",
    operationalStatus: "ready",
    certificateExpired: false,
    daysRemaining: 76,
    nextEligibleRenewalAtUtc: "2026-10-28T00:00:00Z",
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
    manualRenewAvailable: true,
  },
  {
    domainId: "domain-historical",
    baseDomain: "matrixeasyhost.com",
    dnsProvider: "desec",
    dnsZone: "matrixeasyhost.com",
    policyConfigured: false,
    autoRenewEnabled: false,
    acmeEmail: null,
    renewalWindowDays: 30,
    retryIntervalHours: 24,
    credentialConfigured: false,
    credentialUpdatedAtUtc: null,
    hasActiveProductionCertificate: true,
    activeCertificateId: "cert-historical",
    activeCertificateExpiresAtUtc: "2026-12-10T00:00:00Z",
    readinessStatus: "RenewalCredentialRequired",
    readinessMessage: "Renewal credential required.",
    policyUpdatedAtUtc: null,
    operationalStatus: "unready",
    certificateExpired: false,
    daysRemaining: 89,
    nextEligibleRenewalAtUtc: "2026-11-10T00:00:00Z",
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
    manualRenewAvailable: false,
  },
]

const failedRenewal = {
  ...renewalInventory[0],
  operationalStatus: "failed",
  daysRemaining: 6,
  nextAutomaticAttemptAtUtc: "2026-09-13T01:00:00Z",
  lastAttemptAtUtc: "2026-09-12T01:00:00Z",
  latestOperationId: "11111111-1111-1111-1111-111111111111",
  latestOperationStatus: "failed",
  latestOperationStep: "failed",
  latestOperationAttemptCount: 2,
  latestRequestedBy: "system",
  latestErrorCode: "SyntheticIssueFailure",
  diagnosticsIncidentId: "inc_domain_renewal_11111111111111111111111111111111",
  diagnosticsHref:
    "/diagnostics/logs?incident=inc_domain_renewal_11111111111111111111111111111111",
  manualRenewAvailable: true,
}

function renderDomainPage(domainId = "domain-ready", session = ownerSession) {
  return renderWithProviders(
    <MemoryRouter initialEntries={[`/domains/${domainId}/renewal`]}>
      <OperatorSessionProvider session={session} signOut={async () => undefined}>
        <Routes>
          <Route
            path="/domains/:domainId/renewal"
            element={<OperatorDomainRenewalRoutePage />}
          />
        </Routes>
      </OperatorSessionProvider>
    </MemoryRouter>,
  )
}

function renderPage(session = ownerSession) {
  return renderWithProviders(
    <MemoryRouter initialEntries={["/domains/renewal"]}>
      <OperatorSessionProvider session={session} signOut={async () => undefined}>
        <OperatorDomainRenewalRoutePage />
      </OperatorSessionProvider>
    </MemoryRouter>,
  )
}

function useDomainHandlers(domainId: string, renewal: (typeof renewalInventory)[number]) {
  server.use(
    http.get(`/api/operator/domains/${domainId}/renewal`, () => HttpResponse.json(renewal)),
    http.get(`/api/operator/domains/${domainId}/renewal/history`, () => HttpResponse.json([])),
  )
}

afterEach(() => {
  window.localStorage.clear()
})

describe("OperatorDomainRenewalRoutePage", () => {
  it("renders the fleet route as a compact read-only renewal inventory", async () => {
    server.use(
      http.get("/api/operator/domains/renewal", () => HttpResponse.json(renewalInventory)),
    )

    renderPage()

    expect(await screen.findByRole("heading", { name: "Renewal", level: 1 })).toBeInTheDocument()
    expect(await screen.findByText("Renewal inventory")).toBeInTheDocument()
    expect(await screen.findAllByText("deltabox.dev")).not.toHaveLength(0)
    expect(screen.getAllByText("matrixeasyhost.com")).not.toHaveLength(0)
    expect(screen.getByRole("link", { name: "deltabox.dev" })).toHaveClass(
      "text-emerald-400",
      "hover:text-emerald-300",
    )
    expect(screen.getAllByText("Renewal credential required")).not.toHaveLength(0)
    expect(screen.getAllByText(/Renewal window begins/)).not.toHaveLength(0)
    expect(screen.getByText(/Automatic renewal runs server-side without browser presence/i)).toBeInTheDocument()

    expect(screen.getByTestId("renewal-inventory-table")).toHaveClass("min-w-[1160px]")
    expect(screen.getByTestId("renewal-actions-domain-ready")).toHaveClass(
      "sticky",
      "right-0",
      "min-w-40",
    )

    const openLinks = screen.getAllByRole("link", { name: "Open renewal" })
    expect(openLinks).toHaveLength(2)
    expect(openLinks.some((link) => link.getAttribute("href") === "/domains/domain-ready/renewal")).toBe(true)
    expect(openLinks.some((link) => link.getAttribute("href") === "/domains/domain-historical/renewal")).toBe(true)

    expect(screen.queryByLabelText("ACME contact")).not.toBeInTheDocument()
    expect(screen.queryByLabelText("deSEC token")).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Renew now" })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Remove credential" })).not.toBeInTheDocument()
    expect(screen.queryByDisplayValue(/desec.+secret/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/providerToken/i)).not.toBeInTheDocument()
  })

  it("moves Domain renewal controls into the Domain-owned detail workspace", async () => {
    useDomainHandlers("domain-historical", renewalInventory[1])

    renderDomainPage("domain-historical")

    expect(
      await screen.findByRole("heading", { name: "Renewal · matrixeasyhost.com", level: 1 }),
    ).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Back to renewal" })).toHaveAttribute(
      "href",
      "/domains/renewal",
    )
    expect(screen.getByLabelText("ACME contact")).toBeInTheDocument()
    expect(screen.getByLabelText("deSEC token")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Verify and save" })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Enable automatic renewal" })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Remove credential" })).toBeInTheDocument()
    expect(screen.getByText("Recent renewal history")).toBeInTheDocument()
  })

  it("clears the write-only token while recent owner step-up is completed", async () => {
    const user = userEvent.setup()
    let requestBody: Record<string, unknown> | null = null

    useDomainHandlers("domain-historical", renewalInventory[1])
    server.use(
      http.put(
        "/api/operator/domains/domain-historical/renewal/credential",
        async ({ request }) => {
          requestBody = (await request.json()) as Record<string, unknown>
          return HttpResponse.json(
            {
              type: "https://mem.invalid/problems/operator.step_up_required",
              title: "Recent identity verification required",
              status: 403,
              detail: "Recent password and authenticator verification is required.",
              code: "step_up_required",
              error: "step_up_required",
            },
            { status: 403 },
          )
        },
      ),
    )

    renderDomainPage("domain-historical")

    const emailInput = await screen.findByLabelText("ACME contact")
    const tokenInput = screen.getByLabelText("deSEC token")
    await user.type(emailInput, "ops@matrixeasyhost.com")
    await user.type(tokenInput, "candidate-renewal-token")
    await user.click(screen.getByRole("button", { name: "Verify and save" }))

    expect(await screen.findByRole("dialog")).toHaveTextContent("Verify your identity")
    expect(tokenInput).toHaveValue("")
    expect(requestBody).toMatchObject({
      providerToken: "candidate-renewal-token",
      acmeEmail: "ops@matrixeasyhost.com",
    })
  })

  it("keeps detail mutations Platform Owner-only while leaving renewal state readable", async () => {
    useDomainHandlers("domain-ready", renewalInventory[0])

    renderDomainPage("domain-ready", operatorSession)

    expect(
      await screen.findByRole("heading", { name: "Renewal · deltabox.dev", level: 1 }),
    ).toBeInTheDocument()
    expect(screen.getByText("Platform Owner required for changes")).toBeInTheDocument()
    expect(screen.queryByLabelText("deSEC token")).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Remove credential" })).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Renew now" })).not.toBeInTheDocument()
  })

  it("queues manual Renew now from the Domain renewal workspace", async () => {
    const user = userEvent.setup()
    let runRequests = 0

    useDomainHandlers("domain-ready", renewalInventory[0])
    server.use(
      http.post("/api/operator/domains/domain-ready/renewal/run", () => {
        runRequests += 1
        return HttpResponse.json(
          {
            accepted: true,
            status: "Queued",
            message: "Renewal queued.",
            operationId: "11111111-1111-1111-1111-111111111111",
            renewal: {
              ...renewalInventory[0],
              operationalStatus: "queued",
              latestOperationId: "11111111-1111-1111-1111-111111111111",
              latestOperationStatus: "queued",
              latestOperationStep: "eligibility",
              latestRequestedBy: "operator",
              manualRenewAvailable: false,
            },
          },
          { status: 202 },
        )
      }),
    )

    renderDomainPage("domain-ready")

    await screen.findByRole("heading", { name: "Renewal · deltabox.dev", level: 1 })
    await user.click(screen.getByRole("button", { name: "Renew now" }))

    await waitFor(() => expect(runRequests).toBe(1))
    expect(
      await screen.findByText("Renewal was queued for the server-owned worker."),
    ).toBeInTheDocument()
  })

  it("shows failed renewal recovery, Diagnostics deep link and safe durable history", async () => {
    server.use(
      http.get("/api/operator/domains/domain-ready/renewal", () =>
        HttpResponse.json(failedRenewal),
      ),
      http.get("/api/operator/domains/domain-ready/renewal/history", () =>
        HttpResponse.json([
          {
            operationId: "11111111-1111-1111-1111-111111111111",
            status: "failed",
            step: "failed",
            requestedBy: "system",
            requestedAtUtc: "2026-09-12T00:00:00Z",
            startedAtUtc: "2026-09-12T00:00:01Z",
            completedAtUtc: "2026-09-12T01:00:00Z",
            attemptCount: 2,
            errorCode: "SyntheticIssueFailure",
            diagnosticsIncidentId:
              "inc_domain_renewal_11111111111111111111111111111111",
            diagnosticsHref:
              "/diagnostics/logs?incident=inc_domain_renewal_11111111111111111111111111111111",
          },
        ]),
      ),
    )

    renderDomainPage()

    expect(await screen.findAllByText("Renewal needs attention")).not.toHaveLength(0)
    expect(screen.getByText(/6 days remaining/i)).toBeInTheDocument()
    expect(screen.getByText("Recent renewal history")).toBeInTheDocument()
    expect(screen.getAllByRole("link", { name: "Open Diagnostics" })[0]).toHaveAttribute(
      "href",
      "/diagnostics/logs?incident=inc_domain_renewal_11111111111111111111111111111111",
    )
    expect(screen.queryByText(/providerToken/i)).not.toBeInTheDocument()
  })

  it("renders the renewal inventory contract in German without fleet-level mutation controls", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    server.use(
      http.get("/api/operator/domains/renewal", () => HttpResponse.json(renewalInventory)),
    )

    renderPage()

    expect(
      await screen.findByRole("heading", { name: "Verlängerung", level: 1 }),
    ).toBeInTheDocument()
    expect(await screen.findByText("Verlängerungsübersicht")).toBeInTheDocument()
    expect(screen.getByText("Verlängerungsgrundlage")).toBeInTheDocument()
    expect(await screen.findAllByText("Verlängerungszugangsdaten erforderlich")).not.toHaveLength(0)
    expect(screen.getByText(/serverseitig ohne Browser/i)).toBeInTheDocument()
    expect(screen.getByText(/aktiviert ihn bei Bedarf in NPM, prüft den Ingress/i)).toBeInTheDocument()
    expect(screen.getByText("Auto-Verlängerung aktiv")).toBeInTheDocument()
    expect(
      screen.getAllByRole("link", { name: "Verlängerung öffnen" }).length,
    ).toBeGreaterThanOrEqual(2)
    expect(screen.queryByLabelText("deSEC-Token")).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Jetzt verlängern" })).not.toBeInTheDocument()
  })
})
