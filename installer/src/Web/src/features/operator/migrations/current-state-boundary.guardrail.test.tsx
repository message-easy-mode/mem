import { HttpResponse, http } from "msw"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { router } from "@/app/router"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import { BackupCatalogEntryRouteBoundary } from "@/features/operator/backups/pages/backups/catalog"
import { MigrationIntakesPage } from "@/features/operator/migrations/pages/migration-intakes-page"
import { MigrationSessionPage } from "@/features/operator/migrations/pages/migration-session-page"
import { guidedDetail, withGuidedSnapshot } from "./test/migration-guided-fixtures"
import type { MigrationSessionDetail } from "./api/migration-sessions"
import type { MigrationWorkspaceResponse } from "@/features/operator/migrations/api/migration-workspace"

const catalogDetail = {
  catalogEntryId: "catalog-current-state",
  originKind: "imported-zip",
  displayName: "Historical migration proof artifact",
  sourceStackSlug: "legacy-stack",
  sourceBackupId: "legacy-backup",
  validationId: "validation-current-state",
  manifestVersion: 1,
  memVersion: "0.1.1-dev",
  matrixServerName: "matrix.example.test",
  matrixHost: "matrix.example.test",
  elementHost: "chat.example.test",
  capturedAtUtc: "2026-07-13T00:00:00Z",
  payloadState: "available",
  integrityStatus: "valid",
  integritySummary: "Payload checks passed.",
  warningCount: 0,
  advisoryCount: 0,
  payloadBytes: 1024,
  createdAtUtc: "2026-07-13T00:00:00Z",
  importedAtUtc: "2026-07-13T00:01:00Z",
  materialisedAtUtc: "2026-07-13T00:02:00Z",
  payloadRemovedAtUtc: null,
  payloadRemovedBy: null,
}

function lifecycleInspection(migrationId: string) {
  return {
    migrationId,
    lifecycleStatus: "active",
    archived: false,
    archivedAtUtc: null,
    archivedBy: null,
    closedAtUtc: null,
    closureKind: null,
    stateVersion: 1,
    currentOperation: "none",
    packageState: "retained",
    candidateArtifactState: "none",
    privateStagingRuntimeState: "none",
    productionRuntimeState: "none",
    publicRoutesState: "none",
    sourceState: "external",
    capabilities: {
      canArchive: false,
      canUnarchive: false,
      canCancel: false,
      canDelete: false,
      cancelBlockedCode: "migration_session_historical_compatibility",
      cancelBlockedReason: "Historical compatibility state is read-only.",
      deleteBlockedCode: "migration_session_active",
      deleteBlockedReason: "Permanent deletion is delivered in the next lifecycle slice.",
    },
    sourceUnaffectedNotice:
      "This changes only target-side Migration Session state and files. Source-side files and resources are unaffected.",
  }
}

const historicalMigration = {
  session: {
    migrationId: "migration-current-state",
    displayName: "Historical migration",
    sourceAdapter: "mem-v010",
    sourceDisplay: "Message Easy Mode 0.1.0",
    phase: "cutover",
    status: "legacy-contract-accepted",
    nextAction: "prepare-cutover",
    createdAtUtc: "2026-07-13T00:00:00Z",
    updatedAtUtc: "2026-07-14T00:00:00Z",
    blockerCount: 0,
    warningCount: 0,
    advisoryCount: 0,
    needsAttention: false,
    sourceCount: 1,
    stackCount: 1,
    historicalCompatibility: {
      usesLegacyNeutralImportContract: true,
      legacyContractVersion: "mem-migration-import/v1",
      legacyContractStatus: "accepted",
      legacyManifestSha256: "a".repeat(64),
      usesCatalogRestorePath: true,
      catalogEntryCount: 1,
      restoreSessionCount: 1,
    },
  },
  package: null,
  sources: [],
  findings: [],
  linkedObjects: [
    {
      kind: "backup-catalog-entry",
      id: "catalog-current-state",
      displayName: "Historical migration proof artifact",
      status: "available",
      relationship: "historical-migration-proof-artifact",
      historical: true,
    },
    {
      kind: "restore-session",
      id: "restore-current-state",
      displayName: "Historical restore session",
      status: "completed",
      relationship: "historical-restore-session",
      historical: true,
    },
  ],
}

describe("MIG-WORKSPACE current Web boundary", () => {
  it("has dedicated overview, intake, and details routes", () => {
    const authenticatedRoot = router.routes.find((route) => route.path === "/")
    const childPaths = authenticatedRoot?.children?.map((route) => route.path)

    expect(childPaths).toContain("migrations")
    expect(childPaths).toContain("migrations/new")
    expect(childPaths).toContain("migrations/:migrationId")
  })

  it("removes public cutover controls from Backup Details", async () => {
    server.use(
      http.get(
        "/internal/host-agent/backups/catalog/catalog-current-state",
        () => HttpResponse.json(catalogDetail),
      ),
      http.get(
        "/internal/host-agent/backups/catalog/catalog-current-state/lifecycle",
        () => HttpResponse.json({
          catalogEntryId: "catalog-current-state",
          payloadState: "available",
          payloadPresent: true,
          hasActiveRestore: false,
          activeRestoreSessionId: null,
          canDelete: true,
          deleteBlockReason: null,
          originalArchive: null,
        }),
      ),
    )

    renderWithProviders(
      <MemoryRouter initialEntries={["/backups/catalog/catalog-current-state"]}>
        <Routes>
          <Route
            path="/backups/catalog/:catalogEntryId"
            element={<BackupCatalogEntryRouteBoundary />}
          />
        </Routes>
      </MemoryRouter>,
    )

    expect(
      await screen.findByRole("heading", {
        name: "Historical migration proof artifact",
      }),
    ).toBeInTheDocument()
    expect(screen.queryByText("Public cutover preparation")).not.toBeInTheDocument()
    expect(
      screen.queryByRole("button", { name: "Create private candidate" }),
    ).not.toBeInTheDocument()
  })

  it("keeps the catalog-keyed compatibility cutover workflow under Advanced", async () => {
    const user = userEvent.setup()
    server.use(
      http.get(
        "/api/operator/migrations/sessions/migration-current-state",
        () => HttpResponse.json(historicalMigration),
      ),
      http.get(
        "/api/operator/migrations/sessions/migration-current-state/lifecycle",
        () => HttpResponse.json(lifecycleInspection("migration-current-state")),
      ),
      http.get(
        "/api/operator/migrations/sessions/migration-current-state/workspace",
        () => HttpResponse.json(
          boundaryWorkspace(
            "migration-current-state",
            "Historical migration",
            "create-and-upload-package",
            "action-required",
            "migration.workspace.historical-compatibility",
            "open-technical-details",
          ),
        ),
      ),
    )

    renderWithProviders(
      <MemoryRouter initialEntries={["/migrations/migration-current-state"]}>
        <Routes>
          <Route path="/migrations/:migrationId" element={<MigrationSessionPage />} />
        </Routes>
      </MemoryRouter>,
    )

    expect(
      await screen.findByRole("heading", { name: "Historical migration" }),
    ).toBeInTheDocument()
    expect(screen.queryByText("Public cutover preparation")).not.toBeInTheDocument()

    await user.click(screen.getByRole("tab", { name: "Advanced" }))

    expect(screen.getByText("Public cutover preparation")).toBeInTheDocument()
    expect(
      screen.getByRole("button", { name: "Create private candidate" }),
    ).toBeEnabled()
  })

  it("uses private server creation in the guided path and retires the duplicate cutover workspace", async () => {
    const currentMigration = {
      ...historicalMigration,
      session: {
        ...historicalMigration.session,
        migrationId: "migration-new-boundary",
        displayName: "Current migration",
        phase: "staging",
        status: "staging-verified",
        nextAction: "review-private-staging",
        historicalCompatibility: {
          usesLegacyNeutralImportContract: false,
          legacyContractVersion: null,
          legacyContractStatus: null,
          legacyManifestSha256: null,
          usesCatalogRestorePath: false,
          catalogEntryCount: 0,
          restoreSessionCount: 0,
        },
      },
      packageRevisions: [{
        packageRevisionId: "mpr-preview",
        revisionNumber: 1,
        purpose: "preview",
        status: "package-validated",
        retentionState: "active",
        active: true,
        transferMode: "encrypted",
        fileName: "preview.memmigration.zip.age",
        sizeBytes: 4096,
        encryptedSha256: "2".repeat(64),
        decryptedSha256: "3".repeat(64),
        createdAtUtc: "2026-07-13T00:00:00Z",
        uploadedAtUtc: "2026-07-13T00:05:00Z",
        validatedAtUtc: "2026-07-13T00:06:00Z",
        expiresAtUtc: null,
        supersededAtUtc: null,
        retiredAtUtc: null,
        ageRecipient: null,
        recipientFingerprint: null,
        archiveMigrationId: "capture-1",
        archiveSourceProduct: "MatrixEasyMode",
        archiveSourceVersion: "0.1.0",
        archiveStackCount: 1,
        captureKind: "preview",
        sourceFrozen: false,
        rehearsalOnly: true,
        verifiedFileCount: 10,
        verifiedExpandedBytes: 4096,
        validationCode: "validated",
        validationSummary: "Validated.",
      }],
      linkedObjects: [],
    }

    let targetReviewRequest: unknown = null
    server.use(
      http.get(
        "/api/operator/migrations/sessions/migration-new-boundary",
        () => HttpResponse.json(currentMigration),
      ),
      http.get(
        "/api/operator/migrations/sessions/migration-new-boundary/lifecycle",
        () => HttpResponse.json(lifecycleInspection("migration-new-boundary")),
      ),
      http.get(
        "/api/operator/migrations/sessions/migration-new-boundary/workspace",
        () => HttpResponse.json(
          boundaryWorkspace(
            "migration-new-boundary",
            "Current migration",
            "create-new-server",
            "ready",
            "migration.workspace.prepare-new-server",
            "prepare-new-server",
            currentMigration,
          ),
        ),
      ),
      http.post(
        "/api/operator/migrations/sessions/migration-new-boundary/production-adoption/private-server/review",
        async ({ request }) => {
          targetReviewRequest = await request.json()
          return HttpResponse.json({
            migrationId: "migration-new-boundary",
            sourceStackSlug: "stack",
            sourceElementPublicHost: "chat.example.test",
            targetStackSlug: "stack",
            matrixServerName: "matrix.example.test",
            elementPublicHost: "chat.example.test",
            stackNameStatus: "available",
            matrixAddressStatus: "locked-available",
            elementAddressStatus: "available",
            collisionFree: true,
            suggestedTargetStackSlug: null,
            collisions: [],
            detail: "The target identity is available.",
          })
        },
      ),
      http.get(
        "/api/operator/migrations/sessions/migration-new-boundary/staging-runs",
        () => HttpResponse.json([]),
      ),
      http.get(
        "/api/operator/migrations/sessions/migration-new-boundary/production-authority",
        () => HttpResponse.json({
          status: "active",
          migrationId: "migration-new-boundary",
          authorizesProduction: true,
          detail: "Authorized.",
          authority: {
            productionAuthorityId: "mpa-1",
            authorityType: "operator-attested-snapshot",
            status: "active",
            packageRevisionId: "mpr-preview",
            candidateArtifactId: "mca-1",
            stagingRunId: "mstg-1",
            encryptedPackageSha256: "2".repeat(64),
            decryptedArchiveSha256: "3".repeat(64),
            sourceMigrationId: "capture-1",
            sourceFingerprint: "4".repeat(64),
            matrixServerName: "matrix.example.test",
            signingKeyIdentitySha256: "5".repeat(64),
            captureKind: "preview",
            sourceFrozen: false,
            rehearsalOnly: true,
            capturedAtUtc: "2026-07-13T00:00:00Z",
            usersCount: 3,
            roomsCount: 2,
            eventsCount: 23,
            evidenceSchemaVersion: "migration-production-authority-v1",
            evidenceSha256: "6".repeat(64),
            acknowledgementsSchemaVersion: "operator-attested-snapshot-v1",
            acknowledgementsSha256: "7".repeat(64),
            createdAtUtc: "2026-07-13T01:00:00Z",
          },
        }),
      ),
      http.get(
        "/api/operator/migrations/sessions/migration-new-boundary/production-adoption",
        () => HttpResponse.json({
          source: "control-plane",
          status: "not-prepared",
          migrationId: "migration-new-boundary",
          planPrepared: false,
          plan: null,
          detail: "No production adoption plan has been prepared.",
        }),
      ),
    )

    renderWithProviders(
      <MemoryRouter initialEntries={["/migrations/migration-new-boundary"]}>
        <Routes>
          <Route path="/migrations/:migrationId" element={<MigrationSessionPage />} />
        </Routes>
      </MemoryRouter>,
    )

    expect(await screen.findByRole("heading", { name: "Current migration" })).toBeInTheDocument()
    expect(await screen.findByText("Create the new server privately")).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole("button", { name: "Create new server" })).toBeEnabled())
    expect(targetReviewRequest).toEqual({ targetStackSlug: "stack", elementPublicHost: "chat.example.test" })
    expect(screen.queryByText("Migration cutover workspace")).not.toBeInTheDocument()
    expect(screen.queryByText("Historical transitional migration path")).not.toBeInTheDocument()
  })

  it("mounts only secure Session creation on the dedicated new-migration page", async () => {
    renderWithProviders(
      <MemoryRouter>
        <MigrationIntakesPage />
      </MemoryRouter>,
    )

    expect(
      await screen.findByRole("heading", { name: "Migration intake" }),
    ).toBeInTheDocument()
    expect(screen.queryByText("Public cutover preparation")).not.toBeInTheDocument()
    expect(screen.queryByText("Advanced neutral-contract tools")).not.toBeInTheDocument()
    expect(screen.queryByLabelText("Neutral manifest")).not.toBeInTheDocument()
  })
})

function boundaryWorkspace(
  migrationId: string,
  displayName: string,
  currentStageCode: string,
  currentState: string,
  summaryCode: string,
  actionCode: string,
  detail: MigrationSessionDetail = migrationId === historicalMigration.session.migrationId ? historicalMigration : guidedDetail(migrationId),
): MigrationWorkspaceResponse {
  const codes = [
    "create-and-upload-package",
    "review-old-server",
    "prepare-and-test",
    "create-new-server",
    "make-new-server-live",
    "finish-migration",
  ]
  const currentIndex = codes.indexOf(currentStageCode)

  return withGuidedSnapshot({
    schemaVersion: 2,
    migration: {
      migrationId,
      displayName,
      status: currentState,
      createdAtUtc: "2026-07-13T00:00:00Z",
      updatedAtUtc: "2026-07-14T00:00:00Z",
    },
    source: {
      adapter: "mem-v010",
      displayName: "Message Easy Mode 0.1.0",
      product: "MatrixEasyMode",
      productVersion: "0.1.0",
      matrixServerName: "matrix.example.test",
      usersCount: 3,
      roomsCount: 2,
      eventsCount: 23,
      blockerCount: 0,
      warningCount: 0,
      advisoryCount: 0,
    },
    target: {
      planPrepared: currentIndex >= 3,
      status: currentIndex >= 3 ? "prepared" : "not-prepared",
      stackSlug: currentIndex >= 3 ? "stack" : null,
      displayName: currentIndex >= 3 ? "Stack" : null,
      matrixHost: currentIndex >= 3 ? "matrix.example.test" : null,
      elementHost: currentIndex >= 3 ? "chat.example.test" : null,
    },
    overallStatus: {
      code: summaryCode,
      severity: "info",
      currentStageCode,
      nextAction: { code: actionCode, enabled: true, relatedStage: currentStageCode },
    },
    stages: codes.map((code, index) => ({
      code,
      state: index < currentIndex ? "completed" : index === currentIndex ? currentState : "not-started",
      unlocked: index <= currentIndex,
      completedAtUtc: index < currentIndex ? "2026-07-13T01:00:00Z" : null,
      primaryAction: index === currentIndex
        ? { code: actionCode, enabled: true, relatedStage: code }
        : null,
      secondaryActions: [],
      summaryCode: index === currentIndex
        ? summaryCode
        : index < currentIndex
          ? `migration.workspace.stage.${code}.completed`
          : `migration.workspace.stage.${code}.not-started`,
      problems: [],
      evidenceSummary: {
        itemCount: index < currentIndex ? 1 : 0,
        latestOccurredAtUtc: index < currentIndex ? "2026-07-13T01:00:00Z" : null,
        latestStatus: index < currentIndex ? "completed" : null,
      },
      operationSummary: null,
      failureOutcome: null,
    })),
    currentOperation: null,
    activity: [{
      code: "migration-created",
      status: "completed",
      occurredAtUtc: "2026-07-13T00:00:00Z",
      relatedObjectId: migrationId,
    }],
    verification: {
      status: "not-run",
      hasRun: false,
      allPassed: null,
      checkCount: 0,
      failedCheckCount: 0,
      checkedAtUtc: null,
    },
    retention: {
      status: "not-created",
      retainUntilUtc: null,
      automaticDeletionAllowed: null,
    },
    evidence: { categories: [] },
    advancedTools: [{
      code: "final-frozen-assurance",
      availability: "available",
      reasonUnavailable: null,
      relatedStage: "create-new-server",
    }],
    warnings: [],
  }, detail)
}
