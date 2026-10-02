import { act, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { HttpResponse, http } from "msw"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { beforeEach, describe, expect, it } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import { guidedDetail, guidedWorkspace, guidedStages } from "../test/migration-guided-fixtures"
import { migrationWorkspaceKeys } from "../hooks/use-migration-workspace"
import { migrationSessionKeys } from "../hooks/use-migration-sessions"
import { MigrationSessionPage } from "./migration-session-page"

const migrationId = "mig_workspace_detail"
const base = `/api/operator/migrations/sessions/${migrationId}`
let workspace = guidedWorkspace({ migrationId })
let sessionReads = 0
let lifecycleReads = 0
let authorityReads = 0
const review = {
  migrationId, sourceStackSlug: "tester", sourceElementPublicHost: "element.example.test", targetStackSlug: "tester",
  matrixServerName: "matrix.example.test", elementPublicHost: "element.example.test", stackNameStatus: "available",
  matrixAddressStatus: "locked-available", elementAddressStatus: "available", collisionFree: true,
  suggestedTargetStackSlug: null, collisions: [], detail: "The target identity is available.",
}

beforeEach(() => {
  sessionStorage.clear()
  sessionReads = lifecycleReads = authorityReads = 0
  workspace = guidedWorkspace({ migrationId })
  server.use(
    http.get(`${base}/workspace`, () => HttpResponse.json(workspace)),
    http.get(base, () => { sessionReads++; return HttpResponse.json(guidedDetail(migrationId)) }),
    http.get(`${base}/lifecycle`, () => { lifecycleReads++; return HttpResponse.json({}, { status: 503 }) }),
    http.get(`${base}/production-authority`, () => { authorityReads++; return HttpResponse.json({}, { status: 503 }) }),
    http.get(`${base}/conversion-attempts/options`, () => HttpResponse.json({ packageRevisionId: "mpr_preview",
      boundSourceStack: { sourceStackId: "source-1", slug: "tester", matrixServerName: "matrix.example.test" } })),
    http.get(`${base}/conversion-attempts`, () => HttpResponse.json([])),
    http.get(`${base}/staging-runs`, () => HttpResponse.json([])),
    http.get(`${base}/production-adoption`, () => HttpResponse.json({ source: "control-plane", migrationId,
      status: "not-prepared", planPrepared: false, plan: null, detail: "An older specialist projection." })),
    http.post(`${base}/production-adoption/private-server/review`, () => HttpResponse.json(review)),
    http.get(`${base}/acceptance`, () => HttpResponse.json({}, { status: 503 })),
  )
})

function renderSession() {
  return renderWithProviders(<MemoryRouter initialEntries={[`/migrations/${migrationId}`]}>
    <Routes><Route path="/migrations/:migrationId" element={<MigrationSessionPage />} /></Routes>
  </MemoryRouter>)
}

function header(title: string) {
  const result = screen.getAllByRole("button").find((button) => button.hasAttribute("aria-expanded") && button.textContent?.includes(title))
  expect(result).toBeDefined()
  return result!
}

async function observe(view: ReturnType<typeof renderSession>) {
  await act(async () => { await view.queryClient.refetchQueries({ queryKey: migrationWorkspaceKeys.detail(migrationId), exact: true }) })
}

describe("server-authored guided Migration state", () => {
  it("exposes early cancellation in the Guide without independent lifecycle reads", async () => {
    workspace = guidedWorkspace({ migrationId, stage: "create-and-upload-package", action: "upload-package", guided: {
      cancellation: { stateVersion: 1, lifecycleStatus: "active", canCancel: true,
        confirmationKind: "empty-session", blockedCode: null, blockedReason: null },
    } })
    workspace.guided.operationRevisions["cancel-migration"] = "initial-cancel"
    renderSession()
    expect(await screen.findByRole("button", { name: "Cancel migration" })).toBeEnabled()
    expect(screen.getByRole("tab", { name: "Guide" })).toHaveAttribute("aria-selected", "true")
    expect(sessionReads + lifecycleReads + authorityReads).toBe(0)
  })

  it("withholds early cancellation while the authoritative workspace cannot be refreshed", async () => {
    workspace = guidedWorkspace({ migrationId, stage: "create-and-upload-package", action: "upload-package", guided: {
      cancellation: { stateVersion: 1, lifecycleStatus: "active", canCancel: true,
        confirmationKind: "empty-session", blockedCode: null, blockedReason: null },
    } })
    workspace.guided.operationRevisions["cancel-migration"] = "initial-cancel"
    const view = renderSession()
    expect(await screen.findByRole("button", { name: "Cancel migration" })).toBeEnabled()
    server.use(http.get(`${base}/workspace`, () => HttpResponse.json({}, { status: 503 })))
    await observe(view)
    await waitFor(() => expect(screen.getByRole("button", { name: "Cancel migration" })).toBeDisabled())
    expect(sessionReads + lifecycleReads + authorityReads).toBe(0)
  })

  it("shows six stages and keeps transfer controls inside the package stage", async () => {
    const detail = guidedDetail(migrationId)
    detail.package = { ...detail.package!, status: "awaiting-package", ageRecipient: "age1publicrecipient" }
    workspace = guidedWorkspace({ migrationId, stage: "create-and-upload-package", action: "upload-package", detail })
    renderSession()
    expect(await screen.findByText("Migration package")).toBeInTheDocument()
    expect(screen.getByRole("tab", { name: "Guide" })).toHaveAttribute("aria-selected", "true")
    for (const title of ["Create and upload package", "Review the old server", "Prepare and test", "Create the new server",
      "Make the new server live", "Finish the migration"]) expect(header(title)).toBeInTheDocument()
    expect(screen.getByRole("heading", { name: "Upload the package" })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Download migration request" })).toBeInTheDocument()
    expect(screen.getByText("Security and request details").closest("details")).not.toHaveAttribute("open")
    expect(sessionReads + lifecycleReads + authorityReads).toBe(0)
  })

  it.each(["conversion", "staging", "completed"])("ignores independently cached session phase %s", async (phase) => {
    const view = renderSession()
    // This scenario deliberately retains an unobserved specialist cache entry.
    // The shared test client uses gcTime: 0; retaining this one key ensures the
    // assertion proves the guide ignores stale data, not that GC removed it.
    view.queryClient.setQueryDefaults(migrationSessionKeys.detail(migrationId), { gcTime: Infinity })
    const stale = guidedDetail(migrationId)
    stale.session.phase = phase
    stale.session.nextAction = "review-conversion"
    await act(async () => { view.queryClient.setQueryData(migrationSessionKeys.detail(migrationId), stale) })
    expect(await screen.findByRole("button", { name: "Run private test" })).toBeEnabled()
    expect(header("Prepare and test")).toHaveAttribute("aria-expanded", "true")
    expect(sessionReads + lifecycleReads + authorityReads).toBe(0)
    expect(view.queryClient.getQueryData(migrationSessionKeys.detail(migrationId))).toEqual(stale)
  })

  it("starts preparation once and advances from the next workspace snapshot, not a session refetch", async () => {
    workspace = guidedWorkspace({ migrationId, stage: "review-old-server", action: "start-conversion", guided: { hasVerifiedCandidate: false } })
    let posts = 0
    server.use(http.post(`${base}/conversion-attempts`, () => {
      posts++
      workspace = guidedWorkspace({ migrationId, stage: "prepare-and-test", state: "running", action: null, guided: {
        hasVerifiedCandidate: false, conversionAttemptId: "conv_accepted", conversionStatus: "running",
        operationRevisions: { ...workspace.guided.operationRevisions, conversion: "conv_accepted:running" },
      } })
      return HttpResponse.error()
    }))
    renderSession()
    await userEvent.setup().click(await screen.findByRole("button", { name: "Prepare migration data" }))
    await waitFor(() => expect(header("Prepare and test")).toHaveAttribute("aria-expanded", "true"))
    expect(posts).toBe(1)
    expect(screen.queryByText("Failed to fetch")).not.toBeInTheDocument()
    expect(sessionReads + lifecycleReads + authorityReads).toBe(0)
  })

  it("converges a running private test into an actionable failure using only workspace reads", async () => {
    workspace = guidedWorkspace({ migrationId, state: "running", action: null })
    const view = renderSession()
    expect(await screen.findByText("Testing a private copy")).toBeInTheDocument()
    workspace = guidedWorkspace({ migrationId, state: "failed", action: "retry-private-test", guided: {
      stagingRunId: "mstg_failed", stagingStatus: "failed-cleaned",
    } })
    await observe(view)
    // Evidence may be absent. The authoritative retry gate still permits the command.
    expect(screen.getByRole("button", { name: "Run private test" })).toBeEnabled()
    expect(sessionReads).toBe(0)
  })

  it("preserves same-stage target input when the canonical revision advances", async () => {
    workspace = guidedWorkspace({ migrationId, stage: "create-new-server", action: "confirm-tested-data" })
    const view = renderSession()
    const input = await screen.findByDisplayValue("tester")
    await userEvent.setup().clear(input)
    await userEvent.setup().type(input, "operator-choice")
    workspace = structuredClone(workspace)
    workspace.migration.updatedAtUtc = "2026-09-20T00:10:00Z"
    workspace.guided.revision = "new-same-stage-revision"
    await observe(view)
    expect(screen.getByDisplayValue("operator-choice")).toBe(input)
    expect(sessionReads + lifecycleReads + authorityReads).toBe(0)
  })

  it("moves from server creation to go-live without joining an independent authority read", async () => {
    workspace = guidedWorkspace({ migrationId, stage: "create-new-server", state: "running", action: null })
    const view = renderSession()
    expect(await screen.findByText("Creating new server...")).toBeInTheDocument()
    workspace = guidedWorkspace({ migrationId, stage: "make-new-server-live", action: "make-server-live", guided: {
      privateRuntimeReady: true, productionAuthorized: true, cutoverPreviewStatus: "ready", cutoverPreviewId: "preview_ready",
    } })
    await observe(view)
    expect(await screen.findByRole("button", { name: "Make server live" })).toBeEnabled()
    expect(screen.queryByText("The new server is not ready to go live")).not.toBeInTheDocument()
    expect(sessionReads + lifecycleReads + authorityReads).toBe(0)
  })

  it("does not let missing acceptance evidence veto the workspace Finish gate", async () => {
    workspace = guidedWorkspace({ migrationId, stage: "finish-migration", action: "finish-migration" })
    renderSession()
    expect(await screen.findByRole("button", { name: "Finish migration" })).toBeEnabled()
    expect(await screen.findByText(/Technical evidence is temporarily unavailable/)).toBeInTheDocument()
    expect(sessionReads + lifecycleReads + authorityReads).toBe(0)
  })

  it("withholds Finish when the canonical acceptance gate is blocked", async () => {
    workspace = guidedWorkspace({ migrationId, stage: "finish-migration", state: "blocked", action: "open-technical-details" })
    renderSession()
    expect(await screen.findByText("Finishing is not ready")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Finish migration" })).not.toBeInTheDocument()
  })

  it("keeps the last confirmed snapshot visible but pauses actions during a workspace read failure", async () => {
    const view = renderSession()
    expect(await screen.findByRole("button", { name: "Run private test" })).toBeEnabled()
    server.use(http.get(`${base}/workspace`, () => HttpResponse.json({}, { status: 503 })))
    await observe(view)
    expect(await screen.findByText("Showing the last confirmed server state")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Run private test" })).toBeDisabled()
    server.use(http.get(`${base}/workspace`, () => HttpResponse.json(workspace)))
    await observe(view)
    // A completed refetch is not itself proof that the subscribed UI has
    // rendered the recovered snapshot. Assert both sides of that boundary.
    await waitFor(() => {
      expect(screen.getByRole("button", { name: "Run private test" })).toBeEnabled()
      expect(screen.queryByText("Showing the last confirmed server state")).not.toBeInTheDocument()
    })
    expect(sessionReads + lifecycleReads + authorityReads).toBe(0)
  })

  it("fails closed when the server declares its guided state unknown", async () => {
    workspace.guided.uncertaintyState = "unknown"
    renderSession()
    expect(await screen.findByRole("button", { name: "Run private test" })).toBeDisabled()
  })

  it("renders activity and technical findings from the same workspace response", async () => {
    workspace.activity.push({ code: "package-verified", status: "completed", occurredAtUtc: "2026-09-20T00:00:00Z", relatedObjectId: null })
    workspace.evidence.categories.push({ code: "package-and-source", status: "package-validated", itemCount: 3, latestOccurredAtUtc: null })
    workspace.guided.detail.findings.push({ severity: "warning", code: "test_warning", message: "A safe warning is retained.",
      artifactId: null, createdAtUtc: "2026-09-20T00:00:00Z" })
    renderSession()
    await screen.findByRole("button", { name: "Run private test" })
    const user = userEvent.setup()
    await user.click(screen.getByRole("tab", { name: "Activity" }))
    expect(screen.getByText("Package verified")).toBeInTheDocument()
    await user.click(screen.getByRole("tab", { name: "Evidence" }))
    expect(screen.getByText("3 durable item(s)")).toBeInTheDocument()
    expect(screen.getByText("A safe warning is retained.")).toBeInTheDocument()
    expect(sessionReads).toBe(0)
  })

  it.each(["missing", "older"])("does not reconstruct a %s workspace contract from specialist reads", async (mode) => {
    server.use(http.get(`${base}/workspace`, () => mode === "missing"
      ? HttpResponse.json({}, { status: 503 }) : HttpResponse.json({ ...workspace, schemaVersion: 1, guided: undefined })))
    renderSession()
    expect(await screen.findByText("Migration session could not be loaded")).toBeInTheDocument()
    expect(sessionReads + lifecycleReads + authorityReads).toBe(0)
  })

  it.each(guidedStages)("uses the same six-stage order when reloading at %s", async (stage) => {
    workspace = guidedWorkspace({ migrationId, stage, action: null })
    renderSession()
    await screen.findByRole("heading", { name: "Guided migration" })
    const stageButtons = screen.getAllByRole("button").filter((button) => button.hasAttribute("aria-expanded"))
    expect(stageButtons).toHaveLength(6)
    expect(stageButtons.filter((button) => button.getAttribute("aria-expanded") === "true")).toHaveLength(1)
    expect(stageButtons[guidedStages.indexOf(stage)]).toHaveAttribute("aria-expanded", "true")
  })
})
