import { http, HttpResponse } from "msw"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import { verifyOperatorStepUp } from "@/features/auth/control-plane-auth"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

import { StackDetailPage } from "./stack-detail-page"
import { StackServicesPage } from "./stack-services-page"
import { StackBackupsPage } from "./stack-backups-page"
import { StackUsersPage } from "./stack-users-page"
import { StackDiagnosticsPage } from "./stack-diagnostics-page"
import { StackSettingsPage } from "./stack-settings-page"
import { StackWorkspaceRedirectPage } from "./stack-workspace-redirect-page"

vi.mock("@/features/auth/control-plane-auth", () => ({
  verifyOperatorStepUp: vi.fn(),
}))

const verifyOperatorStepUpMock = vi.mocked(verifyOperatorStepUp)

const stackBase = "/internal/host-agent/runtime-stacks/demo-stack"
const localBackupsBase = "/internal/host-agent/backups/artifacts/local-backups/stacks/demo-stack"
const catalogBase = "/internal/host-agent/backups/catalog"
const restoresBase = "/internal/host-agent/backups/restores"
const usersBase = `${stackBase}/users`
const turnBase = `${stackBase}/turn`
const destroyOperationId = "9411c5eb-6144-491c-af43-574bc1db4eb9"

const stack = {
  source: "host-agent",
  status: "public_routes_verified",
  stackId: "7085b97d-3d30-434e-976a-62df0178be16",
  slug: "demo-stack",
  displayName: "Dewar Family Chat",
  category: "Family",
  logoUrl: null,
  health: "healthy",
  verificationFreshness: "current",
  lastVerifiedAtUtc: "2026-07-02T14:14:00Z",
  detail: null,
  matrix: {
    serviceKey: "matrix",
    instanceId: "matrix-instance-1",
    containerId: "container-matrix",
    containerName: "mem-matrix-demo-stack",
    internalHost: "mem-matrix-demo-stack",
    internalBaseUrl: "http://mem-matrix-demo-stack:8008",
    publicHost: "matrix-demo-stack.deltabox.dev",
    publicBaseUrl: "https://matrix-demo-stack.deltabox.dev",
    dataPath: "/srv/mem/demo-stack/matrix",
    configPath: "/srv/mem/demo-stack/matrix/homeserver.yaml",
    publicRouteId: "53",
    npmCertificateId: 2,
    runtimeMetadata: {
      databaseEngine: "postgres",
      databaseHost: "mem-postgres",
      databasePort: "5432",
      databaseName: "matrix_demo_stack",
      databaseUsername: "mxu_demo_stack",
      databasePasswordSecretKind: "matrix_postgres_password",
      databaseStatus: "active",
      turnConfigured: "true",
      turnPublicHost: "turn.deltabox.dev",
      turnRealm: "turn.deltabox.dev",
      turnUris: "turn:turn.deltabox.dev:3478?transport=udp, turn:turn.deltabox.dev:3478?transport=tcp",
      turnRelayPortsPublished: "true",
      turnSharedSecretPresent: "true",
      turnAllowGuests: "false",
      turnUserLifetime: "3600",
      turnConfigurationSource: "platform-coturn",
    },
  },
  element: {
    serviceKey: "element-web",
    instanceId: "element-instance-1",
    containerId: "container-element",
    containerName: "mem-element-demo-stack",
    internalHost: "mem-element-demo-stack",
    internalBaseUrl: "http://mem-element-demo-stack:80",
    publicHost: "chat-demo-stack.deltabox.dev",
    publicBaseUrl: "https://chat-demo-stack.deltabox.dev",
    dataPath: "/srv/mem/demo-stack/element",
    configPath: "/srv/mem/demo-stack/element/config.json",
    publicRouteId: "54",
    npmCertificateId: 2,
    runtimeMetadata: {},
  },
}

function destroyAcceptedResponse() {
  return {
    operationId: destroyOperationId,
    runtimeStackId: stack.stackId,
    slug: stack.slug,
    status: "accepted",
    pollUrl: `/internal/host-agent/operations/${destroyOperationId}`,
    reusedExistingOperation: false,
  }
}

function destroyOperationResponse({
  status = "running",
  currentStep = "remove-matrix-route",
  terminal = false,
  succeeded = false,
  lastError = null,
}: {
  status?: string
  currentStep?: string
  terminal?: boolean
  succeeded?: boolean
  lastError?: string | null
} = {}) {
  return {
    operationId: destroyOperationId,
    runtimeStackId: stack.stackId,
    status,
    currentStep,
    requestedAtUtc: "2026-08-16T02:00:00Z",
    startedAtUtc: "2026-08-16T02:00:00Z",
    completedAtUtc: terminal ? "2026-08-16T02:00:03Z" : null,
    lastError,
    terminal,
    succeeded,
  }
}

const turnInspection = {
  source: "control-plane",
  status: "ok",
  runtimeStackId: stack.stackId,
  slug: stack.slug,
  inspectedAtUtc: "2026-07-26T03:30:00Z",
  state: "connected",
  management: "mem-managed",
  liveConfiguration: {
    supported: true,
    anyTurnSettings: true,
    memManagedMarkerPresent: true,
    turnUris: [
      "turn:turn.deltabox.dev:3478?transport=udp",
      "turn:turn.deltabox.dev:3478?transport=tcp",
    ],
    credentialMechanism: "inline-shared-secret",
    sharedSecretPresent: true,
    sharedSecretMatchesPlatform: true,
    userLifetime: "3600",
    allowGuests: false,
    publicHost: "turn.deltabox.dev",
    realm: "turn.deltabox.dev",
    fileSha256: "sha256:safe-config",
    problemCode: null,
    detail: null,
  },
  persistedMetadata: {
    recorded: true,
    configured: true,
    turnUris: [
      "turn:turn.deltabox.dev:3478?transport=udp",
      "turn:turn.deltabox.dev:3478?transport=tcp",
    ],
    publicHost: "turn.deltabox.dev",
    realm: "turn.deltabox.dev",
    configurationSource: "platform-coturn",
    relayPortsPublished: true,
    sharedSecretPresent: true,
    userLifetime: "3600",
    allowGuests: false,
    matchesLiveConfiguration: true,
  },
  platform: {
    status: "ok",
    readiness: "ready",
    running: true,
    ownershipVerified: true,
    imageApproved: true,
    publicHost: "turn.deltabox.dev",
    turnUris: [
      "turn:turn.deltabox.dev:3478?transport=udp",
      "turn:turn.deltabox.dev:3478?transport=tcp",
    ],
    secretPresent: true,
    relayPortsPublished: true,
    securityPolicyApplied: true,
    detail: "Coturn is ready.",
  },
  matrixRuntime: {
    exists: true,
    running: true,
    identityMatches: true,
    problemCode: null,
    detail: null,
  },
  diagnostics: [
    {
      code: "turn_config_read",
      status: "passed",
      message: "The effective Synapse configuration was read successfully.",
    },
  ],
  warnings: [],
  detail: "This stack is connected to MEM-managed TURN.",
}

const operations = {
  source: "host-agent",
  status: "ok",
  stackId: stack.stackId,
  slug: stack.slug,
  detail: null,
  operations: [
    {
      id: "backup-operation-1",
      runtimeStackId: stack.stackId,
      operation: "backup-stack",
      status: "completed",
      idempotencyKey: "backup-demo-stack-1",
      requestedBy: "host-agent",
      hostMutationLevel: "filesystem,docker",
      currentStep: "completed",
      requestedAtUtc: "2026-07-02T13:00:00Z",
      startedAtUtc: "2026-07-02T13:00:05Z",
      completedAtUtc: "2026-07-02T13:02:00Z",
      lastError: null,
    },
    {
      id: "create-operation-1",
      runtimeStackId: stack.stackId,
      operation: "create-stack-runtime",
      status: "completed",
      idempotencyKey: "create-demo-stack-1",
      requestedBy: "host-agent",
      hostMutationLevel: "filesystem,docker,ingress,postgres",
      currentStep: "completed",
      requestedAtUtc: "2026-07-01T13:00:00Z",
      startedAtUtc: "2026-07-01T13:00:05Z",
      completedAtUtc: "2026-07-01T13:02:00Z",
      lastError: null,
    },
  ],
}

const storage = {
  source: "host-agent",
  status: "ok",
  runtimeStackId: stack.stackId,
  slug: stack.slug,
  detail: null,
  matrix: {
    dataPath: "/srv/mem/demo-stack/matrix",
    homeserverYamlPath: "/srv/mem/demo-stack/matrix/homeserver.yaml",
    homeserverYamlBytes: 2048,
    signingKeyPath: "/srv/mem/demo-stack/matrix/signing.key",
    signingKeyBytes: 59,
    mediaStorePath: "/srv/mem/demo-stack/matrix/media_store",
    mediaStoreExists: true,
    totalBytes: 3800,
    totalFiles: 7,
    sections: [
      {
        key: "local-media",
        displayName: "Local uploads",
        path: "/srv/mem/demo-stack/matrix/media_store/local",
        exists: true,
        bytes: 2400,
        files: 4,
      },
      {
        key: "remote-media-cache",
        displayName: "Remote media cache",
        path: "/srv/mem/demo-stack/matrix/media_store/remote",
        exists: true,
        bytes: 1400,
        files: 3,
      },
    ],
  },
  element: {
    dataPath: "/srv/mem/demo-stack/element",
    configPath: "/srv/mem/demo-stack/element/config.json",
    configBytes: 294,
  },
}


const matrixUsers = {
  source: "host-agent",
  status: "ok",
  stackId: stack.stackId,
  slug: stack.slug,
  inventorySource: "synapse-postgres",
  inventoryStatus: "synchronized",
  inventoryLastAttemptedAtUtc: "2026-07-02T12:06:00Z",
  inventoryLastSynchronizedAtUtc: "2026-07-02T12:06:00Z",
  inventoryUserCount: 2,
  activeAdminCount: 1,
  inventoryErrorCode: null,
  synchronizationRequired: false,
  requiresFirstAdmin: false,
  canCreateUsers: true,
  adminAuthority: {
    status: "available",
    canResetPasswords: true,
    source: "created-admin-token",
    adminUserId: "@nigel:matrix-demo-stack.deltabox.dev",
    storedAtUtc: "2026-07-02T12:05:00Z",
    lastValidatedAtUtc: "2026-07-02T12:06:00Z",
    errorCode: null,
  },
  detail: null,
  users: [
    {
      id: "matrix-user-admin",
      runtimeStackId: stack.stackId,
      matrixInstanceId: stack.matrix.instanceId,
      username: "nigel",
      matrixUserId: "@nigel:matrix-demo-stack.deltabox.dev",
      isAdmin: true,
      isFirstAdmin: true,
      status: "active",
      origin: "mem-created",
      displayName: "Nigel",
      email: "nigel@example.test",
      lastError: null,
      createdAtUtc: "2026-07-02T12:00:00Z",
      updatedAtUtc: "2026-07-02T12:01:00Z",
      matrixSyncedAtUtc: "2026-07-02T12:01:00Z",
    },
    {
      id: "matrix-user-member",
      runtimeStackId: stack.stackId,
      matrixInstanceId: stack.matrix.instanceId,
      username: "member",
      matrixUserId: "@member:matrix-demo-stack.deltabox.dev",
      isAdmin: false,
      isFirstAdmin: false,
      status: "active",
      origin: "synapse-discovered",
      displayName: null,
      email: null,
      lastError: null,
      createdAtUtc: "2026-07-02T12:05:00Z",
      updatedAtUtc: "2026-07-02T12:06:00Z",
      matrixSyncedAtUtc: "2026-07-02T12:06:00Z",
    },
  ],
}

const firstAdminRequired = {
  ...matrixUsers,
  inventoryUserCount: 0,
  activeAdminCount: 0,
  requiresFirstAdmin: true,
  users: [],
}


const catalog = {
  totalCount: 3,
  entries: [
    {
      catalogEntryId: "catalog-demo-local",
      originKind: "local-captured",
      displayName: "MEM backup of demo-stack",
      sourceStackSlug: "demo-stack",
      sourceBackupId: "bkp_demo_stack_20260702",
      capturedAtUtc: "2026-07-02T14:25:00Z",
      payloadState: "available",
      integrityStatus: "valid",
      warningCount: 0,
      advisoryCount: 0,
      payloadBytes: 3425,
      createdAtUtc: "2026-07-02T14:25:00Z",
      importedAtUtc: null,
      materialisedAtUtc: null,
      payloadRemovedAtUtc: null,
    },
    {
      catalogEntryId: "catalog-demo-warning",
      originKind: "local-captured",
      displayName: "Earlier demo-stack backup",
      sourceStackSlug: "demo-stack",
      sourceBackupId: "bkp_demo_stack_20260701",
      capturedAtUtc: "2026-07-01T14:25:00Z",
      payloadState: "available",
      integrityStatus: "warning",
      warningCount: 1,
      advisoryCount: 0,
      payloadBytes: 2048,
      createdAtUtc: "2026-07-01T14:25:00Z",
      importedAtUtc: null,
      materialisedAtUtc: null,
      payloadRemovedAtUtc: null,
    },
    {
      catalogEntryId: "catalog-other-stack",
      originKind: "local-captured",
      displayName: "Other stack backup",
      sourceStackSlug: "other-stack",
      sourceBackupId: "bkp_other_stack_20260702",
      capturedAtUtc: "2026-07-02T12:00:00Z",
      payloadState: "available",
      integrityStatus: "valid",
      warningCount: 0,
      advisoryCount: 0,
      payloadBytes: 1024,
      createdAtUtc: "2026-07-02T12:00:00Z",
      importedAtUtc: null,
      materialisedAtUtc: null,
      payloadRemovedAtUtc: null,
    },
  ],
}

const restoreSessions = {
  source: "host-agent",
  status: "ok",
  query: {
    page: 1,
    pageSize: 10,
    search: null,
    status: null,
    targetStack: "demo-stack",
    sortBy: "updated",
    sortDirection: "desc",
  },
  summary: {
    totalSessions: 1,
    productionRecreateCount: 1,
    publiclyVerifiedCount: 0,
    needsActionCount: 0,
  },
  totalSessions: 1,
  page: 1,
  pageSize: 10,
  totalPages: 1,
  hasPreviousPage: false,
  hasNextPage: false,
  targetStacks: ["demo-stack"],
  warnings: [],
  detail: null,
  sessions: [
    {
      restoreSessionId: "restore-demo-target",
      workspaceAvailable: true,
      sourceKind: "backup-catalog",
      sourceLabel: "bkp_demo_stack_20260702",
      sourceDeleted: false,
      catalogEntryId: "catalog-demo-local",
      sourceStackSlug: "demo-stack",
      sourceBackupId: "bkp_demo_stack_20260702",
      targetStackSlug: "demo-stack",
      status: "completed",
      statusLabel: "Completed",
      currentStage: "public-verification",
      currentStageLabel: "Public verification",
      progressPercent: 100,
      startedAtUtc: "2026-07-02T15:00:00Z",
      lastUpdatedAtUtc: "2026-07-02T15:20:00Z",
      terminalAtUtc: "2026-07-02T15:20:00Z",
      warningCount: 0,
      errorCount: 0,
      productionRecreateStarted: true,
      publiclyVerified: true,
      nextActionCode: "view-restore",
      nextActionTitle: "Open restore workspace",
      detail: "Public verification completed.",
    },
  ],
}

function renderPage(initialEntry = "/stacks/demo-stack") {
  return renderWithProviders(
    <MemoryRouter initialEntries={[initialEntry]}>
      <Routes>
        <Route path="/stacks/:slugOrId" element={<StackDetailPage />} />
        <Route path="/stacks" element={<div>Chat server inventory destination</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

function renderServicesPage() {
  return renderWithProviders(
    <MemoryRouter initialEntries={["/stacks/demo-stack/services"]}>
      <Routes>
        <Route path="/stacks/:slugOrId/services" element={<StackServicesPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

function renderRecoveryPage() {
  return renderWithProviders(
    <MemoryRouter initialEntries={["/stacks/demo-stack/recovery"]}>
      <Routes>
        <Route path="/stacks/:slugOrId/recovery" element={<StackBackupsPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

function renderUsersPage() {
  return renderWithProviders(
    <MemoryRouter initialEntries={["/stacks/demo-stack/users"]}>
      <Routes>
        <Route path="/stacks/:slugOrId/users" element={<StackUsersPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

function renderDiagnosticsPage() {
  return renderWithProviders(
    <MemoryRouter initialEntries={["/stacks/demo-stack/diagnostics"]}>
      <Routes>
        <Route path="/stacks/:slugOrId/diagnostics" element={<StackDiagnosticsPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

function renderRetiredSettingsRedirect() {
  return renderWithProviders(
    <MemoryRouter initialEntries={["/stacks/demo-stack/settings"]}>
      <Routes>
        <Route path="/stacks/:slugOrId/settings" element={<StackSettingsPage />} />
        <Route path="/stacks/:slugOrId" element={<div>Stack overview destination</div>} />
      </Routes>
    </MemoryRouter>,
  )
}


function renderRetiredStackSectionRedirect(path: string, target: "services" | "recovery") {
  return renderWithProviders(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/stacks/:slugOrId/storage-media" element={<StackWorkspaceRedirectPage target={target} />} />
        <Route path="/stacks/:slugOrId/network" element={<StackWorkspaceRedirectPage target={target} />} />
        <Route path="/stacks/:slugOrId/voice-video" element={<StackWorkspaceRedirectPage target={target} />} />
        <Route path="/stacks/:slugOrId/backups" element={<StackWorkspaceRedirectPage target={target} />} />
        <Route path="/stacks/:slugOrId/services" element={<div>Stack services destination</div>} />
        <Route path="/stacks/:slugOrId/recovery" element={<div>Stack recovery destination</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

function renderOverviewToDiagnosticsFlow() {
  return renderWithProviders(
    <MemoryRouter initialEntries={["/stacks/demo-stack"]}>
      <Routes>
        <Route path="/stacks/:slugOrId" element={<StackDetailPage />} />
        <Route path="/stacks/:slugOrId/diagnostics" element={<StackDiagnosticsPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

function renderDiagnosticsToOverviewFlow() {
  return renderWithProviders(
    <MemoryRouter initialEntries={["/stacks/demo-stack/diagnostics"]}>
      <Routes>
        <Route path="/stacks/:slugOrId/diagnostics" element={<StackDiagnosticsPage />} />
        <Route path="/stacks/:slugOrId" element={<StackDetailPage />} />
      </Routes>
    </MemoryRouter>,
  )
}

function registerStackHandlers({
  onDoctor,
  onBackup,
  onRestoreTarget,
  onCreateFirstAdmin,
  onCreateUser,
  onIdentity,
}: {
  onDoctor?: () => void | Promise<void>
  onBackup?: () => void
  onRestoreTarget?: (targetStack: string | null) => void
  onCreateFirstAdmin?: (request: Record<string, unknown>) => void
  onCreateUser?: (request: Record<string, unknown>) => void
  onIdentity?: (request: Record<string, unknown>) => void
} = {}) {
  server.use(
    http.get(stackBase, () => HttpResponse.json(stack)),
    http.put(`${stackBase}/identity`, async ({ request }) => {
      const body = (await request.json()) as Record<string, unknown>
      onIdentity?.(body)
      return HttpResponse.json({
        ...stack,
        displayName: String(body.displayName ?? stack.displayName),
        category: body.category === null ? null : String(body.category ?? stack.category),
      })
    }),
    http.get(turnBase, () => HttpResponse.json(turnInspection)),
    http.get(`${stackBase}/operations`, () => HttpResponse.json(operations)),
    http.get(`${stackBase}/storage`, () => HttpResponse.json(storage)),
    http.post(`${stackBase}/doctor`, async () => {
      await onDoctor?.()

      return HttpResponse.json({
        source: "host-agent",
        status: "passed",
        stackId: stack.stackId,
        slug: stack.slug,
        lastVerifiedStatus: "Public Routes Verified",
        lastVerifiedAtUtc: "2026-07-02T14:20:00Z",
        checkedAtUtc: "2026-07-02T14:20:00Z",
        allPassed: true,
        checks: [
          {
            code: "matrix-public",
            name: "Matrix public route",
            url: stack.matrix.publicBaseUrl,
            success: true,
            statusCode: 200,
            detail: "Matrix client endpoint responded.",
            bodyPreview: null,
          },
        ],
        detail: "All checks passed.",
        operationId: "doctor-operation-1",
        reportId: "doctor-report-1",
      })
    }),
    http.post(localBackupsBase, () => {
      onBackup?.()

      return HttpResponse.json({
        operationId: "backup-operation-2",
        runtimeStackId: stack.stackId,
        stackSlug: stack.slug,
        backupId: "bkp_demo_stack_20260702",
        backupRootPath: "/srv/mem/backups/bkp_demo_stack_20260702",
        manifestPath: "/srv/mem/backups/bkp_demo_stack_20260702/manifest.json",
        databaseDumpPath: "/srv/mem/backups/bkp_demo_stack_20260702/synapse.sql",
        matrixConfigPath: "/srv/mem/backups/bkp_demo_stack_20260702/homeserver.yaml",
        matrixSigningKeyPath: "/srv/mem/backups/bkp_demo_stack_20260702/signing.key",
        matrixMediaBackupPath: "/srv/mem/backups/bkp_demo_stack_20260702/media_store",
        elementConfigPath: "/srv/mem/backups/bkp_demo_stack_20260702/config.json",
        stats: {
          databaseDump: { included: true, path: "database/synapse.sql", bytes: 1024 },
          homeserverConfig: { included: true, path: "matrix/homeserver.yaml", bytes: 2048 },
          signingKey: { included: true, path: "matrix/signing.key", bytes: 59 },
          mediaStore: { included: true, path: "matrix/media_store", bytes: 0, files: 0 },
          elementConfig: { included: true, path: "element/config.json", bytes: 294 },
          totalBytes: 3425,
          totalFiles: 4,
        },
        warnings: [],
        createdAtUtc: "2026-07-02T14:25:00Z",
      })
    }),
    http.get(usersBase, () => HttpResponse.json(matrixUsers)),
    http.post(`${usersBase}/first-admin`, async ({ request }) => {
      onCreateFirstAdmin?.((await request.json()) as Record<string, unknown>)

      return HttpResponse.json(matrixUsers.users[0])
    }),
    http.post(usersBase, async ({ request }) => {
      onCreateUser?.((await request.json()) as Record<string, unknown>)

      return HttpResponse.json(matrixUsers.users[1])
    }),
    http.get(catalogBase, () => HttpResponse.json(catalog)),
    http.get(restoresBase, ({ request }) => {
      const url = new URL(request.url)
      const targetStack = url.searchParams.get("targetStack")
      onRestoreTarget?.(targetStack)

      return HttpResponse.json({
        ...restoreSessions,
        query: {
          ...restoreSessions.query,
          targetStack,
        },
      })
    }),
  )
}

beforeEach(() => {
  server.use(
    http.get(`${stackBase}/doctor/latest`, () =>
      HttpResponse.json({
        source: "control-plane",
        status: "not_recorded",
        stackId: stack.stackId,
        slug: stack.slug,
        report: null,
        detail: "No persisted Doctor report has been recorded for this runtime stack yet.",
      })),
    http.get(`${stackBase}/doctor/history`, () =>
      HttpResponse.json({
        source: "control-plane",
        status: "ok",
        stackId: stack.stackId,
        slug: stack.slug,
        reports: [],
        totalCount: 0,
        page: 1,
        pageSize: 10,
        totalPages: 1,
        hasPreviousPage: false,
        hasNextPage: false,
        detail: "No previous completed Doctor reports have been recorded for this runtime stack yet.",
      })),
  )
})

afterEach(() => {
  window.localStorage.clear()
  verifyOperatorStepUpMock.mockReset()
})

describe("Stack Workspace pages", () => {
  it("uses the existing stack route as a focused Overview while keeping technical detail available", async () => {
    const user = userEvent.setup()
    registerStackHandlers()

    renderPage()

    expect(await screen.findByText("Runtime overview")).toBeInTheDocument()
    expect(screen.getByRole("heading", { name: "Dewar Family Chat" })).toBeInTheDocument()
    expect(screen.getByText("Stack identity")).toBeInTheDocument()
    expect(screen.getByText("Family")).toBeInTheDocument()
    expect(screen.getAllByText("demo-stack").length).toBeGreaterThan(0)
    expect(screen.getByRole("link", { name: "Back to chat servers" })).toHaveAttribute("href", "/stacks")
    expect(screen.getByRole("link", { name: "Open Element" })).toHaveAttribute(
      "href",
      stack.element.publicBaseUrl,
    )
    expect(screen.getByRole("link", { name: "Open Element" })).toHaveAttribute("target", "_blank")
    expect(screen.getByRole("link", { name: "Open Element" })).toHaveAttribute(
      "rel",
      "noopener noreferrer",
    )
    expect(
      screen.getByRole("link", { name: "Open Element" }).querySelector('img[src="/brands/element.svg"]'),
    ).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Matrix API" })).toHaveAttribute(
      "href",
      stack.matrix.publicBaseUrl,
    )
    expect(screen.getByRole("link", { name: "Matrix API" })).toHaveAttribute("target", "_blank")
    expect(
      screen.getByRole("link", { name: "Matrix API" }).querySelector('[data-brand-icon="matrix"]'),
    ).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Overview" })).toHaveAttribute("href", "/stacks/demo-stack")
    expect(screen.getByRole("link", { name: "Services" })).toHaveAttribute(
      "href",
      "/stacks/demo-stack/services",
    )
    expect(screen.getByRole("link", { name: "Users" })).toHaveAttribute(
      "href",
      "/stacks/demo-stack/users",
    )
    expect(screen.getByRole("link", { name: "Recovery" })).toHaveAttribute(
      "href",
      "/stacks/demo-stack/recovery",
    )
    expect(screen.getByRole("link", { name: "Diagnostics" })).toHaveAttribute(
      "href",
      "/stacks/demo-stack/diagnostics",
    )
    expect(screen.queryByRole("link", { name: "Storage & media" })).not.toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "Network & domains" })).not.toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "Voice & video" })).not.toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "Backups" })).not.toBeInTheDocument()
    expect(screen.getByText("Latest doctor result")).toBeInTheDocument()
    expect(screen.getByText("Latest backup")).toBeInTheDocument()
    expect(screen.getByText("Recent operations")).toBeInTheDocument()
    expect(screen.getByText("Technical details")).toBeInTheDocument()
    const workspaceActions = screen.getByTestId("stack-workspace-actions")
    expect(workspaceActions).toHaveClass("flex-col", "min-[1500px]:flex-row")
    expect(within(workspaceActions).getByRole("button", { name: "Delete chat server" })).toBeInTheDocument()
    expect(screen.queryByText("Matrix database ownership")).not.toBeInTheDocument()
    expect(screen.queryByText("Backup boundary")).not.toBeInTheDocument()

    await user.click(screen.getByText("Technical details"))
    expect(screen.queryByText("Matrix users")).not.toBeInTheDocument()
    expect(screen.queryByText("Latest doctor checks")).not.toBeInTheDocument()
    expect(screen.queryByText("Voice / video TURN")).not.toBeInTheDocument()
  })

  it("keeps stack deletion in the contextual Overview header and accepts one durable removal request", async () => {
    let submittedDestroyRequest: Record<string, unknown> | undefined
    let destroyPosts = 0

    registerStackHandlers()
    server.use(
      http.post(`${stackBase}/destroy`, async ({ request }) => {
        destroyPosts += 1
        submittedDestroyRequest = (await request.json()) as Record<string, unknown>
        return HttpResponse.json(destroyAcceptedResponse(), { status: 202 })
      }),
      http.get(`/internal/host-agent/operations/${destroyOperationId}`, () =>
        HttpResponse.json(destroyOperationResponse({
          status: "succeeded",
          currentStep: "complete",
          terminal: true,
          succeeded: true,
        })),
      ),
    )

    const user = userEvent.setup()
    renderPage()

    expect(await screen.findByRole("button", { name: "Delete chat server" })).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Delete chat server" }))
    await user.click(
      within(await screen.findByRole("alertdialog")).getByRole("button", {
        name: "Destroy runtime",
      }),
    )

    await waitFor(() => expect(destroyPosts).toBe(1))
    expect(submittedDestroyRequest).toMatchObject({
      removeContainers: true,
      removeRoutes: true,
      removeDatabase: false,
      removeFiles: false,
      force: false,
    })
    expect(submittedDestroyRequest?.idempotencyKey).toEqual(expect.any(String))
    expect(await screen.findByText("Chat server inventory destination")).toBeInTheDocument()
  })

  it("does not start stack deletion when the Overview confirmation is cancelled", async () => {
    let destroyAttempts = 0

    registerStackHandlers()
    server.use(
      http.post(`${stackBase}/destroy`, () => {
        destroyAttempts += 1
        return HttpResponse.json(destroyAcceptedResponse(), { status: 202 })
      }),
    )

    const user = userEvent.setup()
    renderPage()

    await screen.findByRole("button", { name: "Delete chat server" })
    await user.click(screen.getByRole("button", { name: "Delete chat server" }))
    await user.click(
      within(await screen.findByRole("alertdialog")).getByRole("button", { name: "Cancel" }),
    )

    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()
    expect(destroyAttempts).toBe(0)
  })

  it("requires recent identity verification and resumes the exact Overview destroy request", async () => {
    verifyOperatorStepUpMock.mockResolvedValue({
      status: "verified",
      expiresAtUtc: "2026-07-06T12:00:00Z",
    })

    const submittedDestroyRequests: Record<string, unknown>[] = []
    let destroyAttempts = 0

    registerStackHandlers()
    server.use(
      http.post(`${stackBase}/destroy`, async ({ request }) => {
        destroyAttempts += 1
        submittedDestroyRequests.push((await request.json()) as Record<string, unknown>)

        if (destroyAttempts === 1) {
          return HttpResponse.json(
            {
              error: "step_up_required",
              detail: "Fresh identity verification is required before this action.",
            },
            { status: 403, headers: { "Cache-Control": "no-store" } },
          )
        }

        return HttpResponse.json(destroyAcceptedResponse(), { status: 202 })
      }),
      http.get(`/internal/host-agent/operations/${destroyOperationId}`, () =>
        HttpResponse.json(destroyOperationResponse({
          status: "succeeded",
          currentStep: "complete",
          terminal: true,
          succeeded: true,
        })),
      ),
    )

    const user = userEvent.setup()
    renderPage()

    await screen.findByRole("button", { name: "Delete chat server" })
    await user.click(screen.getByRole("button", { name: "Delete chat server" }))
    await user.click(
      within(await screen.findByRole("alertdialog")).getByRole("button", {
        name: "Destroy runtime",
      }),
    )

    expect(await screen.findByRole("dialog", { name: "Verify your identity" })).toBeInTheDocument()
    await user.type(screen.getByLabelText("Current password"), "Secure!Foundation123")
    await user.type(screen.getByLabelText("Current authenticator code"), "123456")
    await user.click(screen.getByRole("button", { name: "Verify identity" }))

    await waitFor(() => expect(destroyAttempts).toBe(2))
    expect(verifyOperatorStepUpMock).toHaveBeenCalledWith("Secure!Foundation123", "123456")
    expect(submittedDestroyRequests[0]?.idempotencyKey).toEqual(
      submittedDestroyRequests[1]?.idempotencyKey,
    )
    expect(await screen.findByText("Chat server inventory destination")).toBeInTheDocument()
  })

  it("surfaces stale verification consistently in the Stack Workspace", async () => {
    server.use(
      http.get(stackBase, () => HttpResponse.json({
        ...stack,
        health: "needs_attention",
        verificationFreshness: "stale",
      })),
      http.get(`${stackBase}/operations`, () => HttpResponse.json(operations)),
    )

    renderPage()

    expect(await screen.findByRole("heading", { name: "Dewar Family Chat" })).toBeInTheDocument()
    expect(screen.getAllByText("Needs attention").length).toBeGreaterThan(0)
    expect(screen.getByText("Verification evidence is stale. Run Doctor to refresh the current readiness result.")).toBeInTheDocument()
  })

  it("updates the friendly stack identity without changing the technical slug", async () => {
    let submitted: Record<string, unknown> | null = null
    registerStackHandlers({ onIdentity: (request) => { submitted = request } })
    const user = userEvent.setup()

    renderPage()

    await screen.findByText("Stack identity")
    await user.click(screen.getByRole("button", { name: "Edit identity" }))

    const displayName = screen.getByLabelText("Display name")
    const category = screen.getByLabelText("Category")
    await user.clear(displayName)
    await user.type(displayName, "Dewar Distillery Chat")
    await user.clear(category)
    await user.type(category, "Family")
    await user.click(screen.getByRole("button", { name: "Save identity" }))

    await waitFor(() => expect(submitted).toEqual({
      displayName: "Dewar Distillery Chat",
      category: "Family",
    }))
    expect(await screen.findByRole("heading", { name: "Dewar Distillery Chat" })).toBeInTheDocument()
    expect(screen.getAllByText("demo-stack").length).toBeGreaterThan(0)
  })

  it("uploads, projects, replaces, and removes the custom stack logo without changing stack identity", async () => {
    registerStackHandlers()
    const user = userEvent.setup()
    const logoUrl = `${stackBase}/logo?v=revision-1`
    let uploadObserved = false
    let removes = 0

    server.use(
      http.put(`${stackBase}/logo`, async ({ request }) => {
        // Inspect the serialized multipart request directly. JSDOM's File and
        // Node/Undici's File are distinct Web API realms, so request.formData()
        // cannot safely reconstruct this test upload even though the browser
        // transport itself is valid.
        const multipartBody = new TextDecoder().decode(await request.arrayBuffer())
        // Node/Undici may normalize a cross-realm JSDOM File filename to
        // "blob". The server does not trust the client filename, so the
        // component contract is that a PNG is sent in the multipart `file`
        // field and the returned logo identity is projected.
        uploadObserved =
          multipartBody.includes('name="file"') &&
          multipartBody.includes("Content-Type: image/png")
        return HttpResponse.json({ ...stack, logoUrl })
      }),
      http.delete(`${stackBase}/logo`, () => {
        removes += 1
        return HttpResponse.json({ ...stack, logoUrl: null })
      }),
    )

    renderPage()
    await screen.findByText("Stack identity")

    const file = new File([new Uint8Array([0x89, 0x50, 0x4e, 0x47])], "family.png", {
      type: "image/png",
    })
    await user.upload(screen.getByLabelText("Choose PNG stack logo"), file)

    await waitFor(() => expect(uploadObserved).toBe(true))
    await waitFor(() => {
      expect(document.querySelector(`img[src="${logoUrl}"]`)).toBeInTheDocument()
    })
    expect(screen.getByRole("heading", { name: "Dewar Family Chat" })).toBeInTheDocument()
    expect(screen.getAllByText("demo-stack").length).toBeGreaterThan(0)
    expect(screen.getByRole("button", { name: "Replace logo" })).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Remove logo" }))
    await waitFor(() => expect(removes).toBe(1))
    await waitFor(() => {
      expect(document.querySelector(`img[src="${logoUrl}"]`)).not.toBeInTheDocument()
    })
    expect(screen.getByRole("button", { name: "Upload logo" })).toBeInTheDocument()
  })

  it("projects Matrix and Element runtime services at their dedicated stack route", async () => {
    registerStackHandlers()

    renderServicesPage()

    expect(await screen.findByText("Stack services")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Overview" })).toHaveAttribute("href", "/stacks/demo-stack")
    expect(screen.getByRole("link", { name: "Services" })).toHaveAttribute(
      "href",
      "/stacks/demo-stack/services",
    )
    expect(screen.getByRole("link", { name: "Services" })).toHaveAttribute("aria-current", "page")

    expect(screen.getByText("Matrix homeserver")).toBeInTheDocument()
    expect(screen.getByText("Element web client")).toBeInTheDocument()
    expect(screen.getAllByText("mem-matrix-demo-stack")).not.toHaveLength(0)
    expect(screen.getAllByText("mem-element-demo-stack")).not.toHaveLength(0)
    expect(screen.getByText("matrix_demo_stack")).toBeInTheDocument()
    expect(screen.getAllByRole("link", { name: "Open Matrix homeserver" })[0]).toHaveAttribute(
      "href",
      stack.matrix.publicBaseUrl,
    )
    expect(screen.getAllByRole("link", { name: "Open Element web client" })[0]).toHaveAttribute(
      "href",
      stack.element.publicBaseUrl,
    )
    expect(screen.getByText(/Most service changes use dedicated safe workflows\./)).toBeInTheDocument()
  })

  it("projects real storage and media evidence inside Services", async () => {
    const user = userEvent.setup()
    registerStackHandlers()

    renderServicesPage()

    expect(await screen.findByText("Recovery boundary")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Services" })).toHaveAttribute("aria-current", "page")
    expect(screen.getByRole("link", { name: "Recovery" })).toHaveAttribute(
      "href",
      "/stacks/demo-stack/recovery",
    )
    expect(screen.queryByRole("link", { name: "Storage & media" })).not.toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "Backups" })).not.toBeInTheDocument()
    expect(screen.getByText("Matrix media sections")).toBeInTheDocument()
    expect(screen.getByText("Local uploads")).toBeInTheDocument()
    expect(screen.getByText("Remote media cache")).toBeInTheDocument()
    expect(screen.getAllByText("3.7 KiB")).not.toHaveLength(0)
    await user.click(screen.getByText("Important paths"))
    expect(screen.getAllByText("/srv/mem/demo-stack/matrix")).not.toHaveLength(0)
  })

  it("keeps Storage & media honest when the storage inspection is unavailable", async () => {
    registerStackHandlers()
    server.use(
      http.get(`${stackBase}/storage`, () =>
        HttpResponse.json({ error: "Storage manifest is unavailable." }, { status: 503 }),
      ),
    )

    renderServicesPage()

    expect(await screen.findByText("Storage inspection failed")).toBeInTheDocument()
    expect(await screen.findByText("Storage details are not available yet.")).toBeInTheDocument()
    expect(screen.queryByText("Matrix media sections")).not.toBeInTheDocument()
  })

  it("projects public network and domain runtime evidence inside Services", async () => {
    registerStackHandlers()

    renderServicesPage()

    expect(await screen.findByText("Matrix homeserver public route")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Services" })).toHaveAttribute("aria-current", "page")
    expect(screen.queryByRole("link", { name: "Network & domains" })).not.toBeInTheDocument()
    expect(screen.getByText("Element web client public route")).toBeInTheDocument()
    expect(screen.getAllByText(stack.matrix.publicHost)).not.toHaveLength(0)
    expect(screen.getAllByText(stack.element.publicHost)).not.toHaveLength(0)
    expect(screen.getAllByText("NPM route ID")).toHaveLength(2)
    expect(screen.getAllByText("NPM certificate ID")).toHaveLength(2)
    expect(screen.getAllByRole("link", { name: "Open Matrix homeserver" })[0]).toHaveAttribute(
      "href",
      stack.matrix.publicBaseUrl,
    )
    expect(screen.getAllByRole("link", { name: "Open Element web client" })[0]).toHaveAttribute(
      "href",
      stack.element.publicBaseUrl,
    )
    expect(screen.getByText(/does not inspect DNS-provider records/)).toBeInTheDocument()
    expect(screen.queryByText("Technical details")).not.toBeInTheDocument()
  })

  it("keeps Network & domains honest when the runtime manifest has no service routes", async () => {
    registerStackHandlers()
    server.use(
      http.get(stackBase, () =>
        HttpResponse.json({
          ...stack,
          matrix: null,
          element: null,
        }),
      ),
    )

    renderServicesPage()

    expect(
      await screen.findAllByText(
        /No service route was reported in the runtime manifest, so MEM cannot safely describe its domain, ingress, or internal delivery path\./,
      ),
    ).toHaveLength(2)
    expect(screen.queryByRole("link", { name: /Open .* (homeserver|client)/ })).not.toBeInTheDocument()
  })

  it("projects configured TURN evidence inside Services", async () => {
    registerStackHandlers()

    renderServicesPage()

    expect(await screen.findByText("Voice / video TURN")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Services" })).toHaveAttribute("aria-current", "page")
    expect(screen.queryByRole("link", { name: "Voice & video" })).not.toBeInTheDocument()
    expect(await screen.findAllByText("Connected")).not.toHaveLength(0)
    expect(screen.getAllByText("turn.deltabox.dev")).not.toHaveLength(0)
    expect(screen.getByText("turn:turn.deltabox.dev:3478?transport=udp")).toBeInTheDocument()
    expect(screen.getByText("turn:turn.deltabox.dev:3478?transport=tcp")).toBeInTheDocument()
    expect(screen.getByText(/does not place a test call/)).toBeInTheDocument()
    expect(screen.getByText("Technical details")).toBeInTheDocument()
  })

  it("classifies a live stack with no TURN settings as not connected", async () => {
    registerStackHandlers()
    server.use(
      http.get(turnBase, () =>
        HttpResponse.json({
          ...turnInspection,
          state: "not-connected",
          management: "none",
          liveConfiguration: {
            ...turnInspection.liveConfiguration,
            anyTurnSettings: false,
            memManagedMarkerPresent: false,
            turnUris: [],
            credentialMechanism: "none",
            sharedSecretPresent: false,
            sharedSecretMatchesPlatform: null,
            publicHost: null,
            realm: null,
          },
          persistedMetadata: {
            ...turnInspection.persistedMetadata,
            recorded: false,
            configured: null,
            turnUris: [],
            publicHost: null,
            realm: null,
            configurationSource: null,
            matchesLiveConfiguration: null,
          },
          detail: "This stack is not configured to use a TURN service.",
        }),
      ),
    )

    renderServicesPage()

    expect(await screen.findByText("Voice / video TURN")).toBeInTheDocument()
    expect(await screen.findAllByText("Not connected")).not.toHaveLength(0)
    expect(
      screen.getByText(
        "This stack is not configured to use a TURN service. Voice and video may fail for users behind restrictive networks.",
      ),
    ).toBeInTheDocument()
    expect(screen.queryByText("TURN URIs")).not.toBeInTheDocument()
  })

  it("localises the Stack Workspace shell in German while preserving raw stack identifiers", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    registerStackHandlers()

    renderPage()

    expect(await screen.findByRole("heading", { name: "Dewar Family Chat" })).toBeInTheDocument()
    expect(screen.getAllByText("demo-stack").length).toBeGreaterThan(0)
    await screen.findByText("Laufzeitüberblick")
    expect(screen.getByRole("link", { name: "Zurück zu den Chatservern" })).toHaveAttribute(
      "href",
      "/stacks",
    )
    expect(screen.getByRole("link", { name: "Element öffnen" })).toHaveAttribute(
      "href",
      stack.element.publicBaseUrl,
    )
    expect(screen.getByRole("link", { name: "Matrix-API" })).toHaveAttribute(
      "href",
      stack.matrix.publicBaseUrl,
    )
    expect(screen.getByRole("button", { name: "Aktualisieren" })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Diagnose ausführen" })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Sicherung erstellen" })).toBeInTheDocument()

    const navigation = screen.getByRole("navigation", { name: "Bereiche des Stack-Arbeitsbereichs" })

    expect(within(navigation).getAllByRole("link").map((link) => link.textContent)).toEqual([
      "Überblick",
      "Dienste",
      "Benutzer",
      "Wiederherstellung",
      "Föderation",
      "Diagnose",
    ])
    expect(within(navigation).getByRole("link", { name: "Überblick" })).toHaveAttribute(
      "href",
      "/stacks/demo-stack",
    )
    expect(screen.getAllByText("demo-stack")).not.toHaveLength(0)
    expect(screen.getAllByText("matrix-demo-stack.deltabox.dev")).not.toHaveLength(0)
  })

  it("localises the Stack Overview body in German while preserving technical evidence", async () => {
    const user = userEvent.setup()
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    registerStackHandlers()

    renderPage()

    expect(await screen.findByText("Laufzeitüberblick")).toBeInTheDocument()
    expect(screen.getByText("Öffentliche Bereitschaft")).toBeInTheDocument()
    expect(screen.getByText("Zuletzt verifiziert")).toBeInTheDocument()
    expect(screen.getByText("Öffentlicher Matrix-Host")).toBeInTheDocument()
    expect(screen.getByText("Öffentlicher Element-Host")).toBeInTheDocument()
    expect(screen.getByText("Letztes Diagnoseergebnis")).toBeInTheDocument()
    expect(screen.getByText("Letzte Sicherung")).toBeInTheDocument()
    expect(screen.getByText("Aktuelle Vorgänge")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Chatserver löschen" })).toBeInTheDocument()
    expect(screen.getAllByText("Sicherung erstellen")).not.toHaveLength(0)
    expect(screen.getByText("Stack-Laufzeit erstellen")).toBeInTheDocument()
    expect(screen.getAllByText("Abgeschlossen")).not.toHaveLength(0)
    expect(screen.getByText("matrix-demo-stack.deltabox.dev")).toBeInTheDocument()
    expect(screen.getByText("chat-demo-stack.deltabox.dev")).toBeInTheDocument()
    expect(screen.queryByText("Runtime overview")).not.toBeInTheDocument()

    await user.click(screen.getByText("Technische Details"))
    expect(screen.getByText("Lokaler Vorgangsverlauf der Steuerungsoberfläche für diesen Stack.")).toBeInTheDocument()
    expect(screen.getAllByText("Angefordert von")).not.toHaveLength(0)
    expect(screen.getAllByText("Änderungsebene")).not.toHaveLength(0)
    expect(screen.getByText("backup-operation-1")).toBeInTheDocument()
    expect(screen.getByText("backup-demo-stack-1")).toBeInTheDocument()
  })

  it("localises Stack Services in German while preserving raw runtime identifiers", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    registerStackHandlers()

    renderServicesPage()

    expect(await screen.findByText("Stack-Dienste")).toBeInTheDocument()
    expect(screen.getByText("Matrix-Homeserver")).toBeInTheDocument()
    expect(screen.getByText("Element-Webclient")).toBeInTheDocument()
    expect(screen.getAllByRole("link", { name: "Matrix-Homeserver öffnen" })[0]).toHaveAttribute(
      "href",
      stack.matrix.publicBaseUrl,
    )
    expect(screen.getAllByRole("link", { name: "Element-Webclient öffnen" })[0]).toHaveAttribute(
      "href",
      stack.element.publicBaseUrl,
    )
    expect(screen.getAllByText("Container")).not.toHaveLength(0)
    expect(screen.getAllByText("Datenbank")).not.toHaveLength(0)
    expect(screen.getByText("Geheimnis verborgen")).toBeInTheDocument()
    expect(screen.getAllByText("Datenpfad")).not.toHaveLength(0)
    expect(screen.getAllByText("Konfigurationspfad")).not.toHaveLength(0)
    expect(screen.getAllByText("mem-matrix-demo-stack")).not.toHaveLength(0)
    expect(screen.getByText("matrix_demo_stack")).toBeInTheDocument()
    expect(screen.getAllByText("https://matrix-demo-stack.deltabox.dev")).not.toHaveLength(0)
    expect(screen.queryByText("Stack services")).not.toBeInTheDocument()
  })


  it("localises Stack Storage and media in German while preserving paths and media section names", async () => {
    const user = userEvent.setup()
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    registerStackHandlers()

    renderServicesPage()

    expect(await screen.findByText("Wiederherstellungsgrenze")).toBeInTheDocument()
    expect(screen.getAllByText("Speicher & Medien")).not.toHaveLength(0)
    expect(screen.getByText("Matrix-Medienbereiche")).toBeInTheDocument()
    expect(screen.getByText("Medienspeicher")).toBeInTheDocument()
    expect(screen.getAllByText("verfügbar")).not.toHaveLength(0)
    expect(screen.getByText("Größe der Matrix-Medien")).toBeInTheDocument()
    expect(screen.getAllByText("3,7 KiB")).not.toHaveLength(0)
    expect(screen.getByText("Local uploads")).toBeInTheDocument()
    expect(screen.getByText("Remote media cache")).toBeInTheDocument()
    expect(screen.queryByText("Recovery boundary")).not.toBeInTheDocument()

    await user.click(screen.getByText("Wichtige Pfade"))
    expect(screen.getByText("Matrix-Datenpfad")).toBeInTheDocument()
    expect(screen.getByText("Medienspeicherpfad")).toBeInTheDocument()
    expect(screen.getAllByText("/srv/mem/demo-stack/matrix")).not.toHaveLength(0)
  })

  it("localises Stack Network and domains in German while preserving route identifiers", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    registerStackHandlers()

    renderServicesPage()

    expect(await screen.findByText("Öffentliche Route des Matrix-Homeservers")).toBeInTheDocument()
    expect(screen.getByText("Öffentliche Route des Element-Webclients")).toBeInTheDocument()
    expect(screen.getByText("Schreibgeschützte Nachweise")).toBeInTheDocument()
    expect(screen.getAllByText("NPM-Routen-ID")).toHaveLength(2)
    expect(screen.getAllByText("NPM-Zertifikats-ID")).not.toHaveLength(0)
    expect(screen.getAllByText(stack.matrix.publicHost)).not.toHaveLength(0)
    expect(screen.getAllByText(stack.element.publicHost)).not.toHaveLength(0)
    expect(screen.getAllByRole("link", { name: "Matrix-Homeserver öffnen" })[0]).toHaveAttribute(
      "href",
      stack.matrix.publicBaseUrl,
    )
    expect(screen.getAllByRole("link", { name: "Element-Webclient öffnen" })[0]).toHaveAttribute(
      "href",
      stack.element.publicBaseUrl,
    )
    expect(screen.queryByText(/does not inspect DNS-provider records/)).not.toBeInTheDocument()
  })

  it("localises Stack Voice and video TURN evidence in German while preserving TURN URIs", async () => {
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    registerStackHandlers()

    renderServicesPage()

    expect(await screen.findByText("Sprache-/Video-TURN")).toBeInTheDocument()
    expect(await screen.findAllByText("Verbunden")).not.toHaveLength(0)
    expect(screen.getByText("Stack-Verbindung")).toBeInTheDocument()
    expect(screen.getByText("MEM-verwaltet")).toBeInTheDocument()
    expect(screen.getByText("Plattform-TURN-Dienst")).toBeInTheDocument()
    expect(screen.getByText("Bereit")).toBeInTheDocument()
    expect(screen.getByText("turn:turn.deltabox.dev:3478?transport=udp")).toBeInTheDocument()
    expect(screen.getByText("turn:turn.deltabox.dev:3478?transport=tcp")).toBeInTheDocument()
    expect(screen.queryByText("Connected")).not.toBeInTheDocument()
    expect(screen.queryByText(/does not place a test call/)).not.toBeInTheDocument()
  })

  it("localises Stack Users in German while preserving Matrix identifiers and create semantics", async () => {
    const user = userEvent.setup()
    let createRequest: Record<string, unknown> | null = null
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    registerStackHandlers({
      onCreateUser: (request) => {
        createRequest = request
      },
    })

    renderUsersPage()

    expect(await screen.findByText("Matrix-Benutzer")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Benutzer" })).toHaveAttribute(
      "href",
      "/stacks/demo-stack/users",
    )
    expect(screen.getByRole("link", { name: "Benutzer" })).toHaveAttribute("aria-current", "page")
    expect(screen.getByText("Benutzerinventar synchronisiert")).toBeInTheDocument()
    expect(screen.getAllByText("Aktiv")).not.toHaveLength(0)
    expect(screen.getByText("nigel")).toBeInTheDocument()
    expect(screen.getByText("@nigel:matrix-demo-stack.deltabox.dev")).toBeInTheDocument()
    expect(screen.queryByText("Matrix users")).not.toBeInTheDocument()

    await user.type(screen.getByLabelText("Benutzername"), "Neues Mitglied")
    expect(screen.getByText("Wird erstellt als: neuesmitglied")).toBeInTheDocument()
    await user.type(screen.getByLabelText("Passwort"), "correct-horse-battery-staple")
    await user.click(screen.getByRole("button", { name: "Matrix-Benutzer erstellen" }))

    await waitFor(() =>
      expect(createRequest).toEqual({
        username: "neuesmitglied",
        password: "correct-horse-battery-staple",
        isAdmin: false,
        displayName: null,
        email: null,
      }),
    )
  })

  it("localises Stack Diagnostics in German while preserving Doctor evidence", async () => {
    const user = userEvent.setup()
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    registerStackHandlers()

    renderDiagnosticsPage()

    expect(await screen.findByText("Stack-Diagnose")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Diagnose" })).toHaveAttribute(
      "href",
      "/stacks/demo-stack/diagnostics",
    )
    expect(screen.getByRole("link", { name: "Diagnose" })).toHaveAttribute(
      "aria-current",
      "page",
    )
    expect(
      screen.getByText(
        "Es wurde noch kein persistierter Diagnosebericht aufgezeichnet. Führen Sie die Diagnose aus, um einen neuen Bereitschaftsbericht zu erstellen.",
      ),
    ).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Diagnose ausführen" }))

    expect(await screen.findByText("Aktueller Diagnosebericht")).toBeInTheDocument()
    expect(screen.getByText("Die Diagnose hat alle gemeldeten Prüfungen bestanden.")).toBeInTheDocument()
    expect(screen.getByText("Prüfergebnisse")).toBeInTheDocument()
    expect(screen.getAllByText("Endpunkt")).not.toHaveLength(0)
    expect(screen.getAllByText("HTTP-Ergebnis")).not.toHaveLength(0)
    expect(screen.getByText("Matrix public route")).toBeInTheDocument()
    expect(screen.getByText("matrix-public")).toBeInTheDocument()
    expect(screen.getByText("HTTP 200")).toBeInTheDocument()
    expect(screen.getByText("Matrix client endpoint responded.")).toBeInTheDocument()
    expect(screen.getByText("doctor-operation-1")).toBeInTheDocument()
    expect(screen.getByText("doctor-report-1")).toBeInTheDocument()
    expect(screen.queryByText("Stack diagnostics")).not.toBeInTheDocument()
  })

  it("localises Stack Recovery in German while preserving catalog and restore identifiers", async () => {
    let requestedTarget: string | null = null
    window.localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    registerStackHandlers({
      onRestoreTarget: (targetStack) => {
        requestedTarget = targetStack
      },
    })

    renderRecoveryPage()

    expect(await screen.findAllByText("Wiederherstellungsquellen")).not.toHaveLength(0)
    expect(screen.getByRole("link", { name: "Wiederherstellung" })).toHaveAttribute(
      "href",
      "/stacks/demo-stack/recovery",
    )
    expect(screen.getByRole("link", { name: "Wiederherstellung" })).toHaveAttribute("aria-current", "page")
    expect(screen.queryByRole("link", { name: "Sicherungen" })).not.toBeInTheDocument()
    expect(screen.getByText("Bereit zur Wiederherstellung")).toBeInTheDocument()
    expect(screen.getByText("Neueste Wiederherstellungsquelle")).toBeInTheDocument()
    expect(screen.getByText("Zielgerichtete Wiederherstellungssitzungen")).toBeInTheDocument()
    expect(await screen.findAllByText("bkp_demo_stack_20260702")).not.toHaveLength(0)
    expect(screen.getByText("Earlier demo-stack backup")).toBeInTheDocument()
    expect(screen.queryByText("Other stack backup")).not.toBeInTheDocument()
    expect(screen.getAllByRole("link", { name: "Katalogeintrag öffnen" })[0]).toHaveAttribute(
      "href",
      "/backups/catalog/catalog-demo-local",
    )
    expect(screen.getByText("Wiederherstellungssitzungen mit diesem Stack als Ziel")).toBeInTheDocument()
    expect(await screen.findByText("restore-demo-target")).toBeInTheDocument()
    expect(screen.getAllByText("Öffentliche Überprüfung")).not.toHaveLength(0)
    expect(screen.getByText("Abgeschlossen")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Arbeitsbereich öffnen" })).toHaveAttribute(
      "href",
      "/restores/restore-demo-target",
    )
    expect(screen.getByRole("link", { name: "Alle Wiederherstellungssitzungen öffnen" })).toHaveAttribute(
      "href",
      "/restores?target=demo-stack",
    )
    await waitFor(() => expect(requestedTarget).toBe("demo-stack"))
    expect(screen.queryByText("Restore sessions targeting this stack")).not.toBeInTheDocument()
    expect(screen.queryByText("Open workspace")).not.toBeInTheDocument()
  })

  it("uses compact underlined navigation and does not offer redundant Settings", async () => {
    registerStackHandlers()

    renderPage()

    await screen.findByText("Runtime overview")

    const navigation = screen.getByRole("navigation", { name: "Stack workspace sections" })
    const links = within(navigation).getAllByRole("link")

    expect(links.map((link) => link.textContent)).toEqual([
      "Overview",
      "Services",
      "Users",
      "Recovery",
      "Federation",
      "Diagnostics",
    ])
    expect(within(navigation).queryByRole("link", { name: "Settings" })).not.toBeInTheDocument()
    expect(navigation).toHaveClass("border-b")
    expect(navigation).not.toHaveClass("overflow-x-auto")
    expect(navigation).not.toHaveClass("bg-muted/15")

    const overview = within(navigation).getByRole("link", { name: "Overview" })
    expect(overview).toHaveAttribute("aria-current", "page")
    expect(overview).toHaveClass("border-primary", "text-primary")
    expect(overview).not.toHaveClass("bg-background")
  })

  it("redirects legacy Settings bookmarks to Stack Overview", async () => {
    renderRetiredSettingsRedirect()

    expect(await screen.findByText("Stack overview destination")).toBeInTheDocument()
    expect(screen.queryByText("Stack settings")).not.toBeInTheDocument()
  })

  it("redirects retired stack evidence routes to their canonical sections", async () => {
    renderRetiredStackSectionRedirect("/stacks/demo-stack/network", "services")
    expect(await screen.findByText("Stack services destination")).toBeInTheDocument()

    renderRetiredStackSectionRedirect("/stacks/demo-stack/voice-video", "services")
    expect(await screen.findAllByText("Stack services destination")).not.toHaveLength(0)

    renderRetiredStackSectionRedirect("/stacks/demo-stack/storage-media", "services")
    expect(await screen.findAllByText("Stack services destination")).not.toHaveLength(0)

    renderRetiredStackSectionRedirect("/stacks/demo-stack/backups", "recovery")
    expect(await screen.findByText("Stack recovery destination")).toBeInTheDocument()
  })

  it("projects catalog recovery sources and target restore sessions at the dedicated stack route", async () => {
    let requestedTarget: string | null = null
    registerStackHandlers({
      onRestoreTarget: (targetStack) => {
        requestedTarget = targetStack
      },
    })

    renderRecoveryPage()

    expect(await screen.findAllByText("Recovery sources")).not.toHaveLength(0)
    expect(screen.getByRole("link", { name: "Recovery" })).toHaveAttribute(
      "href",
      "/stacks/demo-stack/recovery",
    )
    expect(screen.getByRole("link", { name: "Recovery" })).toHaveAttribute("aria-current", "page")
    expect(screen.queryByRole("link", { name: "Backups" })).not.toBeInTheDocument()
    expect(await screen.findAllByText("bkp_demo_stack_20260702")).not.toHaveLength(0)
    expect(await screen.findByText("Earlier demo-stack backup")).toBeInTheDocument()
    expect(screen.queryByText("Other stack backup")).not.toBeInTheDocument()
    expect(screen.getAllByRole("link", { name: "Open catalog entry" })[0]).toHaveAttribute(
      "href",
      "/backups/catalog/catalog-demo-local",
    )
    expect(screen.getByText("Restore sessions targeting this stack")).toBeInTheDocument()
    expect(await screen.findByText("restore-demo-target")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Open workspace" })).toHaveAttribute(
      "href",
      "/restores/restore-demo-target",
    )
    expect(screen.getByRole("link", { name: "Open all restore sessions" })).toHaveAttribute(
      "href",
      "/restores?target=demo-stack",
    )
    await waitFor(() => expect(requestedTarget).toBe("demo-stack"))
    expect(screen.queryByText("Technical details")).not.toBeInTheDocument()
  })

  it("keeps Stack Recovery honest when no catalogued source or target restore exists", async () => {
    registerStackHandlers()
    server.use(
      http.get(catalogBase, () => HttpResponse.json({ totalCount: 0, entries: [] })),
      http.get(restoresBase, () =>
        HttpResponse.json({
          ...restoreSessions,
          summary: {
            totalSessions: 0,
            productionRecreateCount: 0,
            publiclyVerifiedCount: 0,
            needsActionCount: 0,
          },
          totalSessions: 0,
          sessions: [],
          targetStacks: [],
        }),
      ),
    )

    renderRecoveryPage()

    expect(await screen.findAllByText("Recovery sources")).not.toHaveLength(0)
    expect(await screen.findByText("No catalogued recovery sources belong to this stack yet. Create a backup to capture a new local recovery source.")).toBeInTheDocument()
    expect(await screen.findByText("No restore session currently targets this stack.")).toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "Open workspace" })).not.toBeInTheDocument()
  })

  it("recovers the latest persisted Doctor report after a fresh browser render", async () => {
    registerStackHandlers()
    server.use(
      http.get(`${stackBase}/doctor/latest`, () =>
        HttpResponse.json({
          source: "control-plane",
          status: "ok",
          stackId: stack.stackId,
          slug: stack.slug,
          report: {
            source: "control-plane",
            status: "passed",
            stackId: stack.stackId,
            slug: stack.slug,
            lastVerifiedStatus: "passed",
            lastVerifiedAtUtc: "2026-08-25T01:08:00Z",
            checkedAtUtc: "2026-08-25T01:08:00Z",
            allPassed: true,
            checks: [
              {
                code: "persisted-doctor-check",
                name: "Persisted Doctor check",
                url: stack.matrix.publicBaseUrl,
                success: true,
                statusCode: 200,
                detail: "Recovered from durable readiness evidence.",
                bodyPreview: null,
              },
            ],
            detail: "Runtime readiness checks passed.",
            operationId: "persisted-doctor-operation",
            reportId: "persisted-doctor-report",
          },
          detail: null,
        })),
    )

    renderDiagnosticsPage()

    expect(await screen.findByText("Current doctor report")).toBeInTheDocument()
    expect(screen.getByText("Persisted Doctor check")).toBeInTheDocument()
    expect(screen.getByText("persisted-doctor-operation")).toBeInTheDocument()
    expect(screen.getByText("persisted-doctor-report")).toBeInTheDocument()
    expect(screen.queryByText(/browser session/i)).not.toBeInTheDocument()
  })

  it("lists previous completed Doctor reports and opens persisted historical evidence", async () => {
    const user = userEvent.setup()
    const historicalReportId = "11111111-1111-1111-1111-111111111111"
    const historicalOperationId = "22222222-2222-2222-2222-222222222222"
    let historyPageRequested = 0

    registerStackHandlers()
    server.use(
      http.get(`${stackBase}/doctor/latest`, () =>
        HttpResponse.json({
          source: "control-plane",
          status: "ok",
          stackId: stack.stackId,
          slug: stack.slug,
          report: {
            source: "control-plane",
            status: "passed",
            stackId: stack.stackId,
            slug: stack.slug,
            lastVerifiedStatus: "passed",
            lastVerifiedAtUtc: "2026-08-26T07:47:00Z",
            checkedAtUtc: "2026-08-26T07:47:00Z",
            allPassed: true,
            checks: [],
            detail: "Runtime readiness checks passed.",
            operationId: "33333333-3333-3333-3333-333333333333",
            reportId: "44444444-4444-4444-4444-444444444444",
          },
          detail: null,
        })),
      http.get(`${stackBase}/doctor/history`, ({ request }) => {
        const url = new URL(request.url)
        historyPageRequested = Number(url.searchParams.get("page"))

        if (historyPageRequested === 2) {
          return HttpResponse.json({
            source: "control-plane",
            status: "ok",
            stackId: stack.stackId,
            slug: stack.slug,
            reports: [
              {
                reportId: "55555555-5555-5555-5555-555555555555",
                operationId: "66666666-6666-6666-6666-666666666666",
                status: "passed",
                allPassed: true,
                checkedAtUtc: "2026-08-20T07:00:00Z",
                checkCount: 7,
                failedCheckCount: 0,
                warningCount: 0,
                detail: "Runtime readiness checks passed.",
              },
            ],
            totalCount: 11,
            page: 2,
            pageSize: 10,
            totalPages: 2,
            hasPreviousPage: true,
            hasNextPage: false,
            detail: null,
          })
        }

        return HttpResponse.json({
          source: "control-plane",
          status: "ok",
          stackId: stack.stackId,
          slug: stack.slug,
          reports: [
            {
              reportId: historicalReportId,
              operationId: historicalOperationId,
              status: "failed",
              allPassed: false,
              checkedAtUtc: "2026-08-26T07:41:00Z",
              checkCount: 7,
              failedCheckCount: 2,
              warningCount: 0,
              detail: "Runtime readiness checks failed. Failed checks: 2.",
            },
          ],
          totalCount: 11,
          page: 1,
          pageSize: 10,
          totalPages: 2,
          hasPreviousPage: false,
          hasNextPage: true,
          detail: null,
        })
      }),
      http.get(`${stackBase}/doctor/reports/${historicalReportId}`, () =>
        HttpResponse.json({
          source: "control-plane",
          status: "failed",
          stackId: stack.stackId,
          slug: stack.slug,
          lastVerifiedStatus: "failed",
          lastVerifiedAtUtc: "2026-08-26T07:41:00Z",
          checkedAtUtc: "2026-08-26T07:41:00Z",
          allPassed: false,
          checks: [
            {
              code: "historical.matrix.public",
              name: "Historical Matrix public check",
              url: stack.matrix.publicBaseUrl,
              success: false,
              statusCode: 502,
              detail: "Matrix was unavailable during this completed Doctor run.",
              bodyPreview: null,
            },
          ],
          detail: "Runtime readiness checks failed. Failed checks: 2.",
          operationId: historicalOperationId,
          reportId: historicalReportId,
        })),
    )

    renderDiagnosticsPage()

    expect(await screen.findByText("Current doctor report")).toBeInTheDocument()
    expect(await screen.findByText("Previous doctor reports")).toBeInTheDocument()
    expect(screen.getByText(historicalReportId)).toBeInTheDocument()
    expect(screen.getByText(historicalOperationId)).toBeInTheDocument()
    expect(screen.getByText("7 checks")).toBeInTheDocument()
    expect(screen.getByText("2 failed")).toBeInTheDocument()
    expect(screen.getByText("Page 1 of 2")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "View report" }))

    expect(await screen.findByText("Historical doctor report")).toBeInTheDocument()
    expect(screen.getByText("Historical Matrix public check")).toBeInTheDocument()
    expect(screen.getByText("HTTP 502")).toBeInTheDocument()
    expect(screen.getByText("Matrix was unavailable during this completed Doctor run.")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Next" }))
    await waitFor(() => expect(historyPageRequested).toBe(2))
    expect(await screen.findByText("Page 2 of 2")).toBeInTheDocument()
    expect(screen.queryByText("Historical doctor report")).not.toBeInTheDocument()
  })

  it("opens Diagnostics and starts Doctor from the workspace header", async () => {
    const user = userEvent.setup()
    let doctorCalls = 0
    const doctorCompletion: { current: (() => void) | null } = { current: null }

    registerStackHandlers({
      onDoctor: () => {
        doctorCalls += 1

        return new Promise<void>((resolve) => {
          doctorCompletion.current = resolve
        })
      },
    })

    renderOverviewToDiagnosticsFlow()

    await screen.findByText("Runtime overview")
    await user.click(screen.getByRole("button", { name: "Run doctor" }))

    expect(await screen.findByText("Stack diagnostics")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Diagnostics" })).toHaveAttribute(
      "aria-current",
      "page",
    )
    await waitFor(() => expect(doctorCalls).toBe(1))
    expect(screen.getByText("Running doctor checks...")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Running..." })).toBeDisabled()

    const finishDoctor = doctorCompletion.current
    if (finishDoctor === null) {
      throw new Error("Doctor completion callback was not registered.")
    }

    finishDoctor()

    expect(await screen.findByText("Current doctor report")).toBeInTheDocument()
    expect(screen.getByText("Matrix public route")).toBeInTheDocument()
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Run doctor" })).toBeEnabled(),
    )
    expect(screen.queryByText("Running doctor checks...")).not.toBeInTheDocument()
    expect(doctorCalls).toBe(1)
  })

  it("labels the retained report as previous while a repeat Doctor run is pending", async () => {
    const user = userEvent.setup()
    let doctorCalls = 0
    const secondCompletion: { current: (() => void) | null } = { current: null }

    registerStackHandlers({
      onDoctor: () => {
        doctorCalls += 1
        if (doctorCalls === 1) return

        return new Promise<void>((resolve) => {
          secondCompletion.current = resolve
        })
      },
    })

    renderDiagnosticsPage()
    await screen.findByText("Stack diagnostics")

    await user.click(screen.getByRole("button", { name: "Run doctor" }))
    expect(await screen.findByText("Current doctor report")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Run doctor" }))
    await waitFor(() => expect(doctorCalls).toBe(2))

    expect(screen.getByText("Running doctor checks...")).toBeInTheDocument()
    expect(
      screen.getByText(
        "The current Doctor request is in progress. MEM retries unreachable services and stops the run after at most 2 minutes.",
      ),
    ).toBeInTheDocument()
    expect(screen.getByText("Previous doctor report")).toBeInTheDocument()
    expect(screen.queryByText("Current doctor report")).not.toBeInTheDocument()

    const finishDoctor = secondCompletion.current
    if (finishDoctor === null) {
      throw new Error("Second Doctor completion callback was not registered.")
    }

    finishDoctor()

    expect(await screen.findByText("Current doctor report")).toBeInTheDocument()
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Run doctor" })).toBeEnabled(),
    )
    expect(screen.queryByText("Running doctor checks...")).not.toBeInTheDocument()
  })

  it("stops the pending Doctor UI and shows bounded timeout guidance", async () => {
    const user = userEvent.setup()
    registerStackHandlers()
    server.use(
      http.post(`${stackBase}/doctor`, () =>
        HttpResponse.json(
          {
            source: "control-plane",
            status: "timeout",
            stackId: stack.stackId,
            slug: stack.slug,
            lastVerifiedStatus: stack.status,
            lastVerifiedAtUtc: stack.lastVerifiedAtUtc,
            checkedAtUtc: "2026-07-02T14:22:00Z",
            allPassed: false,
            checks: [],
            detail:
              "Doctor did not complete within 120 seconds. Check DNS, routing, NPM, and stack service availability, then run Doctor again.",
            operationId: "doctor-timeout-operation",
            reportId: null,
          },
          { status: 504 },
        ),
      ),
    )

    renderDiagnosticsPage()
    await screen.findByText("Stack diagnostics")
    await user.click(screen.getByRole("button", { name: "Run doctor" }))

    expect(
      await screen.findByText(
        "Doctor did not complete within 120 seconds. Check DNS, routing, NPM, and stack service availability, then run Doctor again.",
      ),
    ).toBeInTheDocument()
    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Run doctor" })).toBeEnabled(),
    )
    expect(screen.queryByText("Running doctor checks...")).not.toBeInTheDocument()
  })

  it("runs and projects the current Doctor report at the dedicated stack route", async () => {
    const user = userEvent.setup()
    let doctorCalls = 0
    registerStackHandlers({
      onDoctor: () => {
        doctorCalls += 1
      },
    })

    renderDiagnosticsPage()

    expect(await screen.findByText("Stack diagnostics")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Diagnostics" })).toHaveAttribute(
      "href",
      "/stacks/demo-stack/diagnostics",
    )
    expect(screen.getByRole("link", { name: "Diagnostics" })).toHaveAttribute(
      "aria-current",
      "page",
    )
    expect(
      screen.getByText(
        "No persisted Doctor report has been recorded yet. Run Doctor to create a new readiness report.",
      ),
    ).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Run doctor" }))

    await waitFor(() => expect(doctorCalls).toBe(1))
    expect(await screen.findByText("Current doctor report")).toBeInTheDocument()
    expect(screen.getByText("Matrix public route")).toBeInTheDocument()
    expect(screen.getByText("matrix-public")).toBeInTheDocument()
    expect(screen.getByText("HTTP 200")).toBeInTheDocument()
    expect(screen.getByText("Matrix client endpoint responded.")).toBeInTheDocument()
    expect(screen.getByText("doctor-operation-1")).toBeInTheDocument()
    expect(screen.getByText("doctor-report-1")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Diagnostics" })).toHaveAttribute(
      "aria-current",
      "page",
    )
    expect(screen.queryByText("Technical details")).not.toBeInTheDocument()
  })

  it("keeps the latest Doctor result available when returning to Overview", async () => {
    const user = userEvent.setup()
    registerStackHandlers()

    renderDiagnosticsToOverviewFlow()

    await screen.findByText("Stack diagnostics")
    await user.click(screen.getByRole("button", { name: "Run doctor" }))
    await screen.findByText("Matrix public route")

    await user.click(screen.getByRole("link", { name: "Overview" }))

    expect(await screen.findAllByText("Doctor passed")).not.toHaveLength(0)
    expect(screen.getByText("Latest doctor result")).toBeInTheDocument()
  })

  it("keeps failed Doctor evidence visible without inventing a repair action", async () => {
    const user = userEvent.setup()
    registerStackHandlers()
    server.use(
      http.post(`${stackBase}/doctor`, () =>
        HttpResponse.json({
          source: "host-agent",
          status: "failed",
          stackId: stack.stackId,
          slug: stack.slug,
          lastVerifiedStatus: "Public Routes Verified",
          lastVerifiedAtUtc: "2026-07-02T14:20:00Z",
          checkedAtUtc: "2026-07-02T14:30:00Z",
          allPassed: false,
          checks: [
            {
              code: "element-public",
              name: "Element public route",
              url: stack.element.publicBaseUrl,
              success: false,
              statusCode: 502,
              detail: "The configured Element upstream did not respond.",
              bodyPreview: "upstream refused connection",
            },
          ],
          detail: "One or more checks failed.",
          operationId: "doctor-operation-2",
          reportId: "doctor-report-2",
        }),
      ),
    )

    renderDiagnosticsPage()

    await screen.findByText("Stack diagnostics")
    await user.click(screen.getByRole("button", { name: "Run doctor" }))

    expect(await screen.findByText("Doctor reported one or more failed checks.")).toBeInTheDocument()
    expect(screen.getByText("Element public route")).toBeInTheDocument()
    expect(screen.getByText("HTTP 502")).toBeInTheDocument()
    expect(screen.getByText("The configured Element upstream did not respond.")).toBeInTheDocument()
    await user.click(screen.getByText("Response preview"))
    expect(screen.getByText("upstream refused connection")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: /repair|restart|reconfigure/i })).not.toBeInTheDocument()
  })

  it("projects Matrix homeserver users at their dedicated stack route", async () => {
    registerStackHandlers()

    renderUsersPage()

    expect(await screen.findByText("Matrix users")).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Users" })).toHaveAttribute(
      "href",
      "/stacks/demo-stack/users",
    )
    expect(screen.getByRole("link", { name: "Users" })).toHaveAttribute("aria-current", "page")
    expect(screen.getByText("Local Matrix accounts reported by this stack's Synapse database. These are separate from MEM control-plane users.")).toBeInTheDocument()
    expect(screen.getByText("nigel")).toBeInTheDocument()
    expect(screen.getByText("@nigel:matrix-demo-stack.deltabox.dev")).toBeInTheDocument()
    expect(screen.getByText("User inventory synchronized")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Create Matrix user" })).toBeInTheDocument()
    expect(screen.queryByText("Technical details")).not.toBeInTheDocument()
  })

  it("preserves the existing Matrix user creation command", async () => {
    const user = userEvent.setup()
    let createRequest: Record<string, unknown> | null = null
    registerStackHandlers({
      onCreateUser: (request) => {
        createRequest = request
      },
    })

    renderUsersPage()

    await screen.findByText("Matrix users")
    await user.type(screen.getByLabelText("Username"), "New Member")
    await user.type(screen.getByLabelText("Password"), "correct-horse-battery-staple")
    await user.click(screen.getByRole("button", { name: "Create Matrix user" }))

    await waitFor(() =>
      expect(createRequest).toEqual({
        username: "newmember",
        password: "correct-horse-battery-staple",
        isAdmin: false,
        displayName: null,
        email: null,
      }),
    )
  })

  it("preserves first-admin creation and forces the Matrix admin role", async () => {
    const user = userEvent.setup()
    let createRequest: Record<string, unknown> | null = null
    registerStackHandlers({
      onCreateFirstAdmin: (request) => {
        createRequest = request
      },
    })
    server.use(http.get(usersBase, () => HttpResponse.json(firstAdminRequired)))

    renderUsersPage()

    expect(await screen.findByText("First Matrix admin required")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Create first Matrix admin" })).toBeInTheDocument()
    await user.type(screen.getByLabelText("Username"), "First Admin")
    await user.type(screen.getByLabelText("Password"), "correct-horse-battery-staple")
    await user.click(screen.getByRole("button", { name: "Create first Matrix admin" }))

    await waitFor(() =>
      expect(createRequest).toEqual({
        username: "firstadmin",
        password: "correct-horse-battery-staple",
        isAdmin: true,
        displayName: null,
        email: null,
      }),
    )
  })

  it("does not expose Matrix user creation while the inventory is unavailable", async () => {
    registerStackHandlers()
    server.use(
      http.get(usersBase, () =>
        HttpResponse.json({ error: "Matrix user inventory is unavailable." }, { status: 503 }),
      ),
    )

    renderUsersPage()

    expect(await screen.findByText("Could not load Matrix users")).toBeInTheDocument()
    expect(
      screen.getByText(
        "Matrix user inventory is unavailable. User creation is disabled until the current state can be loaded.",
      ),
    ).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: /Create .*Matrix/ })).not.toBeInTheDocument()
    expect(screen.getByText("User inventory unavailable")).toBeInTheDocument()
  })

  it("keeps the Stack Services route honest when a runtime manifest does not expose a service", async () => {
    registerStackHandlers()
    server.use(
      http.get(stackBase, () =>
        HttpResponse.json({
          ...stack,
          matrix: null,
          element: null,
        }),
      ),
    )

    renderServicesPage()

    expect(await screen.findByText("Stack services")).toBeInTheDocument()
    expect(screen.getByText("Matrix homeserver")).toBeInTheDocument()
    expect(screen.getByText("Element web client")).toBeInTheDocument()
    expect(screen.getAllByText("This service is not present in the runtime manifest.")).toHaveLength(2)
    expect(screen.queryByRole("link", { name: /Open / })).not.toBeInTheDocument()
  })

  it("does not expose unsafe or malformed service launch URLs in the workspace header", async () => {
    registerStackHandlers()
    server.use(
      http.get(stackBase, () =>
        HttpResponse.json({
          ...stack,
          matrix: {
            ...stack.matrix,
            publicBaseUrl: "javascript:alert('matrix')",
          },
          element: {
            ...stack.element,
            publicBaseUrl: "not-a-url",
          },
        }),
      ),
    )

    renderPage()

    expect(await screen.findByRole("heading", { name: "Dewar Family Chat" })).toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "Open Element" })).not.toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "Matrix API" })).not.toBeInTheDocument()
  })

  it("preserves the existing backup command from the workspace header", async () => {
    const user = userEvent.setup()
    let backupCalls = 0
    registerStackHandlers({
      onBackup: () => {
        backupCalls += 1
      },
    })

    renderPage()

    await screen.findByRole("heading", { name: "Dewar Family Chat" })

    await user.click(screen.getByRole("button", { name: "Create backup" }))
    await waitFor(() => expect(backupCalls).toBe(1))
    expect(await screen.findByText("Backup created")).toBeInTheDocument()
  })
  it("renders a bounded not-found state for an unknown stack without exposing internal request details", async () => {
    const unknownSlug = "restore-staging-synapse-20260917-abcd1234"

    server.use(
      http.get(`/internal/host-agent/runtime-stacks/${unknownSlug}`, () =>
        HttpResponse.json(
          {
            source: "control-plane",
            status: "not_found",
            stackId: null,
            slug: unknownSlug,
            matrix: null,
            element: null,
            lastVerifiedAtUtc: null,
            detail: `Runtime stack '${unknownSlug}' was not found in the local control-plane manifest store.`,
          },
          { status: 404 },
        ),
      ),
    )

    renderPage(`/stacks/${unknownSlug}`)

    expect(
      await screen.findByRole("heading", { name: "Chat server not found" }),
    ).toBeInTheDocument()
    expect(
      screen.getByText(
        "This address does not match a managed chat server in MEM. It may be stale or refer to temporary restore staging.",
      ),
    ).toBeInTheDocument()
    expect(screen.getByRole("link", { name: "Back to chat servers" })).toHaveAttribute(
      "href",
      "/stacks",
    )
    expect(screen.queryByText(/internal\/host-agent/)).not.toBeInTheDocument()
    expect(screen.queryByText(/local control-plane manifest store/i)).not.toBeInTheDocument()
  })

})
