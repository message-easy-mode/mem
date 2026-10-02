import { http, HttpResponse } from "msw"
import { MemoryRouter, useLocation } from "react-router-dom"
import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import { SetupReviewPage } from "./setup-review-page"

function reviewResponse(reviewAccepted = false) {
  return {
    installationId: "11111111-1111-1111-1111-111111111111",
    installationStatus: reviewAccepted ? "Ready" : "Draft",
    canAccept: !reviewAccepted,
    reviewAccepted,
    message: reviewAccepted
      ? "Review accepted. The exact server-owned plan is frozen and ready for installation."
      : "Review the server-owned plan. Accepting Review freezes this exact intent without changing external systems.",
    errorCode: null,
    planSha256: reviewAccepted ? "a".repeat(64) : null,
    reviewedAtUtc: reviewAccepted ? "2026-08-11T08:00:00Z" : null,
    preflight: {
      available: true,
      ready: true,
      runId: "preflight-01e",
      completedAtUtc: "2026-08-11T07:30:00Z",
      passed: 9,
      warnings: 0,
      failed: 0,
      skipped: 2,
      unavailable: 2,
      unknown: 0,
      blockingIssueCount: 0,
    },
    domain: {
      validated: true,
      baseDomain: "deltabox.dev",
      wildcardCertificate: "*.deltabox.dev",
      dnsProvider: "desec",
      acmeEmail: "admin@deltabox.dev",
      certificateEnvironment: "Let's Encrypt staging",
      providerAccessConfirmed: true,
      providerCredentialStored: true,
    },
    npmAdministrator: {
      administratorEmail: "npm-admin@deltabox.dev",
      credentialStored: true,
      verifiedAtUtc: null,
    },
    platform: {
      networkName: "mem-gateway",
      postgresContainerName: "mem-postgres",
      postgresVolumeName: "mem_postgres_data",
      npmContainerName: "mem-npm",
      npmHttpPort: 80,
      npmHttpsPort: 443,
      npmAdminPort: 81,
      coturnContainerName: "mem-coturn",
      coturnPublicHost: "turn.deltabox.dev",
      coturnTurnPort: 3478,
      coturnRelayPortRange: "49160-49200/udp",
      enabledSupportTools: ["Portainer"],
    },
    plannedActions: [
      "Create or verify Docker network 'mem-gateway'.",
      "Create the DNS-01 challenge for '*.deltabox.dev' only after installation starts.",
      "Request a Let's Encrypt staging wildcard certificate.",
      "Import the platform certificate into NPM.",
    ],
    willNotChange: [
      "The private MEM Control Plane exposure.",
      "DNS records before the reviewed installation is explicitly started.",
    ],
    blockers: [],
  }
}

function LocationProbe() {
  const location = useLocation()
  return <span data-testid="location-probe">{location.pathname}</span>
}

describe("SetupReviewPage frozen review boundary", () => {
  it("renders the exact server-owned plan without stale v0.1.1 placeholder semantics", async () => {
    server.use(
      http.get("/api/setup/review/", () => HttpResponse.json(reviewResponse())),
      http.get("/api/setup/npm-administrator/", () => HttpResponse.json({
        installationId: "11111111-1111-1111-1111-111111111111",
        status: "Stored",
        message: "Stored protected",
        errorCode: null,
        administratorEmail: "npm-admin@deltabox.dev",
        credentialStored: true,
        verifiedAtUtc: null,
      })),
    )

    renderWithProviders(
      <MemoryRouter>
        <SetupReviewPage />
      </MemoryRouter>,
    )

    expect(await screen.findByRole("heading", { name: "Review before installation" })).toBeInTheDocument()
    expect(screen.getByText("deltabox.dev")).toBeInTheDocument()
    expect(screen.getByText("*.deltabox.dev")).toBeInTheDocument()
    expect(screen.getByText("Let's Encrypt staging")).toBeInTheDocument()
    expect(screen.getByText("mem-postgres")).toBeInTheDocument()
    expect(screen.getByText("mem-npm")).toBeInTheDocument()
    expect(screen.getByText("mem-coturn")).toBeInTheDocument()
    expect(screen.getByText("turn.deltabox.dev")).toBeInTheDocument()
    expect(screen.getByText("49160-49200/udp")).toBeInTheDocument()
    expect(screen.getByText("npm-admin@deltabox.dev")).toBeInTheDocument()
    expect(screen.getAllByText("Stored protected").length).toBeGreaterThan(0)
    expect(screen.getByText(/No external mutation occurs when Review is accepted/)).toBeInTheDocument()
    expect(screen.getByText(/DNS-01 challenge/)).toBeInTheDocument()
    expect(screen.queryByText(/v0\.1\.1/i)).not.toBeInTheDocument()
  })

  it("accepts and freezes Review before navigating to the install mutation boundary", async () => {
    const user = userEvent.setup()
    let acceptCalls = 0

    server.use(
      http.get("/api/setup/review/", () => HttpResponse.json(reviewResponse())),
      http.get("/api/setup/npm-administrator/", () => HttpResponse.json({
        installationId: "11111111-1111-1111-1111-111111111111",
        status: "Stored",
        message: "Stored protected",
        errorCode: null,
        administratorEmail: "npm-admin@deltabox.dev",
        credentialStored: true,
        verifiedAtUtc: null,
      })),
      http.post("/api/setup/review/accept", () => {
        acceptCalls += 1
        return HttpResponse.json(reviewResponse(true))
      }),
    )

    renderWithProviders(
      <MemoryRouter>
        <SetupReviewPage />
        <LocationProbe />
      </MemoryRouter>,
    )

    await user.click(await screen.findByRole("button", { name: "Accept reviewed plan" }))

    await waitFor(() => expect(acceptCalls).toBe(1))
    await waitFor(() =>
      expect(screen.getByTestId("location-probe")).toHaveTextContent("/setup/install"),
    )
  })
  it("stores the NPM administrator credential without redisplaying the password", async () => {
    const user = userEvent.setup()
    let storedPassword: string | null = null
    let hasCredential = false

    server.use(
      http.get("/api/setup/review/", () => {
        const response = reviewResponse()
        return HttpResponse.json({
          ...response,
          canAccept: hasCredential,
          npmAdministrator: {
            administratorEmail: hasCredential ? "owner@deltabox.dev" : null,
            credentialStored: hasCredential,
            verifiedAtUtc: null,
          },
          blockers: hasCredential
            ? []
            : ["Choose and protect the Nginx Proxy Manager administrator credential before accepting Review."],
        })
      }),
      http.get("/api/setup/npm-administrator/", () => HttpResponse.json({
        installationId: "11111111-1111-1111-1111-111111111111",
        status: hasCredential ? "Stored" : "CredentialRequired",
        message: hasCredential ? "Stored protected" : "Credential required",
        errorCode: hasCredential ? null : "NpmAdminCredentialRequired",
        administratorEmail: hasCredential ? "owner@deltabox.dev" : null,
        credentialStored: hasCredential,
        verifiedAtUtc: null,
      })),
      http.post("/api/setup/npm-administrator/", async ({ request }) => {
        const body = await request.json() as { email: string; password: string }
        storedPassword = body.password
        hasCredential = true
        return HttpResponse.json({
          installationId: "11111111-1111-1111-1111-111111111111",
          status: "Stored",
          message: "Stored protected",
          errorCode: null,
          administratorEmail: body.email,
          credentialStored: true,
          verifiedAtUtc: null,
        })
      }),
    )

    renderWithProviders(
      <MemoryRouter>
        <SetupReviewPage />
      </MemoryRouter>,
    )

    const email = await screen.findByLabelText("Administrator email")
    await user.clear(email)
    await user.type(email, "owner@deltabox.dev")
    await user.type(screen.getByLabelText("Administrator password"), "very-secret-npm-password")
    await user.click(screen.getByRole("button", { name: "Save protected credential" }))

    await waitFor(() => expect(storedPassword).toBe("very-secret-npm-password"))
    expect(await screen.findByText("owner@deltabox.dev")).toBeInTheDocument()
    expect(screen.queryByDisplayValue("very-secret-npm-password")).not.toBeInTheDocument()
    expect(screen.getAllByText("Stored protected").length).toBeGreaterThan(0)
  })

  it("redirects a post-Review recovery visit back to the durable installation instead of rendering an empty Review", async () => {
    const activeInstallationId = "95f7eebb-0e83-4bab-9916-b503c853caca"

    server.use(
      http.get("/api/setup/review/", () => HttpResponse.json({
        ...reviewResponse(),
        installationId: null,
        installationStatus: "Unavailable",
        canAccept: false,
        reviewAccepted: false,
        message: "No active first-time Setup plan is available for Review.",
        errorCode: "SetupAuthorityMissing",
        planSha256: null,
        reviewedAtUtc: null,
        preflight: {
          available: false,
          ready: false,
          runId: null,
          completedAtUtc: null,
          passed: 0,
          warnings: 0,
          failed: 0,
          skipped: 0,
          unavailable: 0,
          unknown: 0,
          blockingIssueCount: 0,
        },
        domain: {
          validated: false,
          baseDomain: "",
          wildcardCertificate: "",
          dnsProvider: "desec",
          acmeEmail: "",
          certificateEnvironment: "",
          providerAccessConfirmed: false,
          providerCredentialStored: false,
        },
        npmAdministrator: { administratorEmail: null, credentialStored: false, verifiedAtUtc: null },
        platform: {
          networkName: "mem-gateway",
          postgresContainerName: "mem-postgres",
          postgresVolumeName: "mem_postgres_data",
          npmContainerName: "mem-npm",
          npmHttpPort: 80,
          npmHttpsPort: 443,
          npmAdminPort: 81,
          coturnContainerName: "mem-coturn",
          coturnPublicHost: "turn.deltabox.dev",
          coturnTurnPort: 3478,
          coturnRelayPortRange: "49160-49200/udp",
          enabledSupportTools: [],
        },
        plannedActions: [],
        willNotChange: [],
        blockers: ["No active first-time Setup plan is available for Review."],
      })),
      http.get("/api/setup/npm-administrator/", () => HttpResponse.json({
        installationId: activeInstallationId,
        status: "Stored",
        message: "Stored protected",
        errorCode: null,
        administratorEmail: "owner@deltabox.dev",
        credentialStored: true,
        verifiedAtUtc: "2026-08-12T10:49:25Z",
      })),
      http.get("/api/setup/start/status", () => HttpResponse.json({
        installationState: "partially-installed",
        recommendedAction: "resume-install",
        startupTarget: "resume-installation",
        setupMode: "repair",
        detectedInstallation: null,
        docker: { reachable: true, message: null },
        requiredServices: [],
        supportToolsServices: [],
        warnings: [],
        activeInstallationId,
        activeInstallationStage: "failure-review",
      })),
    )

    renderWithProviders(
      <MemoryRouter>
        <SetupReviewPage />
      </MemoryRouter>,
    )

    expect(await screen.findByRole("heading", { name: "Review is complete" })).toBeInTheDocument()
    expect(screen.getByText("Installation already underway")).toBeInTheDocument()
    expect(await screen.findByRole("link", { name: "Review failure" })).toHaveAttribute(
      "href",
      `/setup/install/${activeInstallationId}`,
    )
    expect(screen.queryByRole("button", { name: "Accept reviewed plan" })).not.toBeInTheDocument()
    expect(screen.queryByLabelText("Administrator password")).not.toBeInTheDocument()
    expect(screen.queryByText("Review blockers")).not.toBeInTheDocument()
  })

  it("keeps a premature Review visit informational when setup has not reached Review yet", async () => {
    server.use(
      http.get("/api/setup/review/", () => HttpResponse.json({
        ...reviewResponse(),
        installationId: null,
        installationStatus: "Unavailable",
        canAccept: false,
        reviewAccepted: false,
        message: "No active first-time Setup plan is available for Review.",
        errorCode: "SetupAuthorityMissing",
        planSha256: null,
        reviewedAtUtc: null,
        blockers: ["No active first-time Setup plan is available for Review."],
      })),
      http.get("/api/setup/start/status", () => HttpResponse.json({
        installationState: "not-installed",
        recommendedAction: "run-preflight",
        startupTarget: "setup-start",
        setupMode: "fresh-install",
        detectedInstallation: null,
        docker: { reachable: true, message: null },
        requiredServices: [],
        supportToolsServices: [],
        warnings: [],
        activeInstallationId: null,
        activeInstallationStage: null,
      })),
    )

    renderWithProviders(
      <MemoryRouter>
        <SetupReviewPage />
      </MemoryRouter>,
    )

    expect(await screen.findByRole("heading", { name: "Review is not available yet" })).toBeInTheDocument()
    expect(screen.getByText("No active Review")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Return to setup start" })).toHaveAttribute("href", "/setup/start")
    expect(screen.queryByRole("button", { name: "Accept reviewed plan" })).not.toBeInTheDocument()
  })

  it("treats Review as historical after first-time setup has completed", async () => {
    server.use(
      http.get("/api/setup/review/", () => HttpResponse.json({
        ...reviewResponse(),
        installationId: null,
        installationStatus: "Unavailable",
        canAccept: false,
        reviewAccepted: false,
        message: "No active first-time Setup plan is available for Review.",
        errorCode: "SetupAuthorityMissing",
        planSha256: null,
        reviewedAtUtc: null,
        blockers: ["No active first-time Setup plan is available for Review."],
      })),
      http.get("/api/setup/start/status", () => HttpResponse.json({
        installationState: "installed",
        recommendedAction: "open-dashboard",
        startupTarget: "dashboard",
        setupMode: "already-installed",
        detectedInstallation: null,
        docker: { reachable: true, message: null },
        requiredServices: [],
        supportToolsServices: [],
        warnings: [],
        activeInstallationId: null,
        activeInstallationStage: null,
      })),
    )

    renderWithProviders(
      <MemoryRouter>
        <SetupReviewPage />
      </MemoryRouter>,
    )

    expect(await screen.findByRole("heading", { name: "Setup is complete" })).toBeInTheDocument()
    expect(screen.getByText("Review is historical")).toBeInTheDocument()
    expect(screen.queryByText("No active first-time Setup plan is available for Review.")).not.toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open dashboard" })).toHaveAttribute("href", "/dashboard")
    expect(screen.queryByRole("button", { name: "Accept reviewed plan" })).not.toBeInTheDocument()
  })

})
