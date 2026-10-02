import { HttpResponse, http } from "msw"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { beforeEach, describe, expect, it, vi } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import { OperatorDomainCertificateDetailPage } from "./operator-domain-certificate-detail-page"
import { OperatorDomainCertificateIssuePage } from "./operator-domain-certificate-issue-page"
import { OperatorDomainCertificateListPage } from "./operator-domain-certificate-list-page"

const domainSummary = {
  id: "domain-example",
  baseDomain: "example.com",
  displayName: "Example",
  purpose: "stack",
  isMainPlatformDomain: false,
  dnsProvider: "desec",
  dnsZone: "example.com",
  status: "Active",
  activeCertificateEntityId: null,
  activeCertificateId: null,
  activeCertificateCommonName: null,
  activeCertificateIsStaging: null,
  activeCertificateExpiresAtUtc: null,
  certificateCount: 1,
  createdAtUtc: "2026-09-12T00:00:00Z",
  updatedAtUtc: "2026-09-12T00:00:00Z",
}

const domainDetail = {
  ...domainSummary,
  notes: null,
  certificates: [],
}

const certificate = {
  id: "registry-cert-example",
  domainId: "domain-example",
  domainBaseDomain: "example.com",
  domainDisplayName: "Example",
  domainIsMainPlatformDomain: false,
  domainActiveCertificateEntityId: null,
  domainDnsProvider: "desec",
  domainDnsZone: "example.com",
  certificateId: "cert-example-production",
  commonName: "*.example.com",
  domain: "*.example.com",
  zone: "example.com",
  provider: "desec",
  isWildcard: true,
  isStaging: false,
  isMainPlatformCertificate: false,
  isActive: true,
  isInUse: false,
  purpose: null,
  status: "Succeeded",
  createdAtUtc: "2026-09-12T01:00:00Z",
  expiresAtUtc: "2026-12-11T01:00:00Z",
  thumbprint: "ABC123",
  npmCertificateId: 7,
  importedToNpm: true,
  lastValidatedAtUtc: "2026-09-12T01:05:00Z",
  lastImportedToNpmAtUtc: "2026-09-12T01:06:00Z",
  lastError: null,
}

let latestIssuance: Record<string, unknown> | null = null

const stagingCertificate = {
  ...certificate,
  id: "registry-cert-example-staging",
  certificateId: "cert-example-staging",
  isStaging: true,
  importedToNpm: false,
  npmCertificateId: null,
}

beforeEach(() => {
  window.localStorage.removeItem(LANGUAGE_STORAGE_KEY)
  latestIssuance = null
  server.use(
    http.get("/api/operator/domains", () => HttpResponse.json([domainSummary])),
    http.get("/api/operator/domains/domain-example", () => HttpResponse.json(domainDetail)),
    http.get(
      "/api/operator/domains/domain-example/certificates",
      () => HttpResponse.json([certificate]),
    ),
    http.get(
      "/api/operator/domains/domain-example/certificates/cert-example-production",
      () => HttpResponse.json(certificate),
    ),
    http.get(
      "/api/operator/domains/domain-example/certificates/issuance/latest",
      () => HttpResponse.json({ operation: latestIssuance }),
    ),
  )
})

describe("Domain-owned certificate routes", () => {
  it("uses the global new route only to choose an owning Domain", async () => {
    renderWithProviders(
      <MemoryRouter initialEntries={["/domains/certificates/new"]}>
        <OperatorDomainCertificateIssuePage />
      </MemoryRouter>,
    )

    expect(await screen.findByText("Choose owning Domain")).toBeInTheDocument()
    expect(await screen.findByRole("link", { name: /Example/ })).toHaveAttribute(
      "href",
      "/domains/domain-example/certificates/new",
    )
    expect(screen.queryByLabelText("Wildcard domain")).not.toBeInTheDocument()
  })

  it("locks issuance identity to the Domain resource on the nested route", async () => {
    renderWithProviders(
      <MemoryRouter initialEntries={["/domains/domain-example/certificates/new"]}>
        <Routes>
          <Route
            path="/domains/:domainId/certificates/new"
            element={<OperatorDomainCertificateIssuePage />}
          />
        </Routes>
      </MemoryRouter>,
    )

    expect(await screen.findByText("Owning Domain: example.com")).toBeInTheDocument()
    const wildcard = screen.getByDisplayValue("*.example.com")
    expect(wildcard).toHaveAttribute("readonly")
    expect(screen.getByDisplayValue("example.com")).toBeInTheDocument()
  })

  it("lists only the scoped Domain certificate resource and links to nested details", async () => {
    renderWithProviders(
      <MemoryRouter initialEntries={["/domains/domain-example/certificates"]}>
        <Routes>
          <Route
            path="/domains/:domainId/certificates"
            element={<OperatorDomainCertificateListPage />}
          />
        </Routes>
      </MemoryRouter>,
    )

    expect(await screen.findByText("Domain certificates")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: /\*\.example\.com/ })).toHaveAttribute(
      "href",
      "/domains/domain-example/certificates/cert-example-production",
    )
  })

  it("shows an active issuance placeholder on the Domain certificate inventory", async () => {
    latestIssuance = {
      operationId: "issue-running",
      domainId: "domain-example",
      requestId: "request-running",
      status: "running",
      phaseCode: "certificate.dns-authoritative",
      phaseSummary: "Waiting for authoritative DNS readiness.",
      useStaging: false,
      requestedAtUtc: "2026-09-14T10:40:00Z",
      startedAtUtc: "2026-09-14T10:40:01Z",
      completedAtUtc: null,
      attemptCount: 1,
      progress: [],
      result: null,
      certificateId: null,
      diagnosticsIncidentId: null,
      isTerminal: false,
    }

    renderWithProviders(
      <MemoryRouter initialEntries={["/domains/domain-example/certificates"]}>
        <Routes>
          <Route
            path="/domains/:domainId/certificates"
            element={<OperatorDomainCertificateListPage />}
          />
        </Routes>
      </MemoryRouter>,
    )

    const placeholder = await screen.findByTestId(
      "certificate-issuance-placeholder-domain-example",
    )
    expect(placeholder).toHaveTextContent("*.example.com")
    expect(placeholder).toHaveTextContent("Wait for authoritative DNS readiness")
    expect(within(placeholder).getByRole("link", { name: "View progress" })).toHaveAttribute(
      "href",
      "/domains/domain-example/certificates/new",
    )
  })

  it("refreshes the Domain certificate inventory after durable issuance becomes terminal", async () => {
    let certificateReads = 0
    latestIssuance = {
      operationId: "issue-completed",
      domainId: "domain-example",
      requestId: "request-completed",
      status: "succeeded",
      phaseCode: "certificate.complete",
      phaseSummary: "Certificate issuance completed.",
      useStaging: false,
      requestedAtUtc: "2026-09-14T10:40:00Z",
      startedAtUtc: "2026-09-14T10:40:01Z",
      completedAtUtc: "2026-09-14T10:41:00Z",
      attemptCount: 1,
      progress: [],
      result: null,
      certificateId: "cert-example-production",
      diagnosticsIncidentId: null,
      isTerminal: true,
    }

    server.use(
      http.get(
        "/api/operator/domains/domain-example/certificates",
        () => {
          certificateReads += 1
          return HttpResponse.json(certificateReads === 1 ? [] : [certificate])
        },
      ),
    )

    renderWithProviders(
      <MemoryRouter initialEntries={["/domains/domain-example/certificates"]}>
        <Routes>
          <Route
            path="/domains/:domainId/certificates"
            element={<OperatorDomainCertificateListPage />}
          />
        </Routes>
      </MemoryRouter>,
    )

    expect(
      await screen.findByRole("link", { name: /\*\.example\.com/ }),
    ).toHaveAttribute(
      "href",
      "/domains/domain-example/certificates/cert-example-production",
    )
    expect(certificateReads).toBeGreaterThanOrEqual(2)
  })

  it("sets active through the Domain-scoped endpoint", async () => {
    const user = userEvent.setup()
    const confirm = vi.spyOn(window, "confirm").mockReturnValue(true)
    let scopedSetActiveCalls = 0

    server.use(
      http.post(
        "/api/operator/domains/domain-example/certificates/cert-example-production/set-active",
        () => {
          scopedSetActiveCalls += 1
          return HttpResponse.json({
            succeeded: true,
            status: "Succeeded",
            message: "Active certificate updated.",
            certificateId: "cert-example-production",
            baseDomain: "example.com",
          })
        },
      ),
    )

    renderWithProviders(
      <MemoryRouter
        initialEntries={["/domains/domain-example/certificates/cert-example-production"]}
      >
        <Routes>
          <Route
            path="/domains/:domainId/certificates/:certificateId"
            element={<OperatorDomainCertificateDetailPage />}
          />
        </Routes>
      </MemoryRouter>,
    )

    await user.click(await screen.findByRole("button", { name: "Make active for this Domain" }))

    await waitFor(() => expect(scopedSetActiveCalls).toBe(1))
    confirm.mockRestore()
  })

  it("deletes a certificate only through its owning Domain route", async () => {
    const user = userEvent.setup()
    let deleteCalls = 0

    server.use(
      http.get(
        "/api/operator/domains/domain-example/certificates/cert-example-staging",
        () => HttpResponse.json(stagingCertificate),
      ),
      http.delete(
        "/api/operator/domains/domain-example/certificates/cert-example-staging",
        () => {
          deleteCalls += 1
          return HttpResponse.json({
            succeeded: true,
            status: "Deleted",
            message: "Certificate was deleted from the Domain.",
            certificateId: "cert-example-staging",
            metadataDeleted: true,
            filesDeleted: true,
            npmCertificateDeleted: false,
            warnings: [],
          })
        },
      ),
    )

    renderWithProviders(
      <MemoryRouter
        initialEntries={["/domains/domain-example/certificates/cert-example-staging"]}
      >
        <Routes>
          <Route
            path="/domains/:domainId/certificates/:certificateId"
            element={<OperatorDomainCertificateDetailPage />}
          />
          <Route
            path="/domains/:domainId/certificates"
            element={<div>certificate list destination</div>}
          />
        </Routes>
      </MemoryRouter>,
    )

    await user.click(await screen.findByRole("button", { name: "Delete certificate" }))

    const dialog = await screen.findByRole("alertdialog")
    expect(
      within(dialog).getByRole("heading", { name: "Delete *.example.com?" }),
    ).toBeInTheDocument()
    expect(deleteCalls).toBe(0)

    await user.click(within(dialog).getByRole("button", { name: "Delete certificate" }))

    await waitFor(() => expect(deleteCalls).toBe(1))
    expect(await screen.findByText("certificate list destination")).toBeInTheDocument()
  })

  it("keeps the main platform certificate protected from deletion", async () => {
    const mainDomain = {
      ...domainDetail,
      isMainPlatformDomain: true,
      activeCertificateEntityId: certificate.id,
    }
    const mainCertificate = {
      ...certificate,
      domainIsMainPlatformDomain: true,
      domainActiveCertificateEntityId: certificate.id,
      isMainPlatformCertificate: true,
      isInUse: true,
    }

    server.use(
      http.get("/api/operator/domains/domain-example", () => HttpResponse.json(mainDomain)),
      http.get(
        "/api/operator/domains/domain-example/certificates/cert-example-production",
        () => HttpResponse.json(mainCertificate),
      ),
    )

    renderWithProviders(
      <MemoryRouter
        initialEntries={["/domains/domain-example/certificates/cert-example-production"]}
      >
        <Routes>
          <Route
            path="/domains/:domainId/certificates/:certificateId"
            element={<OperatorDomainCertificateDetailPage />}
          />
        </Routes>
      </MemoryRouter>,
    )

    expect(await screen.findByRole("button", { name: "Delete certificate" })).toBeDisabled()
    expect(screen.getByText(/main platform certificate cannot be deleted/i)).toBeInTheDocument()
  })

  it("queues Domain-scoped issuance and renders durable server progress", async () => {
    const user = userEvent.setup()
    let requestBody: Record<string, unknown> | null = null

    server.use(
      http.post(
        "/api/operator/domains/domain-example/certificates/issuance",
        async ({ request }) => {
          requestBody = (await request.json()) as Record<string, unknown>
          latestIssuance = {
            operationId: "11111111-1111-4111-8111-111111111111",
            domainId: "domain-example",
            requestId: requestBody.requestId,
            status: "running",
            phaseCode: "certificate.dns-publish",
            phaseSummary: "Publishing the DNS-01 challenge through deSEC.",
            useStaging: false,
            requestedAtUtc: "2026-09-12T06:00:00Z",
            startedAtUtc: "2026-09-12T06:00:01Z",
            completedAtUtc: null,
            attemptCount: 1,
            progress: [],
            result: null,
            certificateId: null,
            diagnosticsIncidentId: null,
            isTerminal: false,
          }
          return HttpResponse.json(
            {
              accepted: true,
              status: "Queued",
              message: "Queued.",
              operation: latestIssuance,
            },
            { status: 202 },
          )
        },
      ),
    )

    renderWithProviders(
      <MemoryRouter initialEntries={["/domains/domain-example/certificates/new"]}>
        <Routes>
          <Route
            path="/domains/:domainId/certificates/new"
            element={<OperatorDomainCertificateIssuePage />}
          />
        </Routes>
      </MemoryRouter>,
    )

    await user.type(await screen.findByLabelText("ACME email"), "owner@example.test")
    await user.type(screen.getByLabelText("deSEC token"), "candidate-token")
    await user.click(screen.getByRole("button", { name: "Issue production certificate" }))

    await waitFor(() => expect(requestBody).not.toBeNull())
    expect(requestBody).toMatchObject({
      email: "owner@example.test",
      providerToken: "candidate-token",
      useStaging: false,
    })
    expect(requestBody).toHaveProperty("requestId")
    expect(requestBody).not.toHaveProperty("domain")
    expect(requestBody).not.toHaveProperty("zone")
    expect(requestBody).not.toHaveProperty("storageName")
    expect(requestBody).not.toHaveProperty("provider")

    expect(
      await screen.findByText("Publishing the DNS-01 challenge through deSEC."),
    ).toBeInTheDocument()
    expect(screen.getByTestId("certificate-issuance-timeline")).toBeInTheDocument()
    expect(screen.getByTestId("certificate-issuance-phase-prepare")).toHaveAttribute(
      "data-state",
      "completed",
    )
    expect(screen.getByTestId("certificate-issuance-phase-publish-dns")).toHaveAttribute(
      "data-state",
      "current",
    )
    expect(screen.getByTestId("certificate-issuance-phase-wait-dns")).toHaveAttribute(
      "data-state",
      "pending",
    )
    expect(
      screen.getByText(/You can refresh, navigate away, or return later/),
    ).toBeInTheDocument()
  })

  it("rediscovers durable state after an ambiguous POST failure", async () => {
    const user = userEvent.setup()
    let latestReads = 0
    let postAttempted = false

    server.use(
      http.get(
        "/api/operator/domains/domain-example/certificates/issuance/latest",
        () => {
          latestReads += 1
          return HttpResponse.json({
            operation: !postAttempted
              ? null
              : {
                  operationId: "66666666-6666-4666-8666-666666666666",
                  domainId: "domain-example",
                  requestId: "77777777-7777-4777-8777-777777777777",
                  status: "running",
                  phaseCode: "certificate.acme-order",
                  phaseSummary: "Creating the Let's Encrypt certificate order.",
                  useStaging: true,
                  requestedAtUtc: new Date(Date.now() - 5_000).toISOString(),
                  startedAtUtc: new Date(Date.now() - 4_000).toISOString(),
                  completedAtUtc: null,
                  attemptCount: 1,
                  progress: [],
                  result: null,
                  certificateId: null,
                  diagnosticsIncidentId: null,
                  isTerminal: false,
                },
          })
        },
      ),
      http.post(
        "/api/operator/domains/domain-example/certificates/issuance",
        () => {
          postAttempted = true
          return HttpResponse.json(
          {
            type: "about:blank",
            title: "Gateway response lost",
            status: 503,
            detail: "The browser did not receive the successful queue response.",
          },
          { status: 503 },
          )
        },
      ),
    )

    renderWithProviders(
      <MemoryRouter initialEntries={["/domains/domain-example/certificates/new"]}>
        <Routes>
          <Route
            path="/domains/:domainId/certificates/new"
            element={<OperatorDomainCertificateIssuePage />}
          />
        </Routes>
      </MemoryRouter>,
    )

    await user.type(await screen.findByLabelText("ACME email"), "owner@example.test")
    await user.type(screen.getByLabelText("deSEC token"), "staging-candidate-token")
    await user.click(screen.getByRole("checkbox", { name: /Use Let’s Encrypt staging/ }))
    await user.click(screen.getByRole("button", { name: "Issue staging certificate" }))

    expect(
      await screen.findByText("Creating the Let's Encrypt certificate order."),
    ).toBeInTheDocument()
    expect(latestReads).toBeGreaterThanOrEqual(2)
    expect(screen.queryByText("The browser did not receive the successful queue response.")).not.toBeInTheDocument()
  })

  it("rediscovers an active durable issuance operation after navigation or refresh", async () => {
    latestIssuance = {
      operationId: "22222222-2222-4222-8222-222222222222",
      domainId: "domain-example",
      requestId: "33333333-3333-4333-8333-333333333333",
      status: "running",
      phaseCode: "certificate.acme-validation",
      phaseSummary: "Let's Encrypt is validating the DNS challenge.",
      useStaging: false,
      requestedAtUtc: new Date(Date.now() - 65_000).toISOString(),
      startedAtUtc: new Date(Date.now() - 64_000).toISOString(),
      completedAtUtc: null,
      attemptCount: 1,
      progress: [],
      result: null,
      certificateId: null,
      diagnosticsIncidentId: null,
      isTerminal: false,
    }

    renderWithProviders(
      <MemoryRouter initialEntries={["/domains/domain-example/certificates/new"]}>
        <Routes>
          <Route
            path="/domains/:domainId/certificates/new"
            element={<OperatorDomainCertificateIssuePage />}
          />
        </Routes>
      </MemoryRouter>,
    )

    expect(
      await screen.findByText("Let's Encrypt is validating the DNS challenge."),
    ).toBeInTheDocument()
    expect(screen.getByTestId("certificate-issuance-progress")).toBeInTheDocument()
    expect(screen.getByTestId("certificate-request-summary")).toHaveTextContent("*.example.com")
    expect(screen.queryByLabelText("ACME email")).not.toBeInTheDocument()
    expect(screen.queryByLabelText("deSEC token")).not.toBeInTheDocument()
    expect(screen.getByText(/1m 0[4-9]s|1m 1[0-9]s/)).toBeInTheDocument()
  })

  it("renders durable failure result with a Diagnostics deep link", async () => {
    latestIssuance = {
      operationId: "44444444-4444-4444-8444-444444444444",
      domainId: "domain-example",
      requestId: "55555555-5555-4555-8555-555555555555",
      status: "failed",
      phaseCode: "certificate.failed",
      phaseSummary: "Certificate issuance failed.",
      useStaging: false,
      requestedAtUtc: "2026-09-12T06:00:00Z",
      startedAtUtc: "2026-09-12T06:00:01Z",
      completedAtUtc: "2026-09-12T06:01:00Z",
      attemptCount: 1,
      progress: [
        {
          phaseCode: "certificate.acme-order",
          safeSummary: "Creating the Let's Encrypt certificate order.",
          timestampUtc: "2026-09-12T06:00:05Z",
        },
        {
          phaseCode: "certificate.acme-validation",
          safeSummary: "Let's Encrypt is validating the DNS challenge.",
          timestampUtc: "2026-09-12T06:00:30Z",
        },
        {
          phaseCode: "certificate.failed",
          safeSummary: "Certificate issuance failed.",
          timestampUtc: "2026-09-12T06:01:00Z",
        },
      ],
      result: {
        succeeded: false,
        status: "Failed",
        message: "ACME DNS validation failed.",
        errorCode: "AcmeChallengeValidationFailed",
        errorDetail: "Review Diagnostics.",
        evidence: [
          { key: "incidentId", value: "inc-domain-issue", sensitive: false, status: "Info" },
        ],
      },
      certificateId: null,
      diagnosticsIncidentId: "inc-domain-issue",
      isTerminal: true,
    }

    renderWithProviders(
      <MemoryRouter initialEntries={["/domains/domain-example/certificates/new"]}>
        <Routes>
          <Route
            path="/domains/:domainId/certificates/new"
            element={<OperatorDomainCertificateIssuePage />}
          />
        </Routes>
      </MemoryRouter>,
    )

    expect(await screen.findByText("ACME DNS validation failed.")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open recorded failure in Diagnostics" })).toHaveAttribute(
      "href",
      "/diagnostics/logs?incident=inc-domain-issue",
    )
    expect(screen.getByTestId("certificate-issuance-phase-validate-dns")).toHaveAttribute(
      "data-state",
      "failed",
    )
  })

  it("renders terminal staging phases and labels evidence as recorded observations", async () => {
    const user = userEvent.setup()
    latestIssuance = {
      operationId: "88888888-8888-4888-8888-888888888888",
      domainId: "domain-example",
      requestId: "99999999-9999-4999-8999-999999999999",
      status: "succeeded",
      phaseCode: "certificate.complete",
      phaseSummary: "Certificate issuance completed.",
      useStaging: true,
      requestedAtUtc: "2026-09-12T06:00:00Z",
      startedAtUtc: "2026-09-12T06:00:01Z",
      completedAtUtc: "2026-09-12T06:01:15Z",
      attemptCount: 1,
      progress: [
        {
          phaseCode: "certificate.acme-validation",
          safeSummary: "Let's Encrypt is validating the DNS challenge.",
          timestampUtc: "2026-09-12T06:00:30Z",
        },
        {
          phaseCode: "certificate.store",
          safeSummary: "Storing the issued TLS certificate and private key securely.",
          timestampUtc: "2026-09-12T06:01:00Z",
        },
        {
          phaseCode: "certificate.validate",
          safeSummary: "Validating the stored TLS certificate before platform registration.",
          timestampUtc: "2026-09-12T06:01:10Z",
        },
        {
          phaseCode: "certificate.complete",
          safeSummary: "Certificate issuance completed.",
          timestampUtc: "2026-09-12T06:01:15Z",
        },
      ],
      result: {
        succeeded: true,
        status: "Succeeded",
        message: "Certificate issued, stored, and validated.",
        errorCode: null,
        errorDetail: null,
        evidence: [
          {
            key: "acmeDnsValidationAttempt",
            value: "1 of 20",
            sensitive: false,
            status: "Running",
          },
        ],
      },
      certificateId: "cert-example-staging",
      diagnosticsIncidentId: null,
      isTerminal: true,
    }

    renderWithProviders(
      <MemoryRouter initialEntries={["/domains/domain-example/certificates/new"]}>
        <Routes>
          <Route
            path="/domains/:domainId/certificates/new"
            element={<OperatorDomainCertificateIssuePage />}
          />
        </Routes>
      </MemoryRouter>,
    )

    expect(
      await screen.findByText("Certificate issuance completed successfully."),
    ).toBeInTheDocument()
    expect(screen.getByTestId("certificate-issuance-phase-store-validate")).toHaveAttribute(
      "data-state",
      "completed",
    )
    expect(screen.getByTestId("certificate-issuance-phase-renewal-credential")).toHaveAttribute(
      "data-state",
      "not-applicable",
    )
    expect(screen.getByText("Recorded: Running")).toBeInTheDocument()
    expect(screen.queryByLabelText("ACME email")).not.toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Issue another certificate" }))

    expect(await screen.findByLabelText("ACME email")).toBeInTheDocument()
    expect(screen.getByLabelText("deSEC token")).toHaveValue("")
  })


  it("does not render a dead issued-certificate link after the certificate was deleted", async () => {
    latestIssuance = {
      operationId: "issue-deleted-certificate",
      domainId: "domain-example",
      requestId: "request-deleted-certificate",
      status: "succeeded",
      phaseCode: "certificate.complete",
      phaseSummary: "Certificate issuance completed.",
      useStaging: false,
      requestedAtUtc: "2026-09-14T10:00:00Z",
      startedAtUtc: "2026-09-14T10:00:01Z",
      completedAtUtc: "2026-09-14T10:01:00Z",
      attemptCount: 1,
      progress: [],
      result: {
        succeeded: true,
        status: "Succeeded",
        message: "Certificate issued, stored, and validated.",
        errorCode: null,
        errorDetail: null,
        evidence: [],
      },
      certificateId: "cert-example-deleted",
      diagnosticsIncidentId: null,
      isTerminal: true,
    }

    renderWithProviders(
      <MemoryRouter initialEntries={["/domains/domain-example/certificates/new"]}>
        <Routes>
          <Route
            path="/domains/:domainId/certificates/new"
            element={<OperatorDomainCertificateIssuePage />}
          />
        </Routes>
      </MemoryRouter>,
    )

    expect(
      await screen.findByText(
        "The certificate issued by this historical operation no longer exists.",
      ),
    ).toBeInTheDocument()
    expect(
      screen.queryByRole("link", { name: "Open issued certificate" }),
    ).not.toBeInTheDocument()
  })

  it("renders the certificate issuance phase timeline in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    latestIssuance = {
      operationId: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
      domainId: "domain-example",
      requestId: "bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb",
      status: "running",
      phaseCode: "certificate.dns-authoritative",
      phaseSummary: "Waiting for the DNS challenge on all deSEC authoritative DNS servers.",
      useStaging: false,
      requestedAtUtc: new Date(Date.now() - 10_000).toISOString(),
      startedAtUtc: new Date(Date.now() - 9_000).toISOString(),
      completedAtUtc: null,
      attemptCount: 1,
      progress: [],
      result: null,
      certificateId: null,
      diagnosticsIncidentId: null,
      isTerminal: false,
    }

    renderWithProviders(
      <MemoryRouter initialEntries={["/domains/domain-example/certificates/new"]}>
        <Routes>
          <Route
            path="/domains/:domainId/certificates/new"
            element={<OperatorDomainCertificateIssuePage />}
          />
        </Routes>
      </MemoryRouter>,
    )

    expect(await screen.findByText("Ausstellungsphasen")).toBeInTheDocument()
    const currentPhase = screen.getByTestId("certificate-issuance-phase-wait-dns")
    expect(currentPhase).toHaveTextContent("Auf autoritative DNS-Bereitschaft warten")
    expect(currentPhase).toHaveAttribute("data-state", "current")
  })

  it("does not offer Domain activation for a staging certificate", async () => {
    server.use(
      http.get(
        "/api/operator/domains/domain-example/certificates/cert-example-staging",
        () => HttpResponse.json(stagingCertificate),
      ),
    )

    renderWithProviders(
      <MemoryRouter
        initialEntries={["/domains/domain-example/certificates/cert-example-staging"]}
      >
        <Routes>
          <Route
            path="/domains/:domainId/certificates/:certificateId"
            element={<OperatorDomainCertificateDetailPage />}
          />
        </Routes>
      </MemoryRouter>,
    )

    expect(
      await screen.findByText(/staging certificates are for ACME\/DNS workflow testing only/i),
    ).toBeInTheDocument()
    expect(
      screen.queryByRole("button", { name: "Make active for this Domain" }),
    ).not.toBeInTheDocument()
  })

})
