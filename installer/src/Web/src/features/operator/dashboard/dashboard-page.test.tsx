import { delay, http, HttpResponse } from "msw"
import { MemoryRouter } from "react-router-dom"
import { screen, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import type { DiagnosticsAttentionResponse } from "@/features/operator/diagnostics/api/diagnostics.types"
import { createRuntimeContext } from "@/features/runtime-context/runtime-context.test-fixture"
import { dashboardOverviewEndpoint } from "./api/dashboard.api"
import { dashboardOverviewFixture } from "./dashboard.test-fixtures"
import type { DashboardOverviewResponse } from "./api/dashboard.types"
import { DashboardPage } from "./dashboard-page"

function renderPage() {
  return renderWithProviders(
    <MemoryRouter>
      <DashboardPage />
    </MemoryRouter>,
  )
}



function renderPageFromSetupLockout() {
  return renderWithProviders(
    <MemoryRouter
      initialEntries={[
        { pathname: "/dashboard", state: { firstTimeSetupLocked: true } },
      ]}
    >
      <DashboardPage />
    </MemoryRouter>,
  )
}


function managedOverview(overrides: Partial<DashboardOverviewResponse> = {}): DashboardOverviewResponse {
  return {
    ...dashboardOverviewFixture,
    hero: {
      state: "ready",
      headlineCode: "platform_ready",
      action: null,
    },
    onboarding: {
      state: "not_needed",
      completedStepCodes: ["create_chat_server"],
      nextSteps: [],
    },
    stacks: {
      total: 1,
      healthyLastVerifiedCount: 1,
      attentionCount: 0,
      truncated: false,
      items: [
        {
          stackId: "stack-1",
          slug: "community-chat",
          lastVerification: {
            state: "passed",
            verifiedAtUtc: "2026-07-06T03:30:00Z",
          },
          matrixPublicBaseUrl: "https://matrix.community.example.test",
          elementPublicBaseUrl: "https://chat.community.example.test",
        },
      ],
    },
    recovery: {
      ...dashboardOverviewFixture.recovery,
      state: "covered",
      managedStackCount: 1,
      stacksWithValidRecoveryPointCount: 1,
      catalogEntryCount: 1,
      availableCatalogEntryCount: 1,
      validCatalogEntryCount: 1,
      latestCapturedAtUtc: "2026-07-06T02:00:00Z",
      totalPayloadBytes: 1_024,
    },
    ...overrides,
  }
}

function overviewHandler() {
  return http.get(dashboardOverviewEndpoint, () =>
    HttpResponse.json(dashboardOverviewFixture),
  )
}

const diagnosticsAttentionEndpoint = "/api/operator/diagnostics/attention"

function diagnosticsAttentionResponse(
  overrides: Partial<DiagnosticsAttentionResponse> = {},
): DiagnosticsAttentionResponse {
  return {
    schemaVersion: 1,
    observedAtUtc: "2026-07-06T03:45:00Z",
    state: "ready",
    total: 0,
    highestSeverity: null,
    items: [],
    partial: false,
    warnings: [],
    ...overrides,
  }
}

beforeEach(() => {
  server.use(
    http.get(diagnosticsAttentionEndpoint, () =>
      HttpResponse.json(diagnosticsAttentionResponse()),
    ),
  )
})

afterEach(() => {
  window.localStorage.clear()
})

describe("DashboardPage", () => {
  it("explains when an installed operator was redirected away from first-time setup", async () => {
    server.use(overviewHandler())

    renderPageFromSetupLockout()

    expect(await screen.findByText("First-time setup is complete")).toBeInTheDocument()
    expect(
      screen.getByText(/returned you to Home/),
    ).toBeInTheDocument()
  })

  it("localises the first-time setup lockout notice in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    server.use(overviewHandler())

    renderPageFromSetupLockout()

    expect(
      await screen.findByText("Die Ersteinrichtung ist abgeschlossen"),
    ).toBeInTheDocument()
    expect(screen.getByText(/zur Startseite zurückgeführt/)).toBeInTheDocument()
  })

  it("uses a real zero-stack response for onboarding and never displays the retired mock dashboard data", async () => {
    server.use(overviewHandler())

    renderPage()

    expect(await screen.findByText("No chat servers yet")).toBeInTheDocument()
    for (const link of screen.getAllByRole("link", { name: "Create chat server" })) {
      expect(link).toHaveAttribute("href", "/stacks/new")
    }
    expect(screen.getByText("deltabox.dev is the configured platform domain.")).toBeInTheDocument()
    expect(screen.queryByText(/federationops\.net/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/Matrix HQ/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/ESS Community/i)).not.toBeInTheDocument()
    expect(screen.queryByText(/recovery key/i)).not.toBeInTheDocument()
    expect(screen.queryByText("MEM API")).not.toBeInTheDocument()
    expect(screen.queryByText("MEM Web")).not.toBeInTheDocument()
  })

  it("shows a focused first-load failure and can retry without falling back to mock health data", async () => {
    let canLoad = false
    server.use(
      http.get(dashboardOverviewEndpoint, () =>
        canLoad
          ? HttpResponse.json(dashboardOverviewFixture)
          : HttpResponse.json({ status: "unavailable" }, { status: 503 }),
      ),
    )

    const user = userEvent.setup()
    renderPage()

    expect(
      await screen.findByText("Could not load the Home dashboard", {}, { timeout: 3_000 }),
    ).toBeInTheDocument()
    expect(screen.queryByText("MEM is ready")).not.toBeInTheDocument()

    canLoad = true
    await user.click(screen.getByRole("button", { name: "Try again" }))

    expect(await screen.findByText("No chat servers yet")).toBeInTheDocument()
  })

  it("links a correlated dashboard failure to its recorded Diagnostics incident", async () => {
    server.use(
      http.get(
        dashboardOverviewEndpoint,
        () => HttpResponse.json(
          {
            type: "https://mem.invalid/problems/dashboard_projection_failed",
            title: "Home could not be prepared",
            status: 500,
            detail: "MEM could not build the current Home overview.",
            code: "dashboard_projection_failed",
            traceId: "trace-dashboard-1",
            incidentId: "inc_dashboard_1",
            retryable: true,
          },
          {
            status: 500,
            headers: { "Content-Type": "application/problem+json" },
          },
        ),
      ),
    )

    renderPage()

    expect(
      await screen.findByText("MEM could not build the current Home overview."),
    ).toBeInTheDocument()
    expect(screen.getByText(/inc_dashboard_1/)).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open diagnostics" })).toHaveAttribute(
      "href",
      "/diagnostics/logs?incident=inc_dashboard_1",
    )
  })

  it("keeps the last successful snapshot visible when a manual refresh fails", async () => {
    let requestCount = 0
    server.use(
      http.get(dashboardOverviewEndpoint, () => {
        requestCount += 1
        return requestCount === 1
          ? HttpResponse.json(dashboardOverviewFixture)
          : HttpResponse.json({ status: "unavailable" }, { status: 503 })
      }),
    )

    const user = userEvent.setup()
    renderPage()

    expect(await screen.findByText("No chat servers yet")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Refresh" }))

    expect(await screen.findByText("Could not refresh the Home dashboard")).toBeInTheDocument()
    expect(screen.getByText("No chat servers yet")).toBeInTheDocument()
  })

  it("renders a localised loading state before the overview response arrives", async () => {
    server.use(
      http.get(dashboardOverviewEndpoint, async () => {
        await delay(75)
        return HttpResponse.json(dashboardOverviewFixture)
      }),
    )

    renderPage()

    expect(
      screen.getByRole("status", { name: "Loading Home dashboard" }),
    ).toBeInTheDocument()
    expect(screen.queryByText("MEM is ready")).not.toBeInTheDocument()

    expect(await screen.findByText("No chat servers yet")).toBeInTheDocument()
  })



  it("renders only API-returned managed chat servers with recorded verification and safe public endpoints", async () => {
    const overview = managedOverview({
      stacks: {
        total: 2,
        healthyLastVerifiedCount: 1,
        attentionCount: 1,
        truncated: false,
        items: [
          {
            stackId: "stack-family",
            slug: "family-chat",
            lastVerification: {
              state: "passed",
              verifiedAtUtc: "2026-07-06T03:30:00Z",
            },
            matrixPublicBaseUrl: "https://matrix.family.example.test",
            elementPublicBaseUrl: "https://chat.family.example.test",
          },
          {
            stackId: "stack-gaming",
            slug: "gaming-chat",
            lastVerification: {
              state: "failed",
              verifiedAtUtc: "2026-07-06T03:20:00Z",
            },
            matrixPublicBaseUrl: "https://matrix.gaming.example.test",
            elementPublicBaseUrl: "https://chat.gaming.example.test",
          },
        ],
      },
    })
    server.use(http.get(dashboardOverviewEndpoint, () => HttpResponse.json(overview)))

    renderPage()

    const familyChat = await screen.findByRole("link", { name: "family-chat" })
    expect(familyChat).toHaveAttribute("href", "/stacks/family-chat")
    expect(screen.getByText("matrix.family.example.test")).toBeInTheDocument()
    expect(screen.getByText("chat.family.example.test")).toBeInTheDocument()
    expect(screen.getByText("Last check passed")).toBeInTheDocument()
    expect(screen.getByText("Last check failed")).toBeInTheDocument()
    expect(screen.queryByText("Matrix HQ")).not.toBeInTheDocument()
    expect(screen.queryByText("mem-synapse-family")).not.toBeInTheDocument()
  })

  it("uses one Diagnostics-backed incident strip and does not repeat resource posture as dashboard banners", async () => {
    const overview = managedOverview({
      hero: {
        state: "attention",
        headlineCode: "restore_needs_attention",
        action: {
          code: "open_restore_workspace",
          stackSlug: null,
          restoreSessionId: "restore-20260706-001",
        },
      },
      recovery: {
        ...dashboardOverviewFixture.recovery,
        state: "covered",
        managedStackCount: 1,
        stacksWithValidRecoveryPointCount: 1,
        catalogEntryCount: 1,
        availableCatalogEntryCount: 1,
        validCatalogEntryCount: 1,
        warningCatalogEntryCount: 0,
        invalidCatalogEntryCount: 0,
        latestCapturedAtUtc: "2026-07-06T02:00:00Z",
        totalPayloadBytes: 2_048,
        activeRestoreCount: 0,
        attentionRestoreCount: 1,
        priorityRestoreSessionId: "restore-20260706-001",
      },
      publicAccess: {
        ...dashboardOverviewFixture.publicAccess,
        state: "attention",
        certificate: {
          state: "staging",
          commonName: "*.example.test",
          expiresAtUtc: "2026-09-01T00:00:00Z",
        },
      },
      notices: [
        {
          id: "public-access",
          severity: "warning",
          code: "public_access_staging_certificate",
          action: {
            code: "manage_domains",
            stackSlug: null,
            restoreSessionId: null,
          },
        },
      ],
    })
    server.use(
      http.get(dashboardOverviewEndpoint, () => HttpResponse.json(overview)),
      http.get(diagnosticsAttentionEndpoint, () =>
        HttpResponse.json(diagnosticsAttentionResponse({
          state: "attention",
          total: 2,
          highestSeverity: "error",
          items: [
            {
              incidentId: "inc_restore_1",
              severity: "error",
              eventCode: "restore.database_import_failed",
              feature: "restore",
              stage: "database-import",
              summary: "Restore database import failed.",
              lastSeenAtUtc: "2026-07-06T03:40:00Z",
              href: "/diagnostics/logs?incident=inc_restore_1",
            },
          ],
        })),
      ),
    )

    renderPage()

    const strip = await screen.findByRole("status", {
      name: "2 diagnostic incidents require urgent review",
    })
    expect(strip).toHaveTextContent("Latest: Restore database import failed.")
    expect(within(strip).getByRole("link", { name: "Review incidents" })).toHaveAttribute(
      "href",
      "/diagnostics/logs?tab=incidents",
    )

    expect(screen.queryByRole("heading", { name: "A restore needs attention" })).not.toBeInTheDocument()
    expect(screen.queryByText("Public access needs attention")).not.toBeInTheDocument()

    expect(screen.getByText("Public access")).toBeInTheDocument()
    expect(
      screen.getByText("The active certificate is staging-only and is not trusted for normal browser use."),
    ).toBeInTheDocument()

    const recovery = screen.getByRole("region", { name: "Recovery coverage" })
    expect(recovery).toHaveTextContent("1 of 1 managed chat servers have a valid recovery point.")
    expect(within(recovery).getByRole("link", { name: "Open restore workspace" })).toHaveAttribute(
      "href",
      "/restores/restore-20260706-001",
    )
  })

  it("does not show an incident strip when Diagnostics reports no current incidents", async () => {
    server.use(http.get(dashboardOverviewEndpoint, () => HttpResponse.json(managedOverview())))

    renderPage()

    expect(await screen.findByText("Managed chat servers")).toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "Review incidents" })).not.toBeInTheDocument()
    expect(screen.queryByRole("status", { name: /diagnostic incident/i })).not.toBeInTheDocument()
  })

  it("shows warning incidents as one compact amber Diagnostics strip", async () => {
    server.use(
      http.get(dashboardOverviewEndpoint, () => HttpResponse.json(managedOverview())),
      http.get(diagnosticsAttentionEndpoint, () =>
        HttpResponse.json(diagnosticsAttentionResponse({
          state: "warning",
          total: 1,
          highestSeverity: "warning",
          items: [
            {
              incidentId: "inc_warning_1",
              severity: "warning",
              eventCode: "runtime.warning",
              feature: "runtime",
              stage: null,
              summary: "A managed runtime needs review.",
              lastSeenAtUtc: "2026-07-06T03:40:00Z",
              href: "/diagnostics/logs?incident=inc_warning_1",
            },
          ],
        })),
      ),
    )

    renderPage()

    const strip = await screen.findByRole("status", {
      name: "1 diagnostic incident needs attention",
    })
    expect(strip).toHaveAttribute("data-dashboard-incident-state", "warning")
    expect(strip).toHaveTextContent("Latest: A managed runtime needs review.")
  })

  it("renders the single incident strip in German", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    server.use(
      http.get(dashboardOverviewEndpoint, () => HttpResponse.json(managedOverview())),
      http.get(diagnosticsAttentionEndpoint, () =>
        HttpResponse.json(diagnosticsAttentionResponse({
          state: "warning",
          total: 1,
          highestSeverity: "warning",
        })),
      ),
    )

    renderPage()

    const strip = await screen.findByRole("status", {
      name: "1 Diagnosevorfall benötigt Aufmerksamkeit",
    })
    expect(within(strip).getByRole("link", { name: "Vorfälle prüfen" })).toHaveAttribute(
      "href",
      "/diagnostics/logs?tab=incidents",
    )
  })

  it("presents concise platform service state without Docker identifiers", async () => {
    const overview = managedOverview({
      platform: {
        ...dashboardOverviewFixture.platform,
        state: "degraded",
        runningRequiredServiceCount: 2,
        services: dashboardOverviewFixture.platform.services.map((service) =>
          service.key === "npm_ingress"
            ? { ...service, state: "stopped" }
            : service,
        ),
      },
    })
    server.use(http.get(dashboardOverviewEndpoint, () => HttpResponse.json(overview)))

    renderPage()

    expect(await screen.findByText("NPM / ingress")).toBeInTheDocument()
    expect(screen.getByText("Coturn (TURN)")).toBeInTheDocument()
    expect(screen.getByText("2 of 3 required services are running.")).toBeInTheDocument()
    expect(screen.getAllByText("Stopped").length).toBeGreaterThan(0)
    expect(screen.getByText("Docker")).toBeInTheDocument()
    expect(screen.queryByText("mem-npm")).not.toBeInTheDocument()
    expect(screen.queryByText("/var/run/docker.sock")).not.toBeInTheDocument()
  })

  it("distinguishes expected protected-service verification limits from runtime degradation", async () => {
    const overview = managedOverview({
      platform: {
        ...dashboardOverviewFixture.platform,
        state: "verification_limited",
        runningRequiredServiceCount: 3,
        services: dashboardOverviewFixture.platform.services.map((service) =>
          service.key === "coturn"
            ? { ...service, state: "verification_limited" }
            : service,
        ),
      },
    })
    server.use(http.get(dashboardOverviewEndpoint, () => HttpResponse.json(overview)))

    renderPage()

    expect(await screen.findAllByText("Verification limited")).not.toHaveLength(0)
    expect(screen.getByText("3 of 3 required services are running.")).toBeInTheDocument()
    expect(
      screen.getByText("1 required service has limited protected verification in this development mode."),
    ).toBeInTheDocument()
    expect(screen.queryByText("Degraded")).not.toBeInTheDocument()
  })

  it("keeps public access healthy while a certificate is inside a healthy automatic-renewal window", async () => {
    const overview = managedOverview({
      publicAccess: {
        ...dashboardOverviewFixture.publicAccess,
        state: "ready",
        mainDomain: "deltabox.dev",
        certificate: {
          state: "renewing",
          commonName: "*.deltabox.dev",
          expiresAtUtc: "2026-07-26T03:30:00Z",
          renewalState: "scheduled",
          renewalNextAttemptAtUtc: "2026-07-06T04:00:00Z",
        },
      },
    })

    server.use(
      http.get(dashboardOverviewEndpoint, () => HttpResponse.json(overview)),
    )

    renderPage()

    expect(await screen.findByText("Public access")).toBeInTheDocument()
    expect(screen.getAllByText("Renewing")).toHaveLength(2)
    expect(
      screen.getByText("Automatic certificate renewal is healthy for deltabox.dev."),
    ).toBeInTheDocument()
    expect(screen.queryByText("Expiring")).not.toBeInTheDocument()
  })

  it("explains that an active staging certificate is not production-ready public access", async () => {
    const overview = managedOverview({
      publicAccess: {
        ...dashboardOverviewFixture.publicAccess,
        state: "attention",
        certificate: {
          state: "staging",
          commonName: "*.example.test",
          expiresAtUtc: "2026-09-01T00:00:00Z",
        },
      },
      notices: [
        {
          id: "public-access",
          severity: "warning",
          code: "public_access_staging_certificate",
          action: { code: "manage_domains", stackSlug: null, restoreSessionId: null },
        },
      ],
    })
    server.use(http.get(dashboardOverviewEndpoint, () => HttpResponse.json(overview)))

    renderPage()

    expect(await screen.findByText("Public access")).toBeInTheDocument()
    expect(
      screen.getByText("The active certificate is staging-only and is not trusted for normal browser use."),
    ).toBeInTheDocument()
    expect(screen.queryByText("Public access needs attention")).not.toBeInTheDocument()
  })

  it("shows Auditors only read-oriented shortcuts", async () => {
    const overview = managedOverview({
      capabilities: {
        canOperate: false,
        canManagePlatform: false,
      },
      hero: {
        state: "ready",
        headlineCode: "platform_ready",
        action: null,
      },
    })
    server.use(http.get(dashboardOverviewEndpoint, () => HttpResponse.json(overview)))

    renderPage()

    await screen.findByRole("region", { name: "Quick actions" })

    expect(screen.queryByRole("link", { name: "Create chat server" })).not.toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "Manage domains" })).not.toBeInTheDocument()
    for (const link of screen.getAllByRole("link", { name: "View chat servers" })) {
      expect(link).toHaveAttribute("href", "/stacks")
    }
    for (const link of screen.getAllByRole("link", { name: "View backups" })) {
      expect(link).toHaveAttribute("href", "/backups")
    }
    for (const link of screen.getAllByRole("link", { name: "View restores" })) {
      expect(link).toHaveAttribute("href", "/restores")
    }
    expect(screen.queryByRole("link", { name: "Open services" })).not.toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Coturn TURN server" })).toHaveAttribute(
      "href",
      "/services/coturn",
    )

    const quickActions = screen.getByRole("region", { name: "Quick actions" })
    expect(within(quickActions).queryByRole("link", { name: "Open diagnostics" })).not.toBeInTheDocument()
    expect(screen.getByRole("region", { name: "Host status" })).toContainElement(
      screen.getByRole("link", { name: "Open diagnostics" }),
    )
  })

  it("keeps dense dashboard layouts out of intermediate widths and balances recovery and quick actions", async () => {
    const overview = managedOverview({
      host: {
        state: "available",
        observedAtUtc: "2026-07-06T03:40:00Z",
        unavailableReasonCode: null,
        operatingSystem: "Ubuntu 24.04.2 LTS",
        architecture: "x86_64",
        cpuCount: 8,
        memoryTotalBytes: 16 * 1024 ** 3,
        dockerServerVersion: "29.4.2",
        containerCount: 7,
        imageCount: 12,
        disk: null,
        storageUnavailableReasonCode: "containerized_host_filesystem_unavailable",
      },
    })
    server.use(http.get(dashboardOverviewEndpoint, () => HttpResponse.json(overview)))

    renderPage()

    const recovery = await screen.findByRole("region", { name: "Recovery coverage" })
    const quickActions = screen.getByRole("region", { name: "Quick actions" })
    const host = screen.getByRole("region", { name: "Host status" })
    expect(recovery).toHaveAttribute("data-size", "sm")
    expect(quickActions).toHaveAttribute("data-size", "sm")
    expect(host).toHaveAttribute("data-size", "sm")

    expect(recovery.parentElement).toHaveClass(
      "grid",
      "gap-4",
      "2xl:grid-cols-[minmax(0,1.35fr)_minmax(360px,1fr)]",
    )
    expect(recovery.parentElement).not.toHaveClass(
      "xl:grid-cols-[minmax(0,1.35fr)_minmax(340px,1fr)]",
    )

    const platformSummaryHelper = screen.getByText(
      "Required MEM services reported by the current overview.",
    )
    const platformSummaryCard = platformSummaryHelper.closest("section")
    expect(platformSummaryCard?.parentElement).toHaveClass(
      "md:grid-cols-2",
      "2xl:grid-cols-4",
    )
    expect(platformSummaryCard).toHaveClass("p-3")
    expect(platformSummaryCard?.querySelector(".text-2xl")).not.toBeNull()
    expect(platformSummaryCard?.querySelector(".text-3xl")).toBeNull()

    const quickActionGrid = within(quickActions)
      .getByRole("link", { name: "Create chat server" })
      .parentElement
    expect(quickActionGrid).toHaveClass(
      "sm:grid-cols-2",
      "lg:grid-cols-3",
      "2xl:grid-cols-2",
    )
    expect(within(quickActions).getAllByRole("link")).toHaveLength(6)
    expect(within(quickActions).queryByRole("link", { name: "Open diagnostics" })).not.toBeInTheDocument()
    expect(within(quickActions).getByRole("link", { name: "Create chat server" })).toHaveClass(
      "min-h-14",
      "py-2",
    )

    const restoreWorkLabel = within(recovery).getByText("Restore work")
    expect(restoreWorkLabel.parentElement).toHaveClass(
      "sm:col-span-2",
      "lg:col-span-1",
    )
    expect(restoreWorkLabel.parentElement?.parentElement).toHaveClass(
      "sm:grid-cols-2",
      "lg:grid-cols-3",
    )

    const systemLabel = within(host).getByText("System")
    expect(systemLabel.parentElement?.parentElement?.parentElement).toHaveClass(
      "sm:grid-cols-2",
      "xl:grid-cols-4",
    )
    expect(within(host).getByRole("link", { name: "Open diagnostics" })).toHaveAttribute(
      "href",
      "/diagnostics",
    )
  })

  it("does not render historical activity on Home even when the overview still carries activity records", async () => {
    const overview = managedOverview({
      activity: {
        state: "available",
        items: [
          {
            id: "backup:historical-deleted-stack",
            kind: "backup",
            severity: "success",
            occurredAtUtc: "2026-07-06T03:20:00Z",
            titleCode: "backup_imported",
            detailCode: null,
            stackSlug: "deleted-historical-stack",
            restoreSessionId: null,
          },
        ],
      },
    })
    server.use(http.get(dashboardOverviewEndpoint, () => HttpResponse.json(overview)))

    renderPage()

    await screen.findByRole("region", { name: "Host status" })
    expect(screen.queryByRole("region", { name: "Recent activity" })).not.toBeInTheDocument()
    expect(screen.queryByText("Backup imported")).not.toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "Backup imported" })).not.toBeInTheDocument()
  })

  it("renders unavailable host information honestly without fabricated resource values", async () => {
    server.use(overviewHandler())

    renderPage()

    const host = await screen.findByRole("region", { name: "Host status" })
    expect(within(host).getByText("Host information unavailable")).toBeInTheDocument()
    expect(
      within(host).getByText(
        "MEM could not read safe host information from the Docker Engine. Open Diagnostics to review the available platform checks.",
      ),
    ).toBeInTheDocument()
    expect(within(host).queryByText("0%")).not.toBeInTheDocument()
    expect(within(host).queryByText("0 B")).not.toBeInTheDocument()
    expect(within(host).getByRole("link", { name: "Open diagnostics" })).toHaveAttribute(
      "href",
      "/diagnostics",
    )
  })

  it("renders bounded Docker host facts without inventing CPU, memory, or disk usage", async () => {
    const overview = managedOverview({
      host: {
        state: "available",
        observedAtUtc: "2026-07-06T03:40:00Z",
        unavailableReasonCode: null,
        operatingSystem: "Ubuntu 24.04.2 LTS",
        architecture: "x86_64",
        cpuCount: 8,
        memoryTotalBytes: 16 * 1024 ** 3,
        dockerServerVersion: "29.4.2",
        containerCount: 7,
        imageCount: 12,
        disk: null,
        storageUnavailableReasonCode: "containerized_host_filesystem_unavailable",
      },
    })
    server.use(http.get(dashboardOverviewEndpoint, () => HttpResponse.json(overview)))

    renderPage()

    const host = await screen.findByRole("region", { name: "Host status" })
    expect(within(host).getByText("Available")).toBeInTheDocument()
    expect(within(host).getByText("Ubuntu 24.04.2 LTS")).toBeInTheDocument()
    expect(within(host).getByText("x86_64")).toBeInTheDocument()
    expect(within(host).getByText("8 logical CPUs")).toBeInTheDocument()
    expect(within(host).getByText("16 GiB total")).toBeInTheDocument()
    expect(within(host).getByText("Version 29.4.2")).toBeInTheDocument()
    expect(within(host).getByText("7 containers · 12 images")).toBeInTheDocument()
    expect(within(host).getByText("Control Plane access")).toBeInTheDocument()
    expect(within(host).getByText("SSH tunnel · Loopback only")).toBeInTheDocument()
    expect(within(host).getByText("127.0.0.1:8443")).toBeInTheDocument()
    expect(within(host).getByText("Private")).toBeInTheDocument()
    expect(within(host).getByText("Not measured")).toBeInTheDocument()
    expect(
      within(host).getByText(
        "Exact host filesystem usage is intentionally not inferred from the Control Plane container.",
      ),
    ).toBeInTheDocument()
    expect(within(host).queryByText(/current host usage/i)).not.toBeInTheDocument()
    expect(within(host).queryByText(/0%/)).not.toBeInTheDocument()
  })


  it("surfaces unsafe Control Plane publication in Host status without hiding safe host facts", async () => {
    const overview = managedOverview({
      host: {
        state: "available",
        observedAtUtc: "2026-07-06T03:40:00Z",
        unavailableReasonCode: null,
        operatingSystem: "Ubuntu 24.04.2 LTS",
        architecture: "x86_64",
        cpuCount: 8,
        memoryTotalBytes: 16 * 1024 ** 3,
        dockerServerVersion: "29.4.2",
        containerCount: 7,
        imageCount: 12,
        disk: null,
        storageUnavailableReasonCode: "containerized_host_filesystem_unavailable",
      },
    })
    server.use(
      http.get(dashboardOverviewEndpoint, () => HttpResponse.json(overview)),
      http.get("/api/operator/runtime-context", () => HttpResponse.json(
        createRuntimeContext({
          controlPlaneExposure: {
            state: "needs-attention",
            accessMode: "unsupported",
            hostAddress: "0.0.0.0",
            hostPort: 8443,
            bindingCount: 1,
            warningCode: "control_plane_exposure_wildcard_binding",
            isPrivate: false,
          },
        }),
      )),
    )

    renderPage()

    const host = await screen.findByRole("region", { name: "Host status" })
    const access = within(host).getByTestId("dashboard-control-plane-access")
    expect(access).toHaveTextContent("Control Plane access")
    expect(access).toHaveTextContent("Unsupported exposure")
    expect(access).toHaveTextContent("0.0.0.0:8443")
    expect(access).toHaveTextContent("Needs attention")
    expect(within(host).getByText("Ubuntu 24.04.2 LTS")).toBeInTheDocument()
  })

  it("renders host storage usage only when the overview supplies an authoritative filesystem snapshot", async () => {
    const overview = managedOverview({
      host: {
        state: "available",
        observedAtUtc: "2026-07-06T03:40:00Z",
        unavailableReasonCode: null,
        operatingSystem: "Ubuntu 24.04.2 LTS",
        architecture: "x86_64",
        cpuCount: 8,
        memoryTotalBytes: 16 * 1024 ** 3,
        dockerServerVersion: "29.4.2",
        containerCount: 7,
        imageCount: 12,
        disk: {
          usedBytes: 128 * 1024 ** 3,
          totalBytes: 256 * 1024 ** 3,
          scope: "mem_data",
        },
        storageUnavailableReasonCode: null,
      },
    })
    server.use(http.get(dashboardOverviewEndpoint, () => HttpResponse.json(overview)))

    renderPage()

    const host = await screen.findByRole("region", { name: "Host status" })
    expect(within(host).getByText("128 GiB of 256 GiB")).toBeInTheDocument()
    expect(within(host).getByText("50%")).toBeInTheDocument()
    expect(within(host).getByText("MEM data storage")).toBeInTheDocument()
    expect(within(host).queryByText("Not measured")).not.toBeInTheDocument()
  })

  it("keeps an available host snapshot useful when optional Docker facts are absent", async () => {
    const overview = managedOverview({
      host: {
        state: "available",
        observedAtUtc: "2026-07-06T03:40:00Z",
        unavailableReasonCode: null,
        operatingSystem: "Linux",
        architecture: null,
        cpuCount: null,
        memoryTotalBytes: null,
        dockerServerVersion: null,
        containerCount: null,
        imageCount: null,
        disk: null,
        storageUnavailableReasonCode: "host_filesystem_unavailable",
      },
    })
    server.use(http.get(dashboardOverviewEndpoint, () => HttpResponse.json(overview)))

    renderPage()

    const host = await screen.findByRole("region", { name: "Host status" })
    expect(within(host).getByText("Linux")).toBeInTheDocument()
    expect(within(host).getAllByText("Not reported").length).toBeGreaterThanOrEqual(3)
    expect(within(host).getByText("Not measured")).toBeInTheDocument()
    expect(within(host).queryByText("Host information unavailable")).not.toBeInTheDocument()
  })

  it("maps dashboard codes to German UI copy while preserving the API supplied domain", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    server.use(overviewHandler())

    renderPage()

    expect(await screen.findByText("Noch keine Chatserver")).toBeInTheDocument()
    expect(screen.getByRole("heading", { name: "Startseite" })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Aktualisieren" })).toBeInTheDocument()
    expect(screen.getByText("deltabox.dev ist die konfigurierte Plattform-Domain.")).toBeInTheDocument()
  })
})
