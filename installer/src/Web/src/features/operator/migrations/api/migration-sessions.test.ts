import { http, HttpResponse } from "msw"
import { describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"

import {
  archiveMigrationSession,
  cancelMigrationSession,
  deleteMigrationSession,
  isMigrationSessionLifecycleStepUpRequired,
  listMigrationSessions,
  unarchiveMigrationSession,
  type MigrationSessionInventoryResponse,
} from "./migration-sessions"

const response: MigrationSessionInventoryResponse = {
  schemaVersion: 1,
  query: {
    search: "David",
    lifecycle: "active",
    action: "review",
    stage: "prepare-and-test",
    targetStack: "tester",
    sortBy: "stage",
    sortDirection: "asc",
    includeArchived: true,
  },
  summary: {
    totalSessions: 0,
    activeCount: 0,
    needsActionCount: 0,
    completedCount: 0,
    closedCount: 0,
    cancelledCount: 0,
    archivedCount: 0,
  },
  totalSessions: 0,
  page: 2,
  pageSize: 25,
  totalPages: 0,
  hasPreviousPage: false,
  hasNextPage: false,
  targetStacks: [],
  sessions: [],
  warnings: [],
}

describe("listMigrationSessions", () => {
  it("requests the compact server-paged inventory with every URL-backed filter", async () => {
    server.use(
      http.get("/api/operator/migrations/sessions/inventory", ({ request }) => {
        const url = new URL(request.url)

        expect(url.searchParams.get("page")).toBe("2")
        expect(url.searchParams.get("pageSize")).toBe("25")
        expect(url.searchParams.get("search")).toBe("David")
        expect(url.searchParams.get("lifecycle")).toBe("active")
        expect(url.searchParams.get("action")).toBe("review")
        expect(url.searchParams.get("stage")).toBe("prepare-and-test")
        expect(url.searchParams.get("targetStack")).toBe("tester")
        expect(url.searchParams.get("sortBy")).toBe("stage")
        expect(url.searchParams.get("sortDirection")).toBe("asc")
        expect(url.searchParams.get("includeArchived")).toBe("true")

        return HttpResponse.json(response)
      }),
    )

    await expect(listMigrationSessions({
      page: 2,
      pageSize: 25,
      search: "  David  ",
      lifecycle: "active",
      action: "review",
      stage: "prepare-and-test",
      targetStack: "tester",
      sortBy: "stage",
      sortDirection: "asc",
      includeArchived: true,
    })).resolves.toEqual(response)
  })

  it("omits default and empty filters while retaining paging", async () => {
    server.use(
      http.get("/api/operator/migrations/sessions/inventory", ({ request }) => {
        const url = new URL(request.url)
        expect(url.search).toBe("?page=1&pageSize=10")
        return HttpResponse.json({ ...response, page: 1, pageSize: 10 })
      }),
    )

    await listMigrationSessions({
      page: 1,
      pageSize: 10,
      search: null,
      lifecycle: "all",
      action: "all",
      stage: null,
      targetStack: null,
      sortBy: "updated",
      sortDirection: "desc",
      includeArchived: false,
    })
  })
})


describe("migration session lifecycle API", () => {
  it("sends state-version for archive and unarchive mutations", async () => {
    const requests: Array<{ action: string; body: unknown }> = []
    server.use(
      http.post(
        "/api/operator/migrations/sessions/mig-lifecycle/lifecycle/archive",
        async ({ request }) => {
          requests.push({ action: "archive", body: await request.json() })
          return HttpResponse.json({
            resultCode: "migration_session_archived",
            idempotent: false,
            lifecycle: { ...lifecycleInspection, archived: true },
          })
        },
      ),
      http.post(
        "/api/operator/migrations/sessions/mig-lifecycle/lifecycle/unarchive",
        async ({ request }) => {
          requests.push({ action: "unarchive", body: await request.json() })
          return HttpResponse.json({
            resultCode: "migration_session_unarchived",
            idempotent: false,
            lifecycle: { ...lifecycleInspection, archived: false },
          })
        },
      ),
    )

    await archiveMigrationSession("mig-lifecycle", 7)
    await unarchiveMigrationSession("mig-lifecycle", 8)

    expect(requests).toEqual([
      { action: "archive", body: { expectedStateVersion: 7 } },
      { action: "unarchive", body: { expectedStateVersion: 8 } },
    ])
  })

  it("sends state-version and explicit cancellation decisions to the migration-keyed route", async () => {
    server.use(
      http.post(
        "/api/operator/migrations/sessions/mig-lifecycle/lifecycle/cancel",
        async ({ request }) => {
          expect(await request.json()).toEqual({
            expectedStateVersion: 7,
            acknowledgeSourceUnaffected: true,
            encryptedPackageRetention: "retain-encrypted",
          })

          return HttpResponse.json({
            resultCode: "migration_session_cancelled",
            idempotent: false,
            lifecycle: lifecycleInspection,
          })
        },
      ),
    )

    await expect(cancelMigrationSession("mig-lifecycle", {
      expectedStateVersion: 7,
      acknowledgeSourceUnaffected: true,
      encryptedPackageRetention: "retain-encrypted",
    })).resolves.toMatchObject({
      resultCode: "migration_session_cancelled",
      idempotent: false,
    })
  })

  it("sends exact-ID confirmation and source acknowledgement for permanent deletion", async () => {
    server.use(
      http.post(
        "/api/operator/migrations/sessions/mig-lifecycle/lifecycle/delete",
        async ({ request }) => {
          expect(await request.json()).toEqual({
            expectedStateVersion: 8,
            confirmationMigrationId: "mig-lifecycle",
            acknowledgeSourceUnaffected: true,
          })

          return HttpResponse.json({
            resultCode: "migration_session_deleted",
            idempotent: false,
            migrationId: "mig-lifecycle",
            deletedAtUtc: "2026-07-29T08:31:00Z",
          })
        },
      ),
    )

    await expect(deleteMigrationSession("mig-lifecycle", {
      expectedStateVersion: 8,
      confirmationMigrationId: "mig-lifecycle",
      acknowledgeSourceUnaffected: true,
    })).resolves.toMatchObject({
      resultCode: "migration_session_deleted",
      migrationId: "mig-lifecycle",
    })
  })

  it("recognises the existing recent-step-up response contract", async () => {
    server.use(
      http.post(
        "/api/operator/migrations/sessions/mig-lifecycle/lifecycle/cancel",
        () => HttpResponse.json({ status: "step_up_required" }, { status: 403 }),
      ),
    )

    const error = await cancelMigrationSession("mig-lifecycle", {
      expectedStateVersion: 7,
      acknowledgeSourceUnaffected: true,
      encryptedPackageRetention: "remove",
    }).catch((value: unknown) => value)

    expect(isMigrationSessionLifecycleStepUpRequired(error)).toBe(true)
  })
})

const lifecycleInspection = {
  migrationId: "mig-lifecycle",
  lifecycleStatus: "cancelled",
  archived: false,
  archivedAtUtc: null,
  archivedBy: null,
  closedAtUtc: "2026-07-29T08:30:00Z",
  closureKind: "operator-cancelled",
  stateVersion: 8,
  currentOperation: "none",
  packageState: "retired",
  candidateArtifactState: "none",
  privateStagingRuntimeState: "none",
  productionRuntimeState: "none",
  publicRoutesState: "none",
  sourceState: "external",
  capabilities: {
    canArchive: true,
    canUnarchive: false,
    canCancel: false,
    canDelete: true,
    cancelBlockedCode: "migration_session_lifecycle_terminal",
    cancelBlockedReason: "This Session lifecycle is not eligible for early cancellation.",
    deleteBlockedCode: null,
    deleteBlockedReason: null,
  },
  sourceUnaffectedNotice: "The source server is unaffected.",
}
