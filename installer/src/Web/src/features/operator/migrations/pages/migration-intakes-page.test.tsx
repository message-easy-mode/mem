import { act, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { HttpResponse, http } from "msw"
import { MemoryRouter, Route, Routes } from "react-router-dom"
import { describe, expect, it, vi } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"

vi.mock("@/features/auth/operator-step-up-dialog", () => ({
  OperatorStepUpDialog: ({ open, onVerified }: { open: boolean; onVerified?: () => void }) =>
    open ? <button onClick={onVerified}>Complete step-up</button> : null,
}))

import { MigrationIntakesPage } from "./migration-intakes-page"

function renderPage() {
  return renderWithProviders(
    <MemoryRouter initialEntries={["/migrations/new"]}>
      <Routes>
        <Route path="/migrations" element={<div>Migration inventory destination</div>} />
        <Route path="/migrations/new" element={<MigrationIntakesPage />} />
        <Route path="/migrations/:migrationId" element={<div>Migration details destination</div>} />
      </Routes>
    </MemoryRouter>,
  )
}

describe("MigrationIntakesPage", () => {
  it("cancels an untouched intake form without creating a session or asking for confirmation", async () => {
    let posts = 0
    server.use(http.post("/api/operator/migrations/secure-intakes", () => { posts++; return HttpResponse.json({}) }))
    renderPage()
    await userEvent.setup().click(screen.getByRole("button", { name: "Cancel" }))
    expect(await screen.findByText("Migration inventory destination")).toBeInTheDocument()
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument()
    expect(posts).toBe(0)
  })

  it("does not offer form cancellation or a second create while intake creation is in flight", async () => {
    let complete!: () => void
    const gate = new Promise<void>((resolve) => { complete = resolve })
    let posts = 0
    server.use(http.post("/api/operator/migrations/secure-intakes", async () => {
      posts++
      await gate
      return HttpResponse.json({ intakeId: "mig_pending_intake" })
    }))
    renderPage()
    await userEvent.setup().dblClick(screen.getByRole("button", { name: "Create secure intake" }))
    await waitFor(() => expect(posts).toBe(1))
    expect(screen.getByRole("button", { name: "Cancel" })).toBeDisabled()
    await act(async () => { complete() })
    expect(await screen.findByText("Migration details destination")).toBeInTheDocument()
    expect(posts).toBe(1)
  })

  it("creates a secure Session then moves the operator into its durable workspace", async () => {
    const user = userEvent.setup()

    server.use(
      http.post("/api/operator/migrations/secure-intakes", async ({ request }) => {
        expect(await request.json()).toEqual({ displayName: "Imported MEM server" })
        return HttpResponse.json({
          intakeId: "mig_20260716-workspace",
          displayName: "Imported MEM server",
          status: "awaiting-package",
          ageRecipient: "age1publicrecipient",
          recipientFingerprint: "A1B2-C3D4-E5F6-0718",
          createdAtUtc: "2026-07-16T01:00:00Z",
          expiresAtUtc: "2026-07-17T01:00:00Z",
        })
      }),
    )

    renderPage()

    expect(screen.getByText("Recommended")).toBeInTheDocument()
    expect(screen.queryByText("Advanced neutral-contract tools")).not.toBeInTheDocument()
    expect(screen.queryByLabelText("Neutral manifest")).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Preview" })).not.toBeInTheDocument()
    expect(screen.queryByText(/AGE-SECRET-KEY/)).not.toBeInTheDocument()

    await user.click(screen.getByRole("button", { name: "Create secure intake" }))

    expect(await screen.findByText("Migration details destination")).toBeInTheDocument()
  })

  it("retries secure Session creation after operator step-up", async () => {
    const user = userEvent.setup()
    let attempt = 0

    server.use(
      http.post("/api/operator/migrations/secure-intakes", () => {
        attempt += 1
        if (attempt === 1) {
          return HttpResponse.json({ status: "step_up_required" }, { status: 403 })
        }

        return HttpResponse.json({
          intakeId: "mig_20260716-step-up",
          displayName: "Imported MEM server",
          status: "awaiting-package",
          ageRecipient: "age1publicrecipient",
          recipientFingerprint: "A1B2-C3D4-E5F6-0718",
          createdAtUtc: "2026-07-16T01:00:00Z",
          expiresAtUtc: "2026-07-17T01:00:00Z",
        })
      }),
    )

    renderPage()
    await user.click(screen.getByRole("button", { name: "Create secure intake" }))
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))

    expect(await screen.findByText("Migration details destination")).toBeInTheDocument()
    expect(attempt).toBe(2)
  })
})
