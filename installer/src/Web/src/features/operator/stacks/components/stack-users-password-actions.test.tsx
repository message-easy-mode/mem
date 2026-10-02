import { http, HttpResponse } from "msw"
import { screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, describe, expect, it, vi } from "vitest"

import "@/test/msw-lifecycle"
import { server } from "@/test/msw-server"
import { renderWithProviders } from "@/test/render-with-providers"
import type { RuntimeStackUsersResponse } from "../api/stacks.types"

vi.mock("@/features/auth/operator-step-up-dialog", () => ({
  OperatorStepUpDialog: ({
    open,
    onVerified,
  }: {
    open: boolean
    onVerified?: () => void
  }) => open ? (
    <button type="button" onClick={() => onVerified?.()}>
      Complete step-up
    </button>
  ) : null,
}))

import { StackUsersPanel } from "./stack-users-panel"

const usersEndpoint = "/internal/host-agent/runtime-stacks/restored-stack/users"
const adminId = "11111111-1111-1111-1111-111111111111"
const memberId = "22222222-2222-2222-2222-222222222222"
const resetEndpoint = `${usersEndpoint}/${memberId}/password`

const recoveredAdmin = {
  id: adminId,
  runtimeStackId: "7085b97d-3d30-434e-976a-62df0178be16",
  matrixInstanceId: "33333333-3333-3333-3333-333333333333",
  username: "restored-admin",
  matrixUserId: "@restored-admin:matrix.example.test",
  isAdmin: true,
  isFirstAdmin: false,
  status: "active",
  origin: "synapse-discovered",
  displayName: null,
  email: null,
  lastError: null,
  createdAtUtc: "2026-07-01T10:00:00Z",
  updatedAtUtc: "2026-07-10T00:00:00Z",
  matrixSyncedAtUtc: "2026-07-10T00:00:00Z",
}

const recoveredMember = {
  ...recoveredAdmin,
  id: memberId,
  username: "restored-member",
  matrixUserId: "@restored-member:matrix.example.test",
  isAdmin: false,
}

function inventory(overrides: Partial<RuntimeStackUsersResponse> = {}): RuntimeStackUsersResponse {
  return {
    source: "control-plane",
    status: "ok",
    stackId: "7085b97d-3d30-434e-976a-62df0178be16",
    slug: "restored-stack",
    inventorySource: "synapse-postgres",
    inventoryStatus: "synchronized",
    inventoryLastAttemptedAtUtc: "2026-07-10T00:00:00Z",
    inventoryLastSynchronizedAtUtc: "2026-07-10T00:00:00Z",
    inventoryUserCount: 2,
    activeAdminCount: 1,
    inventoryErrorCode: null,
    synchronizationRequired: false,
    users: [recoveredAdmin, recoveredMember],
    requiresFirstAdmin: false,
    canCreateUsers: true,
    adminAuthority: {
      status: "available",
      canResetPasswords: true,
      source: "shared-secret-registration",
      adminUserId: recoveredAdmin.matrixUserId,
      storedAtUtc: "2026-07-10T00:00:00Z",
      lastValidatedAtUtc: "2026-07-10T00:00:00Z",
      errorCode: null,
    },
    detail: null,
    ...overrides,
  }
}

function authorityRequiredInventory(): RuntimeStackUsersResponse {
  return inventory({
    adminAuthority: {
      status: "required",
      canResetPasswords: false,
      source: null,
      adminUserId: null,
      storedAtUtc: null,
      lastValidatedAtUtc: null,
      errorCode: null,
    },
  })
}

afterEach(() => window.localStorage.clear())

describe("StackUsersPanel simple Matrix password reset", () => {
  it("offers reset directly when stored authority is absent and lets the backend establish it", async () => {
    let attempts = 0

    server.use(
      http.get(usersEndpoint, () => HttpResponse.json(authorityRequiredInventory())),
      http.post(resetEndpoint, async ({ request }) => {
        attempts += 1
        expect(await request.json()).toEqual({ newPassword: "Replacement!123" })

        if (attempts === 1) {
          return HttpResponse.json(
            { error: "step_up_required", detail: "Recent identity verification is required." },
            { status: 403 },
          )
        }

        return HttpResponse.json({
          source: "control-plane",
          status: "password_reset",
          runtimeStackId: "7085b97d-3d30-434e-976a-62df0178be16",
          userId: memberId,
          matrixUserId: recoveredMember.matrixUserId,
          logoutDevices: true,
          adminAuthorityInvalidated: false,
          completedAtUtc: "2026-07-10T08:05:00Z",
        })
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackUsersPanel slugOrId="restored-stack" />)

    await screen.findByText(recoveredMember.matrixUserId)
    expect(screen.queryByText("Matrix administrator authority required")).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Set authority first" })).not.toBeInTheDocument()

    const resetButtons = screen.getAllByRole("button", { name: "Reset password" })
    expect(resetButtons).toHaveLength(2)
    await user.click(resetButtons[1]!)
    await user.type(screen.getByLabelText("New password"), "Replacement!123")
    await user.type(screen.getByLabelText("Confirm new password"), "Replacement!123")
    await user.click(screen.getAllByRole("button", { name: "Reset password" }).at(-1)!)
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))

    expect(await screen.findByText("Matrix password reset")).toBeInTheDocument()
    expect(screen.getByText(/existing Matrix devices were signed out/)).toBeInTheDocument()
    await waitFor(() => expect(attempts).toBe(2))
  })

  it("resets an active user's password and never exposes reset actions for inactive accounts", async () => {
    const current = inventory({
      inventoryUserCount: 3,
      users: [
        recoveredAdmin,
        recoveredMember,
        {
          ...recoveredMember,
          id: "44444444-4444-4444-4444-444444444444",
          username: "former-member",
          matrixUserId: "@former-member:matrix.example.test",
          status: "deactivated",
        },
      ],
    })
    let attempts = 0

    server.use(
      http.get(usersEndpoint, () => HttpResponse.json(current)),
      http.post(resetEndpoint, async ({ request }) => {
        attempts += 1
        expect(await request.json()).toEqual({ newPassword: "Replacement!123" })

        if (attempts === 1) {
          return HttpResponse.json(
            { error: "step_up_required", detail: "Recent identity verification is required." },
            { status: 403 },
          )
        }

        return HttpResponse.json({
          source: "control-plane",
          status: "password_reset",
          runtimeStackId: current.stackId,
          userId: memberId,
          matrixUserId: recoveredMember.matrixUserId,
          logoutDevices: true,
          adminAuthorityInvalidated: false,
          completedAtUtc: "2026-07-10T08:05:00Z",
        })
      }),
    )

    const user = userEvent.setup()
    renderWithProviders(<StackUsersPanel slugOrId="restored-stack" />)

    await screen.findByText(recoveredMember.matrixUserId)
    expect(screen.getByRole("button", { name: "Reactivate account" })).toBeInTheDocument()

    const resetButtons = screen.getAllByRole("button", { name: "Reset password" })
    await user.click(resetButtons[1]!)
    await user.type(screen.getByLabelText("New password"), "Replacement!123")
    await user.type(screen.getByLabelText("Confirm new password"), "Replacement!123")
    await user.click(screen.getAllByRole("button", { name: "Reset password" }).at(-1)!)

    expect(screen.getByLabelText("New password")).toHaveValue("")
    expect(screen.getByLabelText("Confirm new password")).toHaveValue("")
    await user.click(await screen.findByRole("button", { name: "Complete step-up" }))

    expect(await screen.findByText("Matrix password reset")).toBeInTheDocument()
    await waitFor(() => expect(attempts).toBe(2))
  })

  it("does not submit mismatched replacement passwords", async () => {
    server.use(http.get(usersEndpoint, () => HttpResponse.json(authorityRequiredInventory())))
    const user = userEvent.setup()
    renderWithProviders(<StackUsersPanel slugOrId="restored-stack" />)

    await screen.findByText(recoveredMember.matrixUserId)
    await user.click(screen.getAllByRole("button", { name: "Reset password" })[1]!)
    await user.type(screen.getByLabelText("New password"), "Replacement!123")
    await user.type(screen.getByLabelText("Confirm new password"), "Different!123")

    expect(screen.getByText("The passwords do not match.")).toBeInTheDocument()
    expect(screen.getAllByRole("button", { name: "Reset password" }).at(-1)).toBeDisabled()
  })

  it("localises the direct password-reset controls in German without authority setup", async () => {
    window.localStorage.setItem("mem.ui-language", "de")
    server.use(http.get(usersEndpoint, () => HttpResponse.json(authorityRequiredInventory())))

    renderWithProviders(<StackUsersPanel slugOrId="restored-stack" />)

    await screen.findByText(recoveredMember.matrixUserId)
    expect(screen.getAllByRole("button", { name: "Passwort zurücksetzen" })).toHaveLength(2)
    expect(screen.queryByText("Matrix-Administratorberechtigung erforderlich")).not.toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Zuerst Berechtigung einrichten" })).not.toBeInTheDocument()
  })
})
