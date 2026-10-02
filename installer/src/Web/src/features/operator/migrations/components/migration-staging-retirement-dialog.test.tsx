import { act, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { MemoryRouter } from "react-router-dom"
import { afterEach, describe, expect, it, vi } from "vitest"
import { LANGUAGE_STORAGE_KEY } from "@/app/i18n/i18n-core"
import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import type { StagingRetirementOperation, StagingRetirementReview } from "../api/migration-staging-retirement"

vi.mock("@/features/auth/operator-step-up-dialog", () => ({
  OperatorStepUpDialog: ({ open, onVerified }: { open: boolean; onVerified: () => void }) =>
    open ? <button onClick={onVerified}>Complete step-up</button> : null,
}))

import { MigrationStagingRetirementDialog } from "./migration-staging-retirement-dialog"

const target = { migrationId: "mig_one", stagingRunId: "mst_one" }
const endpoint = "/api/operator/migrations/sessions/mig_one/staging-runs/mst_one/retirement"
const ready = (): StagingRetirementReview => ({
  ...target, displayName: "Example migration", canRetire: true, blockerCode: null,
  reviewFingerprint: "review-a", containerCount: 3, networkCount: 1, workspacePresent: true, operation: null,
})
const operation = (status: StagingRetirementOperation["status"] = "queued"): StagingRetirementOperation => ({
  operationId: "op-one", status, currentStep: status === "retired" ? "retired" : "queued", attemptCount: 0,
  requestedAtUtc: "2026-09-24T00:00:00Z", updatedAtUtc: "2026-09-24T00:00:01Z",
  completedAtUtc: status === "retired" ? "2026-09-24T00:00:02Z" : null,
  failureCode: status === "needs-attention" ? "cleanup-incomplete" : null,
})
function renderDialog(onClose = vi.fn()) {
  return { onClose, ...renderWithProviders(<MemoryRouter>
    <MigrationStagingRetirementDialog target={target} onClose={onClose} />
  </MemoryRouter>) }
}
async function confirm(user: ReturnType<typeof userEvent.setup>, label = "Retire staging") {
  await user.click(await screen.findByRole("checkbox"))
  const button = screen.getByRole("button", { name: label })
  await waitFor(() => expect(button).toBeEnabled())
  await user.click(button)
}

afterEach(() => { localStorage.removeItem(LANGUAGE_STORAGE_KEY) })

describe("MigrationStagingRetirementDialog", () => {
  it("reviews without mutation, requires explicit confirmation, and can close without retiring", async () => {
    const user = userEvent.setup()
    let posts = 0
    server.use(http.get(endpoint, () => HttpResponse.json(ready())),
      http.post(endpoint, () => { posts++; return HttpResponse.json(operation()) }))
    const { onClose } = renderDialog()
    expect(await screen.findByRole("checkbox")).not.toBeChecked()
    expect(screen.getByRole("button", { name: "Retire staging" })).toBeDisabled()
    expect(screen.getByText(/3 containers and 1 networks/)).toBeInTheDocument()
    expect(screen.getByText(/encrypted package, candidate artifact/)).toBeInTheDocument()
    await user.click(screen.getByRole("button", { name: "Close" }))
    expect(onClose).toHaveBeenCalledOnce()
    expect(posts).toBe(0)
  })

  it("keeps the exact reviewed request across recent step-up and shows acceptance, not completed removal", async () => {
    const user = userEvent.setup()
    const requests: unknown[] = []
    let accepted = false
    server.use(http.get(endpoint, () => HttpResponse.json(accepted
      ? { ...ready(), canRetire: false, reviewFingerprint: null, operation: operation() } : ready())),
      http.post(endpoint, async ({ request }) => {
        requests.push(await request.json())
        if (requests.length === 1) return HttpResponse.json({ status: "step_up_required" }, { status: 403 })
        accepted = true
        return HttpResponse.json(operation(), { status: 202 })
      }))
    renderDialog()
    await confirm(user)
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))
    expect(await screen.findByText("Staging retirement in progress")).toBeInTheDocument()
    expect(requests).toEqual([
      { reviewFingerprint: "review-a", confirmRetirement: true, retry: false },
      { reviewFingerprint: "review-a", confirmRetirement: true, retry: false },
    ])
    expect(screen.queryByText("The selected temporary resources are removed. The Migration Session and historical evidence are retained.")).not.toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Retirement accepted" })).toBeDisabled()
    expect(screen.getByRole("button", { name: "Close" })).toBeEnabled()
  })

  it("reconciles an ambiguous POST through GET without automatically replaying deletion", async () => {
    const user = userEvent.setup()
    let accepted = false
    let posts = 0
    server.use(http.get(endpoint, () => HttpResponse.json(accepted
      ? { ...ready(), canRetire: false, operation: operation("running") } : ready())),
      http.post(endpoint, () => {
        posts++; accepted = true
        return HttpResponse.json({ detail: "RAW-SECRET must not be rendered" }, { status: 503 })
      }))
    renderDialog()
    await confirm(user)
    expect(await screen.findByText("Staging retirement in progress")).toBeInTheDocument()
    expect(posts).toBe(1)
    expect(screen.queryByText(/RAW-SECRET/)).not.toBeInTheDocument()
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument()
  })

  it("withholds stale confirmation when both submission and reconciliation fail", async () => {
    const user = userEvent.setup()
    let failed = false
    let posts = 0
    server.use(http.get(endpoint, () => failed ? HttpResponse.json({}, { status: 503 }) : HttpResponse.json(ready())),
      http.post(endpoint, () => { posts++; failed = true; return HttpResponse.json({}, { status: 503 }) }))
    renderDialog()
    await confirm(user)
    expect(await screen.findByText(/Current evidence is unavailable/)).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Retire staging" })).toBeDisabled()
    expect(screen.getByRole("checkbox")).not.toBeChecked()
    expect(screen.getByRole("checkbox")).toBeDisabled()
    expect(posts).toBe(1)
  })

  it("invalidates the checkbox when server-reviewed scope changes", async () => {
    const user = userEvent.setup()
    let review = ready()
    server.use(http.get(endpoint, () => HttpResponse.json(review)))
    const { queryClient } = renderDialog()
    await user.click(await screen.findByRole("checkbox"))
    expect(screen.getByRole("checkbox")).toBeChecked()
    review = { ...review, reviewFingerprint: "review-b", containerCount: 2 }
    await act(async () => { await queryClient.invalidateQueries({ queryKey: ["migration-staging-retirement"] }) })
    expect(await screen.findByText(/2 containers and 1 networks/)).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole("checkbox")).not.toBeChecked())
    expect(screen.getByRole("button", { name: "Retire staging" })).toBeDisabled()
  })

  it("rejects a stale step-up replay without silently confirming the newer scope", async () => {
    const user = userEvent.setup()
    let review = ready()
    const requests: unknown[] = []
    server.use(http.get(endpoint, () => HttpResponse.json(review)),
      http.post(endpoint, async ({ request }) => {
        requests.push(await request.json())
        if (requests.length === 1) {
          review = { ...ready(), reviewFingerprint: "review-b" }
          return HttpResponse.json({ status: "step_up_required" }, { status: 403 })
        }
        return HttpResponse.json({ error: "review-stale" }, { status: 409 })
      }))
    renderDialog()
    await confirm(user)
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))
    expect(await screen.findByText(/The request was not confirmed here/)).toBeInTheDocument()
    await waitFor(() => expect(screen.getByRole("checkbox")).not.toBeChecked())
    expect(requests).toEqual([
      { reviewFingerprint: "review-a", confirmRetirement: true, retry: false },
      { reviewFingerprint: "review-a", confirmRetirement: true, retry: false },
    ])
    expect(screen.getByRole("button", { name: "Retire staging" })).toBeDisabled()
  })

  it("requires a fresh checked review and explicit retry for partial cleanup", async () => {
    const user = userEvent.setup()
    let retried = false
    let body: unknown
    server.use(http.get(endpoint, () => HttpResponse.json({ ...ready(), canRetire: !retried,
      operation: operation(retried ? "queued" : "needs-attention") })),
      http.post(endpoint, async ({ request }) => {
        body = await request.json(); retried = true
        return HttpResponse.json(operation(), { status: 202 })
      }))
    renderDialog()
    expect(await screen.findByText("Staging retirement needs attention")).toBeInTheDocument()
    expect(screen.getByText(/Already removed resources stay removed/)).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Retry retirement" })).toBeDisabled()
    await confirm(user, "Retry retirement")
    expect(await screen.findByText("Staging retirement in progress")).toBeInTheDocument()
    expect(body).toEqual({ reviewFingerprint: "review-a", confirmRetirement: true, retry: true })
  })

  it.each(["private-target-recovery-required", "completed-recovery-required", "ownership-unproven", "public-resource", "unknown-secret-code"])(
    "does not offer confirmation for blocked review %s", async (blockerCode) => {
      server.use(http.get(endpoint, () => HttpResponse.json({ ...ready(), canRetire: false, reviewFingerprint: null, blockerCode })))
      renderDialog()
      await screen.findByText("Example migration")
      expect(screen.queryByRole("checkbox")).not.toBeInTheDocument()
      expect(screen.getByRole("button", { name: "Retire staging" })).toBeDisabled()
      expect(screen.queryByText("unknown-secret-code")).not.toBeInTheDocument()
    },
  )

  it("rediscovers a completed receipt without performing a POST", async () => {
    let posts = 0
    server.use(http.get(endpoint, () => HttpResponse.json({ ...ready(), canRetire: false, operation: operation("retired") })),
      http.post(endpoint, () => { posts++; return HttpResponse.json(operation("retired")) }))
    renderDialog()
    expect(await screen.findByText("The selected temporary resources are removed. The Migration Session and historical evidence are retained.")).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Staging retired" })).toBeDisabled()
    expect(screen.queryByRole("checkbox")).not.toBeInTheDocument()
    expect(posts).toBe(0)
  })

  it("renders the German review and confirmation contract", async () => {
    localStorage.setItem(LANGUAGE_STORAGE_KEY, "de")
    server.use(http.get(endpoint, () => HttpResponse.json(ready())))
    renderDialog()
    expect(await screen.findByRole("checkbox")).not.toBeChecked()
    expect(screen.getByRole("alertdialog", { name: "Migrations-Staging stilllegen" })).toBeInTheDocument()
    expect(screen.getByRole("button", { name: "Staging stilllegen" })).toBeDisabled()
    expect(screen.getByRole("link", { name: "Migration öffnen" })).toHaveAttribute("href", "/migrations/mig_one")
  })
})
