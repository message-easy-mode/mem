import { http, HttpResponse } from "msw"
import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it, vi } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import type { RuntimeStackUsersResponse } from "../api/stacks.types"

vi.mock("@/features/auth/operator-step-up-dialog", () => ({
  OperatorStepUpDialog: ({ open, onVerified }: { open: boolean; onVerified?: () => void }) =>
    open ? <button type="button" onClick={() => onVerified?.()}>Complete step-up</button> : null,
}))

import { StackUsersPanel } from "./stack-users-panel"

const usersEndpoint = "/internal/host-agent/runtime-stacks/restored-stack/users"
const adminId = "11111111-1111-1111-1111-111111111111"
const memberId = "22222222-2222-2222-2222-222222222222"

const admin = {
  id: adminId,
  runtimeStackId: "7085b97d-3d30-434e-976a-62df0178be16",
  matrixInstanceId: "33333333-3333-3333-3333-333333333333",
  username: "admin",
  matrixUserId: "@admin:matrix.example.test",
  isAdmin: true,
  isFirstAdmin: true,
  status: "active",
  origin: "mem-created",
  displayName: null,
  email: null,
  lastError: null,
  createdAtUtc: "2026-07-01T10:00:00Z",
  updatedAtUtc: "2026-07-10T00:00:00Z",
  matrixSyncedAtUtc: "2026-07-10T00:00:00Z",
}

const member = {
  ...admin,
  id: memberId,
  username: "member",
  matrixUserId: "@member:matrix.example.test",
  isAdmin: false,
  isFirstAdmin: false,
  origin: "synapse-discovered",
}

function inventory(user = member): RuntimeStackUsersResponse {
  return {
    source: "control-plane",
    status: "ok",
    stackId: admin.runtimeStackId,
    slug: "restored-stack",
    inventorySource: "synapse-postgres",
    inventoryStatus: "synchronized",
    inventoryLastAttemptedAtUtc: "2026-07-10T00:00:00Z",
    inventoryLastSynchronizedAtUtc: "2026-07-10T00:00:00Z",
    inventoryUserCount: 2,
    activeAdminCount: 1,
    inventoryErrorCode: null,
    synchronizationRequired: false,
    users: [admin, user],
    requiresFirstAdmin: false,
    canCreateUsers: true,
    adminAuthority: {
      status: "available",
      canResetPasswords: true,
      source: "managed-recovery-registration",
      adminUserId: "@mem_recovery_test:matrix.example.test",
      storedAtUtc: "2026-07-10T00:00:00Z",
      lastValidatedAtUtc: "2026-07-10T00:00:00Z",
      errorCode: null,
    },
    detail: null,
  }
}

describe("Stack users lifecycle actions", () => {
  it("protects the last active admin and deactivates a member after confirmation and step-up", async () => {
    let attempts = 0
    server.use(
      http.get(usersEndpoint, () => HttpResponse.json(inventory())),
      http.post(`${usersEndpoint}/${memberId}/deactivate`, async ({ request }) => {
        attempts += 1
        expect(await request.json()).toEqual({ erase: false })
        if (attempts === 1) {
          return HttpResponse.json(
            { error: "step_up_required", detail: "Recent identity verification is required." },
            { status: 403 },
          )
        }
        return HttpResponse.json({
          source: "control-plane",
          status: "deactivated",
          runtimeStackId: admin.runtimeStackId,
          userId: memberId,
          matrixUserId: member.matrixUserId,
          isDeactivated: true,
          logoutDevices: true,
          completedAtUtc: "2026-07-10T09:00:00Z",
        })
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackUsersPanel slugOrId="restored-stack" />)
    await screen.findByText(member.matrixUserId)

    const deactivateButtons = screen.getAllByRole("button", { name: "Deactivate account" })
    expect(deactivateButtons[0]).toBeDisabled()
    expect(deactivateButtons[0]).toHaveAttribute(
      "title",
      "The last active Matrix administrator cannot be deactivated.",
    )

    await user.click(deactivateButtons[1]!)
    expect(await screen.findByText("Deactivate Matrix account")).toBeInTheDocument()
    await user.click(screen.getAllByRole("button", { name: "Deactivate account" }).at(-1)!)
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))
    await waitFor(() => expect(attempts).toBe(2))
  })

  it("reactivates a deactivated account only after a matching new password and step-up", async () => {
    let attempts = 0
    const deactivatedMember = { ...member, status: "deactivated" }
    server.use(
      http.get(usersEndpoint, () => HttpResponse.json(inventory(deactivatedMember))),
      http.post(`${usersEndpoint}/${memberId}/reactivate`, async ({ request }) => {
        attempts += 1
        expect(await request.json()).toEqual({ newPassword: "Reactivated!123" })
        if (attempts === 1) {
          return HttpResponse.json(
            { error: "step_up_required", detail: "Recent identity verification is required." },
            { status: 403 },
          )
        }
        return HttpResponse.json({
          source: "control-plane",
          status: "reactivated",
          runtimeStackId: admin.runtimeStackId,
          userId: memberId,
          matrixUserId: member.matrixUserId,
          isDeactivated: false,
          logoutDevices: true,
          completedAtUtc: "2026-07-10T09:05:00Z",
        })
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackUsersPanel slugOrId="restored-stack" />)
    await screen.findByText(member.matrixUserId)
    await user.click(screen.getByRole("button", { name: "Reactivate account" }))
    await user.type(screen.getByLabelText("New password"), "Reactivated!123")
    await user.type(screen.getByLabelText("Confirm new password"), "Reactivated!123")
    await user.click(screen.getAllByRole("button", { name: "Reactivate account" }).at(-1)!)
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))
    await waitFor(() => expect(attempts).toBe(2))
  })
})
