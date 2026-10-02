import { HttpResponse, http } from "msw"
import { screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import type { MigrationSessionDetail } from "@/features/operator/migrations/api/migration-sessions"
import { MigrationCutoverWorkspace } from "./migration-cutover-workspace"

vi.mock("@/features/auth/operator-step-up-dialog", () => ({
  OperatorStepUpDialog: ({ open, onVerified }: { open: boolean; onVerified?: () => void }) =>
    open ? <button onClick={onVerified}>Complete step-up</button> : null,
}))

const detail: MigrationSessionDetail = {
  session: {
    migrationId: "mig-new",
    displayName: "Imported MEM server",
    sourceAdapter: "mem-v010",
    sourceDisplay: "Message Easy Mode 0.1.0",
    phase: "staging",
    status: "staging-verified",
    nextAction: "review-private-staging",
    createdAtUtc: "2026-07-16T00:00:00Z",
    updatedAtUtc: "2026-07-17T10:49:00Z",
    blockerCount: 0,
    warningCount: 1,
    advisoryCount: 0,
    needsAttention: false,
    sourceCount: 1,
    stackCount: 1,
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
  package: null,
  sources: [],
  findings: [],
  linkedObjects: [],
}

function cutoverState(overrides: Record<string, unknown> = {}) {
  return {
    source: "control-plane",
    status: "staging-verified",
    migrationId: "mig-new",
    capture: {
      kind: "preview",
      sourceFrozen: false,
      rehearsalOnly: true,
      finalCutoverEligible: false,
      sourceMigrationId: "capture-preview",
      sourceStackSlug: "davids-stack",
      matrixServerName: "matrix.example.test",
      matrixPublicUrl: "https://matrix.example.test",
      elementPublicUrl: "https://chat.example.test",
    },
    candidateArtifactId: "mca-1",
    staging: {
      stagingRunId: "mstg-1",
      privateRuntimeStagingId: "private-1",
      status: "verified",
      privateOnly: true,
      publicRoutesCreated: false,
      databaseImportSucceeded: true,
      synapseHealthPassed: true,
      elementConfigPresent: true,
      usersCount: 3,
      roomsCount: 2,
      eventsCount: 23,
    },
    productionCandidate: null,
    preview: null,
    confirmation: null,
    readiness: {
      status: "blocked",
      executionReady: false,
      finalCaptureRequired: true,
      candidateReady: false,
      previewReady: false,
      confirmationReady: false,
      routesReady: false,
      blockers: ["A final frozen Migration capture is required before public execution."],
      warnings: ["The current package remains valid for rehearsal only."],
      detail: "Cutover remains read-only until every blocker is resolved.",
    },
    routeSnapshot: { capturedAtUtc: null, status: "not-created" },
    latestExecution: null,
    rollback: {
      routeMutationOccurred: false,
      automaticRollbackAvailable: false,
      automaticRollbackAttempted: false,
      automaticRollbackCompleted: false,
      status: "not-required",
      detail: "No execution recorded.",
    },
    backupCatalogItemCreated: false,
    restoreSessionCreated: false,
    detail: "Prepare a candidate.",
    ...overrides,
  }
}

const candidate = {
  candidateId: "candidate-1",
  status: "private_candidate_ready",
  targetStackSlug: "davids-stack",
  matrixServerName: "matrix.example.test",
  privateRuntimeStatus: "ready",
  safety: { privateOnly: true, publicRoutesCreated: false, productionExecutionLocked: true },
  database: { importSucceeded: true },
  runtime: { synapseHealthPassed: true, elementHealthPassed: true },
  warnings: [],
  errors: [],
  detail: "ready",
}

const preview = {
  previewId: "preview-1",
  status: "ready_for_review",
  productionExecutionLocked: true,
  blockers: [],
  warnings: [],
  errors: [],
  detail: "preview",
}

const confirmation = {
  confirmationId: "confirmation-1",
  status: "confirmed_ready_for_execution",
  productionExecutionLocked: true,
  executionAvailable: true,
  blockers: [],
  warnings: [],
  errors: [],
  detail: "confirmed",
}

describe("MigrationCutoverWorkspace", () => {
  it("reconstructs durable Migration-keyed state after refresh and keeps rehearsal execution blocked", async () => {
    server.use(
      http.get("/api/operator/migrations/sessions/mig-new/cutover", () =>
        HttpResponse.json(cutoverState({
          status: "confirmation-recorded",
          productionCandidate: candidate,
          preview,
          confirmation,
          readiness: {
            ...cutoverState().readiness,
            candidateReady: true,
            previewReady: true,
            confirmationReady: true,
            routesReady: true,
          },
        })),
      ),
    )

    renderWithProviders(<MigrationCutoverWorkspace detail={detail} />)

    expect(await screen.findByText("Migration cutover workspace")).toBeInTheDocument()
    expect((await screen.findAllByText("candidate-1")).length).toBeGreaterThan(0)
    expect(screen.getAllByText("preview-1").length).toBeGreaterThan(0)
    expect(screen.getAllByText("confirmation-1").length).toBeGreaterThan(0)
    expect(screen.getByText("Production authority required")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Execute public cutover" })).toBeDisabled()
    expect(screen.getByText("Backup Catalog item created").nextSibling).toHaveTextContent("No")
    expect(screen.getByText("Restore Session created").nextSibling).toHaveTextContent("No")
  })

  it("uses only Migration Session-keyed routes and never submits Catalog, Restore, candidate, or preview IDs", async () => {
    let current = cutoverState()
    const requests: Array<{ url: string; body: Record<string, unknown> }> = []

    server.use(
      http.get("/api/operator/migrations/sessions/mig-new/cutover", () => HttpResponse.json(current)),
      http.post("/api/operator/migrations/sessions/mig-new/cutover/candidate", async ({ request }) => {
        const body = await request.json() as Record<string, unknown>
        requests.push({ url: request.url, body })
        current = cutoverState({ productionCandidate: candidate })
        return HttpResponse.json({ source: "control-plane", status: candidate.status, migrationId: "mig-new", candidateCreated: true, candidateResumed: false, candidate, detail: "created" })
      }),
      http.post("/api/operator/migrations/sessions/mig-new/cutover/preview", async ({ request }) => {
        const body = await request.json() as Record<string, unknown>
        requests.push({ url: request.url, body })
        current = cutoverState({ productionCandidate: candidate, preview })
        return HttpResponse.json({ source: "control-plane", status: preview.status, migrationId: "mig-new", preview, detail: "created" })
      }),
      http.post("/api/operator/migrations/sessions/mig-new/cutover/confirmation", async ({ request }) => {
        const body = await request.json() as Record<string, unknown>
        requests.push({ url: request.url, body })
        current = cutoverState({ productionCandidate: candidate, preview, confirmation })
        return HttpResponse.json({ source: "control-plane", status: confirmation.status, migrationId: "mig-new", confirmation, detail: "recorded" })
      }),
      http.get("/api/operator/migrations/sessions/mig-new/cutover/readiness", () => HttpResponse.json(current)),
    )

    const user = userEvent.setup()
    renderWithProviders(<MigrationCutoverWorkspace detail={detail} />)
    await screen.findByText("Migration cutover workspace")

    await user.click(screen.getByRole("button", { name: "Create private candidate" }))
    await user.click(screen.getByRole("button", { name: "Complete step-up" }))
    expect(await screen.findByText("candidate-1")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Create read-only preview" }))
    expect(await screen.findByText("preview-1")).toBeInTheDocument()

    const confirmationBoxes = screen.getAllByRole("checkbox").filter((box) => !box.hasAttribute("disabled")).slice(0, 6)
    expect(confirmationBoxes).toHaveLength(6)
    for (const box of confirmationBoxes) await user.click(box)
    await user.click(screen.getByRole("button", { name: "Save confirmation" }))
    expect(await screen.findByText("confirmation-1")).toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Check readiness" }))
    expect(screen.getByRole("button", { name: "Execute public cutover" })).toBeDisabled()

    expect(requests).toHaveLength(3)
    for (const entry of requests) {
      expect(new URL(entry.url).pathname).toMatch(/^\/api\/operator\/migrations\/sessions\/mig-new\/cutover/)
      const serialized = JSON.stringify(entry.body).toLowerCase()
      expect(serialized).not.toContain("catalogentry")
      expect(serialized).not.toContain("restoresession")
      expect(serialized).not.toContain("candidateid")
      expect(serialized).not.toContain("previewid")
      expect(serialized).not.toContain("confirmationid")
      expect(serialized).not.toContain("docker")
      expect(serialized).not.toContain("image")
      expect(serialized).not.toContain("path")
    }
  })
})
